using System;
using System.Collections.Generic;
using System.Linq;
using DashboardStarter.Db.Scripts;

namespace DashboardStarter.Db.Migrations;

/// <summary>
/// A migration whose file no longer matches what was applied.
/// </summary>
public sealed record ChangedMigration(string ScriptName, string AppliedHash, string CurrentHash);

/// <summary>
/// Compares the migrations compiled into this build with the ones recorded in the database.
/// Used by the runner before it applies anything, and by the API at startup.
/// </summary>
public sealed class SchemaStatus
{
  private SchemaStatus(
    IReadOnlyList<SqlScript> pending,
    IReadOnlyList<ChangedMigration> changed,
    IReadOnlyList<string> unknown)
  {
    Pending = pending;
    Changed = changed;
    Unknown = unknown;
  }

  /// <summary>Migrations in this build that haven't run yet.</summary>
  public IReadOnlyList<SqlScript> Pending { get; }

  /// <summary>Applied migrations whose file has been edited since.</summary>
  public IReadOnlyList<ChangedMigration> Changed { get; }

  /// <summary>Applied migrations that aren't in this build (deleted, renamed, or from a newer build).</summary>
  public IReadOnlyList<string> Unknown { get; }

  public static SchemaStatus Compare(IReadOnlyList<SqlScript> migrations, IReadOnlyList<AppliedMigration> applied)
  {
    Dictionary<string, AppliedMigration> appliedByName = applied.ToDictionary(
      migration => migration.ScriptName,
      StringComparer.Ordinal);

    List<SqlScript> pending = new();
    List<ChangedMigration> changed = new();

    foreach (SqlScript script in migrations)
    {
      if (!appliedByName.TryGetValue(script.Name, out AppliedMigration? appliedMigration))
      {
        pending.Add(script);
        continue;
      }

      if (appliedMigration.ContentHash != script.Hash)
      {
        changed.Add(new ChangedMigration(script.Name, appliedMigration.ContentHash, script.Hash));
      }
    }

    HashSet<string> known = migrations.Select(script => script.Name).ToHashSet(StringComparer.Ordinal);
    List<string> unknown = applied
      .Select(migration => migration.ScriptName)
      .Where(name => !known.Contains(name))
      .ToList();

    return new SchemaStatus(pending, changed, unknown);
  }
}
