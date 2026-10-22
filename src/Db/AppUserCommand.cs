using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DashboardStarter.Db.Migrations;
using DashboardStarter.Db.Scripts;
using Dapper;
using Microsoft.Data.SqlClient;

namespace DashboardStarter.Db;

/// <summary>
/// <c>app-user</c>: creates the login the API connects with, or resets its password if it
/// already exists, and makes it a member of the app_runtime role (data only, no schema changes).
/// </summary>
/// <remarks>
/// <para>
/// Reads the name and password from the APP_DB_USER and APP_DB_PASSWORD environment variables,
/// so the password never appears on a command line or in a file. Run it with the admin
/// connection string, the same one <c>migrate</c> uses.
/// </para>
/// <para>
/// Safe to run on every deploy: rotating the password is changing the secret and deploying.
/// It uses a server login plus a database user, which works the same on Azure SQL and on the
/// local SQL Server container.
/// </para>
/// </remarks>
internal static class AppUserCommand
{
  // SQL Server's limit for both login names and passwords. QUOTENAME also returns NULL
  // for anything longer, which would silently turn the statement into nothing.
  private const int MaxLength = 128;

  public static async Task<int> RunAsync(RunnerSettings settings)
  {
    string? userName = Environment.GetEnvironmentVariable("APP_DB_USER");
    string? password = Environment.GetEnvironmentVariable("APP_DB_PASSWORD");

    if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
    {
      Console.Error.WriteLine("Set APP_DB_USER and APP_DB_PASSWORD to the login the API should use.");
      return 1;
    }

    if (userName.Length > MaxLength || password.Length > MaxLength)
    {
      Console.Error.WriteLine($"APP_DB_USER and APP_DB_PASSWORD can be at most {MaxLength} characters.");
      return 1;
    }

    // The admin login must stay separate, or the API would keep its schema rights.
    SqlConnectionStringBuilder admin = new(settings.ConnectionString);
    if (string.Equals(userName, admin.UserID, StringComparison.OrdinalIgnoreCase))
    {
      Console.Error.WriteLine("APP_DB_USER must not be the admin login from the connection string.");
      return 1;
    }

    Console.WriteLine($"App user {userName} for {settings.DatabaseName} on {settings.ServerName} (environment: {settings.EnvironmentName})");
    Console.WriteLine();

    await using SqlConnection connection = await settings.OpenAsync();
    IReadOnlyList<AppliedMigration> applied = await SchemaJournal.ReadAsync(connection);
    SchemaStatus status = SchemaStatus.Compare(ScriptCatalog.Migrations, applied);
    if (status.Pending.Count > 0 || status.Changed.Count > 0)
    {
      Console.Error.WriteLine("The schema isn't current. Run this first: dotnet run --project src/Db -- migrate");
      return 1;
    }

    bool loginCreated = await EnsureLoginAsync(settings, userName, password);
    ScriptExecutor.PrintResult(loginCreated ? "created" : "updated", "login " + userName, loginCreated ? null : "password reset");

    bool userCreated = await EnsureUserAsync(connection, userName);
    ScriptExecutor.PrintResult(userCreated ? "created" : "exists", "user " + userName, "member of app_runtime");

    Console.WriteLine();
    Console.WriteLine("Done. Point the API's ConnectionStrings__Default at this login.");
    return 0;
  }

  private static async Task<bool> EnsureLoginAsync(RunnerSettings settings, string userName, string password)
  {
    // CREATE LOGIN and ALTER LOGIN don't accept parameters, so the statement is built on the
    // server: QUOTENAME quotes the name as an identifier and the password as a string literal
    // (doubling any quote characters). The values still arrive as parameters.
    const string sql = """
      DECLARE @verb nvarchar(10) = CASE WHEN EXISTS (SELECT 1 FROM sys.sql_logins WHERE name = @UserName)
        THEN N'ALTER' ELSE N'CREATE' END;

      DECLARE @loginSql nvarchar(max) =
        @verb + N' LOGIN ' + QUOTENAME(@UserName) + N' WITH PASSWORD = ' + QUOTENAME(@Password, N'''') + N';';
      EXEC sys.sp_executesql @loginSql;

      SELECT CAST(CASE WHEN @verb = N'CREATE' THEN 1 ELSE 0 END AS bit);
      """;

    await using SqlConnection master = await settings.OpenMasterAsync();
    return await master.ExecuteScalarAsync<bool>(sql, new { UserName = userName, Password = password });
  }

  private static async Task<bool> EnsureUserAsync(SqlConnection connection, string userName)
  {
    const string sql = """
      DECLARE @created bit = 0;

      IF DATABASE_PRINCIPAL_ID(@UserName) IS NULL
      BEGIN
        DECLARE @userSql nvarchar(max) =
          N'CREATE USER ' + QUOTENAME(@UserName) + N' FOR LOGIN ' + QUOTENAME(@UserName) + N';';
        EXEC sys.sp_executesql @userSql;
        SET @created = 1;
      END

      IF IS_ROLEMEMBER(N'app_runtime', @UserName) = 0
      BEGIN
        DECLARE @roleSql nvarchar(max) = N'ALTER ROLE app_runtime ADD MEMBER ' + QUOTENAME(@UserName) + N';';
        EXEC sys.sp_executesql @roleSql;
      END

      SELECT @created;
      """;

    return await connection.ExecuteScalarAsync<bool>(sql, new { UserName = userName });
  }
}
