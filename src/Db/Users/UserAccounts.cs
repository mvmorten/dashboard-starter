using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;

namespace DashboardStarter.Db.Users;

/// <summary>
/// The SQL behind create-user, set-password, demo, and the demo account in seed. All of it runs inside the caller's
/// transaction, so a command either does everything or nothing.
/// </summary>
internal static class UserAccounts
{
  public const int MaxEmailLength = 256;
  public const int MaxNameLength = 200;

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

  /// <summary>
  /// Replaces the stored hash with a stronger one for the same password. Sessions stay
  /// open: the password didn't change.
  /// </summary>
  public static async Task ReplaceHashAsync(SqlConnection connection, SqlTransaction transaction, int userId, string passwordHash)
  {
    const string sql = """
      UPDATE dbo.Users
      SET PasswordHash = @PasswordHash
      WHERE Id = @Id;
      """;

    await connection.ExecuteAsync(sql, new { Id = userId, PasswordHash = passwordHash }, transaction);
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

  /// <summary>Returns the tenant with this name, creating it if there is none.</summary>
  public static async Task<(int Id, bool Created)> EnsureTenantAsync(SqlConnection connection, SqlTransaction transaction, string name)
  {
    const string sql = """
      DECLARE @id int = (SELECT TOP (1) Id FROM dbo.Tenants WHERE Name = @Name ORDER BY Id);
      DECLARE @created bit = 0;

      IF @id IS NULL
      BEGIN
        INSERT INTO dbo.Tenants (Name) VALUES (@Name);
        SET @id = SCOPE_IDENTITY();
        SET @created = 1;
      END

      SELECT @id AS Id, @created AS Created;
      """;

    return await connection.QuerySingleAsync<(int Id, bool Created)>(sql, new { Name = name }, transaction);
  }

  /// <summary>Adds the membership, or turns a disabled one back on. Returns what it did.</summary>
  public static async Task<string> EnsureMembershipAsync(SqlConnection connection, SqlTransaction transaction, int tenantId, int userId)
  {
    const string sql = """
      DECLARE @status varchar(20) = (SELECT Status FROM dbo.TenantUsers WHERE TenantId = @TenantId AND UserId = @UserId);

      IF @status IS NULL
      BEGIN
        INSERT INTO dbo.TenantUsers (TenantId, UserId, Status) VALUES (@TenantId, @UserId, 'Active');
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

    return await connection.ExecuteScalarAsync<string>(sql, new { TenantId = tenantId, UserId = userId }, transaction) ?? "exists";
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
