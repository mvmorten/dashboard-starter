using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DashboardStarter.Api.Database;
using DashboardStarter.Api.Demo;
using DashboardStarter.Db.Audit;
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
/// written out each time rather than hidden in a view. The exceptions are the queries that are
/// about deleted rows, each named for it: the Deleted list (<see cref="ListAsync"/> with
/// <see cref="CustomerListQuery.Deleted"/>), <see cref="GetDeletedAsync"/>, <see cref="RestoreAsync"/>,
/// <see cref="ExistsIncludingDeletedAsync"/> (a deleted customer still has a history), and the
/// demo row cap, which counts deleted rows on purpose.</item>
/// <item><b>Updates and deletes carry the RowVersion</b> the client read. Zero rows changed means
/// the row is gone (404) or someone else changed it first (409, with the current row).</item>
/// <item><b>Who did it</b> (CreatedBy, UpdatedBy, DeletedBy) is the signed-in user, never a field
/// from the request.</item>
/// <item><b>Every change writes an audit entry</b> (<see cref="AuditLog"/>) in the same
/// transaction, with the fields <see cref="CustomerAudit"/> lists. A save that changes nothing
/// writes nothing at all, and neither does a refused one (404, 409).</item>
/// </list>
/// </remarks>
public sealed class CustomerStore
{
  public const int PageSize = 25;

  // The search goes into LIKE as a prefix, so its wildcards must be taken literally. This is the
  // escape character the queries name (ESCAPE N'\').
  private const char LikeEscape = '\\';

  /// <summary>
  /// The columns the list can sort by: the API's <c>sort</c> value, and the SQL it becomes. This
  /// is the only way a sort reaches the query. The request's text picks a key here and is never
  /// put into SQL itself; a key that isn't here is a 400 (the controller checks).
  /// </summary>
  public static readonly IReadOnlyDictionary<string, string> SortColumns = new Dictionary<string, string>(StringComparer.Ordinal)
  {
    ["name"] = "c.Name",
    ["email"] = "c.Email",
    ["status"] = "c.Status",
    ["updated"] = "c.UpdatedAt",
    ["created"] = "c.CreatedAt",
  };

  // The pieces of the list's WHERE clause, fixed here. Adding one in code (rather than writing
  // "@Prefix IS NULL OR ...") gives each combination its own plan, and one that searches can use
  // the indexes.
  private const string LiveRows = """
    WHERE c.TenantId = @TenantId
      AND c.DeletedAt IS NULL
    """;

  private const string SearchMatches = """

      AND (c.Name LIKE @Prefix ESCAPE N'\' OR c.Email LIKE @Prefix ESCAPE N'\')
    """;

  private const string StatusMatches = """

      AND c.Status = @Status
    """;

  // The Deleted list's rows, instead of LiveRows: the one list that is about deleted customers.
  private const string DeletedRows = """
    WHERE c.TenantId = @TenantId
      AND c.DeletedAt IS NOT NULL
    """;

  private readonly SqlConnectionFactory _connectionFactory;
  private readonly DemoOptions _demoOptions;

  public CustomerStore(SqlConnectionFactory connectionFactory, IOptions<DemoOptions> demoOptions)
  {
    _connectionFactory = connectionFactory;
    _demoOptions = demoOptions.Value;
  }

