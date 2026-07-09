IF COL_LENGTH(N'dbo.Children', N'HeroPortraitUrl') IS NULL
BEGIN
    ALTER TABLE dbo.Children ADD HeroPortraitUrl NVARCHAR(512) NULL;
END;
GO

IF COL_LENGTH(N'dbo.Children', N'HeroPortraitClaimedAt') IS NULL
BEGIN
    ALTER TABLE dbo.Children ADD HeroPortraitClaimedAt DATETIME2 NULL;
END;
GO
