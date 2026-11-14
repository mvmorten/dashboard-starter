-- Customers: the CRUD module's sample entity (step 9), and the first tenant-owned table. It sets
-- the pattern every tenant-owned table follows, so it's deliberately plain:
--
--   TenantId               whose row it is. Leads every index but the primary key. Row-Level
--                          Security filters on it (sql/programmable/Security.fn_TenantAccessPredicate.sql).
--   CreatedAt, CreatedBy   when and by whom it was made (UTC, user id).
--   UpdatedAt, UpdatedBy   the last change. Starts equal to Created*.
--   DeletedAt, DeletedBy   soft delete: NULL until deleted. Every app query says
--                          AND DeletedAt IS NULL, updates included. Rows stay for the audit log.
--   RowVersion             optimistic concurrency: an UPDATE only lands on the version the
--                          client read (WHERE RowVersion = @RowVersion).
--
-- The *By columns are NULL when no person did it (a data migration, an import).
--
-- Email isn't unique, in a tenant or across tenants: two customers can share an address (a
-- shared inbox, a family). User accounts are different, and Users.Email stays unique.
IF OBJECT_ID(N'dbo.Customers', N'U') IS NULL
BEGIN
  CREATE TABLE dbo.Customers
  (
    Id int IDENTITY(1, 1) NOT NULL,
    TenantId int NOT NULL,
    Name nvarchar(200) NOT NULL,
    Email nvarchar(320) NULL,
    Phone nvarchar(50) NULL,
    Status varchar(20) NOT NULL CONSTRAINT DF_Customers_Status DEFAULT 'Active',
    Notes nvarchar(2000) NULL,
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_Customers_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy int NULL,
    UpdatedAt datetime2 NOT NULL CONSTRAINT DF_Customers_UpdatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedBy int NULL,
    DeletedAt datetime2 NULL,
    DeletedBy int NULL,
    RowVersion rowversion NOT NULL,
    CONSTRAINT PK_Customers PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_Customers_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id),
    CONSTRAINT FK_Customers_CreatedBy FOREIGN KEY (CreatedBy) REFERENCES dbo.Users (Id),
    CONSTRAINT FK_Customers_UpdatedBy FOREIGN KEY (UpdatedBy) REFERENCES dbo.Users (Id),
    CONSTRAINT FK_Customers_DeletedBy FOREIGN KEY (DeletedBy) REFERENCES dbo.Users (Id),
    -- A lookup table comes with the forms step, if a fork needs more statuses than this.
    CONSTRAINT CK_Customers_Status CHECK (Status IN ('Active', 'Inactive')),
    -- DeletedBy only on a deleted row. (It can be NULL on one: deleted by a data migration.)
    CONSTRAINT CK_Customers_DeletedBy CHECK (DeletedBy IS NULL OR DeletedAt IS NOT NULL)
  );
END
GO

-- The list: live rows of one tenant, sorted by name then id, and "name starts with". Filtered to
-- live rows, so deleted ones don't take up room in the index the list reads. The filter matches
-- because every list query says DeletedAt IS NULL as a literal, never as a parameter.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Customers_TenantId_Name' AND object_id = OBJECT_ID(N'dbo.Customers'))
BEGIN
  CREATE INDEX IX_Customers_TenantId_Name ON dbo.Customers (TenantId, Name, Id)
    INCLUDE (Email, Phone, Status)
    WHERE DeletedAt IS NULL;
END
GO

-- "Email starts with", for live rows.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Customers_TenantId_Email' AND object_id = OBJECT_ID(N'dbo.Customers'))
BEGIN
  CREATE INDEX IX_Customers_TenantId_Email ON dbo.Customers (TenantId, Email)
    WHERE DeletedAt IS NULL;
END
GO

-- Every row of a tenant, deleted ones included: the demo sandbox's row cap counts these, and
-- sandbox cleanup deletes by TenantId.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Customers_TenantId' AND object_id = OBJECT_ID(N'dbo.Customers'))
BEGIN
  CREATE INDEX IX_Customers_TenantId ON dbo.Customers (TenantId);
END
