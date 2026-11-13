using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DashboardStarter.Api.Database;
using DashboardStarter.Api.Tenants;
using Dapper;
using Microsoft.Data.SqlClient;

namespace DashboardStarter.Api.Auth;

/// <summary>
/// The SQL behind sign-in and sessions. Users, Tenants, and TenantUsers are global tables
/// (no Row-Level Security), so these queries work before anyone has a current tenant.
/// </summary>
public sealed class UserStore
{
  private readonly SqlConnectionFactory _connectionFactory;

  public UserStore(SqlConnectionFactory connectionFactory)
  {
    _connectionFactory = connectionFactory;
  }

  public async Task<SignInCandidate?> FindForSignInAsync(string email, CancellationToken cancellationToken)
  {
    // Email has a case-insensitive collation (checked by migration 0006), so any spelling
    // of the address finds the account. Lockout is decided by the database's clock, the
    // same one that set it. An expired demo user counts as no account at all.
    const string sql = """
      SELECT
        Id,
        PasswordHash,
        SecurityStamp,
        CAST(CASE WHEN LockoutEndAt > SYSUTCDATETIME() THEN 1 ELSE 0 END AS bit) AS IsLockedOut
      FROM dbo.Users
      WHERE Email = @Email
        AND (ExpiresAt IS NULL OR ExpiresAt > SYSUTCDATETIME());
      """;

    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    return await connection.QuerySingleOrDefaultAsync<SignInCandidate>(
      new CommandDefinition(sql, new { Email = email }, cancellationToken: cancellationToken));
  }

  /// <summary>
  /// Counts a wrong password. On the last allowed failure the account is locked and the
  /// count starts over, so the next lockout needs a fresh run of failures. One statement,
  /// so two attempts at once can't both read the old count.
  /// </summary>
  public async Task RecordFailedSignInAsync(int userId, int maxFailures, int lockoutMinutes, CancellationToken cancellationToken)
  {
    const string sql = """
      UPDATE dbo.Users
      SET
        LockoutEndAt = CASE WHEN FailedSignInCount + 1 >= @MaxFailures
          THEN DATEADD(minute, @LockoutMinutes, SYSUTCDATETIME()) ELSE LockoutEndAt END,
        FailedSignInCount = CASE WHEN FailedSignInCount + 1 >= @MaxFailures
          THEN 0 ELSE FailedSignInCount + 1 END
      WHERE Id = @Id;
      """;

    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    await connection.ExecuteAsync(new CommandDefinition(
      sql,
      new { Id = userId, MaxFailures = maxFailures, LockoutMinutes = lockoutMinutes },
      cancellationToken: cancellationToken));
  }

  /// <summary>
  /// A successful sign-in: clears failures and lockout, and stores a stronger hash if the
  /// password was rehashed. The hash is only replaced if it's still the one we verified
  /// against, so a password changed in the meantime is never overwritten.
  /// </summary>
  public async Task RecordSignInAsync(int userId, string verifiedHash, string? newHash, CancellationToken cancellationToken)
  {
    // BIN2: an exact, case-sensitive comparison. The column's own collation ignores case,
    // which is right for emails but not for base64.
    const string sql = """
      UPDATE dbo.Users
      SET
        FailedSignInCount = 0,
        LockoutEndAt = NULL,
        LastSignInAt = SYSUTCDATETIME(),
        PasswordHash = CASE WHEN @NewHash IS NOT NULL AND PasswordHash = @VerifiedHash COLLATE Latin1_General_BIN2
          THEN @NewHash ELSE PasswordHash END
      WHERE Id = @Id;
      """;

    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    await connection.ExecuteAsync(new CommandDefinition(
      sql,
      new { Id = userId, VerifiedHash = verifiedHash, NewHash = newHash },
      cancellationToken: cancellationToken));
  }

  public async Task<UserSummary?> GetUserAsync(int userId, CancellationToken cancellationToken)
  {
    const string sql = """
      SELECT Id, Email, DisplayName, IsDemo
      FROM dbo.Users
      WHERE Id = @Id;
      """;

    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    return await connection.QuerySingleOrDefaultAsync<UserSummary>(
      new CommandDefinition(sql, new { Id = userId }, cancellationToken: cancellationToken));
  }

