using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace DashboardStarter.Api.Auth;

/// <summary>
/// Hands the permissions the session check just loaded (<see cref="AuthCookieEvents"/>) to the
/// authorization handler later in the same request (<see cref="PermissionHandler"/>), through
/// HttpContext.Items. They never go in the cookie.
/// </summary>
public static class SessionPermissions
{
  private static readonly object ItemKey = new();

  public static void Set(HttpContext context, IReadOnlySet<string> permissions)
  {
    context.Items[ItemKey] = permissions;
  }

  /// <summary>
  /// The signed-in user's permissions in the current tenant. Empty when the session check
  /// didn't run or found none, so a missing value fails closed.
  /// </summary>
  public static IReadOnlySet<string> Get(HttpContext context)
  {
    return context.Items.TryGetValue(ItemKey, out object? value) && value is IReadOnlySet<string> permissions
      ? permissions
      : new HashSet<string>(StringComparer.Ordinal);
  }
}
