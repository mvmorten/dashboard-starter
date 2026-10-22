-- Tenants: the organizations that share this database.
-- Global table: no TenantId and no Row-Level Security.
IF OBJECT_ID(N'dbo.Tenants', N'U') IS NULL
BEGIN
  CREATE TABLE dbo.Tenants
  (
    Id int IDENTITY(1, 1) NOT NULL,
    Name nvarchar(200) NOT NULL,
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_Tenants_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy int NULL,
    UpdatedAt datetime2 NOT NULL CONSTRAINT DF_Tenants_UpdatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedBy int NULL,
    RowVersion rowversion NOT NULL,
    CONSTRAINT PK_Tenants PRIMARY KEY CLUSTERED (Id)
  );
END