  /// <summary>The tenants the user can work in, the one they used last first.</summary>
  public async Task<IReadOnlyList<TenantResponse>> GetActiveTenantsAsync(int userId, CancellationToken cancellationToken)
  {
    const string sql = """
      SELECT t.Id, t.Name
      FROM dbo.TenantUsers AS tu
      INNER JOIN dbo.Tenants AS t ON t.Id = tu.TenantId
      WHERE tu.UserId = @UserId
        AND tu.Status = 'Active'
      ORDER BY
        CASE WHEN tu.LastUsedAt IS NULL THEN 1 ELSE 0 END,
        tu.LastUsedAt DESC,
        t.Name;
      """;

    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    IEnumerable<TenantResponse> tenants = await connection.QueryAsync<TenantResponse>(
      new CommandDefinition(sql, new { UserId = userId }, cancellationToken: cancellationToken));
    return tenants.ToList();
  }

  /// <summary>
  /// Records that the user is now working in this tenant. Returns false, and changes
  /// nothing, unless they have an active membership in it.
  /// </summary>
  public async Task<bool> MarkTenantUsedAsync(int userId, int tenantId, CancellationToken cancellationToken)
  {
    const string sql = """
      UPDATE dbo.TenantUsers
      SET LastUsedAt = SYSUTCDATETIME()
      WHERE UserId = @UserId
        AND TenantId = @TenantId
        AND Status = 'Active';
      """;

    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    int updated = await connection.ExecuteAsync(new CommandDefinition(
      sql, new { UserId = userId, TenantId = tenantId }, cancellationToken: cancellationToken));
    return updated == 1;
  }

  /// <summary>What change-password needs to check the current password.</summary>
  public async Task<PasswordChangeCandidate?> FindForPasswordChangeAsync(int userId, CancellationToken cancellationToken)
  {
    const string sql = """
      SELECT Id, Email, PasswordHash, IsDemo
      FROM dbo.Users
      WHERE Id = @Id;
      """;

    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    return await connection.QuerySingleOrDefaultAsync<PasswordChangeCandidate>(
      new CommandDefinition(sql, new { Id = userId }, cancellationToken: cancellationToken));
  }

  /// <summary>
  /// Sets a new password and security stamp, so every other session ends. Only if the stored
  /// hash is still the one the current password was checked against: two changes at once
  /// can't both win. Returns false (nothing changed) otherwise, or for a demo account.
  /// </summary>
  public async Task<bool> ChangePasswordAsync(int userId, string verifiedHash, string newHash, byte[] newSecurityStamp, CancellationToken cancellationToken)
  {
    const string sql = """
      UPDATE dbo.Users
      SET
        PasswordHash = @NewHash,
        PasswordChangedAt = SYSUTCDATETIME(),
        SecurityStamp = @SecurityStamp,
        FailedSignInCount = 0,
        LockoutEndAt = NULL,
        UpdatedAt = SYSUTCDATETIME()
      WHERE Id = @Id
        AND IsDemo = 0
        AND PasswordHash = @VerifiedHash COLLATE Latin1_General_BIN2;
      """;

    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    int updated = await connection.ExecuteAsync(new CommandDefinition(
      sql,
      new { Id = userId, VerifiedHash = verifiedHash, NewHash = newHash, SecurityStamp = newSecurityStamp },
      cancellationToken: cancellationToken));
    return updated == 1;
  }

  /// <summary>Every tenant the user has a membership in, active or not: the session cache has an entry per tenant.</summary>
  public async Task<IReadOnlyList<int>> GetMembershipTenantIdsAsync(int userId, CancellationToken cancellationToken)
  {
    const string sql = """
      SELECT TenantId
      FROM dbo.TenantUsers
      WHERE UserId = @UserId;
      """;

    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    IEnumerable<int> tenantIds = await connection.QueryAsync<int>(
      new CommandDefinition(sql, new { UserId = userId }, cancellationToken: cancellationToken));
    return tenantIds.ToList();
  }

