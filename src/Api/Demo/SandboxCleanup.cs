using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DashboardStarter.Api.Database;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DashboardStarter.Api.Demo;

/// <summary>
/// One cleanup run: deletes every expired demo tenant and demo user, and every row they own.
/// <see cref="SandboxCleanupService"/> runs it on a timer.
/// </summary>
/// <remarks>
/// <para>
/// Only demo rows whose ExpiresAt has passed are deleted. A real tenant or user can't have an
/// ExpiresAt (CK_Tenants_ExpiresAt, CK_Users_ExpiresAt), so they can't be removed here.
/// </para>
/// <para>
/// <b>Adding a table?</b> If it has a TenantId column, or a foreign key to Users or Tenants,
/// add it to <see cref="TenantRows"/> or <see cref="UserRows"/>, children before parents.
/// Otherwise deleting a sandbox would hit a foreign key error or leave rows behind. With
/// sandboxes on, the API refuses to start while a table is missing (<see cref="CheckCoverageAsync"/>),
/// and CI proves that.
/// </para>
/// </remarks>
public sealed class SandboxCleanup
{
  /// <summary>
  /// Rows deleted with their tenant, by its id, on a connection with that tenant in
  /// SESSION_CONTEXT, so Row-Level Security sees them. In order: children first. A table here
  /// is covered entirely, including any of its own foreign keys to Users.
  /// </summary>
  public static IReadOnlyList<OwnedRows> TenantRows { get; } =
  [
    new("dbo.Customers", "TenantId"),
    new("dbo.TenantUsers", "TenantId"),
  ];

  /// <summary>
  /// Rows deleted with their user, by the user's id, for tables that aren't tenant-owned.
  /// Memberships aren't here: they go with their tenant, and a user is only removed once none
  /// are left.
  /// </summary>
  public static IReadOnlyList<OwnedRows> UserRows { get; } =
  [
    new("dbo.PasswordResetTokens", "UserId"),
  ];

  // Only one instance cleans at a time: App Service can run several, and each runs the service.
  private const string LockName = "DemoSandboxCleanup";

  private readonly SqlConnectionFactory _connectionFactory;
  private readonly ILogger<SandboxCleanup> _logger;

  public SandboxCleanup(SqlConnectionFactory connectionFactory, ILogger<SandboxCleanup> logger)
  {
    _connectionFactory = connectionFactory;
    _logger = logger;
  }

  public async Task<CleanupResult> RunAsync(CancellationToken cancellationToken)
  {
    Stopwatch stopwatch = Stopwatch.StartNew();

    // A session-level app lock, held by this connection for the whole run. Another instance
    // asking at the same time gets a negative result at once (timeout 0) and skips its run.
    await using SqlConnection lockConnection = await _connectionFactory.OpenAsync(cancellationToken);
    const string lockSql = """
      DECLARE @result int;
      EXEC @result = sys.sp_getapplock
        @Resource = @LockName, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0;
      SELECT @result;
      """;
    int lockResult = await lockConnection.ExecuteScalarAsync<int>(
      new CommandDefinition(lockSql, new { LockName }, cancellationToken: cancellationToken));
    if (lockResult < 0)
    {
      _logger.LogInformation("Demo cleanup skipped: another instance is running it.");
      return new CleanupResult(Ran: false, 0, 0, 0);
    }

    try
    {
      int rows = 0;
      int tenants = 0;
      foreach (int tenantId in await ExpiredIdsAsync(lockConnection, "dbo.Tenants", cancellationToken))
      {
        int? removed = await DeleteTenantAsync(tenantId, cancellationToken);
        if (removed is not null)
        {
          tenants++;
          rows += removed.Value;
        }
      }

      int users = 0;
      foreach (int userId in await ExpiredIdsAsync(lockConnection, "dbo.Users", cancellationToken))
      {
        int? removed = await DeleteUserAsync(userId, cancellationToken);
        if (removed is not null)
        {
          users++;
          rows += removed.Value;
        }
      }

      // The dashboard home step can show background job status from here (last run, what it
      // removed). For now, each run is a log line.
      _logger.LogInformation(
        "Demo cleanup removed {Tenants} tenant(s), {Users} user(s) and {Rows} other row(s) in {ElapsedMs} ms.",
        tenants,
        users,
        rows,
        stopwatch.ElapsedMilliseconds);
      return new CleanupResult(Ran: true, tenants, users, rows);
    }
    finally
    {
      const string releaseSql = "EXEC sys.sp_releaseapplock @Resource = @LockName, @LockOwner = 'Session';";
      await lockConnection.ExecuteAsync(new CommandDefinition(releaseSql, new { LockName }, cancellationToken: CancellationToken.None));
    }
  }

