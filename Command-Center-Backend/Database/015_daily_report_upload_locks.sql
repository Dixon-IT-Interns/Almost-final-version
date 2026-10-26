IF OBJECT_ID(N'dbo.DailyReportUploadLock', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DailyReportUploadLock
    (
        DailyReportUploadLockId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        PlantId INT NOT NULL,
        ReportDate DATE NOT NULL,
        CustomerId INT NOT NULL,
        CategoryId INT NOT NULL,
        UploadType VARCHAR(30) NOT NULL,
        LockedBy INT NOT NULL,
        LockedAt DATETIME2(7) NOT NULL CONSTRAINT DF_DailyReportUploadLock_LockedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_DailyReportUploadLock_Context UNIQUE (PlantId, ReportDate, CustomerId, CategoryId, UploadType),
        CONSTRAINT CK_DailyReportUploadLock_Type CHECK (UploadType IN ('ie', 'production', 'quality', 'trc', 'woClosure', 'all')),
        CONSTRAINT FK_DailyReportUploadLock_Plant FOREIGN KEY (PlantId) REFERENCES dbo.Plant(PlantId),
        CONSTRAINT FK_DailyReportUploadLock_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Customer(CustomerId),
        CONSTRAINT FK_DailyReportUploadLock_Category FOREIGN KEY (CategoryId) REFERENCES dbo.Category(CategoryId),
        CONSTRAINT FK_DailyReportUploadLock_User FOREIGN KEY (LockedBy) REFERENCES dbo.[User](UserId)
    );
END;
GO

IF OBJECT_ID(N'dbo.DailyReportUploadLock', N'U') IS NOT NULL
   AND EXISTS
   (
       SELECT 1
       FROM sys.check_constraints
       WHERE name = N'CK_DailyReportUploadLock_Type'
         AND parent_object_id = OBJECT_ID(N'dbo.DailyReportUploadLock')
   )
BEGIN
    ALTER TABLE dbo.DailyReportUploadLock
        DROP CONSTRAINT CK_DailyReportUploadLock_Type;
    UPDATE dbo.DailyReportUploadLock
    SET UploadType = CASE UploadType
        WHEN 'operations' THEN 'production'
        WHEN 'issue' THEN 'trc'
        ELSE UploadType
    END;
END;
GO

IF OBJECT_ID(N'dbo.DailyReportUploadLock', N'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1
       FROM sys.check_constraints
       WHERE name = N'CK_DailyReportUploadLock_Type'
         AND parent_object_id = OBJECT_ID(N'dbo.DailyReportUploadLock')
   )
BEGIN
    ALTER TABLE dbo.DailyReportUploadLock
        ADD CONSTRAINT CK_DailyReportUploadLock_Type
        CHECK (UploadType IN ('ie', 'production', 'quality', 'trc', 'woClosure', 'all'));
END;
GO