  /// <summary>
  /// What the session check compares the cookie against, and what the user may do in the
  /// current tenant. Null if the user no longer exists.
  /// </summary>
  /// <remarks>
  /// Permissions are read here, with the membership, rather than kept in the cookie: a cookie
  /// would hold on to old permissions until it expired. Read here, a changed role or a disabled
  /// membership takes effect at the next check (SessionCheckSeconds at most, usually at once on this
  /// instance when the Members page made the change).
  /// </remarks>
  public async Task<SessionState?> GetSessionStateAsync(int userId, int? tenantId, CancellationToken cancellationToken)
  {
    const string sql = """
      SELECT
        u.SecurityStamp,
        CAST(CASE WHEN u.LockoutEndAt > SYSUTCDATETIME() THEN 1 ELSE 0 END AS bit) AS IsLockedOut,
        u.IsDemo,
        CAST(CASE WHEN u.ExpiresAt <= SYSUTCDATETIME() THEN 1 ELSE 0 END AS bit) AS IsExpired,
        CAST(CASE
          WHEN @TenantId IS NULL THEN 1
          WHEN EXISTS (
            SELECT 1
            FROM dbo.TenantUsers AS tu
            WHERE tu.UserId = u.Id
              AND tu.TenantId = @TenantId
              AND tu.Status = 'Active'
          ) THEN 1
          ELSE 0
        END AS bit) AS IsTenantMember,
        (
          SELECT STRING_AGG(rp.Permission, ',')
          FROM dbo.TenantUsers AS tu
          INNER JOIN dbo.RolePermissions AS rp ON rp.RoleId = tu.RoleId
          WHERE tu.UserId = u.Id
            AND tu.TenantId = @TenantId
            AND tu.Status = 'Active'
        ) AS Permissions
      FROM dbo.Users AS u
      WHERE u.Id = @UserId;
      """;

    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    SessionStateRow? row = await connection.QuerySingleOrDefaultAsync<SessionStateRow>(
      new CommandDefinition(sql, new { UserId = userId, TenantId = tenantId }, cancellationToken: cancellationToken));
    if (row is null)
    {
      return null;
    }

    return new SessionState(
      row.SecurityStamp, row.IsLockedOut, row.IsDemo, row.IsExpired, row.IsTenantMember, ParsePermissions(row.Permissions));
  }

  /// <summary>The user's role in a tenant and its permissions, for /api/auth/me. Null role: not an active member.</summary>
  public async Task<TenantRole> GetTenantRoleAsync(int userId, int? tenantId, CancellationToken cancellationToken)
  {
    if (tenantId is null)
    {
      return new TenantRole(null, ParsePermissions(null));
    }

    const string sql = """
      SELECT
        r.Name AS RoleName,
        (
          SELECT STRING_AGG(rp.Permission, ',')
          FROM dbo.RolePermissions AS rp
          WHERE rp.RoleId = tu.RoleId
        ) AS Permissions
      FROM dbo.TenantUsers AS tu
      INNER JOIN dbo.Roles AS r ON r.Id = tu.RoleId
      WHERE tu.UserId = @UserId
        AND tu.TenantId = @TenantId
        AND tu.Status = 'Active';
      """;

    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    TenantRoleRow? row = await connection.QuerySingleOrDefaultAsync<TenantRoleRow>(
      new CommandDefinition(sql, new { UserId = userId, TenantId = tenantId }, cancellationToken: cancellationToken));
    return row is null
      ? new TenantRole(null, ParsePermissions(null))
      : new TenantRole(row.RoleName, ParsePermissions(row.Permissions));
  }

  // STRING_AGG gives "members.manage,members.view", or NULL for none.
  private static IReadOnlySet<string> ParsePermissions(string? permissions)
  {
    if (string.IsNullOrEmpty(permissions))
    {
      return new HashSet<string>(StringComparer.Ordinal);
    }

    return new HashSet<string>(permissions.Split(','), StringComparer.Ordinal);
  }
}

// What the queries above return, before the permissions string is split.
internal sealed record SessionStateRow(
  byte[] SecurityStamp, bool IsLockedOut, bool IsDemo, bool IsExpired, bool IsTenantMember, string? Permissions);

internal sealed record TenantRoleRow(string RoleName, string? Permissions);

public sealed record PasswordChangeCandidate(int Id, string Email, string? PasswordHash, bool IsDemo);

public sealed record SignInCandidate(int Id, string? PasswordHash, byte[] SecurityStamp, bool IsLockedOut);

public sealed record UserSummary(int Id, string Email, string DisplayName, bool IsDemo);

/// <param name="Permissions">What the user may do in the session's tenant (Permissions.cs). Empty without a tenant or an active membership.</param>
/// <param name="IsExpired">A demo sandbox user past its ExpiresAt: the session ends, and cleanup removes the user.</param>
public sealed record SessionState(
  byte[] SecurityStamp, bool IsLockedOut, bool IsDemo, bool IsExpired, bool IsTenantMember, IReadOnlySet<string> Permissions);

/// <param name="RoleName">The role's name, for display only. Never compare it in code; check a permission.</param>
public sealed record TenantRole(string? RoleName, IReadOnlySet<string> Permissions);