  /// <summary>
  /// One page of live customers (or of deleted ones, when the query asks for those), in the
  /// query's sort, then by id in the same direction so rows that tie keep one order across pages.
  /// </summary>
  public async Task<CustomerListResponse> ListAsync(int tenantId, CustomerListQuery query, CancellationToken cancellationToken)
  {
    // Every piece below is a constant from this class: the WHERE pieces above, a column from
    // SortColumns, ASC or DESC. Values (the tenant, the search, the status, the page) are
    // parameters.
    string where = (query.Deleted ? DeletedRows : LiveRows)
      + (query.Search is null ? "" : SearchMatches)
      + (query.Status is null ? "" : StatusMatches);
    string column = SortColumns[query.Sort];
    string direction = query.Descending ? "DESC" : "ASC";
    string sql = $"""
      SELECT COUNT(*)
      FROM dbo.Customers AS c
      {where};

      SELECT c.Id, c.Name, c.Email, c.Phone, c.Status, c.CreatedAt, c.UpdatedAt, c.DeletedAt
      FROM dbo.Customers AS c
      {where}
      ORDER BY {column} {direction}, c.Id {direction}
      OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
      """;

    object parameters = new
    {
      TenantId = tenantId,
      Prefix = query.Search is null ? null : EscapeLike(query.Search) + "%",
      query.Status,
      Offset = (query.Page - 1) * PageSize,
      PageSize,
    };

    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    await using SqlMapper.GridReader results = await connection.QueryMultipleAsync(
      new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
    int total = await results.ReadSingleAsync<int>();
    List<CustomerListItem> items = (await results.ReadAsync<CustomerListItem>()).ToList();
    return new CustomerListResponse(items, total, query.Page, PageSize);
  }

  /// <summary>A live customer, or null if there's none with this id in this tenant (or it's deleted).</summary>
  public async Task<CustomerResponse?> GetAsync(int tenantId, int id, CancellationToken cancellationToken)
  {
    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    return await GetAsync(connection, null, tenantId, id, deleted: false, forUpdate: false, cancellationToken);
  }

  /// <summary>A deleted customer, or null if there's none with this id in this tenant (or it's live).</summary>
  public async Task<CustomerResponse?> GetDeletedAsync(int tenantId, int id, CancellationToken cancellationToken)
  {
    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    return await GetAsync(connection, null, tenantId, id, deleted: true, forUpdate: false, cancellationToken);
  }

  /// <summary>
  /// Is there a customer with this id in this tenant, live or deleted? For the history endpoint:
  /// a deleted customer's history is what you read before restoring it.
  /// </summary>
  public async Task<bool> ExistsIncludingDeletedAsync(int tenantId, int id, CancellationToken cancellationToken)
  {
    // No DeletedAt filter, on purpose.
    const string sql = """
      SELECT CAST(CASE WHEN EXISTS (
        SELECT 1
        FROM dbo.Customers
        WHERE Id = @Id
          AND TenantId = @TenantId
      ) THEN 1 ELSE 0 END AS bit);
      """;

    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    return await connection.ExecuteScalarAsync<bool>(
      new CommandDefinition(sql, new { Id = id, TenantId = tenantId }, cancellationToken: cancellationToken));
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

    await AuditLog.WriteAsync(
      connection, transaction, tenantId, userId, AuditLog.Customer, id, AuditLog.Created, CustomerAudit.Created(customer), cancellationToken);

    CustomerResponse created = (await GetAsync(connection, transaction, tenantId, id, deleted: false, forUpdate: false, cancellationToken))!;
    await transaction.CommitAsync(cancellationToken);
    return new CustomerCreated(created, CapReached: false);
  }

  /// <summary>
  /// Changes a live customer, if it's still at <paramref name="rowVersion"/>, and records which
  /// fields changed. A save with the values the customer already has is
  /// <see cref="CustomerChangeResult.Unchanged"/>: nothing is written, not even UpdatedAt, so
  /// "last changed" always matches the newest history entry.
  /// </summary>
  public async Task<CustomerChange> UpdateAsync(
    int tenantId, int userId, int id, byte[] rowVersion, ValidCustomer customer, CancellationToken cancellationToken)
  {
    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

    // The customer as it is now, locked until this transaction ends: the old values for the
    // audit entry, and nobody can change the row between this read and the UPDATE.
    CustomerResponse? before = await GetAsync(connection, transaction, tenantId, id, deleted: false, forUpdate: true, cancellationToken);
    if (before is null)
    {
      return new CustomerChange(CustomerChangeResult.NotFound, null);
    }

    if (!before.RowVersion.AsSpan().SequenceEqual(rowVersion))
    {
      return new CustomerChange(CustomerChangeResult.Conflict, before);
    }

    AuditChanges changes = CustomerAudit.Updated(before, customer);
    if (changes.IsEmpty)
    {
      return new CustomerChange(CustomerChangeResult.Unchanged, before);
    }

    // The RowVersion is in the WHERE all the same (the usual optimistic concurrency pattern):
    // the UPDATE is safe on its own, whatever the code above it does.
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
    int updated = await connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken));
    if (updated != 1)
    {
      throw new InvalidOperationException($"Customer {id} changed under its own lock.");
    }

    await AuditLog.WriteAsync(
      connection, transaction, tenantId, userId, AuditLog.Customer, id, AuditLog.Updated, changes, cancellationToken);

