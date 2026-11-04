using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DashboardStarter.Api.Hosting;

/// <summary>
/// Answers a request for a file that isn't in wwwroot (/missing.js) with a 404.
/// Static files answer the files that exist, and the single-page-app fallback in Program.cs
/// skips anything that looks like a file, so without this no endpoint would match. The
/// fallback policy (signed in) applies even then, and a signed-out visitor would get 401 for
/// a file that simply isn't there, such as an old script name after a deploy.
/// Public, like the files themselves.
/// </summary>
[ApiController]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class MissingFileController : ControllerBase
{
  // api/{**path} in ApiNotFoundController starts with a literal segment, so it is more
  // specific and wins for /api/x.json.
  [Route("{**path:file}")]
  public IActionResult NotFoundFile(string? path)
  {
    return NotFound();
  }
}
