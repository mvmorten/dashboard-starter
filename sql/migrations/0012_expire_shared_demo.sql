-- Retires the step 7 shared demo account. Sandboxes replace it (0011), so this marks its five
-- users and two tenants as expired demo rows, and the API's cleanup service removes them on
-- its first run, the same way it removes any expired sandbox. Nothing is deleted here.
--
-- This is the one place that finds demo rows by name: the shared account predates
-- Tenants.IsDemo, so there's nothing else to find its tenants by. To be sure a real tenant
-- that happens to be called "Demo" is never touched, a tenant only counts when the shared
-- demo user (IsDemo = 1) is a member of it. Everywhere else, demo rows are found by IsDemo.
--
-- A database that never had the shared account (a fork, a fresh install) matches nothing.

-- The users: guest@example.com and the four sample members, all IsDemo since step 7.
UPDATE dbo.Users
SET ExpiresAt = DATEADD(minute, -1, SYSUTCDATETIME())
WHERE IsDemo = 1
  AND ExpiresAt IS NULL
  AND Email IN (
    N'guest@example.com',
    N'sam.rivera@example.com',
    N'priya.shah@example.com',
    N'lee.chen@example.com',
    N'jordan.blake@example.com');

-- Its tenants. IsDemo and ExpiresAt in one statement: CK_Tenants_ExpiresAt allows an expiry
-- date on demo tenants only.
UPDATE t
SET
  IsDemo = 1,
  ExpiresAt = DATEADD(minute, -1, SYSUTCDATETIME())
FROM dbo.Tenants AS t
WHERE t.Name IN (N'Demo', N'Other Demo')
  AND t.ExpiresAt IS NULL
  AND EXISTS (
    SELECT 1
    FROM dbo.TenantUsers AS tu
    INNER JOIN dbo.Users AS u ON u.Id = tu.UserId
    WHERE tu.TenantId = t.Id
      AND u.Email = N'guest@example.com'
      AND u.IsDemo = 1);
