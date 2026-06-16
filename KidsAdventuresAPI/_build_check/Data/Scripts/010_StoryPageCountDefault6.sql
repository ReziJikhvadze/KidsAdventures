IF EXISTS (
    SELECT 1
    FROM sys.default_constraints
    WHERE name = N'DF_AdventurePacks_StoryPageCount')
BEGIN
    ALTER TABLE dbo.AdventurePacks DROP CONSTRAINT DF_AdventurePacks_StoryPageCount;
    ALTER TABLE dbo.AdventurePacks
        ADD CONSTRAINT DF_AdventurePacks_StoryPageCount DEFAULT (6) FOR StoryPageCount;
END;
GO
