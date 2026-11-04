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
/// tenants they can work in, and which one they're in now (null if they belong to none).
/// </summary>
public sealed record MeResponse(MeUser User, IReadOnlyList<TenantResponse> Tenants, int? CurrentTenantId);

public sealed record MeUser(int Id, string Email, string DisplayName, bool IsDemo);

/// <summary>GET /api/auth/sign-in-info: what the sign-in page shows before anyone signs in.</summary>
/// <param name="DemoAccount">The public demo's shared account, or null when this deployment has none.</param>
public sealed record SignInInfoResponse(DemoAccount? DemoAccount);

public sealed record DemoAccount(string Email, string Password);
