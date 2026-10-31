USE Dixon_Command_Center;
GO

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.ViewerAnalyticsSavedView', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ViewerAnalyticsSavedView
    (
        ViewerAnalyticsSavedViewId INT IDENTITY(1,1) NOT NULL,
        UserId INT NOT NULL,
        Name NVARCHAR(100) NOT NULL,
        ConfigurationJson NVARCHAR(MAX) NOT NULL,
        CreatedAt DATETIME2(7) NOT NULL
            CONSTRAINT DF_ViewerAnalyticsSavedView_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt DATETIME2(7) NOT NULL
            CONSTRAINT DF_ViewerAnalyticsSavedView_UpdatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_ViewerAnalyticsSavedView
            PRIMARY KEY CLUSTERED (ViewerAnalyticsSavedViewId),
        CONSTRAINT FK_ViewerAnalyticsSavedView_User
            FOREIGN KEY (UserId) REFERENCES dbo.[User](UserId) ON DELETE CASCADE
    );

    CREATE INDEX IX_ViewerAnalyticsSavedView_User_UpdatedAt
        ON dbo.ViewerAnalyticsSavedView(UserId, UpdatedAt DESC);
END;

COMMIT TRANSACTION;
GO
