using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;

namespace DashboardStarter.Api.Hosting;

/// <summary>
/// Sends every request that arrives on another host name to the one canonical host,
/// for example myapp.azurewebsites.net and old-name.example.com to app.example.com.
/// </summary>
/// <remarks>
/// <para>
/// Why it matters: cookies belong to a single host. Without this, someone who signs in on
/// one host name is signed out on the other, and search engines index the same page twice.
/// </para>
/// <para>
/// Only active when the <c>CanonicalHost</c> setting is set (the <c>CanonicalHost</c>
/// environment variable on the server). Local development leaves it empty.
/// </para>
/// <para>
/// <c>/api/health</c> is answered on any host, so the deploy pipeline and Azure's own
/// probes can check the app by its default host name before DNS points at it.
/// </para>
/// </remarks>
public sealed class CanonicalHostMiddleware
{
  private readonly RequestDelegate _next;
  private readonly string _canonicalHost;

  public CanonicalHostMiddleware(RequestDelegate next, string canonicalHost)
  {
    _next = next;
    _canonicalHost = canonicalHost;
  }

  public Task InvokeAsync(HttpContext context)
  {
    if (string.Equals(context.Request.Host.Host, _canonicalHost, StringComparison.OrdinalIgnoreCase))
    {
      return _next(context);
    }

    if (context.Request.Path.StartsWithSegments("/api/health"))
    {
      return _next(context);
    }

    // Always https: the hosting platform terminates TLS, and the app only serves
    // the public site over HTTPS. Path and query string carry over unchanged.
    string target = UriHelper.BuildAbsolute(
      "https",
      new HostString(_canonicalHost),
      context.Request.PathBase,
      context.Request.Path,
      context.Request.QueryString);
    context.Response.Redirect(target, permanent: true);
    return Task.CompletedTask;
  }
}
