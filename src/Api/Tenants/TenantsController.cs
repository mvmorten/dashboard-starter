using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DashboardStarter.Api.Database;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace DashboardStarter.Api.Tenants;

/// <summary>
/// TEMPORARY STUB (step 4). Lists every tenant so the app shell's tenant switcher has real data.
/// There is no sign-in yet, so there is nobody to scope the list to, and this endpoint does
/// NOT enforce tenant isolation. Step 5 replaces the query with "the tenants the signed-in user
/// belongs to" (a join through dbo.TenantUsers) and requires authentication.
/// </summary>
/// <remarks>
/// dbo.Tenants is a global table (no TenantId, no Row-Level Security). A deployment that was
/// never seeded has no rows, so this returns an empty list there.
/// </remarks>
[ApiController]
[Route("api/tenants")]
public sealed class TenantsController : ControllerBase
{
  private readonly SqlConnectionFactory _connectionFactory;

  public TenantsController(SqlConnectionFactory connectionFactory)
  {
    _connectionFactory = connectionFactory;
  }

  [HttpGet]
  public async Task<ActionResult<IReadOnlyList<TenantResponse>>> List(CancellationToken cancellationToken)
  {
    const string sql = """
      SELECT Id, Name
      FROM dbo.Tenants
      ORDER BY Name;
      """;

    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    IEnumerable<TenantResponse> tenants = await connection.QueryAsync<TenantResponse>(
      new CommandDefinition(sql, cancellationToken: cancellationToken));

    return Ok(tenants.ToList());
  }
}
