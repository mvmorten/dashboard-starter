using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;

namespace DashboardStarter.Db.Tenants;

/// <summary>
/// Puts a tenant in SESSION_CONTEXT, which Row-Level Security filters on
/// (sql/programmable/Security.fn_TenantAccessPredicate.sql). Shared by the API, which sets it
/// once per request, and the Db runner, which sets it per tenant for data migrations.
/// </summary>
/// <remarks>
/// <para>
/// No login is exempt from Row-Level Security, not even the admin the Db runner uses. So work on
/// a tenant-owned table has to say which tenant it's working on, or it sees no rows: an UPDATE
/// with no tenant set "succeeds" and changes nothing, and an INSERT is refused.
/// <see cref="ForEachTenantAsync"/> is how work that spans tenants does it.
/// </para>
/// </remarks>
public static class TenantSession
{
  /// <summary>
  /// Sets the tenant on this connection. <paramref name="readOnly"/>: nothing later on this
  /// connection can change it, which is how a request's connection is set. Until the connection
  /// goes back to the pool, which clears it (sp_reset_connection).
  /// </summary>
  public static async Task SetAsync(
    SqlConnection connection, SqlTransaction? transaction, int tenantId, bool readOnly, CancellationToken cancellationToken)
  {
    const string sql = "EXEC sys.sp_set_session_context @key = N'TenantId', @value = @TenantId, @read_only = @ReadOnly;";
    await connection.ExecuteAsync(new CommandDefinition(
      sql, new { TenantId = tenantId, ReadOnly = readOnly }, transaction, cancellationToken: cancellationToken));
  }

  /// <summary>Clears a tenant set without readOnly, so what runs next sees no tenant-owned rows.</summary>
  public static async Task ClearAsync(SqlConnection connection, SqlTransaction? transaction, CancellationToken cancellationToken)
  {
    const string sql = "EXEC sys.sp_set_session_context @key = N'TenantId', @value = NULL;";
    await connection.ExecuteAsync(new CommandDefinition(sql, transaction: transaction, cancellationToken: cancellationToken));
  }

  /// <summary>
  /// Runs <paramref name="step"/> once for every tenant, in id order, on this connection and in
  /// this transaction, with that tenant in SESSION_CONTEXT, then clears it. Returns the tenants
  /// it ran for.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Every tenant, demo sandboxes included: a data fix applies to all the data. With no tenants
  /// (a fresh database) the step never runs, which is right: there's no data to fix.
  /// </para>
  /// <para>
  /// If a step throws, the tenant is left set. The caller is about to roll back and stop anyway
  /// (that's what the runner does with a failed migration), and clearing it on a transaction the
  /// error has doomed would throw again and hide the real error.
  /// </para>
  /// </remarks>
  public static async Task<IReadOnlyList<int>> ForEachTenantAsync(
    SqlConnection connection, SqlTransaction transaction, Func<int, Task> step, CancellationToken cancellationToken)
  {
    const string sql = "SELECT Id FROM dbo.Tenants ORDER BY Id;";
    List<int> tenantIds = (await connection.QueryAsync<int>(
      new CommandDefinition(sql, transaction: transaction, cancellationToken: cancellationToken))).ToList();

    foreach (int tenantId in tenantIds)
    {
      await SetAsync(connection, transaction, tenantId, readOnly: false, cancellationToken);
      await step(tenantId);
    }

    await ClearAsync(connection, transaction, cancellationToken);
    return tenantIds;
  }
}