  /// <summary>
  /// With sandboxes on, the API only starts if every table that can hold a sandbox's rows is
  /// in <see cref="TenantRows"/> or <see cref="UserRows"/>: any table with a TenantId column,
  /// and any column with a foreign key to Users or Tenants. Returns true if the API can start.
  /// </summary>
  public async Task<bool> CheckCoverageAsync()
  {
    // Every column that ties a row to a tenant or a user. The API's login sees the metadata
    // of the tables it can use, which is every table in dbo.
    const string sql = """
      SELECT s.name + N'.' + t.name AS TableName, c.name AS ColumnName
      FROM sys.tables AS t
      INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
      INNER JOIN sys.columns AS c ON c.object_id = t.object_id
      WHERE c.name = N'TenantId'
      UNION
      SELECT s.name + N'.' + t.name, c.name
      FROM sys.foreign_key_columns AS fkc
      INNER JOIN sys.tables AS t ON t.object_id = fkc.parent_object_id
      INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
      INNER JOIN sys.columns AS c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
      WHERE fkc.referenced_object_id IN (OBJECT_ID(N'dbo.Users'), OBJECT_ID(N'dbo.Tenants'));
      """;

    await using SqlConnection connection = await _connectionFactory.OpenAsync(CancellationToken.None);
    IEnumerable<(string TableName, string ColumnName)> columns = await connection.QueryAsync<(string, string)>(sql);

    HashSet<string> tenantTables = TenantRows.Select(rows => rows.Table).ToHashSet(StringComparer.OrdinalIgnoreCase);
    HashSet<string> userColumns = UserRows.Select(rows => rows.Table + "." + rows.Column).ToHashSet(StringComparer.OrdinalIgnoreCase);
    List<string> missing = columns
      .Where(column => !tenantTables.Contains(column.TableName) && !userColumns.Contains(column.TableName + "." + column.ColumnName))
      .Select(column => column.TableName + "." + column.ColumnName)
      .Order(StringComparer.Ordinal)
      .ToList();

    if (missing.Count > 0)
    {
      _logger.LogCritical(
        "Demo sandboxes are on, but the cleanup doesn't know these columns, so deleting a sandbox would fail or leave rows behind: {Columns}. "
          + "Add their tables to SandboxCleanup.TenantRows or SandboxCleanup.UserRows (src/Api/Demo/SandboxCleanup.cs).",
        string.Join(", ", missing));
      return false;
    }

    return true;
  }

  // Ids of expired demo rows in dbo.Tenants or dbo.Users (the only two values passed in).
  private static async Task<IReadOnlyList<int>> ExpiredIdsAsync(SqlConnection connection, string table, CancellationToken cancellationToken)
  {
    string sql = $"""
      SELECT Id
      FROM {table}
      WHERE IsDemo = 1
        AND ExpiresAt <= SYSUTCDATETIME();
      """;

    IEnumerable<int> ids = await connection.QueryAsync<int>(new CommandDefinition(sql, cancellationToken: cancellationToken));
    return ids.ToList();
  }

