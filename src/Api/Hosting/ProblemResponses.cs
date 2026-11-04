using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DashboardStarter.Api.Hosting;

/// <summary>
/// Writes a ProblemDetails body from middleware and event handlers, where there's no
/// controller to return one. Same shape as the API's other errors.
/// </summary>
public static class ProblemResponses
{
  public static Task WriteAsync(HttpContext context, int status, string title, string? detail = null)
  {
    context.Response.StatusCode = status;
    ProblemDetails problem = new()
    {
      Status = status,
      Title = title,
      Detail = detail,
    };

    return context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json");
  }
}
