-- Development only. Two sample tenants to add your own test users to:
--   dotnet run --project src/Db -- create-user --email ann@example.com --name "Ann Example" --tenant "Acme Corporation"
-- Safe to run more than once.
--
-- The demo account and its tenants ("Demo" and "Other Demo") aren't here: seed creates them in
-- C# (src/Db/Users/DemoAccountSetup.cs) after these scripts, with the same code the demo
-- command uses on the live demo.
IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE Name = N'Acme Corporation')
BEGIN
  INSERT INTO dbo.Tenants (Name) VALUES (N'Acme Corporation');
END

IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE Name = N'Globex Industries')
BEGIN
  INSERT INTO dbo.Tenants (Name) VALUES (N'Globex Industries');
END
