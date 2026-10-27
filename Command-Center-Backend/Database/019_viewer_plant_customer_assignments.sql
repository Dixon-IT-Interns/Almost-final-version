USE Dixon_Command_Center;
GO

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.ViewerAccessPlantCustomer', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ViewerAccessPlantCustomer
    (
        UserId INT NOT NULL,
        PlantId INT NOT NULL,
        CustomerId INT NOT NULL,
        CONSTRAINT PK_ViewerAccessPlantCustomer PRIMARY KEY (UserId, PlantId, CustomerId),
        CONSTRAINT FK_ViewerAccessPlantCustomer_PlantAccess
            FOREIGN KEY (UserId, PlantId)
            REFERENCES dbo.ViewerAccessPlant(UserId, PlantId)
            ON DELETE CASCADE,
        CONSTRAINT FK_ViewerAccessPlantCustomer_Customer
            FOREIGN KEY (CustomerId) REFERENCES dbo.Customer(CustomerId)
    );

    CREATE INDEX IX_ViewerAccessPlantCustomer_Plant_Customer_User
        ON dbo.ViewerAccessPlantCustomer(PlantId, CustomerId, UserId);
END;

-- Preserve the prior shared-customer behavior for currently valid plant/customer
-- pairs when converting specific-plant Viewer assignments to per-plant choices.
INSERT INTO dbo.ViewerAccessPlantCustomer (UserId, PlantId, CustomerId)
SELECT DISTINCT vap.UserId, vap.PlantId, vac.CustomerId
FROM dbo.ViewerAccess va
INNER JOIN dbo.ViewerAccessPlant vap ON vap.UserId = va.UserId
INNER JOIN dbo.ViewerAccessCustomer vac ON vac.UserId = va.UserId
INNER JOIN dbo.PlantCustomer pc
    ON pc.PlantId = vap.PlantId
   AND pc.CustomerId = vac.CustomerId
INNER JOIN dbo.Plant p ON p.PlantId = vap.PlantId AND p.IsActive = 1
INNER JOIN dbo.Customer c ON c.CustomerId = vac.CustomerId AND c.IsActive = 1
WHERE va.AllPlants = 0
  AND va.AllCustomers = 0
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.ViewerAccessPlantCustomer existing
      WHERE existing.UserId = vap.UserId
        AND existing.PlantId = vap.PlantId
        AND existing.CustomerId = vac.CustomerId
  );

COMMIT TRANSACTION;
GO
