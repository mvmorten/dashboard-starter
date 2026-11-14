using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DashboardStarter.Db.Migrations;
using DashboardStarter.Db.Tenants;
using Dapper;
using Microsoft.Data.SqlClient;

namespace DashboardStarter.Db.Scripts;

/// <summary>
/// Runs one script, batch by batch, inside a single transaction.
/// Either every batch runs (and, for a migration, the journal row is written) or nothing does.
/// </summary>
/// <remarks>
/// A script whose first line is <c>-- for-each-tenant</c> (<see cref="SqlScript.ForEachTenant"/>)
/// runs once per tenant, all in that one transaction, with the tenant in SESSION_CONTEXT
/// (<see cref="TenantSession.ForEachTenantAsync"/>). If it fails for one tenant, it's rolled
/// back for all of them.
/// </remarks>
internal static class ScriptExecutor
{
  // Generous on purpose: a migration that rebuilds an index on a large table can take minutes.
  private const int CommandTimeoutSeconds = 600;

  /// <summary>
  /// Runs the script. <see cref="ScriptResult.Failure"/> is null on success, or a one-line
  /// description of what failed.
  /// </summary>
  public static async Task<ScriptResult> TryRunAsync(SqlConnection connection, SqlScript script, bool recordInJournal)
  {
    IReadOnlyList<string> batches = BatchSplitter.Split(script.Text);
    await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync();

    int? failedTenant = null;
    string? detail = null;
    try
    {
      if (script.ForEachTenant)
      {
        IReadOnlyList<int> tenants = await TenantSession.ForEachTenantAsync(
          connection,
          transaction,
          async tenantId =>
          {
            failedTenant = tenantId;
            await RunBatchesAsync(connection, transaction, batches);
          },
          CancellationToken.None);
        detail = $"for each tenant: {tenants.Count}";
      }
      else
      {
        await RunBatchesAsync(connection, transaction, batches);
      }
    }
    catch (BatchFailedException exception)
    {
      // Disposing the transaction rolls back the batches that already ran.
      string tenant = failedTenant is null ? "" : $"tenant {failedTenant}, ";
      return new ScriptResult(
        $"{tenant}batch {exception.Index + 1} of {batches.Count}, line {exception.Error.LineNumber}: {exception.Error.Message}", null);
    }

    if (recordInJournal)
    {
      await SchemaJournal.RecordAsync(connection, transaction, script.Name, script.Hash);
    }

    await transaction.CommitAsync();
    return new ScriptResult(null, detail);
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

  private static async Task RunBatchesAsync(SqlConnection connection, SqlTransaction transaction, IReadOnlyList<string> batches)
  {
    for (int index = 0; index < batches.Count; index++)
    {
      try
      {
        await connection.ExecuteAsync(batches[index], transaction: transaction, commandTimeout: CommandTimeoutSeconds);
      }
      catch (SqlException exception)
      {
        throw new BatchFailedException(index, exception);
      }
    }
  }

  private sealed class BatchFailedException : Exception
  {
    public BatchFailedException(int index, SqlException error)
      : base(error.Message, error)
    {
      Index = index;
      Error = error;
    }

    public int Index { get; }

    public SqlException Error { get; }
  }
}

/// <param name="Failure">Null on success; otherwise where and why the script failed.</param>
/// <param name="Detail">Extra words for the result line, like how many tenants a per-tenant script ran for.</param>
internal sealed record ScriptResult(string? Failure, string? Detail);
