using System;
using System.Threading.Tasks;
using DashboardStarter.Api.Database;
using DashboardStarter.Api.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DashboardStarter.Api;

public static class Program
{
  public static async Task<int> Main(string[] args)
  {
    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    // Development: appsettings.Development.json. Anywhere else: the ConnectionStrings__Default
    // environment variable. appsettings.json deliberately has no connection string.
    string? connectionString = builder.Configuration.GetConnectionString("Default");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
      // Logging isn't set up until the app is built, so this one message goes to stderr.
      Console.Error.WriteLine(
        $"No connection string. Environment is '{builder.Environment.EnvironmentName}', so set ConnectionStrings__Default.");
      return 1;
    }

    builder.Services.AddSingleton(new SqlConnectionFactory(connectionString));
    builder.Services.AddSingleton<StartupSchemaCheck>();
    builder.Services.AddControllers();

    WebApplication app = builder.Build();

    bool canStart = await app.Services.GetRequiredService<StartupSchemaCheck>().RunAsync();
    if (!canStart)
    {
      return 1;
    }

    // Production: one host name for the whole site (see CanonicalHostMiddleware).
    // Empty in Development, so nothing is redirected locally.
    string? canonicalHost = app.Configuration["CanonicalHost"];
    if (!string.IsNullOrWhiteSpace(canonicalHost))
    {
      app.UseMiddleware<CanonicalHostMiddleware>(canonicalHost);
    }

    // Outside Development the API also serves the React app: the deploy pipeline copies
    // the Vite build (src/Web/dist) into wwwroot, so the browser gets the page and the
    // API from one origin, just like the Vite proxy gives it locally.
    // In Development the Vite dev server serves the front end, so there's no wwwroot.
    if (!app.Environment.IsDevelopment())
    {
      app.UseDefaultFiles();
      app.UseStaticFiles();
    }

    app.MapControllers();

    // Deep links and refresh: /health is a React route, not a file, so the server answers it
    // with index.html and the router takes over. {*path:nonfile} skips anything that looks like
    // a file (/missing.js stays a real 404), and unknown /api/* paths never get here because
    // ApiNotFoundController answers them first. This is endpoint routing plumbing, not a
    // minimal API endpoint.
    if (!app.Environment.IsDevelopment())
    {
      app.MapFallbackToFile("{*path:nonfile}", "index.html");
    }

    await app.RunAsync();
    return 0;
  }
}
