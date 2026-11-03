-- When the user last signed in to or switched to this tenant (UTC). Someone who belongs to
-- several tenants lands in the one they used last. NULL means never.
IF COL_LENGTH(N'dbo.TenantUsers', N'LastUsedAt') IS NULL
BEGIN
  ALTER TABLE dbo.TenantUsers ADD LastUsedAt datetime2 NULL;
END
