using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DashboardStarter.Db.Migrations;
using Dapper;
using Microsoft.Data.SqlClient;

namespace DashboardStarter.Db.Scripts;

/// <summary>
/// Runs one script, batch by batch, inside a single transaction.
/// Either every batch runs (and, for a migration, the journal row is written) or nothing does.
/// </summary>
internal static class ScriptExecutor
{
  // Generous on purpose: a migration that rebuilds an index on a large table can take minutes.
  private const int CommandTimeoutSeconds = 600;

  /// <summary>
  /// Runs the script. Returns null on success, or a one-line description of the failure.
  /// </summary>
  public static async Task<string?> TryRunAsync(SqlConnection connection, SqlScript script, bool recordInJournal)
  {
    IReadOnlyList<string> batches = BatchSplitter.Split(script.Text);
    await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync();

    for (int index = 0; index < batches.Count; index++)
    {
      try
      {
        await connection.ExecuteAsync(batches[index], transaction: transaction, commandTimeout: CommandTimeoutSeconds);
      }
      catch (SqlException exception)
      {
        // Disposing the transaction rolls back the batches that already ran.
        return $"batch {index + 1} of {batches.Count}, line {exception.LineNumber}: {exception.Message}";
      }
    }

    if (recordInJournal)
    {
      await SchemaJournal.RecordAsync(connection, transaction, script.Name, script.Hash);
    }

    await transaction.CommitAsync();
    return null;
  }

  public static void PrintResult(string status, string scriptName, string? detail = null)
  {
    string line = $"  {status,-8} {scriptName}";
    if (detail is not null)
    {
      line += $" ({detail})";
    }

    Console.WriteLine(line);
  }
}
