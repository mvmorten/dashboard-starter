-- Development only. Two sample tenants to add your own test users to:
--   dotnet run --project src/Db -- create-user --email ann@example.com --name "Ann Example" --tenant "Acme Corporation"
-- Safe to run more than once.
--
-- After these scripts, seed adds the local account admin@example.com to both (src/Db/Users/LocalAccount.cs).
-- Demo tenants aren't here: "Try the demo" creates a private sandbox per visitor (src/Api/Demo).
IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE Name = N'Acme Corporation')
BEGIN
  INSERT INTO dbo.Tenants (Name) VALUES (N'Acme Corporation');
END

IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE Name = N'Globex Industries')
BEGIN
  INSERT INTO dbo.Tenants (Name) VALUES (N'Globex Industries');
END
