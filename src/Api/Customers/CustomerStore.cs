using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DashboardStarter.Api.Database;
using DashboardStarter.Api.Demo;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DashboardStarter.Api.Customers;

/// <summary>
/// The SQL behind the Customers endpoints. The pattern every tenant-owned table follows:
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>The tenant is always a parameter</b> (WHERE TenantId = @TenantId), from the session,
/// never from the browser. Row-Level Security filters on the same tenant as a safety net, but
/// these queries don't rely on it.</item>
/// <item><b>Deleted rows are excluded by every query</b>, updates included: AND DeletedAt IS NULL,
/// written out each time rather than hidden in a view. The one exception is the demo row cap,
/// which counts deleted rows on purpose.</item>
/// <item><b>Updates and deletes carry the RowVersion</b> the client read. Zero rows changed means
/// the row is gone (404) or someone else changed it first (409, with the current row).</item>
/// <item><b>Who did it</b> (CreatedBy, UpdatedBy, DeletedBy) is the signed-in user, never a field
/// from the request.</item>
/// </list>
/// </remarks>
public sealed class CustomerStore
{
  public const int PageSize = 25;

  // The search goes into LIKE as a prefix, so its wildcards must be taken literally. This is the
  // escape character the queries name (ESCAPE N'\').
  private const char LikeEscape = '\\';

  // The two WHERE clauses the list can use, fixed here. Picking one in code (rather than writing
  // "@Prefix IS NULL OR ...") gives each its own plan, and the search one can use the indexes.
  private const string AllLiveRows = """
    WHERE c.TenantId = @TenantId
      AND c.DeletedAt IS NULL
    """;

  private const string MatchingLiveRows = """
    WHERE c.TenantId = @TenantId
      AND c.DeletedAt IS NULL
      AND (c.Name LIKE @Prefix ESCAPE N'\' OR c.Email LIKE @Prefix ESCAPE N'\')
    """;

  private readonly SqlConnectionFactory _connectionFactory;
  private readonly DemoOptions _demoOptions;

  public CustomerStore(SqlConnectionFactory connectionFactory, IOptions<DemoOptions> demoOptions)
  {
    _connectionFactory = connectionFactory;
    _demoOptions = demoOptions.Value;
  }

