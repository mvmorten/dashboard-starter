using System;
using System.Threading.Tasks;
using Dapper;
using DashboardStarter.Db.Authorization;
using Microsoft.Data.SqlClient;

namespace DashboardStarter.Db.Users;

/// <summary>
/// Changing a membership's role or status, with the rule that protects every tenant: it never
/// loses its last active member who can manage members. Used by the API's Members page and by
/// the Db runner's set-role and create-user, so the rule lives in one place.
/// </summary>
/// <remarks>
/// <para>
/// Every change runs in the caller's transaction and starts by locking the tenant's row
/// (UPDLOCK, HOLDLOCK), so changes to one tenant's memberships happen one at a time. Two admins
/// demoting each other at the same moment can't both see "someone else is still an admin": the
/// second waits for the first, then finds it would leave nobody, and is refused.
/// </para>
/// <para>
/// Anything but <see cref="MembershipChangeResult.Changed"/> means the caller must not commit.
/// The rule is checked by making the change and then counting, so a refused change has to be
/// rolled back (disposing the transaction without committing does that).
/// </para>
/// <para>
/// The rule is about the permission, not a role name: a fork that adds an "Owner" role with
/// members.manage gets the same protection without touching this code.
/// </para>
/// </remarks>
public static class Memberships
{
  public const string Active = "Active";
  public const string Disabled = "Disabled";

  /// <summary>Gives the member another role in this tenant.</summary>
  /// <param name="expectedRowVersion">The membership's RowVersion as the caller last read it, or null to skip the check.</param>
  /// <param name="actorUserId">Who is making the change. Must still be able to manage members, checked under the lock. Null for the Db runner.</param>
  public static Task<MembershipChange> SetRoleAsync(
    SqlConnection connection,
    SqlTransaction transaction,
    int tenantId,
    int userId,
    int roleId,
    byte[]? expectedRowVersion,
    int? actorUserId)
  {
    const string sql = """
      UPDATE dbo.TenantUsers
      SET RoleId = @RoleId, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @ActorUserId
      OUTPUT inserted.RowVersion
      WHERE TenantId = @TenantId
        AND UserId = @UserId;
      """;

    return ChangeAsync(
      connection,
      transaction,
      tenantId,
      userId,
      sql,
      new { TenantId = tenantId, UserId = userId, RoleId = roleId, ActorUserId = actorUserId },
      roleId,
      expectedRowVersion,
      actorUserId);
  }

  /// <summary>Disables the member's access to this tenant, or turns it back on. Other tenants aren't affected.</summary>
  public static Task<MembershipChange> SetStatusAsync(
    SqlConnection connection,
    SqlTransaction transaction,
    int tenantId,
    int userId,
    bool active,
    byte[]? expectedRowVersion,
    int? actorUserId)
  {
    const string sql = """
      UPDATE dbo.TenantUsers
      SET Status = @Status, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @ActorUserId
      OUTPUT inserted.RowVersion
      WHERE TenantId = @TenantId
        AND UserId = @UserId;
      """;

    return ChangeAsync(
      connection,
      transaction,
      tenantId,
      userId,
      sql,
      new { TenantId = tenantId, UserId = userId, Status = active ? Active : Disabled, ActorUserId = actorUserId },
      roleId: null,
      expectedRowVersion,
      actorUserId);
  }

