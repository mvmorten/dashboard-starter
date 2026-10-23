using System;
using System.Threading;
using System.Threading.Tasks;
using DashboardStarter.Api.Database;
using DashboardStarter.Api.Hosting;
using DashboardStarter.Db.Scripts;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DashboardStarter.Api.Health;

/// <summary>
/// Reports whether the API is up and can reach the database. Returns 200 when healthy
/// and 503 when the database can't be reached, so a load balancer can use it as-is.
/// Every response says which build is running; server and migration details are Development only.
/// </summary>
[ApiController]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
  private readonly SqlConnectionFactory _connectionFactory;
  private readonly IWebHostEnvironment _environment;
  private readonly ILogger<HealthController> _logger;

  public HealthController(
    SqlConnectionFactory connectionFactory,
    IWebHostEnvironment environment,
    ILogger<HealthController> logger)
  {
    _connectionFactory = connectionFactory;
    _environment = environment;
    _logger = logger;
  }

  [HttpGet]
  public async Task<ActionResult<HealthResponse>> Get(CancellationToken cancellationToken)
  {
    DatabaseHealth database = await CheckDatabaseAsync(cancellationToken);
    string status = database.Reachable ? "Healthy" : "Unhealthy";

    HealthResponse response = _environment.IsDevelopment()
      ? new HealthResponse(status, BuildInfo.Current, DateTime.UtcNow, database)
      : new HealthResponse(status, BuildInfo.Current);

    if (!database.Reachable)
    {
      return StatusCode(StatusCodes.Status503ServiceUnavailable, response);
    }

    return Ok(response);
  }

  private async Task<DatabaseHealth> CheckDatabaseAsync(CancellationToken cancellationToken)
  {
    const string sql = """
      SELECT
        CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) AS ServerVersion,
        (SELECT TOP (1) ScriptName FROM dbo.SchemaVersions ORDER BY ScriptName DESC) AS SchemaVersion,
        (SELECT COUNT(*) FROM dbo.SchemaVersions) AS AppliedMigrations;
      """;

    int expectedMigrations = ScriptCatalog.Migrations.Count;
    try
    {
      await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
      SchemaRow row = await connection.QuerySingleAsync<SchemaRow>(
        new CommandDefinition(sql, cancellationToken: cancellationToken));

      return new DatabaseHealth(true, row.ServerVersion, row.SchemaVersion, row.AppliedMigrations, expectedMigrations);
    }
    catch (SqlException exception)
    {
      _logger.LogWarning(exception, "Health check couldn't reach the database.");
      return new DatabaseHealth(false, null, null, 0, expectedMigrations);
    }
  }

  private sealed record SchemaRow(string ServerVersion, string? SchemaVersion, int AppliedMigrations);
}
