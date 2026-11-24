using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DashboardStarter.Api.Database;
using DashboardStarter.Db.Audit;
using Dapper;
using Microsoft.Data.SqlClient;

namespace DashboardStarter.Api.Audit;

/// <summary>
/// Reads the audit log. Reading is all the API does with it besides inserting
/// (<see cref="AuditLog.WriteAsync"/>): there is no UPDATE or DELETE of dbo.AuditLog anywhere in
/// the API, and its database role is denied both (sql/migrations/0016_audit_log.sql).
/// </summary>
/// <remarks>
/// Like every tenant-owned table, the tenant is always a parameter (WHERE a.TenantId = @TenantId),
/// from the session; Row-Level Security filters on the same tenant as a safety net. Names (who
/// did it, which customer or member) are joined in here, as they are now, never stored in the log.
/// </remarks>
public sealed class AuditStore
{
  public const int PageSize = 25;

  // The pieces of the WHERE clause, fixed here, added in code so each combination gets its own
  // plan (the same pattern as CustomerStore). Values are always parameters.
  private const string TenantRows = """
    WHERE a.TenantId = @TenantId
    """;

  private const string EntityTypeMatches = """

      AND a.EntityType = @EntityType
    """;

  private const string EntityIdMatches = """

      AND a.EntityId = @EntityId
    """;

  private const string UserMatches = """

      AND a.UserId = @UserId
    """;

  private readonly SqlConnectionFactory _connectionFactory;

  public AuditStore(SqlConnectionFactory connectionFactory)
  {
    _connectionFactory = connectionFactory;
  }

  /// <summary>One page of entries, newest first. By Id, which only goes up: the order is the order things happened in.</summary>
  public async Task<AuditListResponse> ListAsync(int tenantId, AuditQuery query, CancellationToken cancellationToken)
  {
    string where = TenantRows
      + (query.EntityType is null ? "" : EntityTypeMatches)
      + (query.EntityId is null ? "" : EntityIdMatches)
      + (query.UserId is null ? "" : UserMatches);

    // The customer join has no DeletedAt filter, on purpose: the log names deleted customers
    // too, and says they're deleted. The member join is by user id; whether that user is still
    // a member of this tenant doesn't matter for showing their name.
    string sql = $"""
      SELECT COUNT(*)
      FROM dbo.AuditLog AS a
      {where};

      SELECT
        a.Id,
        a.OccurredAt,
        a.UserId,
        u.DisplayName AS UserName,
        a.EntityType,
        a.EntityId,
        CASE a.EntityType WHEN '{AuditLog.Customer}' THEN c.Name WHEN '{AuditLog.Member}' THEN m.DisplayName END AS EntityName,
        CAST(CASE WHEN c.DeletedAt IS NOT NULL THEN 1 ELSE 0 END AS bit) AS EntityDeleted,
        a.Action,
        a.Changes
      FROM dbo.AuditLog AS a
      LEFT JOIN dbo.Users AS u ON u.Id = a.UserId
      LEFT JOIN dbo.Customers AS c ON a.EntityType = '{AuditLog.Customer}' AND c.Id = a.EntityId AND c.TenantId = a.TenantId
      LEFT JOIN dbo.Users AS m ON a.EntityType = '{AuditLog.Member}' AND m.Id = a.EntityId
      {where}
      ORDER BY a.Id DESC
      OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
      """;

    object parameters = new
    {
      TenantId = tenantId,
      query.EntityType,
      query.EntityId,
      query.UserId,
      Offset = (query.Page - 1) * PageSize,
      PageSize,
    };

    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    await using SqlMapper.GridReader results = await connection.QueryMultipleAsync(
      new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
    int total = await results.ReadSingleAsync<int>();
    List<AuditEntryResponse> items = (await results.ReadAsync<AuditRow>())
      .Select(row => new AuditEntryResponse(
        row.Id,
        row.OccurredAt,
        row.UserId,
        row.UserName,
        row.EntityType,
        row.EntityId,
        row.EntityName,
        row.EntityDeleted,
        row.Action,
        AuditChanges.Parse(row.Changes)))
      .ToList();
    return new AuditListResponse(items, total, query.Page, PageSize);
  }

  // A row as the query returns it: Changes is still the JSON text.
  private sealed record AuditRow(
    long Id,
    DateTime OccurredAt,
    int? UserId,
    string? UserName,
    string EntityType,
    int EntityId,
    string? EntityName,
    bool EntityDeleted,
    string Action,
    string? Changes);
}
