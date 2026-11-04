using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DashboardStarter.Db.Migrations;
using DashboardStarter.Db.Scripts;
using Microsoft.Data.SqlClient;

namespace DashboardStarter.Db;

/// <summary>
/// Every command except migrate needs the schema to be current before it touches data.
/// </summary>
internal static class SchemaGuard
{
  public static async Task EnsureCurrentAsync(SqlConnection connection)
  {
    IReadOnlyList<AppliedMigration> applied = await SchemaJournal.ReadAsync(connection);
    SchemaStatus status = SchemaStatus.Compare(ScriptCatalog.Migrations, applied);
    if (status.Pending.Count > 0 || status.Changed.Count > 0)
    {
      throw new RunnerException("The schema isn't current. Run this first: dotnet run --project src/Db -- migrate");
    }
  }

  public static void WriteHeader(string action, RunnerSettings settings)
  {
    Console.WriteLine($"{action} in {settings.DatabaseName} on {settings.ServerName} (environment: {settings.EnvironmentName})");
    Console.WriteLine();
  }
}
