-- PasswordResetTokens: one row per "forgot password" email sent. Global table (a reset happens
-- before anyone is signed in to a tenant): no TenantId and no Row-Level Security.
--
-- Only a SHA-256 hash of the token is stored. The token itself exists in the email and
-- nowhere else, so someone who can read this table still can't reset anyone's password.
--
-- Rows also serve as the per-address cap ("at most N emails an hour"), which is why issuing a
-- new token expires the older ones (ExpiresAt = now) instead of deleting them: a deleted row
-- would no longer count. Rows are deleted once they're older than an hour, and all of a
-- user's rows go when one of them is used. See src/Api/Auth/PasswordResetStore.cs.
IF OBJECT_ID(N'dbo.PasswordResetTokens', N'U') IS NULL
BEGIN
  CREATE TABLE dbo.PasswordResetTokens
  (
    Id int IDENTITY(1, 1) NOT NULL,
    UserId int NOT NULL,
    TokenHash binary(32) NOT NULL,
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_PasswordResetTokens_CreatedAt DEFAULT SYSUTCDATETIME(),
    ExpiresAt datetime2 NOT NULL,
    UsedAt datetime2 NULL,
    CONSTRAINT PK_PasswordResetTokens PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_PasswordResetTokens_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (Id)
  );
END
GO

-- The lookup when a reset link comes back: hash what arrived, find the row.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_PasswordResetTokens_TokenHash' AND object_id = OBJECT_ID(N'dbo.PasswordResetTokens'))
BEGIN
  CREATE UNIQUE INDEX UX_PasswordResetTokens_TokenHash ON dbo.PasswordResetTokens (TokenHash);
END
GO

-- "How many emails has this user had in the last hour?" and the cleanup of a user's old rows.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PasswordResetTokens_UserId_CreatedAt' AND object_id = OBJECT_ID(N'dbo.PasswordResetTokens'))
BEGIN
  CREATE INDEX IX_PasswordResetTokens_UserId_CreatedAt ON dbo.PasswordResetTokens (UserId, CreatedAt);
END