  /// <summary>One page of live customers, sorted by name then id. <paramref name="search"/> matches the start of the name or email.</summary>
  public async Task<CustomerListResponse> ListAsync(int tenantId, string? search, int page, CancellationToken cancellationToken)
  {
    string where = search is null ? AllLiveRows : MatchingLiveRows;
    string sql = $"""
      SELECT COUNT(*)
      FROM dbo.Customers AS c
      {where};

      SELECT c.Id, c.Name, c.Email, c.Phone, c.Status
      FROM dbo.Customers AS c
      {where}
      ORDER BY c.Name, c.Id
      OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
      """;

    object parameters = new
    {
      TenantId = tenantId,
      Prefix = search is null ? null : EscapeLike(search) + "%",
      Offset = (page - 1) * PageSize,
      PageSize,
    };

    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    await using SqlMapper.GridReader results = await connection.QueryMultipleAsync(
      new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
    int total = await results.ReadSingleAsync<int>();
    List<CustomerListItem> items = (await results.ReadAsync<CustomerListItem>()).ToList();
    return new CustomerListResponse(items, total, page, PageSize);
  }

  /// <summary>A live customer, or null if there's none with this id in this tenant (or it's deleted).</summary>
  public async Task<CustomerResponse?> GetAsync(int tenantId, int id, CancellationToken cancellationToken)
  {
    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    return await GetAsync(connection, null, tenantId, id, cancellationToken);
  }

  /// <summary>
  /// Adds a customer. In a demo tenant, refuses once it has <see cref="DemoOptions.MaxCustomersPerTenant"/>
  /// rows, deleted ones included, so adding and deleting in a loop can't grow the table.
  /// </summary>
  public async Task<CustomerCreated> CreateAsync(int tenantId, int userId, ValidCustomer customer, CancellationToken cancellationToken)
  {
    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

    if (await IsCappedAsync(connection, transaction, tenantId, cancellationToken))
    {
      return new CustomerCreated(null, CapReached: true);
    }

    const string insertSql = """
      INSERT INTO dbo.Customers (TenantId, Name, Email, Phone, Status, Notes, CreatedBy, UpdatedBy)
      OUTPUT inserted.Id
      VALUES (@TenantId, @Name, @Email, @Phone, @Status, @Notes, @UserId, @UserId);
      """;
    int id = await connection.QuerySingleAsync<int>(new CommandDefinition(
      insertSql,
      new { TenantId = tenantId, UserId = userId, customer.Name, customer.Email, customer.Phone, customer.Status, customer.Notes },
      transaction,
      cancellationToken: cancellationToken));

    CustomerResponse created = (await GetAsync(connection, transaction, tenantId, id, cancellationToken))!;
    await transaction.CommitAsync(cancellationToken);
    return new CustomerCreated(created, CapReached: false);
  }

  /// <summary>Changes a live customer, if it's still at <paramref name="rowVersion"/>.</summary>
  public async Task<CustomerChange> UpdateAsync(
    int tenantId, int userId, int id, byte[] rowVersion, ValidCustomer customer, CancellationToken cancellationToken)
  {
    const string sql = """
      UPDATE dbo.Customers
      SET
        Name = @Name,
        Email = @Email,
        Phone = @Phone,
        Status = @Status,
        Notes = @Notes,
        UpdatedAt = SYSUTCDATETIME(),
        UpdatedBy = @UserId
      OUTPUT inserted.RowVersion
      WHERE Id = @Id
        AND TenantId = @TenantId
        AND RowVersion = @RowVersion
        AND DeletedAt IS NULL;
      """;

    object parameters = new
    {
      Id = id,
      TenantId = tenantId,
      UserId = userId,
      RowVersion = rowVersion,
      customer.Name,
      customer.Email,
      customer.Phone,
      customer.Status,
      customer.Notes,
    };
    return await ChangeAsync(tenantId, id, sql, parameters, cancellationToken);
  }

  /// <summary>
  /// Soft-deletes a live customer, if it's still at <paramref name="rowVersion"/>: sets DeletedAt
  /// and DeletedBy. The row stays (for the audit log step); every query leaves it out from now on.
  /// </summary>
  public async Task<CustomerChange> DeleteAsync(int tenantId, int userId, int id, byte[] rowVersion, CancellationToken cancellationToken)
  {
    const string sql = """
      UPDATE dbo.Customers
      SET
        DeletedAt = SYSUTCDATETIME(),
        DeletedBy = @UserId
      OUTPUT inserted.RowVersion
      WHERE Id = @Id
        AND TenantId = @TenantId
        AND RowVersion = @RowVersion
        AND DeletedAt IS NULL;
      """;

    return await ChangeAsync(tenantId, id, sql, new { Id = id, TenantId = tenantId, UserId = userId, RowVersion = rowVersion }, cancellationToken);
  }

  /// <summary>
  /// Runs an UPDATE that carries the RowVersion, then reads the row back in the same transaction
  /// (the UPDATE's lock keeps anyone else from changing it in between). Zero rows updated: is it
  /// gone (NotFound), or did someone else change it (Conflict, with the row as it is now)?
  /// </summary>
  private async Task<CustomerChange> ChangeAsync(int tenantId, int id, string updateSql, object parameters, CancellationToken cancellationToken)
  {
    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

    byte[]? newRowVersion = await connection.QuerySingleOrDefaultAsync<byte[]?>(
      new CommandDefinition(updateSql, parameters, transaction, cancellationToken: cancellationToken));

    // The changed row, or the live row that stopped the change. Null: it's gone, or deleted.
    CustomerResponse? current = await GetAsync(connection, transaction, tenantId, id, cancellationToken);
    await transaction.CommitAsync(cancellationToken);

    if (newRowVersion is not null)
    {
      return new CustomerChange(CustomerChangeResult.Changed, current);
    }

    return current is null
      ? new CustomerChange(CustomerChangeResult.NotFound, null)
      : new CustomerChange(CustomerChangeResult.Conflict, current);
  }

  private static async Task<CustomerResponse?> GetAsync(
    SqlConnection connection, SqlTransaction? transaction, int tenantId, int id, CancellationToken cancellationToken)
  {
    const string sql = """
      SELECT
        c.Id,
        c.Name,
        c.Email,
        c.Phone,
        c.Status,
        c.Notes,
        c.CreatedAt,
        c.UpdatedAt,
        u.DisplayName AS UpdatedByName,
        c.RowVersion
      FROM dbo.Customers AS c
      LEFT JOIN dbo.Users AS u ON u.Id = c.UpdatedBy
      WHERE c.Id = @Id
        AND c.TenantId = @TenantId
        AND c.DeletedAt IS NULL;
      """;

    return await connection.QuerySingleOrDefaultAsync<CustomerResponse>(
      new CommandDefinition(sql, new { Id = id, TenantId = tenantId }, transaction, cancellationToken: cancellationToken));
  }

  /// <summary>
  /// A demo tenant at its row cap? Only demo tenants have one. Counted under a lock on the tenant
  /// row, held until the insert commits, so two requests at once can't both take the last place
  /// (the same lock Memberships uses).
  /// </summary>
  private async Task<bool> IsCappedAsync(SqlConnection connection, SqlTransaction transaction, int tenantId, CancellationToken cancellationToken)
  {
    // IsDemo never changes on a tenant, so real tenants skip the lock: their inserts don't wait on each other.
    const string isDemoSql = "SELECT IsDemo FROM dbo.Tenants WHERE Id = @TenantId;";
    bool isDemo = await connection.ExecuteScalarAsync<bool>(
      new CommandDefinition(isDemoSql, new { TenantId = tenantId }, transaction, cancellationToken: cancellationToken));
    if (!isDemo)
    {
      return false;
    }

    // Deleted rows count (no DeletedAt filter, on purpose): they still take up space.
    const string countSql = """
      SELECT Id FROM dbo.Tenants WITH (UPDLOCK, HOLDLOCK) WHERE Id = @TenantId;

      SELECT COUNT(*)
      FROM dbo.Customers
      WHERE TenantId = @TenantId;
      """;
    await using SqlMapper.GridReader results = await connection.QueryMultipleAsync(
      new CommandDefinition(countSql, new { TenantId = tenantId }, transaction, cancellationToken: cancellationToken));
    await results.ReadAsync<int>();
    int rows = await results.ReadSingleAsync<int>();
    return rows >= _demoOptions.MaxCustomersPerTenant;
  }

  /// <summary>Makes LIKE's wildcards (%, _, [) and the escape character itself match only themselves.</summary>
  public static string EscapeLike(string value)
  {
    return value
      .Replace(LikeEscape.ToString(), $"{LikeEscape}{LikeEscape}")
      .Replace("%", $"{LikeEscape}%")
      .Replace("_", $"{LikeEscape}_")
      .Replace("[", $"{LikeEscape}[");
  }
}

/// <param name="Customer">The new customer, or null when <paramref name="CapReached"/>.</param>
public sealed record CustomerCreated(CustomerResponse? Customer, bool CapReached);

public enum CustomerChangeResult
{
  Changed,
  NotFound,
  Conflict,
}

/// <param name="Customer">After a change, the changed customer; after a conflict, the customer as it is now.</param>
public sealed record CustomerChange(CustomerChangeResult Result, CustomerResponse? Customer);
