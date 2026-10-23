using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace DashboardStarter.Db;

/// <summary>
/// Where the runner connects, and which environment it thinks it's in.
/// </summary>
/// <remarks>
/// Read straight from environment variables, using the same names ASP.NET Core uses,
/// so one setting works for both the runner and the API:
/// <list type="bullet">
/// <item>DOTNET_ENVIRONMENT: "Development" when started with <c>dotnet run</c> (from Properties/launchSettings.json, which overrides the variable; pass <c>--no-launch-profile</c> to use the real environment). Unset means Production.</item>
/// <item>ConnectionStrings__Default: the connection string. Required outside Development.</item>
/// </list>
/// </remarks>
internal sealed class RunnerSettings
{
  // Local development container only. The password must match MSSQL_SA_PASSWORD in .env
  // (copied from .env.example). Used only when DOTNET_ENVIRONMENT is Development and
  // ConnectionStrings__Default isn't set. TrustServerCertificate is needed because the
  // container uses a self-signed certificate. 127.0.0.1 rather than localhost: on Windows,
  // SqlClient connections to "localhost" time out behind Docker Desktop's WSL port relay.
  private const string LocalDevelopmentConnectionString =
    "Server=127.0.0.1,1433;Database=DashboardStarter;User Id=sa;Password=LocalDevOnly!ChangeMe1;Encrypt=True;TrustServerCertificate=True";

  private RunnerSettings(string environmentName, string connectionString, string serverName, string databaseName)
  {
    EnvironmentName = environmentName;
    ConnectionString = connectionString;
    ServerName = serverName;
    DatabaseName = databaseName;
  }

  public string EnvironmentName { get; }

  public string ConnectionString { get; }

  public string ServerName { get; }

  public string DatabaseName { get; }

  public bool IsDevelopment => string.Equals(EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase);

  public static RunnerSettings Load()
  {
    string environmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
    bool isDevelopment = string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);

    string? connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
      if (!isDevelopment)
      {
        throw new RunnerException(
          $"No connection string. Environment is '{environmentName}', so set the ConnectionStrings__Default environment variable.");
      }

      connectionString = LocalDevelopmentConnectionString;
    }

    SqlConnectionStringBuilder builder;
    try
    {
      builder = new SqlConnectionStringBuilder(connectionString);
    }
    catch (ArgumentException exception)
    {
      throw new RunnerException($"The connection string is invalid: {exception.Message}");
    }

    if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
    {
      throw new RunnerException("The connection string must name a database (Database=...).");
    }

    return new RunnerSettings(environmentName, connectionString, builder.DataSource, builder.InitialCatalog);
  }

  public async Task<SqlConnection> OpenAsync()
  {
    SqlConnection connection = new(ConnectionString);
    try
    {
      await connection.OpenAsync();
      return connection;
    }
    catch
    {
      await connection.DisposeAsync();
      throw;
    }
  }

  public async Task<SqlConnection> OpenMasterAsync()
  {
    SqlConnectionStringBuilder builder = new(ConnectionString) { InitialCatalog = "master" };
    SqlConnection connection = new(builder.ConnectionString);
    try
    {
      await connection.OpenAsync();
      return connection;
    }
    catch
    {
      await connection.DisposeAsync();
      throw;
    }
  }
}
