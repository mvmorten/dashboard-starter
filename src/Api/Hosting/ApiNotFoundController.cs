using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DashboardStarter.Api.Hosting;

/// <summary>
/// Answers any /api/* request that no other controller matched with a JSON 404.
/// Without it, the single-page-app fallback in Program.cs would answer with index.html
/// (HTTP 200, HTML), and a mistyped API call would look like a broken JSON response.
/// </summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class ApiNotFoundController : ControllerBase
{
  // Literal routes such as api/health are more specific than this catch-all, so they win.
  // The method list is open on purpose: an unknown route is a 404 whatever the verb.
  [Route("api/{**path}")]
  public IActionResult NotFoundJson(string? path)
  {
    return NotFound(new ProblemDetails
    {
      Status = StatusCodes.Status404NotFound,
      Title = "Not found",
      Detail = "No API endpoint matches this request.",
    });
  }
}
