-- The Customers permissions (step 9). This is where Member and Read-only finally differ: both can
-- look, only Member (and Admin) can change. customers.edit covers delete too; a fork that wants
-- delete to be separate adds customers.delete with one migration like this one.
--
-- Every permission string must also be in src/Db/Authorization/Permissions.cs; CI checks that.
INSERT INTO dbo.RolePermissions (RoleId, Permission)
SELECT r.Id, v.Permission
FROM (VALUES
  (N'Admin', 'customers.view'),
  (N'Admin', 'customers.edit'),
  (N'Member', 'customers.view'),
  (N'Member', 'customers.edit'),
  (N'Read-only', 'customers.view')
) AS v (RoleName, Permission)
INNER JOIN dbo.Roles AS r ON r.Name = v.RoleName
WHERE NOT EXISTS (
  SELECT 1
  FROM dbo.RolePermissions AS rp
  WHERE rp.RoleId = r.Id
    AND rp.Permission = v.Permission
);