  /// <summary>Deletes one expired tenant and its rows. Returns how many rows it owned, or null if it's no longer expired.</summary>
  private async Task<int?> DeleteTenantAsync(int tenantId, CancellationToken cancellationToken)
  {
    await using SqlConnection connection = await _connectionFactory.OpenForTenantAsync(tenantId, cancellationToken);
    await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

    // Lock the row and check again: someone may have used the sandbox since the list was read.
    const string checkSql = """
      SELECT COUNT(*)
      FROM dbo.Tenants WITH (UPDLOCK, HOLDLOCK)
      WHERE Id = @Id
        AND IsDemo = 1
        AND ExpiresAt <= SYSUTCDATETIME();
      """;
    if (await connection.ExecuteScalarAsync<int>(Command(checkSql, tenantId, transaction, cancellationToken)) == 0)
    {
      return null;
    }

    int rows = 0;
    foreach (OwnedRows owned in TenantRows)
    {
      rows += await connection.ExecuteAsync(Command(owned.DeleteSql, tenantId, transaction, cancellationToken));
    }

    await connection.ExecuteAsync(Command("DELETE FROM dbo.Tenants WHERE Id = @Id;", tenantId, transaction, cancellationToken));
    await transaction.CommitAsync(cancellationToken);
    return rows;
  }

  /// <summary>Deletes one expired user and its rows. Returns how many rows it owned, or null if it was kept.</summary>
  private async Task<int?> DeleteUserAsync(int userId, CancellationToken cancellationToken)
  {
    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

    const string checkSql = """
      SELECT
        CAST(CASE WHEN ExpiresAt <= SYSUTCDATETIME() THEN 1 ELSE 0 END AS bit) AS IsExpired,
        CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.TenantUsers AS tu WHERE tu.UserId = u.Id) THEN 1 ELSE 0 END AS bit) AS HasMemberships
      FROM dbo.Users AS u WITH (UPDLOCK, HOLDLOCK)
      WHERE u.Id = @Id
        AND u.IsDemo = 1;
      """;
    ExpiredUserState? state = await connection.QuerySingleOrDefaultAsync<ExpiredUserState>(
      Command(checkSql, userId, transaction, cancellationToken));
    if (state is not { IsExpired: true })
    {
      return null;
    }

    // Its sandbox tenants went first. A membership left over is in a tenant that isn't expired,
    // or isn't a demo tenant at all, and cleanup never touches those.
    if (state.HasMemberships)
    {
      _logger.LogWarning("Demo cleanup kept expired demo user {UserId}: they still belong to a tenant that isn't being removed.", userId);
      return null;
    }

    int rows = 0;
    foreach (OwnedRows owned in UserRows)
    {
      rows += await connection.ExecuteAsync(Command(owned.DeleteSql, userId, transaction, cancellationToken));
    }

    await connection.ExecuteAsync(Command("DELETE FROM dbo.Users WHERE Id = @Id;", userId, transaction, cancellationToken));
    await transaction.CommitAsync(cancellationToken);
    return rows;
  }

  private static CommandDefinition Command(string sql, int id, SqlTransaction transaction, CancellationToken cancellationToken)
  {
    return new CommandDefinition(sql, new { Id = id }, transaction, cancellationToken: cancellationToken);
  }
}

/// <summary>A table whose rows belong to a tenant or a user, found by one column.</summary>
/// <remarks>
/// Table and column are names written in this file, never values from outside, so building the
/// statement from them is safe: the id is still a parameter.
/// </remarks>
public sealed record OwnedRows(string Table, string Column)
{
  public string DeleteSql => $"DELETE FROM {Table} WHERE {Column} = @Id;";
}

internal sealed record ExpiredUserState(bool IsExpired, bool HasMemberships);

/// <param name="Ran">False when another instance held the lock, so this run did nothing.</param>
public sealed record CleanupResult(bool Ran, int Tenants, int Users, int Rows);
