-- The Security schema holds the Row-Level Security predicate function and, from step 7,
-- the security policy. Keeping them apart from dbo makes them easy to find and to lock down.
-- CREATE SCHEMA must be the only statement in its batch, hence EXEC.
IF SCHEMA_ID(N'Security') IS NULL
BEGIN
  EXEC (N'CREATE SCHEMA Security AUTHORIZATION dbo;');
END
