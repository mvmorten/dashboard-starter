using System.Threading.Tasks;
using DashboardStarter.Db.Passwords;
using DashboardStarter.Db.Scripts;
using Microsoft.Data.SqlClient;

namespace DashboardStarter.Db.Users;

/// <summary>
/// The one definition of the shared demo account: the user (IsDemo = 1), its tenants, its
/// memberships and roles, and the sample members it sees on the Members page. The <c>demo</c>
/// command (deployed, password from DEMO_PASSWORD) and <c>seed</c> (local, the published local
/// password) both call this, so the two can't drift apart again.
/// </summary>
/// <remarks>
/// <para>
/// Idempotent. The password is only set when it doesn't already match, so running it again with
/// the same password keeps the security stamp and nobody using the demo is signed out. It
/// refuses to turn an existing ordinary account into a demo account, since that would publish
/// the account's new password.
/// </para>
/// <para>
/// The demo account is an Admin in "Demo" and Read-only in "Other Demo", so switching tenants
/// shows the same person with different permissions. It's shared, so the API refuses its member
/// changes (MembersController); the Members page shows an admin's controls, disabled.
/// </para>
/// <para>
/// Role names appear here because this is a definition, like the migration that creates the
/// roles. Code that decides what someone may do checks permissions only.
/// </para>
/// </remarks>
internal static class DemoAccountSetup
{
  public const string DefaultEmail = "guest@example.com";
  public const string DisplayName = "Demo Guest";

  // Two tenants, so the demo shows the tenant switcher and that each tenant only sees its own data.
  public static readonly DemoMembership[] GuestMemberships =
  [
    new("Demo", "Admin", Memberships.Active),
    new("Other Demo", "Read-only", Memberships.Active),
  ];

  // People for the demo account to see on the Members page. Also IsDemo, with no password: they
  // can't sign in, and forgot-password never emails them. example.com is reserved for examples,
  // so these addresses can't belong to anyone.
  public static readonly SampleMember[] SampleMembers =
  [
    new("sam.rivera@example.com", "Sam Rivera", [new("Demo", "Admin", Memberships.Active), new("Other Demo", "Admin", Memberships.Active)]),
    new("priya.shah@example.com", "Priya Shah", [new("Demo", "Member", Memberships.Active), new("Other Demo", "Member", Memberships.Active)]),
    new("lee.chen@example.com", "Lee Chen", [new("Demo", "Read-only", Memberships.Active)]),
    new("jordan.blake@example.com", "Jordan Blake", [new("Demo", "Member", Memberships.Disabled)]),
  ];

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

    await EnsureMembershipsAsync(connection, transaction, userId, GuestMemberships);

    foreach (SampleMember sample in SampleMembers)
    {
      int sampleId = await EnsureSampleUserAsync(connection, transaction, sample);
      await EnsureMembershipsAsync(connection, transaction, sampleId, sample.Memberships);
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

  private static async Task<int> EnsureSampleUserAsync(SqlConnection connection, SqlTransaction transaction, SampleMember sample)
  {
    UserRow? user = await UserAccounts.FindByEmailAsync(connection, transaction, sample.Email);
    if (user is { IsDemo: false })
    {
      throw new RunnerException(
        $"{sample.Email} already exists as an ordinary account, so it can't be a sample member of the demo tenants.");
    }

    if (user is null)
    {
      int id = await UserAccounts.InsertAsync(connection, transaction, sample.Email, sample.DisplayName, isDemo: true);
      ScriptExecutor.PrintResult("created", "sample " + sample.Email, "id " + id);
      return id;
    }

    await UserAccounts.UpdateNameAsync(connection, transaction, user.Id, sample.DisplayName);
    ScriptExecutor.PrintResult("exists", "sample " + sample.Email, "id " + user.Id);
    return user.Id;
  }

  private static async Task EnsureMembershipsAsync(
    SqlConnection connection, SqlTransaction transaction, int userId, DemoMembership[] memberships)
  {
    foreach (DemoMembership membership in memberships)
    {
      (int tenantId, bool tenantCreated) = await UserAccounts.EnsureTenantAsync(connection, transaction, membership.TenantName);
      if (tenantCreated)
      {
        ScriptExecutor.PrintResult("created", "tenant " + membership.TenantName, "id " + tenantId);
      }

      RoleRow role = await UserAccounts.FindRoleAsync(connection, transaction, membership.RoleName);
      string result = await UserAccounts.SetDefinedMembershipAsync(connection, transaction, tenantId, userId, role.Id, membership.Status);
      ScriptExecutor.PrintResult(result, $"  in {membership.TenantName}", $"{role.Name}, {membership.Status}");
    }
  }
}

internal sealed record DemoMembership(string TenantName, string RoleName, string Status);

internal sealed record SampleMember(string Email, string DisplayName, DemoMembership[] Memberships);
