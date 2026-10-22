namespace DashboardStarter.Db.Scripts;

/// <summary>
/// One .sql file from the /sql folder.
/// </summary>
/// <param name="Name">File name relative to its folder, such as "0001_tenants.sql".</param>
/// <param name="Text">The script, with line endings normalized to LF.</param>
/// <param name="Hash">Lowercase hex SHA-256 of <paramref name="Text"/>.</param>
public sealed record SqlScript(string Name, string Text, string Hash);
