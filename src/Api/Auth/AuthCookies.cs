using Microsoft.AspNetCore.Http;

namespace DashboardStarter.Api.Auth;

/// <summary>
/// The names and Secure setting of the app's three cookies, chosen once at startup.
/// </summary>
/// <remarks>
/// <para>
/// Three cookies: the auth cookie (HttpOnly), and antiforgery as the React app uses it.
/// The antiforgery cookie token is HttpOnly, set by ASP.NET Core's antiforgery service. The
/// request token is readable by script, and the app copies it into the <see cref="HeaderName"/>
/// header on every POST, PUT, and DELETE. A forged request from another site can make the
/// browser send cookies, but it can't read them, so it can't produce the header.
/// </para>
/// <para>
/// <b>Deployed:</b> every name starts with <c>__Host-</c>, which makes the browser refuse the
/// cookie unless it's Secure, has Path=/, and has no Domain, so no other host or subdomain can
/// set or overwrite it. Secure is always on.
/// </para>
/// <para>
/// <b>Development:</b> no prefix, and Secure only on HTTPS requests. Locally the Vite proxy calls
/// the API over plain http, ASP.NET Core's antiforgery refuses a non-HTTPS request when its
/// cookie must be Secure, and Safari won't store Secure cookies for http://localhost at all.
/// </para>
/// <para>
/// Antiforgery tokens are tied to the signed-in user, including their current tenant (our
/// claims have no standard user-id claim, so the antiforgery service uses all of them). The
/// app fetches a new pair after sign-in, sign-out, and switching tenants.
/// </para>
/// </remarks>
public sealed class AuthCookies
{
  public const string HeaderName = "X-XSRF-TOKEN";

  private AuthCookies(string prefix, CookieSecurePolicy securePolicy)
  {
    AuthName = prefix + "ds-auth";
    AntiforgeryName = prefix + "ds-af";
    RequestTokenName = prefix + "ds-xsrf";
    SecurePolicy = securePolicy;
  }

  public string AuthName { get; }

  public string AntiforgeryName { get; }

  public string RequestTokenName { get; }

  public CookieSecurePolicy SecurePolicy { get; }

  public static AuthCookies For(bool isDevelopment)
  {
    return isDevelopment
      ? new AuthCookies(string.Empty, CookieSecurePolicy.SameAsRequest)
      : new AuthCookies("__Host-", CookieSecurePolicy.Always);
  }
}
