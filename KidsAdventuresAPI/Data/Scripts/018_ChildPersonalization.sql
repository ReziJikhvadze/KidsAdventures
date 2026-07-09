-- Avatar builder: structured JSON config + personalization type alongside existing photo flow.
IF COL_LENGTH(N'dbo.Children', N'PersonalizationType') IS NULL
BEGIN
    ALTER TABLE dbo.Children ADD PersonalizationType NVARCHAR(16) NULL;
END;
GO

IF COL_LENGTH(N'dbo.Children', N'AvatarConfigJson') IS NULL
BEGIN
    ALTER TABLE dbo.Children ADD AvatarConfigJson NVARCHAR(MAX) NULL;
END;
GO

-- Backfill: existing children with photos keep the photo path.
UPDATE dbo.Children
SET PersonalizationType = N'photo'
WHERE PersonalizationType IS NULL AND PhotoUrl IS NOT NULL;
GO
