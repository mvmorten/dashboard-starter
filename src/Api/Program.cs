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
      Console.Error.WriteLine(
        $"No connection string. Environment is '{builder.Environment.EnvironmentName}', so set ConnectionStrings__Default.");
      return 1;
    }

    SqlConnectionFactory connectionFactory = new(connectionString);
    builder.Services.AddSingleton(connectionFactory);
    builder.Services.AddControllers();

    WebApplication app = builder.Build();

    bool canStart = await StartupSchemaCheck.RunAsync(connectionFactory, app.Logger);
    if (!canStart)
    {
      return 1;
    }

    app.MapControllers();
    await app.RunAsync();
    return 0;
  }
}
