using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DashboardStarter.Api.Email;
using DashboardStarter.Db.Passwords;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DashboardStarter.Api.Auth;

/// <summary>
/// Forgot password, reset password (from the emailed link), and change password (signed in).
/// Its own controller, next to <see cref="AuthController"/>, which is about sessions; the URLs
/// still live under /api/auth. All three share one rate limit per client IP address.
/// </summary>
[ApiController]
[Route("api/auth")]
[EnableRateLimiting(PasswordResetOptions.RateLimitPolicyName)]
public sealed class PasswordController : ControllerBase
{
  // One answer for every link that doesn't work: used, expired, mistyped, or never issued.
  private const string InvalidLinkDetail = "This reset link is invalid or has expired. Request a new one.";

  private readonly PasswordResetStore _resetStore;
  private readonly UserStore _userStore;
  private readonly IPasswordHasher _passwordHasher;
  private readonly EmailQueue _emailQueue;
  private readonly EmailStatus _email;
  private readonly IMemoryCache _sessionCache;
  private readonly PasswordResetOptions _options;
  private readonly AuthOptions _authOptions;
  private readonly ILogger<PasswordController> _logger;

  public PasswordController(
    PasswordResetStore resetStore,
    UserStore userStore,
    IPasswordHasher passwordHasher,
    EmailQueue emailQueue,
    EmailStatus email,
    IMemoryCache sessionCache,
    IOptions<PasswordResetOptions> options,
    IOptions<AuthOptions> authOptions,
    ILogger<PasswordController> logger)
  {
    _resetStore = resetStore;
    _userStore = userStore;
    _passwordHasher = passwordHasher;
    _emailQueue = emailQueue;
    _email = email;
    _sessionCache = sessionCache;
    _options = options.Value;
    _authOptions = authOptions.Value;
    _logger = logger;
  }

  /// <summary>
  /// Emails a reset link, if the address belongs to an account that can have one. Always the
  /// same answer (204), so nobody can use this to find out which addresses have accounts.
  /// </summary>
  /// <remarks>
  /// The work has the same shape whatever the address: make a token, run one database batch.
  /// The email itself is queued and sent in the background (<see cref="EmailQueue"/>), so the
  /// answer never waits on the email service, and a real address isn't slower than an unknown one.
  /// </remarks>
  [AllowAnonymous]
  [HttpPost("forgot-password")]
  public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
  {
    if (!_email.Enabled || _email.LinkBase is null)
    {
      _logger.LogInformation("Password reset requested, but email is off. Nothing sent.");
      return NoContent();
    }

    (string token, byte[] tokenHash) = PasswordResetStore.NewToken();
    ResetRecipient? recipient = await _resetStore.IssueAsync(
      request.Email.Trim(), tokenHash, _options.TokenLifetimeSeconds, _options.MaxEmailsPerHour, cancellationToken);

    // Unknown address, a demo account, or over the hourly cap. Not told apart, here or in the answer.
    if (recipient is null)
    {
      _logger.LogInformation("Password reset requested; no email sent (no such account, a demo account, or over the hourly cap).");
      return NoContent();
    }

    EmailMessage message = PasswordResetEmail.Create(recipient, _email.LinkBase, token, _options.TokenLifetimeSeconds);
    if (!_emailQueue.TryEnqueue(message))
    {
      _logger.LogWarning("The email queue is full; the password reset email for user {UserId} was dropped.", recipient.UserId);
      return NoContent();
    }

    _logger.LogInformation("Password reset email queued for user {UserId}.", recipient.UserId);
    return NoContent();
  }

