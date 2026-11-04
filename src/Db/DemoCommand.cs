using System;
using System.Threading.Tasks;
using DashboardStarter.Db.Passwords;
using DashboardStarter.Db.Scripts;
using DashboardStarter.Db.Users;
using Microsoft.Data.SqlClient;

namespace DashboardStarter.Db;

/// <summary>
/// <c>demo</c>: the shared account for a public demo. Creates the "Demo" tenant and the demo
/// user (IsDemo = 1) with the published password from DEMO_PASSWORD (and DEMO_EMAIL, default
/// guest@example.com). Runs in any environment, unlike seed; the deploy runs it when the repo
/// variable DEMO_ENABLED is true.
/// </summary>
/// <remarks>
/// Safe to run on every deploy. The password is only set when it doesn't already match, so an
/// unchanged DEMO_PASSWORD keeps the security stamp, and people trying the demo stay signed in
/// across deploys. It refuses to turn an existing ordinary account into the demo account,
/// since that would publish the account's new password.
/// </remarks>
internal static class DemoCommand
{
  private const string TenantName = "Demo";
  private const string DisplayName = "Demo Guest";
  private const string DefaultEmail = "guest@example.com";

  public static async Task<int> RunAsync(RunnerSettings settings, string[] args)
  {
    CommandArguments.Parse(args, [], []);

    string? password = Environment.GetEnvironmentVariable("DEMO_PASSWORD");
    if (string.IsNullOrEmpty(password))
    {
      throw new RunnerException("Set DEMO_PASSWORD to the demo account's password. It's shown on the sign-in page, so it isn't a secret.");
    }

    string? problem = PasswordRules.Check(password);
    if (problem is not null)
    {
      throw new RunnerException("DEMO_PASSWORD: " + problem);
    }

    string? configuredEmail = Environment.GetEnvironmentVariable("DEMO_EMAIL");
    string email = UserAccounts.NormalizeEmail(string.IsNullOrWhiteSpace(configuredEmail) ? DefaultEmail : configuredEmail);
    IPasswordHasher hasher = settings.CreatePasswordHasher();

    SchemaGuard.WriteHeader($"Demo account {email}", settings);

    await using SqlConnection connection = await settings.OpenAsync();
    await SchemaGuard.EnsureCurrentAsync(connection);
    await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync();

    UserRow? user = await UserAccounts.FindByEmailAsync(connection, transaction, email);
    if (user is { IsDemo: false })
    {
      throw new RunnerException(
        $"{email} already exists as an ordinary account. The demo password is published, so it won't take over a real account. Set DEMO_EMAIL to another address.");
    }

    (int tenantId, bool tenantCreated) = await UserAccounts.EnsureTenantAsync(connection, transaction, TenantName);
    ScriptExecutor.PrintResult(tenantCreated ? "created" : "exists", "tenant " + TenantName, "id " + tenantId);

    int userId;
    if (user is null)
    {
      userId = await UserAccounts.InsertAsync(connection, transaction, email, DisplayName, isDemo: true);
      ScriptExecutor.PrintResult("created", "user " + email, "id " + userId);
    }
    else
    {
      userId = user.Id;
      ScriptExecutor.PrintResult("exists", "user " + email, "id " + userId);
    }

    string membership = await UserAccounts.EnsureMembershipAsync(connection, transaction, tenantId, userId);
    ScriptExecutor.PrintResult(membership, "member of " + TenantName);

    PasswordVerification current = user?.PasswordHash is null
      ? PasswordVerification.Failed
      : hasher.Verify(password, user.PasswordHash);

    if (current == PasswordVerification.Failed)
    {
      await UserAccounts.SetPasswordAsync(connection, transaction, userId, hasher.Hash(password));
      ScriptExecutor.PrintResult("set", "password", user is null ? null : "changed; demo sessions signed out");
    }
    else if (current == PasswordVerification.SuccessRehashNeeded)
    {
      await UserAccounts.ReplaceHashAsync(connection, transaction, userId, hasher.Hash(password));
      ScriptExecutor.PrintResult("rehashed", "password", "same password, current settings");
    }
    else
    {
      ScriptExecutor.PrintResult("unchanged", "password");
    }

    await transaction.CommitAsync();

    Console.WriteLine();
    Console.WriteLine("Done.");
    return 0;
  }
}
