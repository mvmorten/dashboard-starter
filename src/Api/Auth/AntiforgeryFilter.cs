using System.Threading.Tasks;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace DashboardStarter.Api.Auth;

/// <summary>
/// Checks the antiforgery token on every POST, PUT, PATCH, and DELETE to a controller.
/// Registered once, globally, in Program.cs, so a new endpoint can't forget it.
/// </summary>
/// <remarks>
/// ASP.NET Core's own [AutoValidateAntiforgeryToken] does the same, but it needs MVC's view
/// services (AddControllersWithViews), which an API with no views shouldn't carry. This is
/// the part of it we use: safe methods pass, everything else must carry a valid token pair.
/// It runs as an authorization filter, after the authorization policy, so an anonymous call
/// to a protected endpoint still gets 401 rather than 400.
/// </remarks>
public sealed class AntiforgeryFilter : IAsyncAuthorizationFilter
{
  private readonly IAntiforgery _antiforgery;
  private readonly ILogger<AntiforgeryFilter> _logger;

  public AntiforgeryFilter(IAntiforgery antiforgery, ILogger<AntiforgeryFilter> logger)
  {
    _antiforgery = antiforgery;
    _logger = logger;
  }

  public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
  {
    string method = context.HttpContext.Request.Method;
    if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method))
    {
      return;
    }

    // An earlier filter (authorization) already answered, with a 401 or 403.
    if (context.Result is not null)
    {
      return;
    }

    try
    {
      await _antiforgery.ValidateRequestAsync(context.HttpContext);
    }
    catch (AntiforgeryValidationException exception)
    {
      // Name the action from our own route table, not the raw method and path: those come from
      // the request, and a decoded %0A in the path would start a fake line in plain-text logs.
      _logger.LogInformation("Rejected a request to {Action}: {Reason}", context.ActionDescriptor.DisplayName, exception.Message);
      context.Result = new BadRequestObjectResult(new ProblemDetails
      {
        Status = StatusCodes.Status400BadRequest,
        Title = "Missing or invalid antiforgery token",
        Detail = "Fetch a token from GET /api/auth/antiforgery and send it in the X-XSRF-TOKEN header.",
      });
    }
  }
}
