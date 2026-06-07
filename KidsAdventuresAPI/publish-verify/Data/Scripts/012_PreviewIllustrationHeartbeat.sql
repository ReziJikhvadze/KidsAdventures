IF COL_LENGTH(N'dbo.AdventurePacks', N'PreviewIllustrationUpdatedAt') IS NULL
BEGIN
    ALTER TABLE dbo.AdventurePacks
        ADD PreviewIllustrationUpdatedAt DATETIME2 NULL;
END;
GO

UPDATE dbo.AdventurePacks
SET PreviewIllustrationUpdatedAt = CreatedAt
WHERE PreviewIllustrationUpdatedAt IS NULL
  AND PreviewIllustrationStatus = N'Generating';
GO
