-- AuditLog: who changed what, and when (step 12). One row per action on an audited entity,
-- written by the app in the same transaction as the change it records (src/Db/Audit/AuditLog.cs),
-- so there's never a change without its entry, or an entry without its change.
--
--   Id          bigint: this table grows without limit.
--   TenantId    whose entry it is. Tenant-owned: it's in the Row-Level Security policy
--               (sql/programmable/Security.fn_TenantAccessPredicate.sql).
--   OccurredAt  UTC, from the database's clock, never the app's or the browser's.
--   UserId      who did it: the signed-in user. NULL when no person did (the Db runner's set-role).
--   EntityType  'Customer', 'Member': the names in AuditLog.cs. No CHECK: a fork adds its own.
--   EntityId    the entity's id. For a Member, the member's user id.
--   Action      Created, Updated, Deleted or Restored.
--   Changes     JSON, {"name":{"old":"A","new":"B"}}: only the fields that changed, and only
--               fields the code chose to record (an allow-list per entity). NULL when the action
--               says it all (Deleted, Restored).
--
-- Who is shown by name, joined from Users when the log is read. Nothing is copied in here.
--
-- There are no standard columns (UpdatedAt, DeletedAt, RowVersion): an entry is never changed
-- or deleted. The DENY at the end makes the database hold the API to that.
IF OBJECT_ID(N'dbo.AuditLog', N'U') IS NULL
BEGIN
  CREATE TABLE dbo.AuditLog
  (
    Id bigint IDENTITY(1, 1) NOT NULL,
    TenantId int NOT NULL,
    OccurredAt datetime2 NOT NULL CONSTRAINT DF_AuditLog_OccurredAt DEFAULT SYSUTCDATETIME(),
    UserId int NULL,
    EntityType varchar(50) NOT NULL,
    EntityId int NOT NULL,
    Action varchar(20) NOT NULL,
    Changes nvarchar(max) NULL,
    CONSTRAINT PK_AuditLog PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_AuditLog_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id),
    CONSTRAINT FK_AuditLog_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (Id),
    CONSTRAINT CK_AuditLog_Action CHECK (Action IN ('Created', 'Updated', 'Deleted', 'Restored')),
    CONSTRAINT CK_AuditLog_Changes CHECK (Changes IS NULL OR ISJSON(Changes) = 1)
  );
END
GO

-- One entity's history, oldest to newest by Id (the customer page reads it backwards).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AuditLog_TenantId_Entity' AND object_id = OBJECT_ID(N'dbo.AuditLog'))
BEGIN
  CREATE INDEX IX_AuditLog_TenantId_Entity ON dbo.AuditLog (TenantId, EntityType, EntityId, Id);
END
GO

-- The tenant's whole log, newest first (the audit page). Both indexes are needed: the one above
-- can't give this order without a sort, and this one can't find one entity without reading the
-- tenant's whole log. Ordered by Id, not OccurredAt: it's unique and only goes up, so pages
-- don't shuffle rows that share a timestamp.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AuditLog_TenantId_Id' AND object_id = OBJECT_ID(N'dbo.AuditLog'))
BEGIN
  CREATE INDEX IX_AuditLog_TenantId_Id ON dbo.AuditLog (TenantId, Id DESC);
END
GO

-- Append-only, enforced by the database: the API's role may read and insert, and nothing else.
-- A DENY beats the schema-wide GRANT in 0005_app_runtime_role.sql. The one thing that removes
-- entries is the demo sandbox cleanup, through dbo.DeleteDemoTenantAuditLog
-- (sql/programmable), which only works for an expired demo tenant.
DENY UPDATE, DELETE ON dbo.AuditLog TO app_runtime;
