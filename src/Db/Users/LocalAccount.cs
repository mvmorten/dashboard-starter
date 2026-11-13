using System.Threading.Tasks;
using DashboardStarter.Db.Passwords;
using DashboardStarter.Db.Scripts;
using Microsoft.Data.SqlClient;

namespace DashboardStarter.Db.Users;

/// <summary>
/// The ordinary account <c>seed</c> makes for local development: Member in Acme Corporation and
/// Admin in Globex Industries, so you can sign in with a password and see two roles. It's a
/// normal account (not IsDemo), unlike the sandboxes "Try the demo" makes.
/// </summary>
/// <remarks>
/// Idempotent. The password is only set when it doesn't already match, so running seed again
/// doesn't sign you out. An existing membership keeps its role: if you changed it on the
/// Members page, seed leaves your change alone.
/// </remarks>
internal static class LocalAccount
{
  public const string Email = "admin@example.com";
  public const string DisplayName = "Local Admin";

  // Published in the README, like the local SQL Server password. Fine for a laptop: seed
  // refuses to run anywhere but Development.
  public const string Password = "LocalDevOnly!Admin1";

  private static readonly (string Tenant, string Role)[] Memberships =
  [
    ("Acme Corporation", "Member"),
    ("Globex Industries", "Admin"),
  ];

  public static async Task EnsureAsync(SqlConnection connection, SqlTransaction transaction, IPasswordHasher hasher)
  {
    UserRow? user = await UserAccounts.FindByEmailAsync(connection, transaction, Email);
    if (user is { IsDemo: true })
    {
      throw new RunnerException($"{Email} exists as a demo account. Remove it, or start over with: docker compose down -v");
    }

    int userId;
    if (user is null)
    {
      userId = await UserAccounts.InsertAsync(connection, transaction, Email, DisplayName, isDemo: false);
      ScriptExecutor.PrintResult("created", "user " + Email, "id " + userId);
    }
    else
    {
      userId = user.Id;
      ScriptExecutor.PrintResult("exists", "user " + Email, "id " + userId);
    }

    foreach ((string tenantName, string roleName) in Memberships)
    {
      TenantRow tenant = await UserAccounts.FindTenantAsync(connection, transaction, tenantName);
      RoleRow role = await UserAccounts.FindRoleAsync(connection, transaction, roleName);
      string result = await UserAccounts.EnsureMembershipAsync(connection, transaction, tenant.Id, userId, role.Id);
      ScriptExecutor.PrintResult(result, $"  in {tenant.Name}", result == "added" ? role.Name : null);
    }

    bool matches = user?.PasswordHash is not null && hasher.Verify(Password, user.PasswordHash) != PasswordVerification.Failed;
    if (matches)
    {
      ScriptExecutor.PrintResult("unchanged", "password");
      return;
    }

    await UserAccounts.SetPasswordAsync(connection, transaction, userId, hasher.Hash(Password));
    ScriptExecutor.PrintResult("set", "password");
  }
}
