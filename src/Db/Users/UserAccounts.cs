using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;

namespace DashboardStarter.Db.Users;

/// <summary>
/// The SQL behind create-user, set-password, set-role, and the local account in seed. All of it runs inside the
/// caller's transaction, so a command either does everything or nothing.
/// </summary>
internal static class UserAccounts
{
  public const int MaxEmailLength = 256;
  public const int MaxNameLength = 200;

  /// <summary>
  /// Why create-user, set-password, and set-role won't touch a demo account. A sandbox is the
  /// visitor's own and is removed when it expires; with a password it could outlive that.
  /// </summary>
  public static string DemoRefusal(string email)
  {
    return $"{email} is a demo account from \"Try the demo\". Demo accounts can't have a password or be changed from here; "
      + "they're removed when the demo ends.";
  }

  public static async Task<UserRow?> FindByEmailAsync(SqlConnection connection, SqlTransaction transaction, string email)
  {
    // Users.Email has a case-insensitive collation (checked by migration 0006), so this
    // finds Ann@Example.com when asked for ann@example.com.
    const string sql = """
      SELECT Id, Email, IsDemo, PasswordHash
      FROM dbo.Users
      WHERE Email = @Email;
      """;

    return await connection.QuerySingleOrDefaultAsync<UserRow>(sql, new { Email = email }, transaction);
  }

  public static async Task<int> InsertAsync(SqlConnection connection, SqlTransaction transaction, string email, string displayName, bool isDemo)
  {
    const string sql = """
      INSERT INTO dbo.Users (Email, DisplayName, IsDemo)
      OUTPUT inserted.Id
      VALUES (@Email, @DisplayName, @IsDemo);
      """;

    return await connection.ExecuteScalarAsync<int>(
      sql, new { Email = email, DisplayName = displayName, IsDemo = isDemo }, transaction);
  }

  /// <summary>Sets the display name. Leaves the row alone (UpdatedAt too) when it's already that name.</summary>
  public static async Task UpdateNameAsync(SqlConnection connection, SqlTransaction transaction, int userId, string displayName)
  {
    const string sql = """
      UPDATE dbo.Users
      SET DisplayName = @DisplayName, UpdatedAt = SYSUTCDATETIME()
      WHERE Id = @Id
        AND DisplayName <> @DisplayName COLLATE Latin1_General_BIN2;
      """;

    await connection.ExecuteAsync(sql, new { Id = userId, DisplayName = displayName }, transaction);
  }

  /// <summary>
  /// Sets a new password. Also gives the user a new security stamp, which ends every session
  /// they have open, and clears any lockout.
  /// </summary>
  public static async Task SetPasswordAsync(SqlConnection connection, SqlTransaction transaction, int userId, string passwordHash)
  {
    const string sql = """
      UPDATE dbo.Users
      SET PasswordHash = @PasswordHash,
        PasswordChangedAt = SYSUTCDATETIME(),
        SecurityStamp = @SecurityStamp,
        FailedSignInCount = 0,
        LockoutEndAt = NULL,
        UpdatedAt = SYSUTCDATETIME()
      WHERE Id = @Id;
      """;

    await connection.ExecuteAsync(
      sql,
      new { Id = userId, PasswordHash = passwordHash, SecurityStamp = RandomNumberGenerator.GetBytes(16) },
      transaction);
  }

  /// <summary>Finds a tenant by id ("3") or by exact name ("Acme Corporation").</summary>
  public static async Task<TenantRow> FindTenantAsync(SqlConnection connection, SqlTransaction transaction, string idOrName)
  {
    const string sql = """
      SELECT Id, Name
      FROM dbo.Tenants
      WHERE (@Id IS NOT NULL AND Id = @Id) OR (@Id IS NULL AND Name = @Name);
      """;

    int? id = int.TryParse(idOrName, out int parsed) ? parsed : null;
    List<TenantRow> matches = (await connection.QueryAsync<TenantRow>(
      sql, new { Id = id, Name = idOrName }, transaction)).ToList();

    if (matches.Count == 0)
    {
      throw new RunnerException($"No tenant with id or name '{idOrName}'.");
    }

    if (matches.Count > 1)
    {
      throw new RunnerException($"More than one tenant is named '{idOrName}'. Pass its id instead.");
    }

    return matches[0];
  }

  /// <summary>
  /// Adds the membership with the given role (or the default role, Member, when null), or turns
  /// a disabled one back on. An existing membership keeps its role: changing it goes through
  /// <see cref="Memberships.SetRoleAsync"/>, which protects the tenant's last admin.
  /// Returns what it did.
  /// </summary>
  public static async Task<string> EnsureMembershipAsync(SqlConnection connection, SqlTransaction transaction, int tenantId, int userId, int? roleId)
  {
    const string sql = """
      DECLARE @status varchar(20) = (SELECT Status FROM dbo.TenantUsers WHERE TenantId = @TenantId AND UserId = @UserId);

      IF @status IS NULL
      BEGIN
        INSERT INTO dbo.TenantUsers (TenantId, UserId, Status, RoleId)
        VALUES (@TenantId, @UserId, 'Active', COALESCE(@RoleId, (SELECT Id FROM dbo.Roles WHERE IsDefault = 1)));
        SELECT 'added';
      END
      ELSE IF @status <> 'Active'
      BEGIN
        UPDATE dbo.TenantUsers
        SET Status = 'Active', UpdatedAt = SYSUTCDATETIME()
        WHERE TenantId = @TenantId AND UserId = @UserId;
        SELECT 'enabled';
      END
      ELSE
      BEGIN
        SELECT 'exists';
      END
      """;

    return await connection.ExecuteScalarAsync<string>(
      sql, new { TenantId = tenantId, UserId = userId, RoleId = roleId }, transaction) ?? "exists";
  }

  /// <summary>Finds a role by name ("Admin", "Read-only"; any capitalization).</summary>
  public static async Task<RoleRow> FindRoleAsync(SqlConnection connection, SqlTransaction transaction, string name)
  {
    const string sql = """
      SELECT Id, Name
      FROM dbo.Roles
      WHERE Name = @Name;
      """;

    RoleRow? role = await connection.QuerySingleOrDefaultAsync<RoleRow>(sql, new { Name = name.Trim() }, transaction);
    if (role is not null)
    {
      return role;
    }

    const string namesSql = "SELECT Name FROM dbo.Roles ORDER BY Id;";
    IEnumerable<string> names = await connection.QueryAsync<string>(namesSql, transaction: transaction);
    throw new RunnerException($"No role named '{name}'. The roles are: {string.Join(", ", names)}.");
  }

  public static string NormalizeEmail(string email)
  {
    string trimmed = email.Trim();
    int at = trimmed.IndexOf('@');
    if (at <= 0 || at == trimmed.Length - 1 || trimmed.Length > MaxEmailLength)
    {
      throw new RunnerException($"'{email}' doesn't look like an email address (something@something, at most {MaxEmailLength} characters).");
    }

    return trimmed;
  }

  public static string NormalizeName(string name)
  {
    string trimmed = name.Trim();
    if (trimmed.Length == 0 || trimmed.Length > MaxNameLength)
    {
      throw new RunnerException($"The name must be 1 to {MaxNameLength} characters.");
    }

    return trimmed;
  }
}

internal sealed record UserRow(int Id, string Email, bool IsDemo, string? PasswordHash);

internal sealed record TenantRow(int Id, string Name);

internal sealed record RoleRow(int Id, string Name);
