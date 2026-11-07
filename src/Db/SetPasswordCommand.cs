using System;
using System.Threading.Tasks;
using DashboardStarter.Db.Passwords;
using DashboardStarter.Db.Scripts;
using DashboardStarter.Db.Users;
using Microsoft.Data.SqlClient;

namespace DashboardStarter.Db;

/// <summary>
/// <c>set-password --email &lt;email&gt; [--password-stdin]</c>: sets a user's password, clears
/// any lockout, and gives them a new security stamp, so every session they have open ends
/// (within the API's one-minute session check).
/// </summary>
internal static class SetPasswordCommand
{
  public static async Task<int> RunAsync(RunnerSettings settings, string[] args)
  {
    CommandArguments arguments = CommandArguments.Parse(args, ["email"], ["password-stdin"]);
    string email = UserAccounts.NormalizeEmail(arguments.GetRequired("email"));
    IPasswordHasher hasher = settings.CreatePasswordHasher();

    SchemaGuard.WriteHeader($"Password for {email}", settings);

    await using SqlConnection connection = await settings.OpenAsync();
    await SchemaGuard.EnsureCurrentAsync(connection);

    // Check the user exists before asking for a password nobody can use.
    await using (SqlTransaction lookup = (SqlTransaction)await connection.BeginTransactionAsync())
    {
      await FindUserAsync(connection, lookup, email);
    }

    string password = PasswordInput.Read(arguments.Has("password-stdin"), email);
    string passwordHash = hasher.Hash(password);

    await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync();
    UserRow user = await FindUserAsync(connection, transaction, email);
    await UserAccounts.SetPasswordAsync(connection, transaction, user.Id, passwordHash);
    await transaction.CommitAsync();

    ScriptExecutor.PrintResult("set", "password", "lockout cleared, other sessions signed out");
    Console.WriteLine();
    Console.WriteLine("Done.");
    return 0;
  }

  private static async Task<UserRow> FindUserAsync(SqlConnection connection, SqlTransaction transaction, string email)
  {
    UserRow? user = await UserAccounts.FindByEmailAsync(connection, transaction, email);
    if (user is null)
    {
      throw new RunnerException($"No user with email {email}. Create one with create-user.");
    }

    if (user.IsDemo)
    {
      throw new RunnerException(DemoAccountSetup.RefusalMessage(email));
    }

    return user;
  }
}
