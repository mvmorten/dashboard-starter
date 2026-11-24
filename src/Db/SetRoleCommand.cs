using System;
using System.Threading;
using System.Threading.Tasks;
using DashboardStarter.Db.Scripts;
using DashboardStarter.Db.Tenants;
using DashboardStarter.Db.Users;
using Microsoft.Data.SqlClient;

namespace DashboardStarter.Db;

/// <summary>
/// <c>set-role --email &lt;email&gt; --tenant &lt;id or name&gt; --role &lt;role&gt;</c>: gives a member
/// another role in one tenant. This is how a fork makes its first Admin without writing SQL.
/// </summary>
/// <remarks>
/// The same rule as the Members page (<see cref="Memberships"/>): a tenant never loses its last
/// active member who can manage members. A user who is already signed in gets the new role's
/// permissions within the API's session check (a minute at most), since the runner can't reach
/// the API's cache.
/// </remarks>
internal static class SetRoleCommand
{
  public static async Task<int> RunAsync(RunnerSettings settings, string[] args)
  {
    CommandArguments arguments = CommandArguments.Parse(args, ["email", "tenant", "role"], []);
    string email = UserAccounts.NormalizeEmail(arguments.GetRequired("email"));
    string tenant = arguments.GetRequired("tenant").Trim();
    string roleName = arguments.GetRequired("role");

    SchemaGuard.WriteHeader($"Role for {email}", settings);

    await using SqlConnection connection = await settings.OpenAsync();
    await SchemaGuard.EnsureCurrentAsync(connection);
    await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync();

    UserRow? user = await UserAccounts.FindByEmailAsync(connection, transaction, email);
    if (user is null)
    {
      throw new RunnerException($"No user with email {email}. Create one with create-user.");
    }

    if (user.IsDemo)
    {
      throw new RunnerException(UserAccounts.DemoRefusal(email));
    }

    TenantRow tenantRow = await UserAccounts.FindTenantAsync(connection, transaction, tenant);
    RoleRow role = await UserAccounts.FindRoleAsync(connection, transaction, roleName);
    await ChangeRoleAsync(connection, transaction, tenantRow, user.Id, role);
    await transaction.CommitAsync();

    Console.WriteLine();
    Console.WriteLine("Done.");
    return 0;
  }

  /// <summary>Changes the role through the shared rule, or throws with what went wrong.</summary>
  /// <remarks>
  /// The change writes an audit entry, with no user: nobody is signed in here, so the audit page
  /// shows it as done by the system. AuditLog is tenant-owned and Row-Level Security exempts no
  /// login, so the connection works in the tenant while the change runs.
  /// </remarks>
  public static async Task ChangeRoleAsync(SqlConnection connection, SqlTransaction transaction, TenantRow tenant, int userId, RoleRow role)
  {
    await TenantSession.SetAsync(connection, transaction, tenant.Id, readOnly: false, CancellationToken.None);
    MembershipChange change = await Memberships.SetRoleAsync(
      connection, transaction, tenant.Id, userId, role.Id, expectedRowVersion: null, actorUserId: null);
    await TenantSession.ClearAsync(connection, transaction, CancellationToken.None);

    switch (change.Result)
    {
      case MembershipChangeResult.Changed:
        ScriptExecutor.PrintResult("set", "role " + role.Name, "in " + tenant.Name);
        return;
      case MembershipChangeResult.NotFound:
        throw new RunnerException($"That user isn't a member of {tenant.Name}. Add them with create-user --tenant.");
      case MembershipChangeResult.LastManager:
        throw new RunnerException(
          $"That would leave {tenant.Name} with nobody who can manage members. Give someone else that role first.");
      default:
        throw new RunnerException($"The role wasn't changed ({change.Result}).");
    }
  }
}
