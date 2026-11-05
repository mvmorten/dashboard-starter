using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DashboardStarter.Api.Database;
using Dapper;
using Microsoft.Data.SqlClient;

namespace DashboardStarter.Api.Auth;

/// <summary>
/// The SQL behind password reset tokens (dbo.PasswordResetTokens, migration 0008).
/// </summary>
/// <remarks>
/// <para>
/// A token is 32 random bytes from <see cref="RandomNumberGenerator"/>, sent in the email as
/// base64url text. The table keeps only the SHA-256 hash of that text; a link that comes back
/// is hashed the same way and looked up. Someone who can read the table can't use what's in it.
/// </para>
/// <para>
/// The table is global (no tenant, no Row-Level Security): a reset happens before anyone is
/// signed in.
/// </para>
/// </remarks>
public sealed class PasswordResetStore
{
  private readonly SqlConnectionFactory _connectionFactory;

  public PasswordResetStore(SqlConnectionFactory connectionFactory)
  {
    _connectionFactory = connectionFactory;
  }

  /// <summary>A new token: the text for the link, and the hash to store.</summary>
  public static (string Token, byte[] Hash) NewToken()
  {
    string token = System.Buffers.Text.Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
    return (token, Hash(token));
  }

  public static byte[] Hash(string token)
  {
    return SHA256.HashData(Encoding.UTF8.GetBytes(token));
  }

  /// <summary>
  /// Stores a token for the account with this email, if there is one, it isn't the demo
  /// account, and it's under the hourly cap. Returns who to email, or null to send nothing.
  /// </summary>
  /// <remarks>
  /// One batch whatever the outcome, so an unknown address takes the same round trip as a
  /// real one. The user row is locked for the length of the batch, so two requests at once
  /// can't both slip under the cap. Older live tokens are expired, not deleted: rows from the
  /// last hour are how the cap counts.
  /// </remarks>
  public async Task<ResetRecipient?> IssueAsync(string email, byte[] tokenHash, int lifetimeSeconds, int maxPerHour, CancellationToken cancellationToken)
  {
    const string sql = """
      SET XACT_ABORT ON;
      BEGIN TRANSACTION;

      DECLARE @UserId int, @Email nvarchar(256), @Issued bit = 0;

      SELECT @UserId = Id, @Email = Email
      FROM dbo.Users WITH (UPDLOCK, HOLDLOCK)
      WHERE Email = @RequestedEmail
        AND IsDemo = 0;

      -- Older than the cap's hour: no longer counted, and long expired.
      DELETE FROM dbo.PasswordResetTokens
      WHERE UserId = @UserId
        AND CreatedAt < DATEADD(hour, -1, SYSUTCDATETIME());

      IF @UserId IS NOT NULL
        AND (SELECT COUNT(*) FROM dbo.PasswordResetTokens WHERE UserId = @UserId) < @MaxPerHour
      BEGIN
        -- Only the newest link works.
        UPDATE dbo.PasswordResetTokens
        SET ExpiresAt = SYSUTCDATETIME()
        WHERE UserId = @UserId
          AND UsedAt IS NULL
          AND ExpiresAt > SYSUTCDATETIME();

        INSERT INTO dbo.PasswordResetTokens (UserId, TokenHash, ExpiresAt)
        VALUES (@UserId, @TokenHash, DATEADD(second, @LifetimeSeconds, SYSUTCDATETIME()));

        SET @Issued = 1;
      END

      COMMIT TRANSACTION;

      SELECT @UserId AS UserId, @Email AS Email
      WHERE @Issued = 1;
      """;

    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    return await connection.QuerySingleOrDefaultAsync<ResetRecipient>(new CommandDefinition(
      sql,
      new { RequestedEmail = email, TokenHash = tokenHash, LifetimeSeconds = lifetimeSeconds, MaxPerHour = maxPerHour },
      cancellationToken: cancellationToken));
  }

  /// <summary>
  /// The account a token belongs to, if the token is unused and unexpired. Used to check the
  /// new password against the account's email before anything changes; <see cref="ResetPasswordAsync"/>
  /// checks the token again, atomically.
  /// </summary>
  public async Task<ResetRecipient?> FindValidAsync(byte[] tokenHash, CancellationToken cancellationToken)
  {
    const string sql = """
      SELECT u.Id AS UserId, u.Email
      FROM dbo.PasswordResetTokens AS t
      INNER JOIN dbo.Users AS u ON u.Id = t.UserId
      WHERE t.TokenHash = @TokenHash
        AND t.UsedAt IS NULL
        AND t.ExpiresAt > SYSUTCDATETIME()
        AND u.IsDemo = 0;
      """;

    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    return await connection.QuerySingleOrDefaultAsync<ResetRecipient>(
      new CommandDefinition(sql, new { TokenHash = tokenHash }, cancellationToken: cancellationToken));
  }

  /// <summary>
  /// Uses the token and sets the password, in one transaction. Returns the user's id, or null
  /// if the token was already used, expired, or never existed (then nothing changes).
  /// </summary>
  /// <remarks>
  /// Marking the token used is the first statement and only succeeds once, so two requests
  /// with the same link can't both set a password. A new security stamp ends every session;
  /// failures and lockout are cleared, since the person has just proved they own the inbox.
  /// The user's other tokens are deleted; the used one stays until it's an hour old.
  /// </remarks>
  public async Task<int?> ResetPasswordAsync(byte[] tokenHash, string passwordHash, CancellationToken cancellationToken)
  {
    const string sql = """
      SET XACT_ABORT ON;
      BEGIN TRANSACTION;

      DECLARE @Used TABLE (UserId int NOT NULL);

      UPDATE t
      SET UsedAt = SYSUTCDATETIME()
      OUTPUT inserted.UserId INTO @Used
      FROM dbo.PasswordResetTokens AS t
      INNER JOIN dbo.Users AS u ON u.Id = t.UserId
      WHERE t.TokenHash = @TokenHash
        AND t.UsedAt IS NULL
        AND t.ExpiresAt > SYSUTCDATETIME()
        AND u.IsDemo = 0;

      DECLARE @UserId int = (SELECT UserId FROM @Used);

      IF @UserId IS NOT NULL
      BEGIN
        UPDATE dbo.Users
        SET
          PasswordHash = @PasswordHash,
          PasswordChangedAt = SYSUTCDATETIME(),
          SecurityStamp = @SecurityStamp,
          FailedSignInCount = 0,
          LockoutEndAt = NULL,
          UpdatedAt = SYSUTCDATETIME()
        WHERE Id = @UserId;

        DELETE FROM dbo.PasswordResetTokens
        WHERE UserId = @UserId
          AND TokenHash <> @TokenHash;
      END

      COMMIT TRANSACTION;

      SELECT @UserId;
      """;

    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    return await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
      sql,
      new { TokenHash = tokenHash, PasswordHash = passwordHash, SecurityStamp = RandomNumberGenerator.GetBytes(16) },
      cancellationToken: cancellationToken));
  }
}

public sealed record ResetRecipient(int UserId, string Email);
