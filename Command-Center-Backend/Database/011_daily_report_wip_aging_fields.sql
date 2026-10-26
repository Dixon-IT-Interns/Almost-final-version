USE DixonCommandCenterBackup;
GO

IF OBJECT_ID(N'dbo.DailyReport', N'U') IS NULL
    THROW 51000, 'dbo.DailyReport was not found.', 1;
IF OBJECT_ID(N'dbo.KPI_Daily', N'U') IS NULL
    THROW 51000, 'dbo.KPI_Daily was not found.', 1;
GO

IF COL_LENGTH(N'dbo.DailyReport', N'wip_lying_greater_3_days') IS NULL
    ALTER TABLE dbo.DailyReport ADD wip_lying_greater_3_days INT NULL;
GO

IF COL_LENGTH(N'dbo.DailyReport', N'wip_lying_greater_7_days') IS NULL
    ALTER TABLE dbo.DailyReport ADD wip_lying_greater_7_days INT NULL;
GO

IF COL_LENGTH(N'dbo.KPI_Daily', N'wip_lying_greater_3_days') IS NULL
    ALTER TABLE dbo.KPI_Daily ADD wip_lying_greater_3_days INT NULL;
GO

IF COL_LENGTH(N'dbo.KPI_Daily', N'wip_lying_greater_7_days') IS NULL
    ALTER TABLE dbo.KPI_Daily ADD wip_lying_greater_7_days INT NULL;
GO

/* Normalize existing rows before enforcing the non-null contract. */
UPDATE dbo.DailyReport
SET wip_lying_greater_3_days = 0
WHERE wip_lying_greater_3_days IS NULL;

UPDATE dbo.DailyReport
SET wip_lying_greater_7_days = 0
WHERE wip_lying_greater_7_days IS NULL;

UPDATE dbo.KPI_Daily
SET wip_lying_greater_3_days = 0
WHERE wip_lying_greater_3_days IS NULL;

UPDATE dbo.KPI_Daily
SET wip_lying_greater_7_days = 0
WHERE wip_lying_greater_7_days IS NULL;

ALTER TABLE dbo.DailyReport ALTER COLUMN wip_lying_greater_3_days INT NOT NULL;
ALTER TABLE dbo.DailyReport ALTER COLUMN wip_lying_greater_7_days INT NOT NULL;
ALTER TABLE dbo.KPI_Daily ALTER COLUMN wip_lying_greater_3_days INT NOT NULL;
ALTER TABLE dbo.KPI_Daily ALTER COLUMN wip_lying_greater_7_days INT NOT NULL;
GO

/* Add defaults for future INSERTs when a caller omits these fields. */
IF NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.DailyReport')
      AND parent_column_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.DailyReport'), N'wip_lying_greater_3_days', 'ColumnId')
)
    ALTER TABLE dbo.DailyReport
        ADD CONSTRAINT DF_DailyReport_wip_lying_greater_3_days DEFAULT (0)
        FOR wip_lying_greater_3_days;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.DailyReport')
      AND parent_column_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.DailyReport'), N'wip_lying_greater_7_days', 'ColumnId')
)
    ALTER TABLE dbo.DailyReport
        ADD CONSTRAINT DF_DailyReport_wip_lying_greater_7_days DEFAULT (0)
        FOR wip_lying_greater_7_days;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.KPI_Daily')
      AND parent_column_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.KPI_Daily'), N'wip_lying_greater_3_days', 'ColumnId')
)
    ALTER TABLE dbo.KPI_Daily
        ADD CONSTRAINT DF_KPI_Daily_wip_lying_greater_3_days DEFAULT (0)
        FOR wip_lying_greater_3_days;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.KPI_Daily')
      AND parent_column_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.KPI_Daily'), N'wip_lying_greater_7_days', 'ColumnId')
)
    ALTER TABLE dbo.KPI_Daily
        ADD CONSTRAINT DF_KPI_Daily_wip_lying_greater_7_days DEFAULT (0)
        FOR wip_lying_greater_7_days;
GO
