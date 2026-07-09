-- Interactive story path graph: authored nodes + branching choices (alongside legacy linear chapters).
IF OBJECT_ID(N'dbo.StoryPaths', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StoryPaths
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_StoryPaths PRIMARY KEY,
        Title NVARCHAR(200) NOT NULL,
        Theme NVARCHAR(64) NOT NULL,
        StartNodeId UNIQUEIDENTIFIER NULL,
        Version INT NOT NULL CONSTRAINT DF_StoryPaths_Version DEFAULT (1),
        IsActive BIT NOT NULL CONSTRAINT DF_StoryPaths_IsActive DEFAULT (0),
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_StoryPaths_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt DATETIME2 NOT NULL CONSTRAINT DF_StoryPaths_UpdatedAt DEFAULT (SYSUTCDATETIME())
    );

    CREATE INDEX IX_StoryPaths_Theme_IsActive ON dbo.StoryPaths (Theme, IsActive);
END;
GO

IF OBJECT_ID(N'dbo.StoryNodes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StoryNodes
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_StoryNodes PRIMARY KEY,
        StoryPathId UNIQUEIDENTIFIER NOT NULL,
        NodeKey NVARCHAR(64) NOT NULL,
        NodeType NVARCHAR(32) NOT NULL,
        Title NVARCHAR(200) NOT NULL,
        ContentJson NVARCHAR(MAX) NULL,
        ProblemJson NVARCHAR(MAX) NULL,
        RequiresParentApproval BIT NOT NULL CONSTRAINT DF_StoryNodes_RequiresParentApproval DEFAULT (0),
        MapPositionX DECIMAL(5, 2) NULL,
        MapPositionY DECIMAL(5, 2) NULL,
        SortOrder INT NOT NULL CONSTRAINT DF_StoryNodes_SortOrder DEFAULT (0),
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_StoryNodes_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt DATETIME2 NOT NULL CONSTRAINT DF_StoryNodes_UpdatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_StoryNodes_StoryPaths_StoryPathId FOREIGN KEY (StoryPathId) REFERENCES dbo.StoryPaths (Id) ON DELETE CASCADE
    );

    CREATE UNIQUE INDEX UX_StoryNodes_StoryPathId_NodeKey ON dbo.StoryNodes (StoryPathId, NodeKey);
    CREATE INDEX IX_StoryNodes_StoryPathId_SortOrder ON dbo.StoryNodes (StoryPathId, SortOrder);
END;
GO

IF OBJECT_ID(N'dbo.StoryChoices', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StoryChoices
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_StoryChoices PRIMARY KEY,
        StoryPathId UNIQUEIDENTIFIER NOT NULL,
        FromNodeId UNIQUEIDENTIFIER NOT NULL,
        ToNodeId UNIQUEIDENTIFIER NOT NULL,
        ChoiceKey NVARCHAR(64) NOT NULL,
        Label NVARCHAR(200) NOT NULL,
        ConsequenceTag NVARCHAR(64) NULL,
        SortOrder INT NOT NULL CONSTRAINT DF_StoryChoices_SortOrder DEFAULT (0),
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_StoryChoices_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_StoryChoices_StoryPaths_StoryPathId FOREIGN KEY (StoryPathId) REFERENCES dbo.StoryPaths (Id) ON DELETE NO ACTION,
        CONSTRAINT FK_StoryChoices_StoryNodes_FromNodeId FOREIGN KEY (FromNodeId) REFERENCES dbo.StoryNodes (Id) ON DELETE NO ACTION,
        CONSTRAINT FK_StoryChoices_StoryNodes_ToNodeId FOREIGN KEY (ToNodeId) REFERENCES dbo.StoryNodes (Id) ON DELETE NO ACTION
    );

    CREATE UNIQUE INDEX UX_StoryChoices_FromNodeId_ChoiceKey ON dbo.StoryChoices (FromNodeId, ChoiceKey);
    CREATE INDEX IX_StoryChoices_StoryPathId ON dbo.StoryChoices (StoryPathId);
END;
GO

IF OBJECT_ID(N'dbo.StoryPathGraphProgress', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StoryPathGraphProgress
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_StoryPathGraphProgress PRIMARY KEY,
        ChildId UNIQUEIDENTIFIER NOT NULL,
        StoryPathId UNIQUEIDENTIFIER NOT NULL,
        CurrentNodeId UNIQUEIDENTIFIER NULL,
        VisitedNodeIdsJson NVARCHAR(MAX) NOT NULL CONSTRAINT DF_StoryPathGraphProgress_Visited DEFAULT (N'[]'),
        ChoiceHistoryJson NVARCHAR(MAX) NOT NULL CONSTRAINT DF_StoryPathGraphProgress_Choices DEFAULT (N'[]'),
        ProblemResolvedJson NVARCHAR(MAX) NULL,
        ParentApprovedNodeIdsJson NVARCHAR(MAX) NULL,
        UpdatedAt DATETIME2 NOT NULL CONSTRAINT DF_StoryPathGraphProgress_UpdatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_StoryPathGraphProgress_Children_ChildId FOREIGN KEY (ChildId) REFERENCES dbo.Children (Id) ON DELETE CASCADE,
        CONSTRAINT FK_StoryPathGraphProgress_StoryPaths_StoryPathId FOREIGN KEY (StoryPathId) REFERENCES dbo.StoryPaths (Id) ON DELETE CASCADE,
        CONSTRAINT FK_StoryPathGraphProgress_StoryNodes_CurrentNodeId FOREIGN KEY (CurrentNodeId) REFERENCES dbo.StoryNodes (Id) ON DELETE NO ACTION
    );

    CREATE UNIQUE INDEX UX_StoryPathGraphProgress_Child_StoryPath ON dbo.StoryPathGraphProgress (ChildId, StoryPathId);
END;
GO

-- Deferred FK: StartNodeId references StoryNodes (added after both tables exist).
IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_StoryPaths_StoryNodes_StartNodeId'
)
BEGIN
    ALTER TABLE dbo.StoryPaths
        ADD CONSTRAINT FK_StoryPaths_StoryNodes_StartNodeId
        FOREIGN KEY (StartNodeId) REFERENCES dbo.StoryNodes (Id) ON DELETE NO ACTION;
END;
GO