  private static async Task<MembershipChange> ChangeAsync(
    SqlConnection connection,
    SqlTransaction transaction,
    int tenantId,
    int userId,
    string updateSql,
    object updateParameters,
    int? roleId,
    byte[]? expectedRowVersion,
    int? actorUserId)
  {
    // One change at a time per tenant, until this transaction ends.
    const string lockSql = """
      SELECT Id
      FROM dbo.Tenants WITH (UPDLOCK, HOLDLOCK)
      WHERE Id = @TenantId;
      """;
    int? lockedTenant = await connection.QuerySingleOrDefaultAsync<int?>(lockSql, new { TenantId = tenantId }, transaction);
    if (lockedTenant is null)
    {
      return new MembershipChange(MembershipChangeResult.NotFound, null);
    }

    // The actor's permissions came from the session check, cached for up to a minute. Check
    // again under the lock, so someone demoted a moment ago can't still change members.
    if (actorUserId is not null && !await CanManageAsync(connection, transaction, tenantId, actorUserId.Value))
    {
      return new MembershipChange(MembershipChangeResult.ActorCannotManage, null);
    }

    if (roleId is not null && !await RoleExistsAsync(connection, transaction, roleId.Value))
    {
      return new MembershipChange(MembershipChangeResult.UnknownRole, null);
    }

    const string currentSql = """
      SELECT tu.RowVersion
      FROM dbo.TenantUsers AS tu
      WHERE tu.TenantId = @TenantId
        AND tu.UserId = @UserId;
      """;
    byte[]? currentRowVersion = await connection.QuerySingleOrDefaultAsync<byte[]?>(
      currentSql, new { TenantId = tenantId, UserId = userId }, transaction);
    if (currentRowVersion is null)
    {
      return new MembershipChange(MembershipChangeResult.NotFound, null);
    }

    if (expectedRowVersion is not null && !expectedRowVersion.AsSpan().SequenceEqual(currentRowVersion))
    {
      return new MembershipChange(MembershipChangeResult.Conflict, currentRowVersion);
    }

    bool couldManage = await CanManageAsync(connection, transaction, tenantId, userId);
    byte[] newRowVersion = await connection.QuerySingleAsync<byte[]>(updateSql, updateParameters, transaction);

    // Was this the last one? Counted after the change, inside the lock, so it's exact.
    if (couldManage && await CountManagersAsync(connection, transaction, tenantId) == 0)
    {
      return new MembershipChange(MembershipChangeResult.LastManager, null);
    }

    return new MembershipChange(MembershipChangeResult.Changed, newRowVersion);
  }

  /// <summary>An active member of the tenant whose role has members.manage.</summary>
  public static async Task<bool> CanManageAsync(SqlConnection connection, SqlTransaction transaction, int tenantId, int userId)
  {
    const string sql = """
      SELECT CAST(CASE WHEN EXISTS (
        SELECT 1
        FROM dbo.TenantUsers AS tu
        INNER JOIN dbo.RolePermissions AS rp ON rp.RoleId = tu.RoleId
        WHERE tu.TenantId = @TenantId
          AND tu.UserId = @UserId
          AND tu.Status = 'Active'
          AND rp.Permission = @Permission
      ) THEN 1 ELSE 0 END AS bit);
      """;

    return await connection.ExecuteScalarAsync<bool>(
      sql, new { TenantId = tenantId, UserId = userId, Permission = Permissions.MembersManage }, transaction);
  }

  private static async Task<int> CountManagersAsync(SqlConnection connection, SqlTransaction transaction, int tenantId)
  {
    const string sql = """
      SELECT COUNT(*)
      FROM dbo.TenantUsers AS tu
      INNER JOIN dbo.RolePermissions AS rp ON rp.RoleId = tu.RoleId
      WHERE tu.TenantId = @TenantId
        AND tu.Status = 'Active'
        AND rp.Permission = @Permission;
      """;

    return await connection.ExecuteScalarAsync<int>(
      sql, new { TenantId = tenantId, Permission = Permissions.MembersManage }, transaction);
  }

  private static async Task<bool> RoleExistsAsync(SqlConnection connection, SqlTransaction transaction, int roleId)
  {
    const string sql = "SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.Roles WHERE Id = @Id) THEN 1 ELSE 0 END AS bit);";
    return await connection.ExecuteScalarAsync<bool>(sql, new { Id = roleId }, transaction);
  }
}

public enum MembershipChangeResult
{
  Changed,
  NotFound,
  UnknownRole,
  Conflict,
  LastManager,
  ActorCannotManage,
}

/// <param name="Result">What happened. Commit only on <see cref="MembershipChangeResult.Changed"/>.</param>
/// <param name="RowVersion">The new RowVersion after a change, or the current one after a <see cref="MembershipChangeResult.Conflict"/>.</param>
public sealed record MembershipChange(MembershipChangeResult Result, byte[]? RowVersion);
