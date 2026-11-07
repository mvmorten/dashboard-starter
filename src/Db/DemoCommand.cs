using System;
using System.Threading.Tasks;
using DashboardStarter.Db.Passwords;
using DashboardStarter.Db.Users;
using Microsoft.Data.SqlClient;

namespace DashboardStarter.Db;

/// <summary>
/// <c>demo</c>: the shared account for a public demo, with the published password from
/// DEMO_PASSWORD (and DEMO_EMAIL, default guest@example.com). Runs in any environment, unlike
/// seed; the deploy runs it when the repo variable DEMO_ENABLED is true. What the account is
/// (its tenants and memberships) is defined once, in <see cref="DemoAccountSetup"/>, which seed
/// uses too.
/// </summary>
internal static class DemoCommand
{
  public static async Task<int> RunAsync(RunnerSettings settings, string[] args)
  {
    CommandArguments.Parse(args, [], []);

    string? password = Environment.GetEnvironmentVariable("DEMO_PASSWORD");
    if (string.IsNullOrEmpty(password))
    {
      throw new RunnerException("Set DEMO_PASSWORD to the demo account's password. It's shown on the sign-in page, so it isn't a secret.");
    }

    string? configuredEmail = Environment.GetEnvironmentVariable("DEMO_EMAIL");
    string email = UserAccounts.NormalizeEmail(string.IsNullOrWhiteSpace(configuredEmail) ? DemoAccountSetup.DefaultEmail : configuredEmail);

    string? problem = PasswordRules.Check(password, email);
    if (problem is not null)
    {
      throw new RunnerException("DEMO_PASSWORD: " + problem);
    }
    IPasswordHasher hasher = settings.CreatePasswordHasher();

    SchemaGuard.WriteHeader($"Demo account {email}", settings);

    await using SqlConnection connection = await settings.OpenAsync();
    await SchemaGuard.EnsureCurrentAsync(connection);
    await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync();
    await DemoAccountSetup.EnsureAsync(connection, transaction, email, password, hasher);
    await transaction.CommitAsync();

    Console.WriteLine();
    Console.WriteLine("Done.");
    return 0;
  }
}
