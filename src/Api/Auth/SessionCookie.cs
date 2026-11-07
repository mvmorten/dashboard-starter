using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace DashboardStarter.Api.Auth;

/// <summary>Issues the auth cookie. Used by sign-in, switching tenants, and changing your password.</summary>
public static class SessionCookie
{
  public static Task IssueAsync(HttpContext httpContext, int userId, int? tenantId, byte[] securityStamp)
  {
    // Not persistent: the cookie has no expiry date of its own, so the browser drops it when
    // it closes. The ticket inside still expires after SessionHours without activity.
    AuthenticationProperties properties = new() { IsPersistent = false };
    return httpContext.SignInAsync(AuthClaims.Scheme, AuthClaims.CreatePrincipal(userId, tenantId, securityStamp), properties);
  }
}
