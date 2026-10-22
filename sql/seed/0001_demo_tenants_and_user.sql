-- Development only. Two demo tenants and one user who belongs to both, so
-- multi-tenancy is visible from day one. Safe to run more than once.
-- The demo user has no password yet; sign-in arrives in step 4.
IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE Name = N'Acme Corporation')
BEGIN
  INSERT INTO dbo.Tenants (Name) VALUES (N'Acme Corporation');
END

IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE Name = N'Globex Industries')
BEGIN
  INSERT INTO dbo.Tenants (Name) VALUES (N'Globex Industries');
END

IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Email = N'demo@example.com')
BEGIN
  INSERT INTO dbo.Users (Email, DisplayName) VALUES (N'demo@example.com', N'Demo User');
END

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