  /// <summary>
  /// Sets a new password from a reset link. Ends every session (new security stamp) and clears
  /// any lockout. Doesn't sign in: the page sends the person to sign in with the new password.
  /// </summary>
  [AllowAnonymous]
  [HttpPost("reset-password")]
  public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
  {
    // Length first: it doesn't need the account, and a bad password shouldn't cost a lookup.
    string? lengthProblem = PasswordRules.CheckLength(request.NewPassword);
    if (lengthProblem is not null)
    {
      return PasswordRejected(lengthProblem);
    }

    byte[] tokenHash = PasswordResetStore.Hash(request.Token);
    ResetRecipient? recipient = await _resetStore.FindValidAsync(tokenHash, cancellationToken);
    if (recipient is null)
    {
      _logger.LogInformation("Password reset with a link that doesn't work (used, expired, or unknown).");
      return InvalidLink();
    }

    string? problem = PasswordRules.Check(request.NewPassword, recipient.Email);
    if (problem is not null)
    {
      return PasswordRejected(problem);
    }

    string passwordHash = _passwordHasher.Hash(request.NewPassword);

    // Checks the token again, atomically: it may have been used since the lookup above.
    int? userId = await _resetStore.CompleteResetAsync(tokenHash, passwordHash, cancellationToken);
    if (userId is null)
    {
      _logger.LogInformation("Password reset with a link that was used or expired a moment ago.");
      return InvalidLink();
    }

    await ForgetSessionsAsync(userId.Value, cancellationToken);
    _logger.LogInformation("User {UserId} reset their password with an emailed link. All sessions ended.", userId.Value);
    return NoContent();
  }

  /// <summary>
  /// Changes the signed-in user's password. Other sessions end (new security stamp); this one
  /// gets a new cookie with the new stamp, so the device you're on stays signed in.
  /// </summary>
  [HttpPost("change-password")]
  public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
  {
    int userId = AuthClaims.GetUserId(User) ?? throw new InvalidOperationException("Signed-in user without a UserId claim.");
    PasswordChangeCandidate? user = await _userStore.FindForPasswordChangeAsync(userId, cancellationToken);
    if (user is null)
    {
      return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Not signed in");
    }

    // A demo sandbox never gets a password, so it can't outlive its expiry as a real account.
    if (user.IsDemo)
    {
      return Problem(
        statusCode: StatusCodes.Status403Forbidden,
        title: "Not for demo accounts",
        detail: "Demo accounts can't have a password: they're removed when the demo ends.");
    }

    if (user.PasswordHash is null
      || _passwordHasher.Verify(request.CurrentPassword, user.PasswordHash) == PasswordVerification.Failed)
    {
      // Counts toward lockout like a failed sign-in: a stolen session mustn't be a way to
      // guess the password without limit. A lockout ends this session too (the session check).
      await _userStore.RecordFailedSignInAsync(userId, _authOptions.MaxFailedSignIns, _authOptions.LockoutMinutes, cancellationToken);
      _logger.LogInformation("Password change failed for user {UserId}: wrong current password.", userId);

      // 400, not 401: the session is still fine, and a 401 would make the app sign out.
      return Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Password not changed",
        detail: "The current password is incorrect.");
    }

    string? problem = PasswordRules.Check(request.NewPassword, user.Email);
    if (problem is not null)
    {
      return PasswordRejected(problem);
    }

    byte[] newStamp = RandomNumberGenerator.GetBytes(16);
    bool changed = await _userStore.ChangePasswordAsync(
      userId, user.PasswordHash, _passwordHasher.Hash(request.NewPassword), newStamp, cancellationToken);
    if (!changed)
    {
      // The password changed in between (another tab, a reset link). Ask to try again.
      return Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Password not changed",
        detail: "Your password was changed somewhere else a moment ago. Sign in again and retry.");
    }

    await ForgetSessionsAsync(userId, cancellationToken);
    await SessionCookie.IssueAsync(HttpContext, userId, AuthClaims.GetTenantId(User), newStamp);
    _logger.LogInformation("User {UserId} changed their password. Other sessions ended.", userId);
    return NoContent();
  }

  private async Task ForgetSessionsAsync(int userId, CancellationToken cancellationToken)
  {
    IReadOnlyList<int> tenantIds = await _userStore.GetMembershipTenantIdsAsync(userId, cancellationToken);
    SessionCache.Forget(_sessionCache, userId, tenantIds);
  }

  private ObjectResult InvalidLink()
  {
    return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Reset link not valid", detail: InvalidLinkDetail);
  }

  private ObjectResult PasswordRejected(string problem)
  {
    return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Password not accepted", detail: problem);
  }
}
