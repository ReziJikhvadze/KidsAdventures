IF COL_LENGTH(N'dbo.AdventurePacks', N'ChapterIndex') IS NULL
BEGIN
    ALTER TABLE dbo.AdventurePacks ADD ChapterIndex INT NULL;
END;
GO

IF COL_LENGTH(N'dbo.AdventurePacks', N'PreviousChapterPackId') IS NULL
BEGIN
    ALTER TABLE dbo.AdventurePacks ADD PreviousChapterPackId UNIQUEIDENTIFIER NULL;
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_AdventurePacks_PreviousChapterPackId'
)
BEGIN
    ALTER TABLE dbo.AdventurePacks
        ADD CONSTRAINT FK_AdventurePacks_PreviousChapterPackId
        FOREIGN KEY (PreviousChapterPackId) REFERENCES dbo.AdventurePacks (Id);
END;
GO

IF OBJECT_ID(N'dbo.StoryPathChapters', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StoryPathChapters
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_StoryPathChapters PRIMARY KEY,
        ChildId UNIQUEIDENTIFIER NOT NULL,
        Theme NVARCHAR(64) NOT NULL,
        ChapterIndex INT NOT NULL,
        AdventurePackId UNIQUEIDENTIFIER NULL,
        Status NVARCHAR(32) NOT NULL,
        ParentConfirmedAt DATETIME2 NULL,
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_StoryPathChapters_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt DATETIME2 NOT NULL CONSTRAINT DF_StoryPathChapters_UpdatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_StoryPathChapters_Children_ChildId FOREIGN KEY (ChildId) REFERENCES dbo.Children (Id) ON DELETE CASCADE,
        CONSTRAINT FK_StoryPathChapters_AdventurePacks_AdventurePackId FOREIGN KEY (AdventurePackId) REFERENCES dbo.AdventurePacks (Id)
    );

    CREATE UNIQUE INDEX UX_StoryPathChapters_Child_Theme_ChapterIndex
        ON dbo.StoryPathChapters (ChildId, Theme, ChapterIndex);

    CREATE INDEX IX_StoryPathChapters_ChildId_Theme
        ON dbo.StoryPathChapters (ChildId, Theme);
END;
GO
