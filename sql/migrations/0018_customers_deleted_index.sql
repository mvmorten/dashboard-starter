-- The Deleted list (GET /api/customers?status=Deleted): one tenant's deleted customers by name.
-- The list's other indexes are filtered to live rows (0013, 0015), so without this the Deleted
-- list would read every row of the tenant. It matches because the query says
-- DeletedAt IS NOT NULL as a literal.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Customers_TenantId_Name_Deleted' AND object_id = OBJECT_ID(N'dbo.Customers'))
BEGIN
  CREATE INDEX IX_Customers_TenantId_Name_Deleted ON dbo.Customers (TenantId, Name, Id)
    INCLUDE (Email, Phone, Status)
    WHERE DeletedAt IS NOT NULL;
END
