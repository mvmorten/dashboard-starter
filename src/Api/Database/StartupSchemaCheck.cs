using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DashboardStarter.Db.Migrations;
using DashboardStarter.Db.Scripts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DashboardStarter.Api.Database;

/// <summary>
/// Runs once at startup: is the database schema the one this build expects?
/// The API never changes the schema. If a migration is pending, it refuses to start
/// and says which command to run.
/// </summary>
public sealed class StartupSchemaCheck
{
  private const string MigrateCommand = "dotnet run --project src/Db -- migrate";

  // SQL Server error 4060: "Cannot open database requested by the login."
  private const int CannotOpenDatabase = 4060;

  private readonly SqlConnectionFactory _connectionFactory;
  private readonly ILogger<StartupSchemaCheck> _logger;

  public StartupSchemaCheck(SqlConnectionFactory connectionFactory, ILogger<StartupSchemaCheck> logger)
  {
    _connectionFactory = connectionFactory;
    _logger = logger;
  }

  /// <summary>Returns true if the API can start.</summary>
  public async Task<bool> RunAsync()
  {
    IReadOnlyList<AppliedMigration> applied;
    try
    {
      await using SqlConnection connection = await _connectionFactory.OpenAsync(CancellationToken.None);
      applied = await SchemaJournal.ReadAsync(connection);
    }
    catch (SqlException exception) when (exception.Number == CannotOpenDatabase)
    {
      _logger.LogCritical(
        "Database {Database} on {Server} doesn't exist or can't be opened. Create it with: {Command}",
        _connectionFactory.DatabaseName,
        _connectionFactory.ServerName,
        MigrateCommand);
      return false;
    }
    catch (SqlException exception)
    {
      _logger.LogCritical(
        "Can't connect to SQL Server at {Server}: {Error} Is it running? Start it with: docker compose up -d --wait",
        _connectionFactory.ServerName,
        exception.Message);
      return false;
    }

    SchemaStatus status = SchemaStatus.Compare(ScriptCatalog.Migrations, applied);

    if (status.Pending.Count > 0)
    {
      _logger.LogCritical(
        "The database schema is out of date: {Count} migration(s) pending ({Migrations}). "
          + "The API never changes the schema itself. Apply them with: {Command}",
        status.Pending.Count,
        string.Join(", ", status.Pending.Select(script => script.Name)),
        MigrateCommand);
      return false;
    }

    // Not fatal for the API: the schema has every migration this build needs.
    // The migrate command refuses to continue until these are sorted out.
    foreach (ChangedMigration changed in status.Changed)
    {
      _logger.LogWarning(
        "Migration {Migration} was edited after it was applied. Migrations must never change; put the change in a new one.",
        changed.ScriptName);
    }

    if (status.Unknown.Count > 0)
    {
      _logger.LogWarning(
        "The database has migrations this build doesn't know about: {Migrations}. Is this an older build?",
        string.Join(", ", status.Unknown));
    }

    _logger.LogInformation(
      "Database schema is current: {Count} migrations applied to {Database} on {Server}.",
      applied.Count,
      _connectionFactory.DatabaseName,
      _connectionFactory.ServerName);
    return true;
  }
}
