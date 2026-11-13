using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using DashboardStarter.Api.Demo;
using DashboardStarter.Api.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DashboardStarter.Api.Auth;

/// <summary>
/// Hooks into ASP.NET Core's cookie authentication at the two places we need different
/// behavior: checking each session against the database, and answering instead of redirecting.
/// </summary>
/// <remarks>
/// The cookie handler was built for server-rendered sites, so by default an unauthenticated
/// request is redirected to /Account/Login. An API should say 401 (or 403) and let the
/// React app decide what to show.
/// </remarks>
public sealed class AuthCookieEvents : CookieAuthenticationEvents
{
  private readonly UserStore _userStore;
  private readonly SandboxStore _sandboxes;
  private readonly IMemoryCache _cache;
  private readonly AuthOptions _options;
  private readonly ILogger<AuthCookieEvents> _logger;

  public AuthCookieEvents(
    UserStore userStore,
    SandboxStore sandboxes,
    IMemoryCache cache,
    IOptions<AuthOptions> options,
    ILogger<AuthCookieEvents> logger)
  {
    _userStore = userStore;
    _sandboxes = sandboxes;
    _cache = cache;
    _options = options.Value;
    _logger = logger;
  }

  /// <summary>
  /// Runs on every request that carries a valid auth cookie. The cookie proves who signed in,
  /// but not that they still should be: a password change, a lockout, or a removed tenant
  /// membership must end the session. The database is asked at most once per
  /// <see cref="AuthOptions.SessionCheckSeconds"/> per user and tenant, and the same read
  /// brings the user's permissions in that tenant.
  /// </summary>
  public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
  {
    if (context.Principal is null)
    {
      return;
    }

    int? userId = AuthClaims.GetUserId(context.Principal);
    int? tenantId = AuthClaims.GetTenantId(context.Principal);
    byte[]? stamp = AuthClaims.GetSecurityStamp(context.Principal);
    if (userId is null || stamp is null)
    {
      await EndSessionAsync(context, userId, "the cookie is missing claims");
      return;
    }

    string cacheKey = SessionCache.Key(userId.Value, tenantId);
    SessionState? state = await _cache.GetOrCreateAsync(cacheKey, async entry =>
    {
      entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_options.SessionCheckSeconds);
      SessionState? fresh = await _userStore.GetSessionStateAsync(userId.Value, tenantId, context.HttpContext.RequestAborted);

      // A demo sandbox in use stays alive (SandboxStore.KeepAliveAsync). Here, inside the
      // cache, it's written at most once per SessionCheckSeconds, not on every request.
      if (fresh is { IsDemo: true, IsExpired: false })
      {
        await _sandboxes.KeepAliveAsync(userId.Value, context.HttpContext.RequestAborted);
      }

      return fresh;
    });

    string? reason = state switch
    {
      null => "the user no longer exists",
      { IsExpired: true } => "the demo sandbox has expired",
      { IsLockedOut: true } => "the account is locked out",
      _ when !CryptographicOperations.FixedTimeEquals(state.SecurityStamp, stamp) => "the security stamp changed",
      { IsTenantMember: false } => "the tenant membership is no longer active",
      _ => null,
    };

    if (reason is not null)
    {
      await EndSessionAsync(context, userId, reason);
      return;
    }

    // What this user may do in this tenant, for PermissionHandler later in the request.
    SessionPermissions.Set(context.HttpContext, state!.Permissions);
  }

  // Never redirect: an API answers with a status code and a ProblemDetails body.
  public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
  {
    return ProblemResponses.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized, "Not signed in");
  }

  public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
  {
    // Signed in, but without the permission this endpoint needs (PermissionHandler).
    return ProblemResponses.WriteAsync(
      context.HttpContext,
      StatusCodes.Status403Forbidden,
      "You don't have access to this",
      "Your role in this tenant doesn't include it. Ask one of the tenant's admins.");
  }

  public override Task RedirectToLogout(RedirectContext<CookieAuthenticationOptions> context)
  {
    return Task.CompletedTask;
  }

  public override Task RedirectToReturnUrl(RedirectContext<CookieAuthenticationOptions> context)
  {
    return Task.CompletedTask;
  }

  private async Task EndSessionAsync(CookieValidatePrincipalContext context, int? userId, string reason)
  {
    _logger.LogInformation("Ended the session of user {UserId}: {Reason}.", userId, reason);

    // The request carries on as anonymous, so it gets a 401 from the authorization policy,
    // and the response deletes the cookie.
    context.RejectPrincipal();
    await context.HttpContext.SignOutAsync(AuthClaims.Scheme);
  }
}
