using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace DashboardStarter.Db;

/// <summary>
/// The schema runner. The only thing in this repo that changes the database schema.
/// </summary>
public static class Program
{
  private const string Usage = """
    Usage:
      dotnet run --project src/Db -- migrate   Create the database if missing, apply new migrations, rerun programmable objects.
      dotnet run --project src/Db -- seed      Load development data from sql/seed (Development only).
      dotnet run --project src/Db -- app-user  Create or update the API's login (APP_DB_USER, APP_DB_PASSWORD): data access only.
    """;

  // SQL Server error 4060: "Cannot open database requested by the login."
  private const int CannotOpenDatabase = 4060;

  public static async Task<int> Main(string[] args)
  {
    if (args.Length != 1)
    {
      Console.Error.WriteLine(Usage);
      return 2;
    }

    try
    {
      RunnerSettings settings = RunnerSettings.Load();
      switch (args[0])
      {
        case "migrate":
          return await MigrateCommand.RunAsync(settings);
        case "seed":
          return await SeedCommand.RunAsync(settings);
        case "app-user":
          return await AppUserCommand.RunAsync(settings);
        default:
          Console.Error.WriteLine($"Unknown command '{args[0]}'.");
          Console.Error.WriteLine(Usage);
          return 2;
      }
    }
    catch (RunnerException exception)
    {
      Console.Error.WriteLine(exception.Message);
      return 1;
    }
    catch (SqlException exception) when (exception.Number == CannotOpenDatabase)
    {
      Console.Error.WriteLine($"SQL Server error: {exception.Message}");
      Console.Error.WriteLine("Create the database first with: dotnet run --project src/Db -- migrate");
      return 1;
    }
    catch (SqlException exception)
    {
      Console.Error.WriteLine($"SQL Server error: {exception.Message}");
      Console.Error.WriteLine("Is SQL Server running? Start it with: docker compose up -d --wait");
      return 1;
    }
  }
}
