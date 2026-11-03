using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DashboardStarter.Api.Database;
using DashboardStarter.Api.Tenants;
using Dapper;
using Microsoft.Data.SqlClient;

namespace DashboardStarter.Api.Auth;

/// <summary>
/// The SQL behind sign-in and sessions. Users, Tenants, and TenantUsers are global tables
/// (no Row-Level Security), so these queries work before anyone has a current tenant.
/// </summary>
public sealed class UserStore
{
  private readonly SqlConnectionFactory _connectionFactory;

  public UserStore(SqlConnectionFactory connectionFactory)
  {
    _connectionFactory = connectionFactory;
  }

  public async Task<SignInCandidate?> FindForSignInAsync(string email, CancellationToken cancellationToken)
  {
    // Email has a case-insensitive collation (checked by migration 0006), so any spelling
    // of the address finds the account. Lockout is decided by the database's clock, the
    // same one that set it. The demo account is never locked out.
    const string sql = """
      SELECT
        Id,
        PasswordHash,
        SecurityStamp,
        IsDemo,
        CAST(CASE WHEN IsDemo = 0 AND LockoutEndAt > SYSUTCDATETIME() THEN 1 ELSE 0 END AS bit) AS IsLockedOut
      FROM dbo.Users
      WHERE Email = @Email;
      """;

    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    return await connection.QuerySingleOrDefaultAsync<SignInCandidate>(
      new CommandDefinition(sql, new { Email = email }, cancellationToken: cancellationToken));
  }

  /// <summary>
  /// Counts a wrong password. On the last allowed failure the account is locked and the
  /// count starts over, so the next lockout needs a fresh run of failures. One statement,
  /// so two attempts at once can't both read the old count.
  /// </summary>
  public async Task RecordFailedSignInAsync(int userId, int maxFailures, int lockoutMinutes, CancellationToken cancellationToken)
  {
    const string sql = """
      UPDATE dbo.Users
      SET
        LockoutEndAt = CASE WHEN FailedSignInCount + 1 >= @MaxFailures
          THEN DATEADD(minute, @LockoutMinutes, SYSUTCDATETIME()) ELSE LockoutEndAt END,
        FailedSignInCount = CASE WHEN FailedSignInCount + 1 >= @MaxFailures
          THEN 0 ELSE FailedSignInCount + 1 END
      WHERE Id = @Id
        AND IsDemo = 0;
      """;

    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    await connection.ExecuteAsync(new CommandDefinition(
      sql,
      new { Id = userId, MaxFailures = maxFailures, LockoutMinutes = lockoutMinutes },
      cancellationToken: cancellationToken));
  }

  /// <summary>
  /// A successful sign-in: clears failures and lockout, and stores a stronger hash if the
  /// password was rehashed. The hash is only replaced if it's still the one we verified
  /// against, so a password changed in the meantime is never overwritten.
  /// </summary>
  public async Task RecordSignInAsync(int userId, string verifiedHash, string? newHash, CancellationToken cancellationToken)
  {
    // BIN2: an exact, case-sensitive comparison. The column's own collation ignores case,
    // which is right for emails but not for base64.
    const string sql = """
      UPDATE dbo.Users
      SET
        FailedSignInCount = 0,
        LockoutEndAt = NULL,
        LastSignInAt = SYSUTCDATETIME(),
        PasswordHash = CASE WHEN @NewHash IS NOT NULL AND PasswordHash = @VerifiedHash COLLATE Latin1_General_BIN2
          THEN @NewHash ELSE PasswordHash END
      WHERE Id = @Id;
      """;

    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    await connection.ExecuteAsync(new CommandDefinition(
      sql,
      new { Id = userId, VerifiedHash = verifiedHash, NewHash = newHash },
      cancellationToken: cancellationToken));
  }

  public async Task<UserSummary?> GetUserAsync(int userId, CancellationToken cancellationToken)
  {
    const string sql = """
      SELECT Id, Email, DisplayName, IsDemo
      FROM dbo.Users
      WHERE Id = @Id;
      """;

    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    return await connection.QuerySingleOrDefaultAsync<UserSummary>(
      new CommandDefinition(sql, new { Id = userId }, cancellationToken: cancellationToken));
  }

  /// <summary>The tenants the user can work in, the one they used last first.</summary>
  public async Task<IReadOnlyList<TenantResponse>> GetActiveTenantsAsync(int userId, CancellationToken cancellationToken)
  {
    const string sql = """
      SELECT t.Id, t.Name
      FROM dbo.TenantUsers AS tu
      INNER JOIN dbo.Tenants AS t ON t.Id = tu.TenantId
      WHERE tu.UserId = @UserId
        AND tu.Status = 'Active'
      ORDER BY
        CASE WHEN tu.LastUsedAt IS NULL THEN 1 ELSE 0 END,
        tu.LastUsedAt DESC,
        t.Name;
      """;

    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    IEnumerable<TenantResponse> tenants = await connection.QueryAsync<TenantResponse>(
      new CommandDefinition(sql, new { UserId = userId }, cancellationToken: cancellationToken));
    return tenants.ToList();
  }

  /// <summary>
  /// Records that the user is now working in this tenant. Returns false, and changes
  /// nothing, unless they have an active membership in it.
  /// </summary>
  public async Task<bool> MarkTenantUsedAsync(int userId, int tenantId, CancellationToken cancellationToken)
  {
    const string sql = """
      UPDATE dbo.TenantUsers
      SET LastUsedAt = SYSUTCDATETIME()
      WHERE UserId = @UserId
        AND TenantId = @TenantId
        AND Status = 'Active';
      """;

    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    int updated = await connection.ExecuteAsync(new CommandDefinition(
      sql, new { UserId = userId, TenantId = tenantId }, cancellationToken: cancellationToken));
    return updated == 1;
  }

  /// <summary>
  /// What the session check compares the cookie against. Null if the user no longer exists.
  /// </summary>
  public async Task<SessionState?> GetSessionStateAsync(int userId, int? tenantId, CancellationToken cancellationToken)
  {
    const string sql = """
      SELECT
        u.SecurityStamp,
        CAST(CASE WHEN u.IsDemo = 0 AND u.LockoutEndAt > SYSUTCDATETIME() THEN 1 ELSE 0 END AS bit) AS IsLockedOut,
        CAST(CASE
          WHEN @TenantId IS NULL THEN 1
          WHEN EXISTS (
            SELECT 1
            FROM dbo.TenantUsers AS tu
            WHERE tu.UserId = u.Id
              AND tu.TenantId = @TenantId
              AND tu.Status = 'Active'
          ) THEN 1
          ELSE 0
        END AS bit) AS IsTenantMember
      FROM dbo.Users AS u
      WHERE u.Id = @UserId;
      """;

    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    return await connection.QuerySingleOrDefaultAsync<SessionState>(
      new CommandDefinition(sql, new { UserId = userId, TenantId = tenantId }, cancellationToken: cancellationToken));
  }
}

public sealed record SignInCandidate(int Id, string? PasswordHash, byte[] SecurityStamp, bool IsDemo, bool IsLockedOut);

public sealed record UserSummary(int Id, string Email, string DisplayName, bool IsDemo);

public sealed record SessionState(byte[] SecurityStamp, bool IsLockedOut, bool IsTenantMember);
