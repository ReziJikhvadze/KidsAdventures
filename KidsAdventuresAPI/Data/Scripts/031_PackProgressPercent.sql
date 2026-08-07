-- How far along a long job is, as a number.
--
-- ProgressMessage already carried a percentage, but written into the Georgian sentence
-- ("PDF-ს ვაწყობთ… ~90%"), so the only way to draw a progress bar from it was to parse prose.
IF COL_LENGTH(N'dbo.AdventurePacks', N'ProgressPercent') IS NULL
BEGIN
    ALTER TABLE dbo.AdventurePacks
        ADD ProgressPercent INT NULL;
END;
GO
