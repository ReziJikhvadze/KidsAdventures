SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
  Beki story pipeline — persistence for the 12-page series format.

  This sits alongside the existing AdventurePacks tables rather than replacing them. The
  Beki pipeline is a different product shape (one cover + exactly 12 pages, a reviewer
  stage, a memory engine) and runs behind a feature flag, so the two must be able to
  coexist until Beki is proven and the old flow is retired.

  Three decisions worth stating, because the rest of the schema follows from them:

  1. The story is stored as JSON, not shredded into columns. It is validated against a
     fixed schema before it ever arrives here, it is read back whole, and pages are never
     queried individually. Twelve child rows per book would buy nothing and cost joins.

  2. Both the raw generator output and the final story are kept. Comparing them is the
     only way to learn what the reviewer actually fixes, which is what tells us whether a
     rule belongs in the generator prompt instead.

  3. Continuation memory is a first-class table, not a column on the story. It is written
     once per approved book and read as the input to the *next* book, so it has a
     different lifetime and a different access pattern to the story itself.
*/

IF OBJECT_ID(N'dbo.BekiStories', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BekiStories
    (
        Id                      UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_BekiStories PRIMARY KEY,

        -- Idempotency key from the caller. A retried request must not bill twice.
        RequestId               NVARCHAR(100)    NOT NULL,

        UserId                  UNIQUEIDENTIFIER NOT NULL,

        -- The child this book is about. Nullable so a guest preview can be generated
        -- before an account exists.
        CharacterId             UNIQUEIDENTIFIER NULL,

        -- Position in the child's series. Book 1 has no previous memory.
        BookNumber              INT              NOT NULL,

        ChildName               NVARCHAR(80)     NOT NULL,
        AgeBand                 NVARCHAR(16)     NOT NULL,
        Theme                   NVARCHAR(120)    NOT NULL,
        TitleKa                 NVARCHAR(150)    NULL,

        -- pending | generating | approved | needs_human_review | failed
        Status                  NVARCHAR(32)     NOT NULL
            CONSTRAINT DF_BekiStories_Status DEFAULT N'pending',

        -- Full story-output-v1 document. NVARCHAR(MAX): a 12-page Georgian book with
        -- metadata runs well past any fixed width.
        FinalStoryJson          NVARCHAR(MAX)    NULL,

        -- Pre-review draft, kept for prompt tuning. Safe to purge on a retention policy.
        RawGeneratorOutputJson  NVARCHAR(MAX)    NULL,

        -- The exact validated input, so a book can be reproduced or explained later.
        StoryInputJson          NVARCHAR(MAX)    NULL,

        -- Reviewer verdict, surfaced to the admin console without parsing the whole story.
        ReviewStatus            NVARCHAR(32)     NULL,
        ValidationErrorsJson    NVARCHAR(MAX)    NULL,
        FailureReason           NVARCHAR(200)    NULL,

        -- Provenance. Without these, a quality regression cannot be traced to a cause.
        CreativeSeedId          NVARCHAR(100)    NULL,
        GeneratorPromptVersion  NVARCHAR(100)    NULL,
        ReviewerPromptVersion   NVARCHAR(100)    NULL,
        RepairPromptVersion     NVARCHAR(100)    NULL,
        GeneratorModel          NVARCHAR(100)    NULL,
        ReviewerModel           NVARCHAR(100)    NULL,
        InputSchemaVersion      NVARCHAR(16)     NOT NULL
            CONSTRAINT DF_BekiStories_InputSchema DEFAULT N'1.0',
        OutputSchemaVersion     NVARCHAR(16)     NOT NULL
            CONSTRAINT DF_BekiStories_OutputSchema DEFAULT N'1.0',

        CreatedAt               DATETIME2(3)     NOT NULL
            CONSTRAINT DF_BekiStories_CreatedAt DEFAULT SYSUTCDATETIME(),
        CompletedAt             DATETIME2(3)     NULL
    );
END;
GO

-- One story per request id: this is what makes a retry safe.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_BekiStories_RequestId')
BEGIN
    CREATE UNIQUE INDEX UX_BekiStories_RequestId ON dbo.BekiStories (RequestId);
END;
GO

-- "The next book for this child" is the pipeline's most frequent question.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BekiStories_Character_Book')
BEGIN
    CREATE INDEX IX_BekiStories_Character_Book
        ON dbo.BekiStories (CharacterId, BookNumber DESC)
        WHERE CharacterId IS NOT NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BekiStories_User')
BEGIN
    CREATE INDEX IX_BekiStories_User ON dbo.BekiStories (UserId, CreatedAt DESC);
END;
GO

/*
  Continuation memory — what the next book is allowed to assume already happened.

  Written once per approved book and read as input to the following one. Kept separate
  from the story so that memory can be corrected (a wrong fact poisons every later book)
  without rewriting an approved, already-printed story.
*/
IF OBJECT_ID(N'dbo.BekiContinuationMemory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BekiContinuationMemory
    (
        Id              UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_BekiContinuationMemory PRIMARY KEY,

        StoryId         UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT FK_BekiContinuationMemory_Story
                REFERENCES dbo.BekiStories (Id) ON DELETE CASCADE,

        CharacterId     UNIQUEIDENTIFIER NULL,
        BookNumber      INT              NOT NULL,

        -- The continuationMemory object from story-output-v1.
        MemoryJson      NVARCHAR(MAX)    NOT NULL,

        -- Denormalised because the next book's prompt needs it directly and it is the one
        -- field a human is most likely to inspect when a series stops making sense.
        NextChapterHookKa NVARCHAR(500)  NULL,

        CreatedAt       DATETIME2(3)     NOT NULL
            CONSTRAINT DF_BekiMemory_CreatedAt DEFAULT SYSUTCDATETIME()
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_BekiMemory_Story')
BEGIN
    CREATE UNIQUE INDEX UX_BekiMemory_Story ON dbo.BekiContinuationMemory (StoryId);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BekiMemory_Character_Book')
BEGIN
    CREATE INDEX IX_BekiMemory_Character_Book
        ON dbo.BekiContinuationMemory (CharacterId, BookNumber DESC)
        WHERE CharacterId IS NOT NULL;
END;
GO
