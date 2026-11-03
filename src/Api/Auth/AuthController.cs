using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DashboardStarter.Api.Tenants;
using DashboardStarter.Db.Passwords;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DashboardStarter.Api.Auth;

/// <summary>
/// Sign in, sign out, who am I, and switching tenants. Every endpoint needs a signed-in user
/// (the fallback policy in Program.cs) unless it says [AllowAnonymous].
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
  // One message for every way sign-in can fail: unknown email, wrong password, no password
  // set, locked out. Telling them apart would tell an attacker which emails have accounts.
  private const string SignInFailedDetail =
    "The email or password is incorrect, or the account is locked for a few minutes after too many attempts.";

  private readonly UserStore _userStore;
  private readonly IPasswordHasher _passwordHasher;
  private readonly UnknownUserHash _unknownUserHash;
  private readonly IAntiforgery _antiforgery;
  private readonly AuthCookies _cookies;
  private readonly AuthOptions _options;
  private readonly DemoAccountOptions _demo;
  private readonly ILogger<AuthController> _logger;

  public AuthController(
    UserStore userStore,
    IPasswordHasher passwordHasher,
    UnknownUserHash unknownUserHash,
    IAntiforgery antiforgery,
    AuthCookies cookies,
    IOptions<AuthOptions> options,
    IOptions<DemoAccountOptions> demo,
    ILogger<AuthController> logger)
  {
    _userStore = userStore;
    _passwordHasher = passwordHasher;
    _unknownUserHash = unknownUserHash;
    _antiforgery = antiforgery;
    _cookies = cookies;
    _options = options.Value;
    _demo = demo.Value;
    _logger = logger;
  }

  /// <summary>
  /// Issues an antiforgery token pair (see <see cref="AuthCookies"/>) for whoever is
  /// signed in now, or for nobody. Answers 204; the token is in the readable cookie.
  /// </summary>
  [AllowAnonymous]
  [HttpGet("antiforgery")]
  public IActionResult Antiforgery()
  {
    AntiforgeryTokenSet tokens = _antiforgery.GetAndStoreTokens(HttpContext);
    Response.Cookies.Append(_cookies.RequestTokenName, tokens.RequestToken ?? string.Empty, new CookieOptions
    {
      HttpOnly = false,
      Secure = _cookies.SecurePolicy == CookieSecurePolicy.Always || Request.IsHttps,
      SameSite = SameSiteMode.Strict,
      Path = "/",
    });

    return NoContent();
  }

  /// <summary>What the sign-in page needs before anyone signs in: the demo account, if this deployment has one.</summary>
  [AllowAnonymous]
  [HttpGet("sign-in-info")]
  public ActionResult<SignInInfoResponse> SignInInfo()
  {
    DemoAccount? demo = string.IsNullOrWhiteSpace(_demo.Email) || string.IsNullOrEmpty(_demo.Password)
      ? null
      : new DemoAccount(_demo.Email, _demo.Password);

    return Ok(new SignInInfoResponse(demo));
  }

  [AllowAnonymous]
  [HttpPost("sign-in")]
  [EnableRateLimiting(SignInRateLimit.PolicyName)]
  public async Task<ActionResult<MeResponse>> SignIn(SignInRequest request, CancellationToken cancellationToken)
  {
    SignInCandidate? user = await _userStore.FindForSignInAsync(request.Email.Trim(), cancellationToken);

    // No account, or no password yet: spend the same time a real check would, then fail.
    if (user?.PasswordHash is null)
    {
      _passwordHasher.Verify(request.Password, _unknownUserHash.Value);
      _logger.LogInformation("Sign-in failed: no account with that email, or no password set.");
      return SignInFailed();
    }

    // Verify before looking at the lockout, so a locked account answers in the same time as any other.
    PasswordVerification verification = _passwordHasher.Verify(request.Password, user.PasswordHash);

    if (user.IsLockedOut)
    {
      _logger.LogInformation("Sign-in failed for user {UserId}: locked out.", user.Id);
      return SignInFailed();
    }

    if (verification == PasswordVerification.Failed)
    {
      // The demo account's password is public; counting failures would only let anyone
      // lock everyone out. Rate limiting protects it instead.
      if (!user.IsDemo)
      {
        await _userStore.RecordFailedSignInAsync(user.Id, _options.MaxFailedSignIns, _options.LockoutMinutes, cancellationToken);
      }

      _logger.LogInformation("Sign-in failed for user {UserId}: wrong password.", user.Id);
      return SignInFailed();
    }

    // Stored with older, weaker settings: hash again with the current ones while we have the password.
    string? newHash = verification == PasswordVerification.SuccessRehashNeeded
      ? _passwordHasher.Hash(request.Password)
      : null;
    await _userStore.RecordSignInAsync(user.Id, user.PasswordHash, newHash, cancellationToken);

    // Land in the tenant used last (the list is sorted that way), or none if there are none.
    IReadOnlyList<TenantResponse> tenants = await _userStore.GetActiveTenantsAsync(user.Id, cancellationToken);
    int? tenantId = tenants.Count > 0 ? tenants[0].Id : null;
    if (tenantId is not null)
    {
      await _userStore.MarkTenantUsedAsync(user.Id, tenantId.Value, cancellationToken);
    }

    await IssueCookieAsync(user.Id, tenantId, user.SecurityStamp);
    _logger.LogInformation(
      "User {UserId} signed in to tenant {TenantId}{Rehashed}.",
      user.Id,
      tenantId,
      newHash is null ? string.Empty : " (password rehashed with current settings)");

    return await BuildMeAsync(user.Id, tenantId, cancellationToken);
  }

  [HttpPost("sign-out")]
  public async Task<IActionResult> SignOutUser()
  {
    await HttpContext.SignOutAsync(AuthClaims.Scheme);
    return NoContent();
  }

  [HttpGet("me")]
  public async Task<ActionResult<MeResponse>> Me(CancellationToken cancellationToken)
  {
    return await BuildMeAsync(CurrentUserId(), AuthClaims.GetTenantId(User), cancellationToken);
  }

  /// <summary>
  /// Moves the session to another tenant the user belongs to. The membership is checked here,
  /// and again on every request by the session check, so a tenant id from the browser is
  /// never trusted on its own.
  /// </summary>
  [HttpPost("switch-tenant")]
  public async Task<ActionResult<MeResponse>> SwitchTenant(SwitchTenantRequest request, CancellationToken cancellationToken)
  {
    int userId = CurrentUserId();
    bool isMember = await _userStore.MarkTenantUsedAsync(userId, request.TenantId, cancellationToken);
    if (!isMember)
    {
      return Problem(
        statusCode: StatusCodes.Status403Forbidden,
        title: "Not a member of that tenant",
        detail: "You can only switch to a tenant you're an active member of.");
    }

    // Same security stamp: switching tenants doesn't change who you are.
    byte[] stamp = AuthClaims.GetSecurityStamp(User)!;
    await IssueCookieAsync(userId, request.TenantId, stamp);
    _logger.LogInformation("User {UserId} switched to tenant {TenantId}.", userId, request.TenantId);

    return await BuildMeAsync(userId, request.TenantId, cancellationToken);
  }

  private Task IssueCookieAsync(int userId, int? tenantId, byte[] securityStamp)
  {
    // Not persistent: the cookie has no expiry date of its own, so the browser drops it when
    // it closes. The ticket inside still expires after SessionHours without activity.
    AuthenticationProperties properties = new() { IsPersistent = false };
    return HttpContext.SignInAsync(AuthClaims.Scheme, AuthClaims.CreatePrincipal(userId, tenantId, securityStamp), properties);
  }

  private async Task<ActionResult<MeResponse>> BuildMeAsync(int userId, int? tenantId, CancellationToken cancellationToken)
  {
    UserSummary? user = await _userStore.GetUserAsync(userId, cancellationToken);
    if (user is null)
    {
      return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Not signed in");
    }

    IReadOnlyList<TenantResponse> tenants = await _userStore.GetActiveTenantsAsync(userId, cancellationToken);

    // Only report a current tenant the user is still an active member of.
    int? currentTenantId = tenants.Any(tenant => tenant.Id == tenantId) ? tenantId : null;

    return Ok(new MeResponse(
      new MeUser(user.Id, user.Email, user.DisplayName, user.IsDemo),
      tenants.OrderBy(tenant => tenant.Name).ToList(),
      currentTenantId));
  }

  // Every action that calls this requires a signed-in user, whose cookie always has the claim.
  private int CurrentUserId()
  {
    return AuthClaims.GetUserId(User) ?? throw new InvalidOperationException("Signed-in user without a UserId claim.");
  }

  private ObjectResult SignInFailed()
  {
    return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Sign-in failed", detail: SignInFailedDetail);
  }
}
