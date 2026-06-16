IF COL_LENGTH(N'dbo.AdventurePacks', N'PreviewIllustrationUrl') IS NULL
BEGIN
    ALTER TABLE dbo.AdventurePacks
        ADD PreviewIllustrationUrl NVARCHAR(512) NULL;
END;
GO

IF COL_LENGTH(N'dbo.AdventurePacks', N'PreviewIllustrationStatus') IS NULL
BEGIN
    ALTER TABLE dbo.AdventurePacks
        ADD PreviewIllustrationStatus NVARCHAR(32) NOT NULL
            CONSTRAINT DF_AdventurePacks_PreviewIllustrationStatus DEFAULT (N'None');
END;
GO
