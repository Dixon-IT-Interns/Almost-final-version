IF COL_LENGTH(N'dbo.User', N'EmployeeId') IS NULL
    ALTER TABLE dbo.[User] ADD EmployeeId VARCHAR(50) NULL;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.User')
      AND name = N'UX_User_EmployeeId'
)
    CREATE UNIQUE INDEX UX_User_EmployeeId
        ON dbo.[User](EmployeeId)
        WHERE EmployeeId IS NOT NULL;
GO