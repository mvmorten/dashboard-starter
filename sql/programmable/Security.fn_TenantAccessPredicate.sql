-- Row-Level Security predicate: a row is visible only when its TenantId matches the
-- tenant the API put in SESSION_CONTEXT for this connection. No tenant set, no rows.
--
-- The security policy that applies this to tenant-owned tables is created together with
-- the first tenant-owned table in step 7. Once a policy references this function,
-- CREATE OR ALTER fails (Msg 3729, "being referenced by object"), so from then on this
-- script must drop the policy, alter the function, and recreate the policy. The runner
-- wraps each script in one transaction, so tables are never left unprotected.
--
-- To run a query by hand in SSMS, set the tenant first:
--   EXEC sp_set_session_context @key = N'TenantId', @value = 1;
CREATE OR ALTER FUNCTION Security.fn_TenantAccessPredicate (@TenantId int)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
  SELECT 1 AS AccessResult
  WHERE @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS int);
