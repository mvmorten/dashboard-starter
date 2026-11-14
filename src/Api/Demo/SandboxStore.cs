using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DashboardStarter.Api.Database;
using DashboardStarter.Db.Tenants;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DashboardStarter.Api.Demo;

/// <summary>
/// Creates demo sandboxes (<see cref="SandboxDefinition"/>) and keeps them alive while they're
/// used. Removing them is <see cref="SandboxCleanup"/>'s job.
/// </summary>
public sealed class SandboxStore
{
  private readonly SqlConnectionFactory _connectionFactory;
  private readonly DemoOptions _options;

  public SandboxStore(SqlConnectionFactory connectionFactory, IOptions<DemoOptions> options)
  {
    _connectionFactory = connectionFactory;
    _options = options.Value;
  }

  /// <summary>
  /// Creates a sandbox in one transaction: the visitor, the tenants, the sample members, every
  /// membership, and the sample customers. Returns null when <see cref="DemoOptions.MaxActiveSandboxes"/> are
  /// already in use.
  /// </summary>
  public async Task<SandboxCreated?> CreateAsync(CancellationToken cancellationToken)
  {
    // One random suffix for every address in this sandbox: visitor-1a2b3c4d5e6f@demo.invalid.
    // 48 bits, so two sandboxes picking the same one is not a practical worry; the unique
    // index on Users.Email would refuse it anyway.
    string suffix = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6));

    // Signed out, so no tenant is set on this connection. The tables written here are global.
    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

    // Count and insert under one lock, held until commit, so two visitors arriving at once
    // can't both take the last place under the cap.
    const string lockSql = """
      DECLARE @result int;
      EXEC @result = sys.sp_getapplock
        @Resource = N'DemoSandboxCreate', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
      SELECT @result;
      """;
    int lockResult = await connection.ExecuteScalarAsync<int>(Command(lockSql, null, transaction, cancellationToken));
    if (lockResult < 0)
    {
      throw new InvalidOperationException($"Couldn't get the sandbox creation lock (sp_getapplock returned {lockResult}).");
    }

    // A sandbox is one row per tenant in the definition, so count tenants and compare with
    // the cap times that number. Expired ones don't count, even before cleanup removes them.
    const string countSql = """
      SELECT COUNT(*)
      FROM dbo.Tenants
      WHERE IsDemo = 1
        AND ExpiresAt > SYSUTCDATETIME();
      """;
    int activeTenants = await connection.ExecuteScalarAsync<int>(Command(countSql, null, transaction, cancellationToken));
    if (activeTenants >= _options.MaxActiveSandboxes * SandboxDefinition.Tenants.Count)
    {
      return null;
    }

    // A new sandbox starts with the idle allowance; using it extends that (KeepAliveAsync).
    int minutes = Math.Min(_options.SandboxIdleMinutes, _options.SandboxHours * 60);
    DateTime expiresAt = await connection.ExecuteScalarAsync<DateTime>(Command(
      "SELECT DATEADD(minute, @Minutes, SYSUTCDATETIME());", new { Minutes = minutes }, transaction, cancellationToken));

    (int visitorId, byte[] securityStamp) = await InsertUserAsync(
      connection, transaction, $"visitor-{suffix}@{SandboxDefinition.EmailDomain}", SandboxDefinition.VisitorName, expiresAt, cancellationToken);

    Dictionary<string, int> tenantIds = new(StringComparer.Ordinal);
    foreach (string tenantName in SandboxDefinition.Tenants)
    {
      tenantIds[tenantName] = await InsertTenantAsync(connection, transaction, tenantName, visitorId, expiresAt, cancellationToken);
    }

    // The visitor starts in their first tenant ("Demo", where they're Admin), so that one is
    // marked as used, like a sign-in does.
    for (int index = 0; index < SandboxDefinition.Visitor.Count; index++)
    {
      SandboxMembership membership = SandboxDefinition.Visitor[index];
      await InsertMembershipAsync(
        connection, transaction, tenantIds[membership.TenantName], visitorId, membership, visitorId, markUsed: index == 0, cancellationToken);
    }

    foreach (SandboxSampleMember sample in SandboxDefinition.SampleMembers)
    {
      (int sampleId, _) = await InsertUserAsync(
        connection, transaction, $"{sample.EmailName}-{suffix}@{SandboxDefinition.EmailDomain}", sample.DisplayName, expiresAt, cancellationToken);
      foreach (SandboxMembership membership in sample.Memberships)
      {
        await InsertMembershipAsync(
          connection, transaction, tenantIds[membership.TenantName], sampleId, membership, visitorId, markUsed: false, cancellationToken);
      }
    }

    // Customers are tenant-owned: Row-Level Security refuses an insert unless the connection is
    // working in that tenant. This connection belongs to no request (the visitor isn't signed in
    // yet), so it moves from tenant to tenant here, set without read_only. It goes back to the
    // pool when this method ends, and the pool clears SESSION_CONTEXT before anyone reuses it.
    foreach ((string tenantName, IReadOnlyList<SandboxCustomer> customers) in SandboxDefinition.Customers)
    {
      int tenantId = tenantIds[tenantName];
      await TenantSession.SetAsync(connection, transaction, tenantId, readOnly: false, cancellationToken);
      await InsertCustomersAsync(connection, transaction, tenantId, customers, visitorId, cancellationToken);
    }

    await transaction.CommitAsync(cancellationToken);
    return new SandboxCreated(visitorId, tenantIds[SandboxDefinition.Visitor[0].TenantName], securityStamp);
  }

  /// <summary>
  /// Called by the session check (at most once per SessionCheckSeconds) for a demo user whose
  /// sandbox hasn't expired: moves the whole sandbox's expiry to <see cref="DemoOptions.SandboxIdleMinutes"/>
  /// from now, but never past <see cref="DemoOptions.SandboxHours"/> after it was created.
  /// An expired sandbox is never brought back.
  /// </summary>
  public Task KeepAliveAsync(int userId, CancellationToken cancellationToken)
  {
    return SetExpiryAsync(userId, endNow: false, cancellationToken);
  }

  /// <summary>
  /// Signing out of a demo ends it: there's no password to come back with. The whole sandbox
  /// expires now, its place under the cap is free at once, and the next cleanup removes it.
  /// Does nothing for anyone else.
  /// </summary>
  public Task EndAsync(int userId, CancellationToken cancellationToken)
  {
    return SetExpiryAsync(userId, endNow: true, cancellationToken);
  }

  private async Task SetExpiryAsync(int userId, bool endNow, CancellationToken cancellationToken)
  {
    // The sandbox is the visitor's demo tenants and every demo user in them: the visitor and
    // the sample members. All of them carry the same ExpiresAt, so cleanup removes them together.
    const string sql = """
      DECLARE @Now datetime2 = SYSUTCDATETIME();
      DECLARE @ExpiresAt datetime2 = (
        SELECT CASE
          WHEN @EndNow = 1 THEN @Now
          WHEN DATEADD(minute, @IdleMinutes, @Now) < DATEADD(hour, @SandboxHours, CreatedAt)
            THEN DATEADD(minute, @IdleMinutes, @Now)
          ELSE DATEADD(hour, @SandboxHours, CreatedAt)
        END
        FROM dbo.Users
        WHERE Id = @UserId
          AND IsDemo = 1
          AND ExpiresAt > @Now);

      IF @ExpiresAt IS NULL
      BEGIN
        RETURN;
      END

      DECLARE @Tenants TABLE (Id int PRIMARY KEY);
      INSERT INTO @Tenants (Id)
      SELECT t.Id
      FROM dbo.Tenants AS t
      INNER JOIN dbo.TenantUsers AS tu ON tu.TenantId = t.Id
      WHERE tu.UserId = @UserId
        AND t.IsDemo = 1
        AND t.ExpiresAt > @Now;

      UPDATE dbo.Tenants
      SET ExpiresAt = @ExpiresAt
      WHERE Id IN (SELECT Id FROM @Tenants);

      UPDATE u
      SET ExpiresAt = @ExpiresAt
      FROM dbo.Users AS u
      WHERE u.IsDemo = 1
        AND u.ExpiresAt > @Now
        AND EXISTS (
          SELECT 1
          FROM dbo.TenantUsers AS tu
          WHERE tu.UserId = u.Id
            AND tu.TenantId IN (SELECT Id FROM @Tenants));
      """;

    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    await connection.ExecuteAsync(Command(
      sql,
      new { UserId = userId, EndNow = endNow, IdleMinutes = _options.SandboxIdleMinutes, _options.SandboxHours },
      null,
      cancellationToken));
  }

  private static async Task<(int Id, byte[] SecurityStamp)> InsertUserAsync(
    SqlConnection connection, SqlTransaction transaction, string email, string displayName, DateTime expiresAt, CancellationToken cancellationToken)
  {
    // No password: a sandbox user can't sign in except through the button that made it.
    const string sql = """
      INSERT INTO dbo.Users (Email, DisplayName, IsDemo, ExpiresAt)
      OUTPUT inserted.Id, inserted.SecurityStamp
      VALUES (@Email, @DisplayName, 1, @ExpiresAt);
      """;

    return await connection.QuerySingleAsync<(int, byte[])>(Command(
      sql, new { Email = email, DisplayName = displayName, ExpiresAt = expiresAt }, transaction, cancellationToken));
  }

  private static async Task<int> InsertTenantAsync(
    SqlConnection connection, SqlTransaction transaction, string name, int createdBy, DateTime expiresAt, CancellationToken cancellationToken)
  {
    const string sql = """
      INSERT INTO dbo.Tenants (Name, IsDemo, ExpiresAt, CreatedBy, UpdatedBy)
      OUTPUT inserted.Id
      VALUES (@Name, 1, @ExpiresAt, @CreatedBy, @CreatedBy);
      """;

    return await connection.QuerySingleAsync<int>(Command(
      sql, new { Name = name, ExpiresAt = expiresAt, CreatedBy = createdBy }, transaction, cancellationToken));
  }

  private static async Task InsertMembershipAsync(
    SqlConnection connection,
    SqlTransaction transaction,
    int tenantId,
    int userId,
    SandboxMembership membership,
    int createdBy,
    bool markUsed,
    CancellationToken cancellationToken)
  {
    const string sql = """
      INSERT INTO dbo.TenantUsers (TenantId, UserId, RoleId, Status, LastUsedAt, CreatedBy, UpdatedBy)
      SELECT @TenantId, @UserId, r.Id, @Status, CASE WHEN @MarkUsed = 1 THEN SYSUTCDATETIME() END, @CreatedBy, @CreatedBy
      FROM dbo.Roles AS r
      WHERE r.Name = @RoleName;
      """;

    int inserted = await connection.ExecuteAsync(Command(
      sql,
      new { TenantId = tenantId, UserId = userId, membership.RoleName, membership.Status, MarkUsed = markUsed, CreatedBy = createdBy },
      transaction,
      cancellationToken));
    if (inserted != 1)
    {
      throw new InvalidOperationException($"SandboxDefinition names a role that doesn't exist: {membership.RoleName}.");
    }
  }

  // All of a tenant's sample customers in one statement: the list goes in as one JSON parameter,
  // and OPENJSON turns it back into rows on the server. Still a parameter, never SQL text.
  private static async Task InsertCustomersAsync(
    SqlConnection connection,
    SqlTransaction transaction,
    int tenantId,
    IReadOnlyList<SandboxCustomer> customers,
    int createdBy,
    CancellationToken cancellationToken)
  {
    const string sql = """
      INSERT INTO dbo.Customers (TenantId, Name, Email, Phone, Status, Notes, CreatedBy, UpdatedBy)
      SELECT @TenantId, c.Name, c.Email, c.Phone, c.Status, c.Notes, @CreatedBy, @CreatedBy
      FROM OPENJSON(@Customers)
        WITH (
          Name nvarchar(200) '$.Name',
          Email nvarchar(320) '$.Email',
          Phone nvarchar(50) '$.Phone',
          Status varchar(20) '$.Status',
          Notes nvarchar(2000) '$.Notes'
        ) AS c;
      """;

    await connection.ExecuteAsync(Command(
      sql,
      new { TenantId = tenantId, CreatedBy = createdBy, Customers = JsonSerializer.Serialize(customers) },
      transaction,
      cancellationToken));
  }

  private static CommandDefinition Command(string sql, object? parameters, SqlTransaction? transaction, CancellationToken cancellationToken)
  {
    return new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken);
  }
}

/// <summary>A new sandbox: who to sign in as, and which tenant they start in.</summary>
public sealed record SandboxCreated(int UserId, int TenantId, byte[] SecurityStamp);
