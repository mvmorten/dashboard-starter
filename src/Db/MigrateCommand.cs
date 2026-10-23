using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DashboardStarter.Db.Migrations;
using DashboardStarter.Db.Scripts;
using Dapper;
using Microsoft.Data.SqlClient;

namespace DashboardStarter.Db;

/// <summary>
/// <c>migrate</c>: creates the database if it's missing, applies new migrations in order,
/// then reruns every programmable script.
/// </summary>
internal static class MigrateCommand
{
  public static async Task<int> RunAsync(RunnerSettings settings)
  {
    Console.WriteLine($"Migrating {settings.DatabaseName} on {settings.ServerName} (environment: {settings.EnvironmentName})");
    Console.WriteLine();

    bool created = await EnsureDatabaseExistsAsync(settings);
    ScriptExecutor.PrintResult(created ? "created" : "exists", "database " + settings.DatabaseName);

    await using SqlConnection connection = await settings.OpenAsync();
    await SchemaJournal.EnsureCreatedAsync(connection);

    IReadOnlyList<AppliedMigration> applied = await SchemaJournal.ReadAsync(connection);
    SchemaStatus status = SchemaStatus.Compare(ScriptCatalog.Migrations, applied);

    // Check everything before changing anything: an edited or unknown migration
    // means this database and this code disagree about history.
    if (status.Changed.Count > 0 || status.Unknown.Count > 0)
    {
      PrintHistoryProblems(status);
      return 1;
    }

    Console.WriteLine();
    Console.WriteLine("Migrations (sql/migrations):");
    HashSet<string> pending = status.Pending.Select(script => script.Name).ToHashSet(StringComparer.Ordinal);
    int appliedCount = 0;
    int skippedCount = 0;

    foreach (SqlScript script in ScriptCatalog.Migrations)
    {
      if (!pending.Contains(script.Name))
      {
        ScriptExecutor.PrintResult("skipped", script.Name, "already applied");
        skippedCount++;
        continue;
      }

      string? failure = await ScriptExecutor.TryRunAsync(connection, script, recordInJournal: true);
      if (failure is not null)
      {
        ScriptExecutor.PrintResult("failed", script.Name, failure);
        Console.WriteLine();
        Console.WriteLine($"Stopped. Nothing from {script.Name} was applied; migrations before it were.");
        return 1;
      }

      ScriptExecutor.PrintResult("applied", script.Name);
      appliedCount++;
    }

    Console.WriteLine();
    Console.WriteLine("Programmable objects (sql/programmable, rerun every time):");
    int programmableCount = 0;

    foreach (SqlScript script in ScriptCatalog.Programmable)
    {
      string? failure = await ScriptExecutor.TryRunAsync(connection, script, recordInJournal: false);
      if (failure is not null)
      {
        ScriptExecutor.PrintResult("failed", script.Name, failure);
        return 1;
      }

      ScriptExecutor.PrintResult("applied", script.Name);
      programmableCount++;
    }

    Console.WriteLine();
    Console.WriteLine(
      $"Done. Migrations: {appliedCount} applied, {skippedCount} skipped. Programmable: {programmableCount} applied.");
    return 0;
  }

  private static async Task<bool> EnsureDatabaseExistsAsync(RunnerSettings settings)
  {
    // CREATE DATABASE doesn't accept a parameter for the name, so the statement is built
    // on the server, where QUOTENAME turns the name into a safely quoted identifier.
    //
    // sys.databases, not DB_ID(): on Azure SQL, DB_ID() run from master returns NULL for
    // user databases, so the check said "missing" and CREATE DATABASE then failed with
    // "already exists". sys.databases in master lists every database on both Azure SQL
    // and SQL Server.
    const string sql = """
      IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = @DatabaseName)
      BEGIN
        DECLARE @createSql nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@DatabaseName) + N';';
        EXEC sys.sp_executesql @createSql;
        SELECT CAST(1 AS bit);
      END
      ELSE
      BEGIN
        SELECT CAST(0 AS bit);
      END
      """;

    await using SqlConnection master = await settings.OpenMasterAsync();
    return await master.ExecuteScalarAsync<bool>(sql, new { settings.DatabaseName });
  }

  private static void PrintHistoryProblems(SchemaStatus status)
  {
    Console.WriteLine();
    Console.WriteLine("Migrations (sql/migrations):");

    foreach (ChangedMigration changed in status.Changed)
    {
      ScriptExecutor.PrintResult("failed", changed.ScriptName, "edited after it was applied");
      Console.WriteLine($"           applied hash: {changed.AppliedHash}");
      Console.WriteLine($"           file hash:    {changed.CurrentHash}");
    }

    foreach (string name in status.Unknown)
    {
      ScriptExecutor.PrintResult("failed", name, "applied to this database, but not in sql/migrations");
    }

    Console.WriteLine();
    if (status.Changed.Count > 0)
    {
      Console.WriteLine("A migration must never change after it has run. Restore the original file,");
      Console.WriteLine("and put the change in a new migration with the next number.");
    }

    if (status.Unknown.Count > 0)
    {
      Console.WriteLine("This database has migrations this code doesn't know about. Are you on an older branch?");
    }

    Console.WriteLine("Nothing was applied.");
  }
}
