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
public static class StartupSchemaCheck
{
  private const string MigrateCommand = "dotnet run --project src/Db -- migrate";

  // SQL Server error 4060: "Cannot open database requested by the login."
  private const int CannotOpenDatabase = 4060;

  /// <summary>Returns true if the API can start.</summary>
  public static async Task<bool> RunAsync(SqlConnectionFactory connectionFactory, ILogger logger)
  {
    IReadOnlyList<AppliedMigration> applied;
    try
    {
      await using SqlConnection connection = await connectionFactory.OpenAsync(CancellationToken.None);
      applied = await SchemaJournal.ReadAsync(connection);
    }
    catch (SqlException exception) when (exception.Number == CannotOpenDatabase)
    {
      logger.LogCritical(
        "Database {Database} on {Server} doesn't exist or can't be opened. Create it with: {Command}",
        connectionFactory.DatabaseName,
        connectionFactory.ServerName,
        MigrateCommand);
      return false;
    }
    catch (SqlException exception)
    {
      logger.LogCritical(
        "Can't connect to SQL Server at {Server}: {Error} Is it running? Start it with: docker compose up -d --wait",
        connectionFactory.ServerName,
        exception.Message);
      return false;
    }

    SchemaStatus status = SchemaStatus.Compare(ScriptCatalog.Migrations, applied);

    if (status.Pending.Count > 0)
    {
      logger.LogCritical(
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
      logger.LogWarning(
        "Migration {Migration} was edited after it was applied. Migrations must never change; put the change in a new one.",
        changed.ScriptName);
    }

    if (status.Unknown.Count > 0)
    {
      logger.LogWarning(
        "The database has migrations this build doesn't know about: {Migrations}. Is this an older build?",
        string.Join(", ", status.Unknown));
    }

    logger.LogInformation(
      "Database schema is current: {Count} migrations applied to {Database} on {Server}.",
      applied.Count,
      connectionFactory.DatabaseName,
      connectionFactory.ServerName);
    return true;
  }
}
