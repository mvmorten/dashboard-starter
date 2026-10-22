using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace DashboardStarter.Api.Database;

/// <summary>
/// Opens connections to the app database. Every query in the API gets its connection here,
/// which gives us one place to set per-connection state later (the tenant in SESSION_CONTEXT
/// for Row-Level Security, from step 7).
/// </summary>
/// <remarks>
/// Open a connection per unit of work and dispose it (<c>await using</c>).
/// SqlClient pools the underlying connections, so this is cheap.
/// </remarks>
public sealed class SqlConnectionFactory
{
  private readonly string _connectionString;

  public SqlConnectionFactory(string connectionString)
  {
    _connectionString = connectionString;

    SqlConnectionStringBuilder builder = new(connectionString);
    ServerName = builder.DataSource;
    DatabaseName = builder.InitialCatalog;
  }

  /// <summary>The server the connection string points at, for log messages. Never includes the password.</summary>
  public string ServerName { get; }

  public string DatabaseName { get; }

  public async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
  {
    SqlConnection connection = new(_connectionString);
    try
    {
      await connection.OpenAsync(cancellationToken);
      return connection;
    }
    catch
    {
      await connection.DisposeAsync();
      throw;
    }
  }
}
