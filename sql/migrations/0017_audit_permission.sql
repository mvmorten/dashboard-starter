-- audit.view: the tenant's audit log page (GET /api/audit). Admin only: it shows what every
-- member did. A customer's own history needs only customers.view.
INSERT INTO dbo.RolePermissions (RoleId, Permission)
SELECT r.Id, v.Permission
FROM (VALUES
  (N'Admin', 'audit.view')
) AS v (RoleName, Permission)
INNER JOIN dbo.Roles AS r ON r.Name = v.RoleName
WHERE NOT EXISTS (
  SELECT 1 FROM dbo.RolePermissions AS rp
  WHERE rp.RoleId = r.Id AND rp.Permission = v.Permission
);
