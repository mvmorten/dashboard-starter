-- Each membership has exactly one role (step 7): a column on TenantUsers, not a separate
-- TenantUserRoles table, because "one role per membership" is then a rule the schema keeps
-- by itself. TenantUsers stays a global table (no Row-Level Security).

IF COL_LENGTH(N'dbo.TenantUsers', N'RoleId') IS NULL
BEGIN
  ALTER TABLE dbo.TenantUsers ADD RoleId int NULL;
END
GO

-- Memberships made before roles existed get the default role (Member). That errs toward less
-- access: nobody becomes an admin by accident. Make your first admin with
--   dotnet run --project src/Db -- set-role --email <email> --tenant <tenant> --role Admin
-- The live demo account gets its roles from the demo command, which the deploy runs after this.
UPDATE dbo.TenantUsers
SET RoleId = (SELECT Id FROM dbo.Roles WHERE IsDefault = 1)
WHERE RoleId IS NULL;
GO

-- Then required, with no default, so a membership without a role can't be made by mistake.
-- Safe during a deploy: the old API never inserts memberships (only the Db runner does, and
-- the deploy runs the new one).
IF EXISTS (
  SELECT 1
  FROM sys.columns
  WHERE object_id = OBJECT_ID(N'dbo.TenantUsers')
    AND name = N'RoleId'
    AND is_nullable = 1
)
BEGIN
  ALTER TABLE dbo.TenantUsers ALTER COLUMN RoleId int NOT NULL;
END
GO

IF OBJECT_ID(N'dbo.FK_TenantUsers_Roles', N'F') IS NULL
BEGIN
  ALTER TABLE dbo.TenantUsers ADD CONSTRAINT FK_TenantUsers_Roles FOREIGN KEY (RoleId) REFERENCES dbo.Roles (Id);
END
