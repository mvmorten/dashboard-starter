using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DashboardStarter.Api.Database;
using Dapper;
using Microsoft.Data.SqlClient;

namespace DashboardStarter.Api.Members;

/// <summary>
/// The SQL behind the Members page's lists. Changes go through
/// DashboardStarter.Db.Users.Memberships, which the Db runner's set-role uses too, so the
/// last-admin rule is written once.
/// </summary>
/// <remarks>
/// TenantUsers is a global table (no Row-Level Security), so every query here filters by the
/// tenant explicitly. The tenant always comes from the session, never from the browser.
/// </remarks>
public sealed class MemberStore
{
  private readonly SqlConnectionFactory _connectionFactory;

  public MemberStore(SqlConnectionFactory connectionFactory)
  {
    _connectionFactory = connectionFactory;
  }

  public async Task<IReadOnlyList<MemberResponse>> GetMembersAsync(int tenantId, CancellationToken cancellationToken)
  {
    const string sql = """
      SELECT
        u.Id AS UserId,
        u.DisplayName,
        u.Email,
        tu.RoleId,
        r.Name AS RoleName,
        tu.Status,
        u.LastSignInAt,
        tu.RowVersion
      FROM dbo.TenantUsers AS tu
      INNER JOIN dbo.Users AS u ON u.Id = tu.UserId
      INNER JOIN dbo.Roles AS r ON r.Id = tu.RoleId
      WHERE tu.TenantId = @TenantId
      ORDER BY u.DisplayName, u.Email;
      """;

    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    IEnumerable<MemberResponse> members = await connection.QueryAsync<MemberResponse>(
      new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: cancellationToken));
    return members.ToList();
  }

  public async Task<IReadOnlyList<RoleResponse>> GetRolesAsync(CancellationToken cancellationToken)
  {
    const string sql = """
      SELECT Id, Name, Description
      FROM dbo.Roles
      ORDER BY Id;
      """;

    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    IEnumerable<RoleResponse> roles = await connection.QueryAsync<RoleResponse>(
      new CommandDefinition(sql, cancellationToken: cancellationToken));
    return roles.ToList();
  }
}
