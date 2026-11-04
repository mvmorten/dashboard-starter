using System.Threading;
using System.Threading.Tasks;
using DashboardStarter.Api.Auth;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;

namespace DashboardStarter.Api.Database;

/// <summary>
/// Opens connections to the app database. Every query in the API gets its connection here,
/// which makes it the one place that sets per-connection state: the signed-in user's current
/// tenant in SESSION_CONTEXT, which Row-Level Security filters on.
/// </summary>
/// <remarks>
/// <para>
/// Open a connection per unit of work and dispose it (<c>await using</c>).
/// SqlClient pools the underlying connections, so this is cheap.
/// </para>
/// <para>
/// The tenant comes from the auth cookie's TenantId claim, which the session check has
/// already confirmed is an active membership. It's set read-only, so no query on this
/// connection can switch to another tenant. When a pooled connection is reused, SqlClient
/// resets the session first (sp_reset_connection), which clears SESSION_CONTEXT, so one
/// request's tenant never leaks into the next.
/// </para>
/// <para>
/// No signed-in user, or a user without a tenant (and work outside a request, like the
/// startup check): nothing is set, and Row-Level Security shows no tenant-owned rows at all.
/// Global tables (Users, Tenants, TenantUsers) aren't filtered.
/// </para>
/// </remarks>
public sealed class SqlConnectionFactory
{
  private readonly string _connectionString;
  private readonly IHttpContextAccessor _httpContextAccessor;

  public SqlConnectionFactory(string connectionString, IHttpContextAccessor httpContextAccessor)
  {
    _connectionString = connectionString;
    _httpContextAccessor = httpContextAccessor;

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
      await SetTenantAsync(connection, cancellationToken);
      return connection;
    }
    catch
    {
      await connection.DisposeAsync();
      throw;
    }
  }

  private async Task SetTenantAsync(SqlConnection connection, CancellationToken cancellationToken)
  {
    HttpContext? httpContext = _httpContextAccessor.HttpContext;
    if (httpContext?.User.Identity?.IsAuthenticated != true)
    {
      return;
    }

    int? tenantId = AuthClaims.GetTenantId(httpContext.User);
    if (tenantId is null)
    {
      return;
    }

    const string sql = "EXEC sys.sp_set_session_context @key = N'TenantId', @value = @TenantId, @read_only = 1;";
    await connection.ExecuteAsync(new CommandDefinition(sql, new { TenantId = tenantId.Value }, cancellationToken: cancellationToken));
  }
}
