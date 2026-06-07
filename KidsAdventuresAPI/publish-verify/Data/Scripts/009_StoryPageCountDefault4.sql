UPDATE dbo.AdventurePacks
SET StoryPageCount = 4
WHERE IsWelcomeGiftStory = 0
  AND StoryPageCount > 4;
GO

IF EXISTS (
    SELECT 1
    FROM sys.default_constraints
    WHERE name = N'DF_AdventurePacks_StoryPageCount')
BEGIN
    ALTER TABLE dbo.AdventurePacks DROP CONSTRAINT DF_AdventurePacks_StoryPageCount;
    ALTER TABLE dbo.AdventurePacks
        ADD CONSTRAINT DF_AdventurePacks_StoryPageCount DEFAULT (4) FOR StoryPageCount;
END;
GO
