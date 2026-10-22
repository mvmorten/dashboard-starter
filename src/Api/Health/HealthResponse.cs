using System;

namespace DashboardStarter.Api.Health;

/// <summary>Response body for GET /api/health.</summary>
/// <param name="Status">"Healthy" if the API can reach the database, otherwise "Unhealthy".</param>
/// <param name="CheckedAt">When the check ran, in UTC.</param>
public sealed record HealthResponse(string Status, DateTime CheckedAt, DatabaseHealth Database);

/// <param name="Reachable">Whether a query against the app database succeeded.</param>
/// <param name="ServerVersion">SQL Server's product version, such as "17.0.4005.7".</param>
/// <param name="SchemaVersion">The latest applied migration, such as "0004_security_schema.sql".</param>
/// <param name="AppliedMigrations">How many migrations the database has recorded.</param>
/// <param name="ExpectedMigrations">How many migrations this build of the API knows about.</param>
public sealed record DatabaseHealth(
  bool Reachable,
  string? ServerVersion,
  string? SchemaVersion,
  int AppliedMigrations,
  int ExpectedMigrations);
