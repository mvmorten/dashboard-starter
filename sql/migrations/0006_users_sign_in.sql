-- Sign-in columns on Users (step 5). Every column is nullable or has a default, so the
-- previous version of the API keeps working against this schema during a deploy.

-- Email comparisons rely on the column's collation being case-insensitive, so that
-- Ann@Example.com and ann@example.com are one account (UX_Users_Email) and sign-in finds
-- either spelling with a plain WHERE Email = @Email. SQL Server's and Azure SQL's default,
-- SQL_Latin1_General_CP1_CI_AS, is. A database created with a case-sensitive collation
-- would quietly allow duplicate accounts, so stop here instead.
-- ComparisonStyle is a bit mask; bit 1 means "ignore case".
IF CAST(COLLATIONPROPERTY(
     (SELECT collation_name FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Users') AND name = N'Email'),
     'ComparisonStyle') AS int) & 1 = 0
BEGIN
  THROW 50001, 'dbo.Users.Email has a case-sensitive collation. Sign-in expects a case-insensitive one (such as SQL_Latin1_General_CP1_CI_AS).', 1;
END
GO

-- The password as a PHC string: $argon2id$v=19$m=19456,t=2,p=1$<salt>$<hash>.
-- The settings travel with the hash, so they can be raised later and old hashes still verify.
-- NULL until a password is set; a user without one can't sign in.
IF COL_LENGTH(N'dbo.Users', N'PasswordHash') IS NULL
BEGIN
  ALTER TABLE dbo.Users ADD PasswordHash nvarchar(256) NULL;
END
GO

-- 16 random bytes that change whenever the user's credentials change or their sessions
-- must end. The auth cookie carries the value it was issued with; the API compares it on
-- each request (briefly cached), so a new stamp signs out every existing session.
IF COL_LENGTH(N'dbo.Users', N'SecurityStamp') IS NULL
BEGIN
  ALTER TABLE dbo.Users ADD SecurityStamp binary(16) NOT NULL
    CONSTRAINT DF_Users_SecurityStamp DEFAULT CRYPT_GEN_RANDOM(16);
END
GO

-- Wrong passwords in a row. Reset by a successful sign-in, and when a lockout starts.
IF COL_LENGTH(N'dbo.Users', N'FailedSignInCount') IS NULL
BEGIN
  ALTER TABLE dbo.Users ADD FailedSignInCount int NOT NULL
    CONSTRAINT DF_Users_FailedSignInCount DEFAULT 0;
END
GO

-- Sign-in is refused until this time (UTC). NULL means not locked out.
IF COL_LENGTH(N'dbo.Users', N'LockoutEndAt') IS NULL
BEGIN
  ALTER TABLE dbo.Users ADD LockoutEndAt datetime2 NULL;
END
GO

-- The last successful sign-in (UTC).
IF COL_LENGTH(N'dbo.Users', N'LastSignInAt') IS NULL
BEGIN
  ALTER TABLE dbo.Users ADD LastSignInAt datetime2 NULL;
END
GO

-- When the password was last set (UTC). A rehash on sign-in doesn't count: the password
-- itself didn't change.
IF COL_LENGTH(N'dbo.Users', N'PasswordChangedAt') IS NULL
BEGIN
  ALTER TABLE dbo.Users ADD PasswordChangedAt datetime2 NULL;
END
GO

-- The shared account on a public demo, whose password is published on the sign-in page.
-- It is never locked out (anyone could lock everyone out with five bad guesses), and
-- later steps keep it from changing its password or data.
IF COL_LENGTH(N'dbo.Users', N'IsDemo') IS NULL
BEGIN
  ALTER TABLE dbo.Users ADD IsDemo bit NOT NULL
    CONSTRAINT DF_Users_IsDemo DEFAULT 0;
END
