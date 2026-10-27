USE Dixon_Command_Center;
GO

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.ViewerAccess', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ViewerAccess
    (
        UserId INT NOT NULL,
        AllPlants BIT NOT NULL,
        AllCustomers BIT NOT NULL,
        ConfiguredAt DATETIME2(7) NOT NULL
            CONSTRAINT DF_ViewerAccess_ConfiguredAt DEFAULT (SYSUTCDATETIME()),
        ConfiguredBy INT NOT NULL,
        CONSTRAINT PK_ViewerAccess PRIMARY KEY (UserId),
        CONSTRAINT FK_ViewerAccess_User FOREIGN KEY (UserId) REFERENCES dbo.[User](UserId),
        CONSTRAINT FK_ViewerAccess_ConfiguredBy FOREIGN KEY (ConfiguredBy) REFERENCES dbo.[User](UserId)
    );
END;

IF OBJECT_ID(N'dbo.ViewerAccessPlant', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ViewerAccessPlant
    (
        UserId INT NOT NULL,
        PlantId INT NOT NULL,
        CONSTRAINT PK_ViewerAccessPlant PRIMARY KEY (UserId, PlantId),
        CONSTRAINT FK_ViewerAccessPlant_Access FOREIGN KEY (UserId) REFERENCES dbo.ViewerAccess(UserId) ON DELETE CASCADE,
        CONSTRAINT FK_ViewerAccessPlant_Plant FOREIGN KEY (PlantId) REFERENCES dbo.Plant(PlantId)
    );

    CREATE INDEX IX_ViewerAccessPlant_PlantId_UserId
        ON dbo.ViewerAccessPlant(PlantId, UserId);
END;

IF OBJECT_ID(N'dbo.ViewerAccessCustomer', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ViewerAccessCustomer
    (
        UserId INT NOT NULL,
        CustomerId INT NOT NULL,
        CONSTRAINT PK_ViewerAccessCustomer PRIMARY KEY (UserId, CustomerId),
        CONSTRAINT FK_ViewerAccessCustomer_Access FOREIGN KEY (UserId) REFERENCES dbo.ViewerAccess(UserId) ON DELETE CASCADE,
        CONSTRAINT FK_ViewerAccessCustomer_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Customer(CustomerId)
    );

    CREATE INDEX IX_ViewerAccessCustomer_CustomerId_UserId
        ON dbo.ViewerAccessCustomer(CustomerId, UserId);
END;

COMMIT TRANSACTION;
GO

-- Existing viewers are intentionally not backfilled. Their historical scope was
-- implicit and cannot be safely inferred. Review this list and agree each scope
-- before rollout; after deployment, assign them through Admin > Users & access
-- before restoring their Viewer reporting access.
SELECT u.UserId, u.FullName, u.Email, p.PlantName
FROM dbo.[User] u
INNER JOIN dbo.Role r ON r.RoleId = u.RoleId
INNER JOIN dbo.Plant p ON p.PlantId = u.PlantId
LEFT JOIN dbo.ViewerAccess va ON va.UserId = u.UserId
WHERE r.RoleName = 'Viewer'
  AND va.UserId IS NULL
ORDER BY u.FullName;
GO
