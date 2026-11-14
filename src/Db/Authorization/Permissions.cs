using System.Collections.Generic;

namespace DashboardStarter.Db.Authorization;

/// <summary>
/// Every permission in the app, in one place: area.action, with what it allows. Code checks
/// these, never role names: <c>[Authorize(Policy = Permissions.MembersManage)]</c> on the API,
/// <c>can("members.manage")</c> in the front end (src/Web/src/auth/permissions.ts).
/// </summary>
/// <remarks>
/// <para>
/// Which roles have which permissions is data: the RolePermissions table, filled by migrations
/// (sql/migrations/0009_roles.sql, 0014_customer_permissions.sql). To add one, add a constant
/// here, add it to <see cref="All"/>, and write a migration that grants it to the roles that
/// should have it. Every value in RolePermissions must be one of these; CI checks that.
/// </para>
/// <para>
/// It lives in the Db project, like the password rules, because the Db runner needs it too:
/// set-role keeps the same "last member who can manage members" rule as the API.
/// </para>
/// </remarks>
public static class Permissions
{
  public const string MembersView = "members.view";
  public const string MembersManage = "members.manage";
  public const string CustomersView = "customers.view";
  public const string CustomersEdit = "customers.edit";

  /// <summary>Every permission, with a one-line description of what it allows.</summary>
  public static IReadOnlyDictionary<string, string> All { get; } = new Dictionary<string, string>
  {
    [MembersView] = "See the tenant's members, their roles, and when they last signed in.",
    [MembersManage] = "Change members' roles, and disable or re-enable their access to the tenant.",
    [CustomersView] = "See the tenant's customers: the list, search, and each customer's details.",
    [CustomersEdit] = "Add, change, and delete the tenant's customers.",
  };
}
