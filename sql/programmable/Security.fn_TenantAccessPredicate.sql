-- Row-Level Security: the predicate and the security policy that applies it, as one unit.
--
-- A row of a tenant-owned table is visible, and writable, only when its TenantId matches the
-- tenant in SESSION_CONTEXT on this connection. The API sets it per request
-- (src/Api/Database/SqlConnectionFactory.cs); background work and the Db runner set it per
-- tenant. No tenant set, no rows: NULL never equals anything, so the predicate fails closed.
--
-- RLS is the safety net, not the filter. App queries still say WHERE TenantId = @TenantId, and
-- this doesn't hide soft-deleted rows (AND DeletedAt IS NULL does that).
--
-- Why one script: once a policy references the function, ALTER FUNCTION fails (Msg 3729, "being
-- referenced by object"). So this drops the policy, alters the function, and creates the policy
-- again. The runner wraps each script in one transaction, so no table is ever left unprotected,
-- and a failure leaves the old policy in place.
--
-- To run a query by hand in SSMS, set the tenant first:
--   EXEC sp_set_session_context @key = N'TenantId', @value = 1;
DROP SECURITY POLICY IF EXISTS Security.TenantAccessPolicy;
GO

CREATE OR ALTER FUNCTION Security.fn_TenantAccessPredicate (@TenantId int)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
  SELECT 1 AS AccessResult
  WHERE @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS int);
GO

-- Every tenant-owned table, each with all three predicates:
--   FILTER        reads, and which rows an UPDATE or DELETE can find, so another tenant's rows
--                 can't be changed or deleted either (that's why there's no BEFORE UPDATE or
--                 BEFORE DELETE block predicate: the filter already does their job).
--   AFTER INSERT  can't insert a row for another tenant.
--   AFTER UPDATE  can't move a row to another tenant.
--
-- Adding a tenant-owned table? Add its three lines here, and add it to SandboxCleanup.TenantRows
-- (src/Api/Demo/SandboxCleanup.cs). CI fails if a table with a TenantId column is missing here;
-- TenantUsers is the one exception (a global table, see 0003_tenant_users.sql).
CREATE SECURITY POLICY Security.TenantAccessPolicy
  ADD FILTER PREDICATE Security.fn_TenantAccessPredicate(TenantId) ON dbo.Customers,
  ADD BLOCK PREDICATE Security.fn_TenantAccessPredicate(TenantId) ON dbo.Customers AFTER INSERT,
  ADD BLOCK PREDICATE Security.fn_TenantAccessPredicate(TenantId) ON dbo.Customers AFTER UPDATE
WITH (STATE = ON, SCHEMABINDING = ON);
