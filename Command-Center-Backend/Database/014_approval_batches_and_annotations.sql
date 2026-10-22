/* Raw-SQL schema migration: the API uses ADO.NET and versioned SQL scripts, not EF Core. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
IF COL_LENGTH(N'dbo.DailyReport', N'SubmissionBatchId') IS NULL
    ALTER TABLE dbo.DailyReport ADD SubmissionBatchId UNIQUEIDENTIFIER NULL;
GO
IF COL_LENGTH(N'dbo.DailyReport', N'SubmissionBatchEnforced') IS NULL
    ALTER TABLE dbo.DailyReport ADD SubmissionBatchEnforced BIT NOT NULL CONSTRAINT DF_DailyReport_SubmissionBatchEnforced DEFAULT 0;
GO

/* Old queue items keep one-row review semantics until contributors resubmit them through the new batch flow. */
UPDATE dbo.DailyReport SET SubmissionBatchId=NEWID() WHERE SubmissionBatchId IS NULL;
GO

IF OBJECT_ID(N'dbo.ReportFieldAnnotation', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ReportFieldAnnotation
    (
        AnnotationId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ReportFieldAnnotation PRIMARY KEY,
        DailyReportId INT NOT NULL,
        SubmissionBatchId UNIQUEIDENTIFIER NULL,
        FieldKey VARCHAR(80) NOT NULL,
        Severity VARCHAR(10) NOT NULL,
        Comment VARCHAR(1000) NOT NULL,
        OriginalValue NVARCHAR(4000) NULL,
        CorrectedValue NVARCHAR(4000) NULL,
        CreatedBy INT NOT NULL,
        CreatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_ReportFieldAnnotation_CreatedAt DEFAULT SYSUTCDATETIME(),
        ResolvedBy INT NULL,
        ResolvedAt DATETIME2(7) NULL,
        CONSTRAINT FK_ReportFieldAnnotation_Report FOREIGN KEY (DailyReportId) REFERENCES dbo.DailyReport(DailyReportId),
        CONSTRAINT FK_ReportFieldAnnotation_CreatedBy FOREIGN KEY (CreatedBy) REFERENCES dbo.[User](UserId),
        CONSTRAINT FK_ReportFieldAnnotation_ResolvedBy FOREIGN KEY (ResolvedBy) REFERENCES dbo.[User](UserId),
        CONSTRAINT CK_ReportFieldAnnotation_Severity CHECK (Severity IN ('RED', 'YELLOW'))
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ReportFieldAnnotation_Report' AND object_id=OBJECT_ID(N'dbo.ReportFieldAnnotation'))
    CREATE INDEX IX_ReportFieldAnnotation_Report ON dbo.ReportFieldAnnotation(DailyReportId, CreatedAt DESC);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_DailyReport_SubmissionBatch' AND object_id=OBJECT_ID(N'dbo.DailyReport'))
    CREATE INDEX IX_DailyReport_SubmissionBatch ON dbo.DailyReport(PlantId, SubmissionBatchId, ReportDate, CustomerId, CategoryId, CreatedBy) INCLUDE (DailyReportId, Status, IsActive, SubmittedAt);
GO
