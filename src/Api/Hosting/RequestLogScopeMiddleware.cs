using System.Threading.Tasks;
using DashboardStarter.Api.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DashboardStarter.Api.Hosting;

/// <summary>
/// Adds the signed-in user and their current tenant to every log message written while
/// handling a request, so "which user hit this error, in which tenant?" is answered by the
/// log line itself. Runs right after authentication, which is when they're known.
/// </summary>
/// <remarks>
/// Only ids: never names, emails, passwords, hashes, tokens, or cookies. The console logger
/// prints scopes because Logging:Console:FormatterOptions:IncludeScopes is on in appsettings.json.
/// </remarks>
public sealed class RequestLogScopeMiddleware
{
  private readonly RequestDelegate _next;
  private readonly ILogger<RequestLogScopeMiddleware> _logger;

  public RequestLogScopeMiddleware(RequestDelegate next, ILogger<RequestLogScopeMiddleware> logger)
  {
    _next = next;
    _logger = logger;
  }

  public async Task InvokeAsync(HttpContext context)
  {
    int? userId = AuthClaims.GetUserId(context.User);
    if (userId is null)
    {
      await _next(context);
      return;
    }

    // A message template, like a log message: the console prints "UserId:5 TenantId:1", and
    // structured log sinks get UserId and TenantId as named values.
    using (_logger.BeginScope("UserId:{UserId} TenantId:{TenantId}", userId, AuthClaims.GetTenantId(context.User)))
    {
      await _next(context);
    }
  }
}
