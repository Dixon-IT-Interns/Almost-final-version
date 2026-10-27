IF COL_LENGTH(N'dbo.DailyReport', N'TextValuesJson') IS NULL
BEGIN
    ALTER TABLE dbo.DailyReport
        ADD TextValuesJson NVARCHAR(MAX) NULL;
END;

IF COL_LENGTH(N'dbo.KPI_Daily', N'TextValuesJson') IS NULL
BEGIN
    ALTER TABLE dbo.KPI_Daily
        ADD TextValuesJson NVARCHAR(MAX) NULL;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE name = N'CK_DailyReport_TextValuesJson_IsJson'
      AND parent_object_id = OBJECT_ID(N'dbo.DailyReport')
)
BEGIN
    EXEC sys.sp_executesql N'
        ALTER TABLE dbo.DailyReport WITH CHECK
            ADD CONSTRAINT CK_DailyReport_TextValuesJson_IsJson
            CHECK (TextValuesJson IS NULL OR ISJSON(TextValuesJson) = 1);';
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE name = N'CK_KPI_Daily_TextValuesJson_IsJson'
      AND parent_object_id = OBJECT_ID(N'dbo.KPI_Daily')
)
BEGIN
    EXEC sys.sp_executesql N'
        ALTER TABLE dbo.KPI_Daily WITH CHECK
            ADD CONSTRAINT CK_KPI_Daily_TextValuesJson_IsJson
            CHECK (TextValuesJson IS NULL OR ISJSON(TextValuesJson) = 1);';
END;
