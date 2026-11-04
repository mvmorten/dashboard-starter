using System;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DashboardStarter.Api.Auth;

/// <summary>
/// What the auth cookie says about the user: who they are, which tenant they're working in,
/// and the security stamp it was issued with. Nothing else, so nothing in it can go stale:
/// names, emails, and memberships are read from the database when they're needed.
/// </summary>
public static class AuthClaims
{
  public const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;

  public const string UserId = "UserId";
  public const string TenantId = "TenantId";
  public const string SecurityStamp = "SecurityStamp";

  public static ClaimsPrincipal CreatePrincipal(int userId, int? tenantId, byte[] securityStamp)
  {
    ClaimsIdentity identity = new(Scheme, nameType: UserId, roleType: null);
    identity.AddClaim(new Claim(UserId, userId.ToString(CultureInfo.InvariantCulture)));
    if (tenantId is not null)
    {
      identity.AddClaim(new Claim(TenantId, tenantId.Value.ToString(CultureInfo.InvariantCulture)));
    }

    identity.AddClaim(new Claim(SecurityStamp, Convert.ToHexString(securityStamp)));
    return new ClaimsPrincipal(identity);
  }

  public static int? GetUserId(ClaimsPrincipal user)
  {
    return ReadInt(user, UserId);
  }

  /// <summary>The tenant the user is working in, or null if they belong to none.</summary>
  public static int? GetTenantId(ClaimsPrincipal user)
  {
    return ReadInt(user, TenantId);
  }

  public static byte[]? GetSecurityStamp(ClaimsPrincipal user)
  {
    string? value = user.FindFirst(SecurityStamp)?.Value;
    if (value is null)
    {
      return null;
    }

    try
    {
      return Convert.FromHexString(value);
    }
    catch (FormatException)
    {
      return null;
    }
  }

  private static int? ReadInt(ClaimsPrincipal user, string type)
  {
    string? value = user.FindFirst(type)?.Value;
    return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ? parsed : null;
  }
}
