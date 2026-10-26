/*
   Catalog additions required by the supplied Dummy files workbooks.
   Safe to run repeatedly. These are catalog records, not report data.
*/
USE DixonCommandCenterBackup;
GO

DECLARE @PlantId INT = (SELECT TOP (1) PlantId FROM dbo.Plant ORDER BY PlantId);
DECLARE @CustomerId INT = (
    SELECT TOP (1) CustomerId
    FROM dbo.Customer
    WHERE CustomerName = 'HP'
      AND IsActive = 1
    ORDER BY CustomerId
);

IF @PlantId IS NULL OR @CustomerId IS NULL
    THROW 51000, 'The active plant or HP customer was not found. Run the base catalog setup first.', 1;

INSERT INTO dbo.Model (CustomerId, ModelName, IsActive)
SELECT @CustomerId, models.ModelName, 1
FROM (VALUES
    ('Pudding'),
    ('AIO BOGGLE'),
    ('RAINBOW CAKE'),
    ('OLAF'),
    ('JUMANJI'),
    ('ABEI - RPLU'),
    ('ABE HPT'),
    ('RAMESES'),
    ('NPI')
) AS models(ModelName)
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.Model existing
    WHERE existing.CustomerId = @CustomerId
      AND UPPER(LTRIM(RTRIM(existing.ModelName))) = UPPER(LTRIM(RTRIM(models.ModelName)))
);

DECLARE @CategoryId INT = (
    SELECT TOP (1) CategoryId
    FROM dbo.Category
    WHERE CustomerId = @CustomerId
      AND PlantId = @PlantId
      AND CategoryName = 'SMT'
      AND IsActive = 1
    ORDER BY CategoryId
);

IF @CategoryId IS NULL
    THROW 51000, 'The active HP SMT category was not found.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.ProductionLine
    WHERE PlantId = @PlantId
      AND CategoryId = @CategoryId
      AND UPPER(LTRIM(RTRIM(LineName))) = 'SMT-03'
)
BEGIN
    INSERT INTO dbo.ProductionLine
        (PlantId, CategoryId, LineName, SAPLocation, IsActive)
    VALUES
        (@PlantId, @CategoryId, 'SMT-03', '3SAI', 1);
END;
ELSE
BEGIN
    UPDATE dbo.ProductionLine
    SET IsActive = 1,
        SAPLocation = COALESCE(NULLIF(SAPLocation, ''), '3SAI')
    WHERE PlantId = @PlantId
      AND CategoryId = @CategoryId
      AND UPPER(LTRIM(RTRIM(LineName))) = 'SMT-03';
END;
GO