    CustomerResponse current = (await GetAsync(connection, transaction, tenantId, id, deleted: false, forUpdate: false, cancellationToken))!;
    await transaction.CommitAsync(cancellationToken);
    return new CustomerChange(CustomerChangeResult.Changed, current);
  }

  /// <summary>
  /// Soft-deletes a live customer, if it's still at <paramref name="rowVersion"/>: sets DeletedAt
  /// and DeletedBy. The row stays, so it can be restored; every query leaves it out from now on,
  /// except the ones about deleted customers.
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

    return await ChangeAsync(tenantId, userId, id, sql, AuditLog.Deleted, becomesDeleted: true, rowVersion, cancellationToken);
  }

  /// <summary>
  /// Brings a deleted customer back, if it's still at <paramref name="rowVersion"/>: clears
  /// DeletedAt and DeletedBy, and counts as a change (UpdatedAt, UpdatedBy). NotFound if it
  /// isn't deleted, or isn't there.
  /// </summary>
  /// <remarks>
  /// No demo cap check: the cap counts deleted rows already, so restoring adds none.
  /// </remarks>
  public async Task<CustomerChange> RestoreAsync(int tenantId, int userId, int id, byte[] rowVersion, CancellationToken cancellationToken)
  {
    const string sql = """
      UPDATE dbo.Customers
      SET
        DeletedAt = NULL,
        DeletedBy = NULL,
        UpdatedAt = SYSUTCDATETIME(),
        UpdatedBy = @UserId
      OUTPUT inserted.RowVersion
      WHERE Id = @Id
        AND TenantId = @TenantId
        AND RowVersion = @RowVersion
        AND DeletedAt IS NOT NULL;
      """;

    return await ChangeAsync(tenantId, userId, id, sql, AuditLog.Restored, becomesDeleted: false, rowVersion, cancellationToken);
  }

  /// <summary>
  /// Deletes or restores: runs an UPDATE that carries the RowVersion and, if it landed, writes
  /// the audit entry in the same transaction. Zero rows updated: is the row not there in the
  /// state the change needs (NotFound), or did someone else change it (Conflict, with the row as
  /// it is now)? The row is read back in the transaction either way.
  /// </summary>
  /// <param name="becomesDeleted">True for a delete (it works on a live row), false for a restore (on a deleted one).</param>
  private async Task<CustomerChange> ChangeAsync(
    int tenantId, int userId, int id, string updateSql, string auditAction, bool becomesDeleted, byte[] rowVersion, CancellationToken cancellationToken)
  {
    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

    byte[]? newRowVersion = await connection.QuerySingleOrDefaultAsync<byte[]?>(new CommandDefinition(
      updateSql, new { Id = id, TenantId = tenantId, UserId = userId, RowVersion = rowVersion }, transaction, cancellationToken: cancellationToken));

    if (newRowVersion is not null)
    {
      await AuditLog.WriteAsync(
        connection, transaction, tenantId, userId, AuditLog.Customer, id, auditAction, changes: null, cancellationToken);
      CustomerResponse? changed = await GetAsync(connection, transaction, tenantId, id, becomesDeleted, forUpdate: false, cancellationToken);
      await transaction.CommitAsync(cancellationToken);
      return new CustomerChange(CustomerChangeResult.Changed, changed);
    }

    // Nothing changed, so there's nothing to commit. The row in the state the change needed
    // (live for a delete, deleted for a restore) is the one that stopped it: someone else
    // changed it. No such row: it's gone, or already in the other state.
    CustomerResponse? current = await GetAsync(connection, transaction, tenantId, id, deleted: !becomesDeleted, forUpdate: false, cancellationToken);
    return current is null
      ? new CustomerChange(CustomerChangeResult.NotFound, null)
      : new CustomerChange(CustomerChangeResult.Conflict, current);
  }

  /// <summary>
  /// One customer of this tenant: a live one, or (<paramref name="deleted"/>) a deleted one,
  /// never either. <paramref name="forUpdate"/> locks the row until the transaction ends.
  /// </summary>
  private static async Task<CustomerResponse?> GetAsync(
    SqlConnection connection, SqlTransaction? transaction, int tenantId, int id, bool deleted, bool forUpdate, CancellationToken cancellationToken)
  {
    // Two fixed pieces, picked here, never text from a request. The DeletedAt test is a literal
    // in the query either way, so the filtered indexes match.
    string hint = forUpdate ? " WITH (UPDLOCK, ROWLOCK)" : "";
    string rows = deleted ? "c.DeletedAt IS NOT NULL" : "c.DeletedAt IS NULL";
    string sql = $"""
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
        c.DeletedAt,
        d.DisplayName AS DeletedByName,
        c.RowVersion
      FROM dbo.Customers AS c{hint}
      LEFT JOIN dbo.Users AS u ON u.Id = c.UpdatedBy
      LEFT JOIN dbo.Users AS d ON d.Id = c.DeletedBy
      WHERE c.Id = @Id
        AND c.TenantId = @TenantId
        AND {rows};
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

  /// <summary>A save with the values the customer already has. Nothing was written.</summary>
  Unchanged,
  NotFound,
  Conflict,
}

/// <param name="Customer">After a change (or a save that changed nothing), the customer; after a conflict, the customer as it is now.</param>
public sealed record CustomerChange(CustomerChangeResult Result, CustomerResponse? Customer);
