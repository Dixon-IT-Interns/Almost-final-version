USE Dixon_Command_Center;
GO

DECLARE @CustomerId INT = (
    SELECT TOP (1) CustomerId
    FROM dbo.Customer
    WHERE UPPER(LTRIM(RTRIM(CustomerName))) = 'GIGABYTE'
      AND IsActive = 1
    ORDER BY CustomerId
);

IF @CustomerId IS NULL
    THROW 51000, 'The active Gigabyte customer was not found. Run the base catalog setup first.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.Model
    WHERE CustomerId = @CustomerId
      AND UPPER(LTRIM(RTRIM(ModelName))) = 'PVT BUILD'
)
BEGIN
    INSERT INTO dbo.Model (CustomerId, ModelName, IsActive)
    VALUES (@CustomerId, 'PVT Build', 1);
END;
ELSE
BEGIN
    UPDATE dbo.Model
    SET IsActive = 1
    WHERE CustomerId = @CustomerId
      AND UPPER(LTRIM(RTRIM(ModelName))) = 'PVT BUILD';
END;
GO
