using System;

namespace DashboardStarter.Db.Scripts;

/// <summary>
/// One .sql file from the /sql folder.
/// </summary>
/// <param name="Name">File name relative to its folder, such as "0001_tenants.sql".</param>
/// <param name="Text">The script, with line endings normalized to LF.</param>
/// <param name="Hash">Lowercase hex SHA-256 of <paramref name="Text"/>.</param>
public sealed record SqlScript(string Name, string Text, string Hash)
{
  /// <summary>The first line that makes a script a per-tenant data step.</summary>
  public const string ForEachTenantMarker = "-- for-each-tenant";

  /// <summary>
  /// The script's first line is <c>-- for-each-tenant</c>: the runner runs the whole script once
  /// per tenant, with that tenant in SESSION_CONTEXT (<see cref="Tenants.TenantSession.ForEachTenantAsync"/>).
  /// A data migration on a tenant-owned table needs it: with no tenant set, Row-Level Security
  /// shows it no rows, so an UPDATE would quietly change nothing.
  /// </summary>
  public bool ForEachTenant { get; } = FirstLine(Text).Equals(ForEachTenantMarker, StringComparison.OrdinalIgnoreCase);

  private static string FirstLine(string text)
  {
    int end = text.IndexOf('\n');
    return (end < 0 ? text : text[..end]).Trim();
  }
}
