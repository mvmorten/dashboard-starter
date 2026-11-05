using System.Collections.Generic;
using Microsoft.Extensions.Caching.Memory;

namespace DashboardStarter.Api.Auth;

/// <summary>
/// The session check (<see cref="AuthCookieEvents"/>) caches what it reads about a user for
/// <see cref="AuthOptions.SessionCheckSeconds"/>, one entry per user and tenant. After a
/// password change or reset, those entries still hold the old security stamp, so other
/// sessions would carry on until they expire. Forgetting them makes the very next request
/// of every session read the new stamp.
/// </summary>
/// <remarks>
/// The cache is in this process's memory. With more than one instance, other instances keep
/// their entries until they expire, so sessions there end within SessionCheckSeconds instead.
/// </remarks>
public static class SessionCache
{
  public static string Key(int userId, int? tenantId)
  {
    return $"session:{userId}:{tenantId}";
  }

  public static void Forget(IMemoryCache cache, int userId, IEnumerable<int> tenantIds)
  {
    cache.Remove(Key(userId, null));
    foreach (int tenantId in tenantIds)
    {
      cache.Remove(Key(userId, tenantId));
    }
  }
}
