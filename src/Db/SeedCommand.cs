using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DashboardStarter.Db.Migrations;
using DashboardStarter.Db.Passwords;
using DashboardStarter.Db.Scripts;
using DashboardStarter.Db.Users;
using Microsoft.Data.SqlClient;

namespace DashboardStarter.Db;

/// <summary>
/// <c>seed</c>: loads development data from /sql/seed, then creates or updates the local account
/// (<see cref="LocalAccount"/>, admin@example.com). Refuses to run outside Development, so sample
/// data and a published password can never end up in a real database by accident.
/// </summary>
/// <remarks>
/// There's no demo account here any more: "Try the demo" makes a sandbox on the fly, locally
/// as on the live demo (src/Api/Demo).
/// </remarks>
internal static class SeedCommand
{
  public static async Task<int> RunAsync(RunnerSettings settings)
  {
    if (!settings.IsDevelopment)
    {
      Console.Error.WriteLine(
        $"seed only runs in Development. DOTNET_ENVIRONMENT is '{settings.EnvironmentName}'. Seed data is sample data, not for real databases.");
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
      ScriptResult result = await ScriptExecutor.TryRunAsync(connection, script, recordInJournal: false);
      if (result.Failure is not null)
      {
        ScriptExecutor.PrintResult("failed", script.Name, result.Failure);
        return 1;
      }

      ScriptExecutor.PrintResult("applied", script.Name, result.Detail);
    }

    Console.WriteLine();
    Console.WriteLine($"Local account {LocalAccount.Email}:");
    IPasswordHasher hasher = settings.CreatePasswordHasher();
    await using (SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync())
    {
      await LocalAccount.EnsureAsync(connection, transaction, hasher);
      await transaction.CommitAsync();
    }

    Console.WriteLine();
    Console.WriteLine($"Done. Seed: {ScriptCatalog.Seed.Count} applied, local account up to date.");
    return 0;
  }
}
