-- Sorting the Customers list (step 10) by when a row was last changed or first added. Each
-- sort is ORDER BY <column> <dir>, Id <dir>, so a page is 25 rows read in index order (forwards
-- or backwards), with no sort of the whole tenant on every request.
--
-- The other sorts don't need a new index:
--   Name    IX_Customers_TenantId_Name (TenantId, Name, Id), from 0013.
--   Email   IX_Customers_TenantId_Email (TenantId, Email). The clustered key (Id) is part of
--           every non-unique index's key, so it's already in Email, Id order.
--   Status  two values. Sorting by it is close to filtering by it, and it sorts a tenant's live
--           rows, which is cheap at the sizes this starter is built for.
--
-- No INCLUDE columns: a page reads the other columns for its 25 rows from the table.
-- Filtered to live rows like the others: every list query says DeletedAt IS NULL as a literal.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Customers_TenantId_UpdatedAt' AND object_id = OBJECT_ID(N'dbo.Customers'))
BEGIN
  CREATE INDEX IX_Customers_TenantId_UpdatedAt ON dbo.Customers (TenantId, UpdatedAt, Id)
    WHERE DeletedAt IS NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Customers_TenantId_CreatedAt' AND object_id = OBJECT_ID(N'dbo.Customers'))
BEGIN
  CREATE INDEX IX_Customers_TenantId_CreatedAt ON dbo.Customers (TenantId, CreatedAt, Id)
    WHERE DeletedAt IS NULL;
END
