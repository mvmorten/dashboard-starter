using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DashboardStarter.Db.Migrations;
using DashboardStarter.Db.Scripts;
using Microsoft.Data.SqlClient;

namespace DashboardStarter.Db;

/// <summary>
/// <c>seed</c>: loads development data from /sql/seed. Refuses to run outside Development,
/// so demo tenants and users can never end up in a real database by accident.
/// </summary>
internal static class SeedCommand
{
  public static async Task<int> RunAsync(RunnerSettings settings)
  {
    if (!settings.IsDevelopment)
    {
      Console.Error.WriteLine(
        $"seed only runs in Development. DOTNET_ENVIRONMENT is '{settings.EnvironmentName}'. Seed data is demo data, not for real databases.");
      return 1;
    }

    Console.WriteLine($"Seeding {settings.DatabaseName} on {settings.ServerName} (environment: {settings.EnvironmentName})");
    Console.WriteLine();

    await using SqlConnection connection = await settings.OpenAsync();
    IReadOnlyList<AppliedMigration> applied = await SchemaJournal.ReadAsync(connection);
    SchemaStatus status = SchemaStatus.Compare(ScriptCatalog.Migrations, applied);
    if (status.Pending.Count > 0 || status.Changed.Count > 0)
    {
      Console.Error.WriteLine("The schema isn't current. Run this first: dotnet run --project src/Db -- migrate");
      return 1;
    }

    Console.WriteLine("Seed data (sql/seed):");
    foreach (SqlScript script in ScriptCatalog.Seed)
    {
      string? failure = await ScriptExecutor.TryRunAsync(connection, script, recordInJournal: false);
      if (failure is not null)
      {
        ScriptExecutor.PrintResult("failed", script.Name, failure);
        return 1;
      }

      ScriptExecutor.PrintResult("applied", script.Name);
    }

    Console.WriteLine();
    Console.WriteLine($"Done. Seed: {ScriptCatalog.Seed.Count} applied.");
    return 0;
  }
}
