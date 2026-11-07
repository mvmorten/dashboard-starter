-- Development only. Gives the seeded demo@example.com the password LocalDevOnly!Demo1,
-- so you can sign in locally right after seeding. It's published in the README, like the
-- local SQL Server password: fine for a laptop, never for a real database (seed refuses
-- to run outside Development).
--
-- The value is an Argon2id hash in PHC format, made once with the same hasher the API uses
-- (src/Db/Passwords). Only set when the user has no password yet, so rerunning seed is safe.
-- It's a demo account (IsDemo = 1, see 0001), so set-password and the app won't change it.
UPDATE dbo.Users
SET PasswordHash = N'$argon2id$v=19$m=19456,t=2,p=1$ubLyeks2ThYrHHa8Omwneg$7mHN4x6atGOI0orfGVmCFvsuWOv/Td+/TmHWRo3hf14',
  PasswordChangedAt = SYSUTCDATETIME()
WHERE Email = N'demo@example.com'
  AND PasswordHash IS NULL;
