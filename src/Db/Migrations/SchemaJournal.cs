using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;

namespace DashboardStarter.Db.Migrations;

/// <summary>
/// Reads and writes dbo.SchemaVersions, the record of which migrations have run.
/// The runner creates this table itself; it isn't a migration, because it has to
/// exist before the first migration can be recorded.
/// </summary>
public static class SchemaJournal
{
  public static async Task EnsureCreatedAsync(SqlConnection connection)
  {
    const string sql = """
      IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
      BEGIN
        CREATE TABLE dbo.SchemaVersions
        (
          Id int IDENTITY(1, 1) NOT NULL,
          ScriptName nvarchar(255) NOT NULL,
          ContentHash char(64) NOT NULL,
          AppliedAt datetime2 NOT NULL CONSTRAINT DF_SchemaVersions_AppliedAt DEFAULT SYSUTCDATETIME(),
          CONSTRAINT PK_SchemaVersions PRIMARY KEY CLUSTERED (Id),
          CONSTRAINT UQ_SchemaVersions_ScriptName UNIQUE (ScriptName)
        );
      END
      """;

    await connection.ExecuteAsync(sql);
  }

  /// <summary>
  /// Returns the applied migrations in name order, or an empty list if the
  /// journal table doesn't exist yet (nothing has been migrated).
  /// </summary>
  public static async Task<IReadOnlyList<AppliedMigration>> ReadAsync(SqlConnection connection)
  {
    const string existsSql = """
      SELECT CASE WHEN OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL THEN 0 ELSE 1 END;
      """;

    bool exists = await connection.ExecuteScalarAsync<bool>(existsSql);
    if (!exists)
    {
      return [];
    }

    const string sql = """
      SELECT ScriptName, ContentHash, AppliedAt
      FROM dbo.SchemaVersions
      ORDER BY ScriptName;
      """;

    IEnumerable<AppliedMigration> rows = await connection.QueryAsync<AppliedMigration>(sql);
    return rows.ToList();
  }

  public static async Task RecordAsync(SqlConnection connection, IDbTransaction transaction, string scriptName, string contentHash)
  {
    const string sql = """
      INSERT INTO dbo.SchemaVersions (ScriptName, ContentHash)
      VALUES (@ScriptName, @ContentHash);
      """;

    await connection.ExecuteAsync(sql, new { ScriptName = scriptName, ContentHash = contentHash }, transaction);
  }
}
