-- TenantUsers: which users belong to which tenants. One user can belong to many tenants.
-- Global table: no TenantId column of its own to filter on, and no Row-Level Security.
-- Status lets an admin disable someone's access to one tenant without deleting the row.
IF OBJECT_ID(N'dbo.TenantUsers', N'U') IS NULL
BEGIN
  CREATE TABLE dbo.TenantUsers
  (
    Id int IDENTITY(1, 1) NOT NULL,
    TenantId int NOT NULL,
    UserId int NOT NULL,
    Status varchar(20) NOT NULL CONSTRAINT DF_TenantUsers_Status DEFAULT 'Active',
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_TenantUsers_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy int NULL,
    UpdatedAt datetime2 NOT NULL CONSTRAINT DF_TenantUsers_UpdatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedBy int NULL,
    RowVersion rowversion NOT NULL,
    CONSTRAINT PK_TenantUsers PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_TenantUsers_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id),
    CONSTRAINT FK_TenantUsers_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (Id),
    CONSTRAINT CK_TenantUsers_Status CHECK (Status IN ('Active', 'Disabled'))
  );
END
GO

-- One membership per user per tenant. Leads with TenantId: "who is in this tenant?"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_TenantUsers_TenantId_UserId' AND object_id = OBJECT_ID(N'dbo.TenantUsers'))
BEGIN
  CREATE UNIQUE INDEX UX_TenantUsers_TenantId_UserId ON dbo.TenantUsers (TenantId, UserId);
END
GO

-- "Which tenants does this user belong to?" runs at every sign-in and tenant switch.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_TenantUsers_UserId' AND object_id = OBJECT_ID(N'dbo.TenantUsers'))
BEGIN
  CREATE INDEX IX_TenantUsers_UserId ON dbo.TenantUsers (UserId) INCLUDE (Status);
END
