using System;
using System.Threading.Tasks;
using DashboardStarter.Api.Database;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

    app.MapControllers();
    await app.RunAsync();
    return 0;
  }
}
