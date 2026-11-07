-- Roles and their permissions (step 7). A role is a fixed, named set of permissions. Code only
-- ever checks permissions (members.view, members.manage), never a role's name, so a fork can
-- add, split, or rename roles here without touching the code that checks them.
--
-- Global tables: the same roles in every tenant, so no TenantId and no Row-Level Security.
-- Which role a user has in a tenant is TenantUsers.RoleId (0010).
--
-- Roles aren't editable in the app. To change one, write a new migration. Every permission
-- string must also be in src/Db/Authorization/Permissions.cs, the one list of them in code.
IF OBJECT_ID(N'dbo.Roles', N'U') IS NULL
BEGIN
  CREATE TABLE dbo.Roles
  (
    Id int IDENTITY(1, 1) NOT NULL,
    Name nvarchar(50) NOT NULL,
    Description nvarchar(200) NOT NULL,
    -- The role a new membership gets when nobody says which (create-user without --role).
    IsDefault bit NOT NULL CONSTRAINT DF_Roles_IsDefault DEFAULT 0,
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_Roles_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_Roles PRIMARY KEY CLUSTERED (Id)
  );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Roles_Name' AND object_id = OBJECT_ID(N'dbo.Roles'))
BEGIN
  CREATE UNIQUE INDEX UX_Roles_Name ON dbo.Roles (Name);
END
GO

-- At most one default role.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Roles_IsDefault' AND object_id = OBJECT_ID(N'dbo.Roles'))
BEGIN
  CREATE UNIQUE INDEX UX_Roles_IsDefault ON dbo.Roles (IsDefault) WHERE IsDefault = 1;
END
GO

IF OBJECT_ID(N'dbo.RolePermissions', N'U') IS NULL
BEGIN
  CREATE TABLE dbo.RolePermissions
  (
    Id int IDENTITY(1, 1) NOT NULL,
    RoleId int NOT NULL,
    -- area.action, as in Permissions.cs: members.view
    Permission varchar(100) NOT NULL,
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_RolePermissions_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_RolePermissions PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_RolePermissions_Roles FOREIGN KEY (RoleId) REFERENCES dbo.Roles (Id)
  );
END
GO

-- "What can this role do?" runs on every session check, so it leads with RoleId.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_RolePermissions_RoleId_Permission' AND object_id = OBJECT_ID(N'dbo.RolePermissions'))
BEGIN
  CREATE UNIQUE INDEX UX_RolePermissions_RoleId_Permission ON dbo.RolePermissions (RoleId, Permission);
END
GO

-- The three starting roles. Member and Read-only have the same permissions until there's data
-- to edit: the CRUD module adds its own permissions (say, items.edit) and grants them to
-- Admin and Member only.
INSERT INTO dbo.Roles (Name, Description, IsDefault)
SELECT v.Name, v.Description, v.IsDefault
FROM (VALUES
  (N'Admin', N'Everything, including managing who is in the tenant and their roles.', CAST(0 AS bit)),
  (N'Member', N'Everything except managing members.', CAST(1 AS bit)),
  (N'Read-only', N'Can look, can''t change anything.', CAST(0 AS bit))
) AS v (Name, Description, IsDefault)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Roles AS r WHERE r.Name = v.Name);

INSERT INTO dbo.RolePermissions (RoleId, Permission)
SELECT r.Id, v.Permission
FROM (VALUES
  (N'Admin', 'members.view'),
  (N'Admin', 'members.manage'),
  (N'Member', 'members.view'),
  (N'Read-only', 'members.view')
) AS v (RoleName, Permission)
INNER JOIN dbo.Roles AS r ON r.Name = v.RoleName
WHERE NOT EXISTS (
  SELECT 1
  FROM dbo.RolePermissions AS rp
  WHERE rp.RoleId = r.Id
    AND rp.Permission = v.Permission
);
