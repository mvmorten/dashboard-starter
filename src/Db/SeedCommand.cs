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
/// <c>seed</c>: loads development data from /sql/seed, then creates or updates the demo account
/// with the local password. Refuses to run outside Development, so sample data and a published
/// password can never end up in a real database by accident.
/// </summary>
/// <remarks>
/// The demo account comes from <see cref="DemoAccountSetup"/>, the same code the <c>demo</c>
/// command runs, so locally and deployed it's the same account: same email, same tenants, same
/// memberships. Only the password differs. Before step 7 the seed made it in SQL, and the two
/// drifted apart (the seed's copy wasn't flagged IsDemo).
/// </remarks>
internal static class SeedCommand
{
  // Published in the README and on the local sign-in page (src/Api/appsettings.Development.json),
  // like the local SQL Server password. Fine for a laptop: seed refuses to run anywhere else.
  public const string LocalDemoPassword = "LocalDevOnly!Demo1";

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
    Console.WriteLine($"Demo account {DemoAccountSetup.DefaultEmail}:");
    IPasswordHasher hasher = settings.CreatePasswordHasher();
    await using (SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync())
    {
      await DemoAccountSetup.EnsureAsync(connection, transaction, DemoAccountSetup.DefaultEmail, LocalDemoPassword, hasher);
      await transaction.CommitAsync();
    }

    Console.WriteLine();
    Console.WriteLine($"Done. Seed: {ScriptCatalog.Seed.Count} applied, demo account up to date.");
    return 0;
  }
}
