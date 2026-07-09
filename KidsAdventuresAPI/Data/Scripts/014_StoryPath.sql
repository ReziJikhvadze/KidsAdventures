IF OBJECT_ID(N'dbo.CampfirePrompts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CampfirePrompts
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_CampfirePrompts PRIMARY KEY,
        Theme NVARCHAR(64) NOT NULL,
        NodeIndex INT NOT NULL,
        PromptText NVARCHAR(1000) NOT NULL,
        Version INT NOT NULL CONSTRAINT DF_CampfirePrompts_Version DEFAULT (1),
        IsActive BIT NOT NULL CONSTRAINT DF_CampfirePrompts_IsActive DEFAULT (1),
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_CampfirePrompts_CreatedAt DEFAULT (SYSUTCDATETIME())
    );

    CREATE UNIQUE INDEX UX_CampfirePrompts_Theme_NodeIndex_Version
        ON dbo.CampfirePrompts (Theme, NodeIndex, Version);
END;
GO

IF OBJECT_ID(N'dbo.StoryPathNodeProgress', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StoryPathNodeProgress
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_StoryPathNodeProgress PRIMARY KEY,
        ChildId UNIQUEIDENTIFIER NOT NULL,
        AdventurePackId UNIQUEIDENTIFIER NOT NULL,
        Theme NVARCHAR(64) NOT NULL,
        NodeIndex INT NOT NULL,
        Status NVARCHAR(32) NOT NULL,
        CampfirePromptShownAt DATETIME2 NULL,
        ParentConfirmedAt DATETIME2 NULL,
        UpdatedAt DATETIME2 NOT NULL CONSTRAINT DF_StoryPathNodeProgress_UpdatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_StoryPathNodeProgress_Children_ChildId FOREIGN KEY (ChildId) REFERENCES dbo.Children (Id) ON DELETE CASCADE,
        CONSTRAINT FK_StoryPathNodeProgress_AdventurePacks_AdventurePackId FOREIGN KEY (AdventurePackId) REFERENCES dbo.AdventurePacks (Id) ON DELETE CASCADE
    );

    CREATE UNIQUE INDEX UX_StoryPathNodeProgress_Child_Pack_Node
        ON dbo.StoryPathNodeProgress (ChildId, AdventurePackId, NodeIndex);

    CREATE INDEX IX_StoryPathNodeProgress_ChildId_Theme
        ON dbo.StoryPathNodeProgress (ChildId, Theme);
END;
GO

IF OBJECT_ID(N'dbo.StoryPathAchievements', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StoryPathAchievements
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_StoryPathAchievements PRIMARY KEY,
        ChildId UNIQUEIDENTIFIER NOT NULL,
        Theme NVARCHAR(64) NOT NULL,
        AchievementKey NVARCHAR(64) NOT NULL,
        EarnedAt DATETIME2 NOT NULL CONSTRAINT DF_StoryPathAchievements_EarnedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_StoryPathAchievements_Children_ChildId FOREIGN KEY (ChildId) REFERENCES dbo.Children (Id) ON DELETE CASCADE
    );

    CREATE UNIQUE INDEX UX_StoryPathAchievements_Child_Theme
        ON dbo.StoryPathAchievements (ChildId, Theme);
END;
GO

-- Seed campfire prompts (version 1, active) for all five themes
IF NOT EXISTS (SELECT 1 FROM dbo.CampfirePrompts WHERE Theme = N'Dinosaurs' AND NodeIndex = 0 AND Version = 1)
BEGIN
    INSERT INTO dbo.CampfirePrompts (Id, Theme, NodeIndex, PromptText, Version, IsActive)
    VALUES
        (NEWID(), N'Dinosaurs', 0, N'What was your favorite dinosaur in this part of the story? Can you roar like it?', 1, 1),
        (NEWID(), N'Dinosaurs', 1, N'If you could ride any dinosaur, which one would you pick and where would you go?', 1, 1),
        (NEWID(), N'Dinosaurs', 2, N'What do you think the dinosaurs were feeling in this scene?', 1, 1),
        (NEWID(), N'Dinosaurs', 3, N'Can you act out what happened on this page together?', 1, 1),
        (NEWID(), N'Dinosaurs', 4, N'What would you name a dinosaur you discovered?', 1, 1),
        (NEWID(), N'Dinosaurs', 5, N'You finished the dinosaur world! What was the bravest moment in the whole adventure?', 1, 1),

        (NEWID(), N'Airplanes', 0, N'Where would you fly first if you had your own airplane?', 1, 1),
        (NEWID(), N'Airplanes', 1, N'What sounds do you hear when an airplane takes off?', 1, 1),
        (NEWID(), N'Airplanes', 2, N'Who would you take with you on a sky adventure?', 1, 1),
        (NEWID(), N'Airplanes', 3, N'Can you point to something in the clouds and make up a story about it?', 1, 1),
        (NEWID(), N'Airplanes', 4, N'What would you pack for a long flight?', 1, 1),
        (NEWID(), N'Airplanes', 5, N'You completed the sky world! What was the coolest thing about flying in the story?', 1, 1),

        (NEWID(), N'Space', 0, N'Which planet would you visit first and why?', 1, 1),
        (NEWID(), N'Space', 1, N'What do you think stars are made of?', 1, 1),
        (NEWID(), N'Space', 2, N'If you met an alien friend, what would you ask them?', 1, 1),
        (NEWID(), N'Space', 3, N'Can you float like you are in zero gravity?', 1, 1),
        (NEWID(), N'Space', 4, N'What would you name your own spaceship?', 1, 1),
        (NEWID(), N'Space', 5, N'You explored the whole galaxy! What was the most amazing space moment?', 1, 1),

        (NEWID(), N'Pirates', 0, N'What treasure would you most want to find?', 1, 1),
        (NEWID(), N'Pirates', 1, N'What would you name your pirate ship?', 1, 1),
        (NEWID(), N'Pirates', 2, N'Can you draw a treasure map together?', 1, 1),
        (NEWID(), N'Pirates', 3, N'What makes a good shipmate on a pirate crew?', 1, 1),
        (NEWID(), N'Pirates', 4, N'How would you celebrate finding treasure?', 1, 1),
        (NEWID(), N'Pirates', 5, N'You sailed the whole pirate world! What was the best part of the quest?', 1, 1),

        (NEWID(), N'Animals', 0, N'Which animal in the story is most like you?', 1, 1),
        (NEWID(), N'Animals', 1, N'What sound does your favorite animal make?', 1, 1),
        (NEWID(), N'Animals', 2, N'How do animals help each other in the wild?', 1, 1),
        (NEWID(), N'Animals', 3, N'Can you move like the animal on this page?', 1, 1),
        (NEWID(), N'Animals', 4, N'If you could talk to one animal, what would you say?', 1, 1),
        (NEWID(), N'Animals', 5, N'You finished the animal world! Which creature was your favorite?', 1, 1);
END;
GO
