using System;
using System.Text.Json.Serialization;
using DashboardStarter.Api.Hosting;

namespace DashboardStarter.Api.Health;

/// <summary>Response body for GET /api/health.</summary>
/// <remarks>
/// Outside Development only <see cref="Status"/> and <see cref="Build"/> are sent. Server and
/// schema versions tell an attacker exactly what they're dealing with, so they stay on developer
/// machines. The build is different: it names a commit in a public repo, and the deploy pipeline
/// uses it to confirm the new version is the one answering.
/// </remarks>
/// <param name="Status">"Healthy" if the API can reach the database, otherwise "Unhealthy".</param>
/// <param name="Build">Which build is running. Every field is null in a local build.</param>
/// <param name="CheckedAt">When the check ran, in UTC. Development only.</param>
/// <param name="Database">Database details. Development only.</param>
public sealed record HealthResponse(
  string Status,
  BuildInfo Build,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTime? CheckedAt = null,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DatabaseHealth? Database = null);

/// <param name="Reachable">Whether a query against the app database succeeded.</param>
/// <param name="ServerVersion">SQL Server's product version, such as "17.0.5005.3".</param>
/// <param name="SchemaVersion">The latest applied migration, such as "0004_security_schema.sql".</param>
/// <param name="AppliedMigrations">How many migrations the database has recorded.</param>
/// <param name="ExpectedMigrations">How many migrations this build of the API knows about.</param>
public sealed record DatabaseHealth(
  bool Reachable,
  string? ServerVersion,
  string? SchemaVersion,
  int AppliedMigrations,
  int ExpectedMigrations);
