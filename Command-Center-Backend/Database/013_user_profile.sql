IF COL_LENGTH(N'dbo.User', N'PhoneNumber') IS NULL
    ALTER TABLE dbo.[User] ADD PhoneNumber VARCHAR(30) NULL;
GO

IF COL_LENGTH(N'dbo.User', N'ProfilePictureData') IS NULL
    ALTER TABLE dbo.[User] ADD ProfilePictureData VARBINARY(MAX) NULL;
GO

IF COL_LENGTH(N'dbo.User', N'ProfilePictureMimeType') IS NULL
    ALTER TABLE dbo.[User] ADD ProfilePictureMimeType VARCHAR(50) NULL;
GO

IF COL_LENGTH(N'dbo.User', N'PendingPhoneNumber') IS NULL
    ALTER TABLE dbo.[User] ADD PendingPhoneNumber VARCHAR(30) NULL;
GO
IF COL_LENGTH(N'dbo.User', N'PhoneVerificationCodeHash') IS NULL
    ALTER TABLE dbo.[User] ADD PhoneVerificationCodeHash VARBINARY(32) NULL;
GO
IF COL_LENGTH(N'dbo.User', N'PhoneVerificationExpiresAt') IS NULL
    ALTER TABLE dbo.[User] ADD PhoneVerificationExpiresAt DATETIME2 NULL;
GO
IF COL_LENGTH(N'dbo.User', N'PhoneVerificationSentAt') IS NULL
    ALTER TABLE dbo.[User] ADD PhoneVerificationSentAt DATETIME2 NULL;
GO
IF COL_LENGTH(N'dbo.User', N'PhoneVerificationAttempts') IS NULL
    ALTER TABLE dbo.[User] ADD PhoneVerificationAttempts INT NOT NULL CONSTRAINT DF_User_PhoneVerificationAttempts DEFAULT 0;
GO
IF COL_LENGTH(N'dbo.User', N'PhoneVerificationSendCount') IS NULL
    ALTER TABLE dbo.[User] ADD PhoneVerificationSendCount INT NOT NULL CONSTRAINT DF_User_PhoneVerificationSendCount DEFAULT 0;
GO
IF COL_LENGTH(N'dbo.User', N'PhoneVerificationWindowStartedAt') IS NULL
    ALTER TABLE dbo.[User] ADD PhoneVerificationWindowStartedAt DATETIME2 NULL;
GO
