-- app_runtime: the role the API's own database user belongs to (created by
-- `dotnet run --project src/Db -- app-user`).
--
-- It can read and write data and run procedures and functions in dbo, and nothing else:
-- no CREATE, ALTER, or DROP. "The API never changes the schema" is then enforced by
-- SQL Server permissions, not only by our code. Schema changes stay with the admin login
-- that the migrate command uses.
--
-- Granting on the schema (not table by table) covers tables added by later migrations.
IF DATABASE_PRINCIPAL_ID(N'app_runtime') IS NULL
BEGIN
  CREATE ROLE app_runtime AUTHORIZATION dbo;
END
GO

GRANT SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::dbo TO app_runtime;
