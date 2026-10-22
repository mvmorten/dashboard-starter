using System;

namespace DashboardStarter.Db.Migrations;

/// <summary>
/// A row in dbo.SchemaVersions: a migration that has run against this database.
/// </summary>
public sealed record AppliedMigration(string ScriptName, string ContentHash, DateTime AppliedAt);
