using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using DashboardStarter.Api.Tenants;

namespace DashboardStarter.Api.Auth;

/// <summary>Body of POST /api/auth/sign-in.</summary>
/// <remarks>
/// The lengths only bound what the API will spend time on. They're generous on purpose:
/// a too-long value is rejected before any database lookup or hashing.
/// </remarks>
public sealed record SignInRequest(
  [Required, MaxLength(256)] string Email,
  [Required, MaxLength(1024)] string Password);

/// <summary>Body of POST /api/auth/switch-tenant.</summary>
public sealed record SwitchTenantRequest(int TenantId);

/// <summary>
/// GET /api/auth/me, and the answer to sign-in and switch-tenant: who is signed in, the
/// tenants they can work in, which one they're in now (null if they belong to none), and
/// their role and permissions there.
/// </summary>
/// <param name="Role">The role's name in the current tenant, for display. The front end never compares it.</param>
/// <param name="Permissions">
/// What the user may do in the current tenant, sorted. The front end uses it to hide what
/// they can't use (can() in src/Web/src/auth/permissions.ts); the API checks it again on
/// every request.
/// </param>
public sealed record MeResponse(
  MeUser User,
  IReadOnlyList<TenantResponse> Tenants,
  int? CurrentTenantId,
  string? Role,
  IReadOnlyList<string> Permissions);

public sealed record MeUser(int Id, string Email, string DisplayName, bool IsDemo);

/// <summary>GET /api/auth/sign-in-info: what the sign-in page shows before anyone signs in.</summary>
/// <param name="Demo">Demo sandboxes, when this deployment has them ("Try the demo"), or null.</param>
/// <param name="EmailEnabled">Whether password reset emails can be sent; the page hides "Forgot password?" otherwise.</param>
public sealed record SignInInfoResponse(DemoInfo? Demo, bool EmailEnabled);

/// <summary>How long a demo sandbox lasts, so the page can tell visitors before they start.</summary>
public sealed record DemoInfo(int SandboxHours, int SandboxIdleMinutes);

/// <summary>Body of POST /api/auth/forgot-password.</summary>
public sealed record ForgotPasswordRequest([Required, MaxLength(256)] string Email);

/// <summary>Body of POST /api/auth/reset-password: the token from the email link, and the new password.</summary>
/// <remarks>The password's real rules (PasswordRules) are checked in the controller, with a message that says which one.</remarks>
public sealed record ResetPasswordRequest(
  [Required, MaxLength(256)] string Token,
  [Required, MaxLength(1024)] string NewPassword);

/// <summary>Body of POST /api/auth/change-password.</summary>
public sealed record ChangePasswordRequest(
  [Required, MaxLength(1024)] string CurrentPassword,
  [Required, MaxLength(1024)] string NewPassword);
