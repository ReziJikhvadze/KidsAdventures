IF COL_LENGTH(N'dbo.Children', N'PhotoUrl') IS NULL
BEGIN
    ALTER TABLE dbo.Children ADD PhotoUrl NVARCHAR(512) NULL;
END;
GO

IF COL_LENGTH(N'dbo.AdventurePacks', N'OptionalStoryNotes') IS NULL
BEGIN
    ALTER TABLE dbo.AdventurePacks ADD OptionalStoryNotes NVARCHAR(1000) NULL;
END;
GO

IF COL_LENGTH(N'dbo.AdventurePacks', N'StoryLanguage') IS NULL
BEGIN
    ALTER TABLE dbo.AdventurePacks ADD StoryLanguage NVARCHAR(16) NULL;
END;
GO

IF COL_LENGTH(N'dbo.AdventurePacks', N'ProgressMessage') IS NULL
BEGIN
    ALTER TABLE dbo.AdventurePacks ADD ProgressMessage NVARCHAR(256) NULL;
END;
GO
