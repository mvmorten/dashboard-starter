using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;

namespace DashboardStarter.Db.Audit;

/// <summary>
/// Writes audit entries: who changed what (sql/migrations/0016_audit_log.sql). There's no magic
/// here, no trigger and no interceptor: the code that makes a change calls
/// <see cref="WriteAsync"/> right after it, on the same connection and in the same transaction.
/// Both commit or neither does.
/// </summary>
/// <remarks>
/// <para>
/// <b>Auditing another entity in a fork:</b> add its name to <see cref="EntityTypes"/>, and in
/// each method that inserts, updates, deletes or restores it, call <see cref="WriteAsync"/>
/// before the commit, building the changes with <see cref="AuditChanges"/> from the fields you
/// choose to record. A field nobody added is never logged, so a new column (a token, a hash)
/// stays out until someone decides it belongs.
/// </para>
/// <para>
/// AuditLog is tenant-owned, so the connection must be working in the entry's tenant
/// (SESSION_CONTEXT), or Row-Level Security refuses the insert. A request's connection already
/// is; the Db runner sets it (SetRoleCommand).
/// </para>
/// <para>
/// It lives in the Db project because the Db runner writes entries too: set-role goes through
/// the same Memberships code as the Members page.
/// </para>
/// </remarks>
public static class AuditLog
{
  public const string Customer = "Customer";
  public const string Member = "Member";

  /// <summary>Every audited entity. The audit page's filter accepts these and nothing else.</summary>
  public static IReadOnlyList<string> EntityTypes { get; } = [Customer, Member];

  public const string Created = "Created";
  public const string Updated = "Updated";
  public const string Deleted = "Deleted";
  public const string Restored = "Restored";

  /// <summary>Adds one entry. When it happened is the database's clock (the column's default).</summary>
  /// <param name="userId">Who did it: the signed-in user, never a value from the request. Null when no person did (the Db runner).</param>
  /// <param name="entityId">The entity's id. For a Member, the member's user id.</param>
  /// <param name="changes">The changed fields, or null when the action says it all (Deleted, Restored).</param>
  public static async Task WriteAsync(
    SqlConnection connection,
    SqlTransaction transaction,
    int tenantId,
    int? userId,
    string entityType,
    int entityId,
    string action,
    AuditChanges? changes,
    CancellationToken cancellationToken)
  {
    const string sql = """
      INSERT INTO dbo.AuditLog (TenantId, UserId, EntityType, EntityId, Action, Changes)
      VALUES (@TenantId, @UserId, @EntityType, @EntityId, @Action, @Changes);
      """;

    await connection.ExecuteAsync(new CommandDefinition(
      sql,
      new
      {
        TenantId = tenantId,
        UserId = userId,
        EntityType = entityType,
        EntityId = entityId,
        Action = action,
        Changes = changes is null || changes.IsEmpty ? null : changes.ToJson(),
      },
      transaction,
      cancellationToken: cancellationToken));
  }
}
