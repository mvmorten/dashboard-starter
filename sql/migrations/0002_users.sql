-- Users: one account per person, shared across every tenant they belong to.
-- Global table: no TenantId and no Row-Level Security.
-- Password and lockout columns arrive with authentication in step 4.
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
  CREATE TABLE dbo.Users
  (
    Id int IDENTITY(1, 1) NOT NULL,
    Email nvarchar(256) NOT NULL,
    DisplayName nvarchar(200) NOT NULL,
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_Users_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy int NULL,
    UpdatedAt datetime2 NOT NULL CONSTRAINT DF_Users_UpdatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedBy int NULL,
    RowVersion rowversion NOT NULL,
    CONSTRAINT PK_Users PRIMARY KEY CLUSTERED (Id)
  );
END
GO

-- Email is unique across the whole system. With the default case-insensitive
-- collation, Ann@Example.com and ann@example.com count as the same address.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Users_Email' AND object_id = OBJECT_ID(N'dbo.Users'))
BEGIN
  CREATE UNIQUE INDEX UX_Users_Email ON dbo.Users (Email);
END
