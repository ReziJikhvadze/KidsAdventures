IF COL_LENGTH(N'dbo.Children', N'AppearanceDescription') IS NULL
BEGIN
    ALTER TABLE dbo.Children
        ADD AppearanceDescription NVARCHAR(MAX) NULL;
END;
GO

IF COL_LENGTH(N'dbo.Children', N'AppearancePhotoUrl') IS NULL
BEGIN
    ALTER TABLE dbo.Children
        ADD AppearancePhotoUrl NVARCHAR(512) NULL;
END;
GO
