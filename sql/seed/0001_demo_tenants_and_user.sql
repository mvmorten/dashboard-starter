-- Development only. Two demo tenants and one user who belongs to both, so
-- multi-tenancy is visible from day one. Safe to run more than once.
-- 0002_demo_user_password.sql gives the user a password.
IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE Name = N'Acme Corporation')
BEGIN
  INSERT INTO dbo.Tenants (Name) VALUES (N'Acme Corporation');
END

IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE Name = N'Globex Industries')
BEGIN
  INSERT INTO dbo.Tenants (Name) VALUES (N'Globex Industries');
END

-- IsDemo = 1, like the deployed demo account: its password is published, so it gets no
-- reset emails, can't change its password, and failed sign-ins don't lock it out.
IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Email = N'demo@example.com')
BEGIN
  INSERT INTO dbo.Users (Email, DisplayName, IsDemo) VALUES (N'demo@example.com', N'Demo User', 1);
END

-- Databases seeded before the flag was set here: fix them on the next seed.
UPDATE dbo.Users
SET IsDemo = 1
WHERE Email = N'demo@example.com'
  AND IsDemo = 0;

INSERT INTO dbo.TenantUsers (TenantId, UserId, Status)
SELECT t.Id, u.Id, 'Active'
FROM dbo.Tenants AS t
CROSS JOIN dbo.Users AS u
WHERE t.Name IN (N'Acme Corporation', N'Globex Industries')
  AND u.Email = N'demo@example.com'
  AND NOT EXISTS
  (
    SELECT 1
    FROM dbo.TenantUsers AS tu
    WHERE tu.TenantId = t.Id
      AND tu.UserId = u.Id
  );
