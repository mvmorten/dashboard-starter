using System.Threading;
using System.Threading.Tasks;
using DashboardStarter.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DashboardStarter.Api.Demo;

/// <summary>
/// "Try the demo": creates a private sandbox for the visitor and signs them in to it.
/// </summary>
/// <remarks>
/// <para>
/// Anonymous, and it writes to the database, so it's bounded three ways: the global
/// antiforgery filter (a token from GET /api/auth/antiforgery, like sign-in), a rate limit per
/// client address (<see cref="RateLimitPolicyName"/>), and a cap on active sandboxes
/// (<see cref="DemoOptions.MaxActiveSandboxes"/>).
/// </para>
/// <para>
/// With sandboxes off (the default), it answers 404, like any path that doesn't exist.
/// </para>
/// </remarks>
[ApiController]
[Route("api/demo")]
public sealed class DemoController : ControllerBase
{
  public const string RateLimitPolicyName = "demo-sandbox";

  private readonly SandboxStore _sandboxes;
  private readonly DemoOptions _options;
  private readonly ILogger<DemoController> _logger;

  public DemoController(SandboxStore sandboxes, IOptions<DemoOptions> options, ILogger<DemoController> logger)
  {
    _sandboxes = sandboxes;
    _options = options.Value;
    _logger = logger;
  }

  /// <summary>Creates a sandbox and signs in to it. Answers 204; the app then reads GET /api/auth/me.</summary>
  [AllowAnonymous]
  [HttpPost("sandbox")]
  [EnableRateLimiting(RateLimitPolicyName)]
  public async Task<IActionResult> CreateSandbox(CancellationToken cancellationToken)
  {
    if (!_options.SandboxesEnabled)
    {
      return Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found");
    }

    SandboxCreated? sandbox = await _sandboxes.CreateAsync(cancellationToken);
    if (sandbox is null)
    {
      _logger.LogWarning("Refused a new demo sandbox: {Max} are already active.", _options.MaxActiveSandboxes);
      return Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "The demo is busy",
        detail: "The demo is busy, try again later.");
    }

    // Whoever was signed in on this browser before is signed out: the new cookie replaces theirs.
    await SessionCookie.IssueAsync(HttpContext, sandbox.UserId, sandbox.TenantId, sandbox.SecurityStamp);
    _logger.LogInformation("Created demo sandbox for user {UserId}, starting in tenant {TenantId}.", sandbox.UserId, sandbox.TenantId);
    return NoContent();
  }
}
