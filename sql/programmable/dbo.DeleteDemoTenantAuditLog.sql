-- The one way audit entries are ever removed: with their expired demo tenant, by the sandbox
-- cleanup (src/Api/Demo/SandboxCleanup.cs).
--
-- Why a stored procedure, when the default is inline SQL: the API's role is denied UPDATE and
-- DELETE on dbo.AuditLog (sql/migrations/0016_audit_log.sql), so the log is append-only for
-- everything the API does. This procedure is the single, narrow exception. It's owned by dbo,
-- like the table, so SQL Server doesn't check the caller's permissions on the table inside it
-- (ownership chaining); the caller only needs EXECUTE, which app_runtime has on the schema.
--
-- It refuses anything but an expired demo tenant. Row-Level Security still applies inside it:
-- the caller's connection must be working in that tenant, as the cleanup's is.
--
-- A fork that wants retention (delete entries older than N years) writes its own procedure next
-- to this one, on purpose, rather than lifting the DENY.
CREATE OR ALTER PROCEDURE dbo.DeleteDemoTenantAuditLog
  @TenantId int
AS
BEGIN
  IF NOT EXISTS (
    SELECT 1
    FROM dbo.Tenants
    WHERE Id = @TenantId
      AND IsDemo = 1
      AND ExpiresAt <= SYSUTCDATETIME())
  BEGIN
    THROW 50000, N'Audit entries are only deleted with an expired demo tenant.', 1;
  END

  DELETE FROM dbo.AuditLog
  WHERE TenantId = @TenantId;
END
