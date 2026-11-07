using System.Threading.Tasks;
using DashboardStarter.Db.Passwords;
using DashboardStarter.Db.Scripts;
using Microsoft.Data.SqlClient;

namespace DashboardStarter.Db.Users;

/// <summary>
/// The one definition of the shared demo account: the user (IsDemo = 1), its tenants, and its
/// memberships. The <c>demo</c> command (deployed, password from DEMO_PASSWORD) and <c>seed</c>
/// (local, the published local password) both call this, so the two can't drift apart again.
/// </summary>
/// <remarks>
/// Idempotent. The password is only set when it doesn't already match, so running it again with
/// the same password keeps the security stamp and nobody using the demo is signed out. It
/// refuses to turn an existing ordinary account into the demo account, since that would
/// publish the account's new password.
/// </remarks>
internal static class DemoAccountSetup
{
  public const string DefaultEmail = "guest@example.com";
  public const string DisplayName = "Demo Guest";

  // Two tenants, so the demo shows the tenant switcher and that each tenant only sees its own data.
  public static readonly string[] TenantNames = ["Demo", "Other Demo"];

  /// <summary>Why create-user and set-password won't touch the demo account.</summary>
  public static string RefusalMessage(string email)
  {
    return $"{email} is the demo account. Its password is published, so it's only set by the demo command "
      + "(from DEMO_PASSWORD) or, locally, by seed.";
  }

  public static async Task EnsureAsync(
    SqlConnection connection,
    SqlTransaction transaction,
    string email,
    string password,
    IPasswordHasher hasher)
  {
    UserRow? user = await UserAccounts.FindByEmailAsync(connection, transaction, email);
    if (user is { IsDemo: false })
    {
      throw new RunnerException(
        $"{email} already exists as an ordinary account. The demo password is published, so it won't take over a real account. Set DEMO_EMAIL to another address.");
    }

    int userId;
    if (user is null)
    {
      userId = await UserAccounts.InsertAsync(connection, transaction, email, DisplayName, isDemo: true);
      ScriptExecutor.PrintResult("created", "user " + email, "id " + userId);
    }
    else
    {
      userId = user.Id;
      await UserAccounts.UpdateNameAsync(connection, transaction, userId, DisplayName);
      ScriptExecutor.PrintResult("exists", "user " + email, "id " + userId);
    }

    foreach (string tenantName in TenantNames)
    {
      (int tenantId, bool tenantCreated) = await UserAccounts.EnsureTenantAsync(connection, transaction, tenantName);
      ScriptExecutor.PrintResult(tenantCreated ? "created" : "exists", "tenant " + tenantName, "id " + tenantId);

      string membership = await UserAccounts.EnsureMembershipAsync(connection, transaction, tenantId, userId);
      ScriptExecutor.PrintResult(membership, "member of " + tenantName);
    }

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
  }
}
