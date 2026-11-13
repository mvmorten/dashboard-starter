-- Demo sandboxes: every visitor to a public demo gets their own user and tenants, removed
-- after a while by the API's cleanup service (src/Api/Demo/SandboxCleanup.cs).
--
-- Tenants.IsDemo mirrors Users.IsDemo. ExpiresAt is when the row may be removed, on both
-- tables, and only ever on demo rows: the CHECK constraints make it impossible to give a
-- real tenant or user an expiry date, so cleanup can't remove one even through a bug.
IF COL_LENGTH(N'dbo.Tenants', N'IsDemo') IS NULL
BEGIN
  ALTER TABLE dbo.Tenants ADD IsDemo bit NOT NULL
    CONSTRAINT DF_Tenants_IsDemo DEFAULT 0;
END
GO

IF COL_LENGTH(N'dbo.Tenants', N'ExpiresAt') IS NULL
BEGIN
  ALTER TABLE dbo.Tenants ADD ExpiresAt datetime2 NULL;
END
GO

IF COL_LENGTH(N'dbo.Users', N'ExpiresAt') IS NULL
BEGIN
  ALTER TABLE dbo.Users ADD ExpiresAt datetime2 NULL;
END
GO

IF OBJECT_ID(N'dbo.CK_Tenants_ExpiresAt', N'C') IS NULL
BEGIN
  ALTER TABLE dbo.Tenants ADD CONSTRAINT CK_Tenants_ExpiresAt CHECK (ExpiresAt IS NULL OR IsDemo = 1);
END
GO

IF OBJECT_ID(N'dbo.CK_Users_ExpiresAt', N'C') IS NULL
BEGIN
  ALTER TABLE dbo.Users ADD CONSTRAINT CK_Users_ExpiresAt CHECK (ExpiresAt IS NULL OR IsDemo = 1);
END
GO

-- Cleanup and the active-sandbox cap look for demo rows by expiry. Filtered, so the index
-- holds only demo rows and real tenants and users don't pay for it.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Tenants_Demo_ExpiresAt' AND object_id = OBJECT_ID(N'dbo.Tenants'))
BEGIN
  CREATE INDEX IX_Tenants_Demo_ExpiresAt ON dbo.Tenants (ExpiresAt) WHERE IsDemo = 1;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Users_Demo_ExpiresAt' AND object_id = OBJECT_ID(N'dbo.Users'))
BEGIN
  CREATE INDEX IX_Users_Demo_ExpiresAt ON dbo.Users (ExpiresAt) WHERE IsDemo = 1;
END
