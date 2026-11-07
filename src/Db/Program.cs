using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace DashboardStarter.Db;

/// <summary>
/// The schema runner. The only thing in this repo that changes the database schema.
/// It also creates user accounts until the app has screens for that.
/// </summary>
public static class Program
{
  private const string Usage = """
    Usage:
      dotnet run --project src/Db -- migrate   Create the database if missing, apply new migrations, rerun programmable objects.
      dotnet run --project src/Db -- seed      Load development data from sql/seed and the demo account (Development only).
      dotnet run --project src/Db -- app-user  Create or update the API's login (APP_DB_USER, APP_DB_PASSWORD): data access only.

      dotnet run --project src/Db -- create-user --email <email> --name <name> [--tenant <id or name>] [--password-stdin]
          Create a user (or rename an existing one), add them to a tenant, and set their password.
      dotnet run --project src/Db -- set-password --email <email> [--password-stdin]
          Set a user's password. Ends their other sessions and clears any lockout.
      dotnet run --project src/Db -- demo
          Create or update the public demo account (DEMO_PASSWORD, optional DEMO_EMAIL) in the "Demo" and "Other Demo" tenants.

    Passwords are typed at a hidden prompt, or read from standard input with --password-stdin.
    They are never taken as arguments, which end up in shell history.
    """;

  // SQL Server error 4060: "Cannot open database requested by the login."
  private const int CannotOpenDatabase = 4060;

  public static async Task<int> Main(string[] args)
  {
    if (args.Length == 0)
    {
      Console.Error.WriteLine(Usage);
      return 2;
    }

    string[] options = args[1..];

    try
    {
      RunnerSettings settings = RunnerSettings.Load();
      switch (args[0])
      {
        case "migrate":
          CommandArguments.Parse(options, [], []);
          return await MigrateCommand.RunAsync(settings);
        case "seed":
          CommandArguments.Parse(options, [], []);
          return await SeedCommand.RunAsync(settings);
        case "app-user":
          CommandArguments.Parse(options, [], []);
          return await AppUserCommand.RunAsync(settings);
        case "create-user":
          return await CreateUserCommand.RunAsync(settings, options);
        case "set-password":
          return await SetPasswordCommand.RunAsync(settings, options);
        case "demo":
          return await DemoCommand.RunAsync(settings, options);
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
      Console.Error.WriteLine("Is SQL Server running and reachable? For local development, start it with: docker compose up -d --wait");
      return 1;
    }
  }
}
