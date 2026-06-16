IF COL_LENGTH(N'dbo.Users', N'WelcomeStoryRemaining') IS NULL
BEGIN
    ALTER TABLE dbo.Users
        ADD WelcomeStoryRemaining INT NOT NULL
            CONSTRAINT DF_Users_WelcomeStoryRemaining DEFAULT (0);
END;
GO

IF COL_LENGTH(N'dbo.AdventurePacks', N'StoryPageCount') IS NULL
BEGIN
    ALTER TABLE dbo.AdventurePacks
        ADD StoryPageCount INT NOT NULL
            CONSTRAINT DF_AdventurePacks_StoryPageCount DEFAULT (6);
END;
GO

IF COL_LENGTH(N'dbo.AdventurePacks', N'IsWelcomeGiftStory') IS NULL
BEGIN
    ALTER TABLE dbo.AdventurePacks
        ADD IsWelcomeGiftStory BIT NOT NULL
            CONSTRAINT DF_AdventurePacks_IsWelcomeGiftStory DEFAULT (0);
END;
GO
