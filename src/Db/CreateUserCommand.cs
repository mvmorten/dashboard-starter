using System;
using System.Threading.Tasks;
using DashboardStarter.Db.Passwords;
using DashboardStarter.Db.Scripts;
using DashboardStarter.Db.Users;
using Microsoft.Data.SqlClient;

namespace DashboardStarter.Db;

/// <summary>
/// <c>create-user --email &lt;email&gt; --name &lt;name&gt; [--tenant &lt;id or name&gt;] [--password-stdin]</c>:
/// creates a user (or updates the name of an existing one), optionally adds them to a tenant,
/// and sets their password. Until there's an invite flow, this is how accounts are made.
/// </summary>
internal static class CreateUserCommand
{
  public static async Task<int> RunAsync(RunnerSettings settings, string[] args)
  {
    CommandArguments arguments = CommandArguments.Parse(args, ["email", "name", "tenant"], ["password-stdin"]);
    string email = UserAccounts.NormalizeEmail(arguments.GetRequired("email"));
    string name = UserAccounts.NormalizeName(arguments.GetRequired("name"));
    string? tenant = arguments.Get("tenant");
    IPasswordHasher hasher = settings.CreatePasswordHasher();

    SchemaGuard.WriteHeader($"User {email}", settings);

    await using SqlConnection connection = await settings.OpenAsync();
    await SchemaGuard.EnsureCurrentAsync(connection);

    // Ask for the password before changing anything, so giving up at the prompt
    // leaves the database as it was.
    string password = PasswordInput.Read(arguments.Has("password-stdin"), email);
    string passwordHash = hasher.Hash(password);

    await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync();

    TenantRow? tenantRow = tenant is null ? null : await UserAccounts.FindTenantAsync(connection, transaction, tenant.Trim());

    UserRow? existing = await UserAccounts.FindByEmailAsync(connection, transaction, email);
    if (existing is { IsDemo: true })
    {
      throw new RunnerException($"{email} is the demo account. Its password comes from DEMO_PASSWORD; use the demo command.");
    }

    int userId;
    if (existing is null)
    {
      userId = await UserAccounts.InsertAsync(connection, transaction, email, name, isDemo: false);
      ScriptExecutor.PrintResult("created", "user " + email, "id " + userId);
    }
    else
    {
      userId = existing.Id;
      await UserAccounts.UpdateNameAsync(connection, transaction, userId, name);
      ScriptExecutor.PrintResult("updated", "user " + email, "name set to " + name);
    }

    await UserAccounts.SetPasswordAsync(connection, transaction, userId, passwordHash);
    ScriptExecutor.PrintResult("set", "password", existing is null ? null : "other sessions signed out");

    if (tenantRow is not null)
    {
      string membership = await UserAccounts.EnsureMembershipAsync(connection, transaction, tenantRow.Id, userId);
      ScriptExecutor.PrintResult(membership, "member of " + tenantRow.Name, "tenant id " + tenantRow.Id);
    }

    await transaction.CommitAsync();

    Console.WriteLine();
    Console.WriteLine("Done.");
    return 0;
  }
}
