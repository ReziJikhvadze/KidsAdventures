SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
  One row per master story call.

  The table exists for two reasons that happen to want the same columns.

  The first is evidence. When a book comes out wrong the only useful question is what the
  model was actually told, and a prompt rebuilt afterwards from the same inputs is not an
  answer — the inputs may have been edited since. So the prompts are stored as sent, beside
  the story that came back, before anything is projected or normalised.

  The second is patience. A whole book takes minutes to write and Azure closes an inbound
  request at 230 seconds, so the call cannot happen inside the request that asks for it. It
  runs as a job, and the browser needs somewhere to look while it waits. That is what
  Status and ProgressMessage are for.

  Guest rows are the reason ExpiresAt exists. The preview path deliberately used to write
  nothing at all, so that a visitor who never signs up left no trace. Polling makes storing
  something unavoidable, and the expiry is how that promise is kept anyway: the row lives
  long enough to be collected and then it is deleted. A row that has been claimed by an
  account has ExpiresAt set to NULL and is kept like any other book.
*/

IF OBJECT_ID(N'dbo.MasterStoryRuns', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MasterStoryRuns
    (
        Id                      UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_MasterStoryRuns PRIMARY KEY,

        -- NULL while the story belongs to a guest. Set when an account claims it.
        UserId                  UNIQUEIDENTIFIER NULL,

        -- The book this story became, once one exists.
        PackId                  UNIQUEIDENTIFIER NULL,

        -- Pending | Writing | Illustrating | Ready | Failed
        Status                  NVARCHAR(30)     NOT NULL
            CONSTRAINT DF_MasterStoryRuns_Status DEFAULT N'Pending',

        -- What the waiting parent is told. Free text because it is copy, not a state machine.
        ProgressMessage         NVARCHAR(400)    NULL,

        -- The six inputs a book is built from, kept so a run can be reproduced exactly.
        ChildName               NVARCHAR(200)    NOT NULL,
        Age                     INT              NOT NULL,
        Gender                  NVARCHAR(20)     NOT NULL,
        Theme                   NVARCHAR(50)     NOT NULL,
        EyeColor                NVARCHAR(50)     NULL,
        ExtraWishes             NVARCHAR(MAX)    NULL,
        AppearanceDescription   NVARCHAR(MAX)    NULL,

        -- The uploaded portrait, parked where the job can reach it. The photo arrives with the
        -- request but the illustration happens minutes later in a different process, and the
        -- likeness is noticeably better when the image model sees the actual face rather than
        -- only a written description of it. Deleted with the row.
        PhotoBlobUrl            NVARCHAR(1000)   NULL,
        StoryLanguage           NVARCHAR(10)     NOT NULL
            CONSTRAINT DF_MasterStoryRuns_Language DEFAULT N'ka',
        SpreadCount             INT              NOT NULL
            CONSTRAINT DF_MasterStoryRuns_Spreads DEFAULT 8,

        -- Exactly what was sent, and what it cost. Nullable because the row is created when
        -- the request arrives and the prompts are only built when the job starts; they are
        -- written just before the call, so a call that fails still leaves evidence of what it
        -- was asked.
        Model                   NVARCHAR(100)    NULL,
        SystemPrompt            NVARCHAR(MAX)    NULL,
        UserPrompt              NVARCHAR(MAX)    NULL,
        PromptTokens            INT              NOT NULL
            CONSTRAINT DF_MasterStoryRuns_PromptTokens DEFAULT 0,
        CompletionTokens        INT              NOT NULL
            CONSTRAINT DF_MasterStoryRuns_CompletionTokens DEFAULT 0,

        -- The story as the model returned it, untouched. Kept separately from the projected
        -- book so that a change to the projection can be replayed against old runs rather
        -- than losing what the model actually wrote.
        StoryJson               NVARCHAR(MAX)    NULL,

        -- The same story in the shape the app renders: sixteen pages, nine of them pictures.
        ContentJson             NVARCHAR(MAX)    NULL,

        CoverImageUrl           NVARCHAR(1000)   NULL,
        ErrorMessage            NVARCHAR(1000)   NULL,

        CreatedAt               DATETIME2(3)     NOT NULL
            CONSTRAINT DF_MasterStoryRuns_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt               DATETIME2(3)     NOT NULL
            CONSTRAINT DF_MasterStoryRuns_UpdatedAt DEFAULT SYSUTCDATETIME(),

        -- NULL means keep. A value means delete after it passes.
        ExpiresAt               DATETIME2(3)     NULL
    );
END;
GO

-- Reading a run back is always by id from the polling client, so the primary key covers
-- that. These two cover the other two questions asked of the table: which runs belong to an
-- account, and which have outlived their welcome.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MasterStoryRuns_UserId' AND object_id = OBJECT_ID(N'dbo.MasterStoryRuns'))
BEGIN
    CREATE INDEX IX_MasterStoryRuns_UserId
        ON dbo.MasterStoryRuns (UserId, CreatedAt DESC)
        WHERE UserId IS NOT NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MasterStoryRuns_ExpiresAt' AND object_id = OBJECT_ID(N'dbo.MasterStoryRuns'))
BEGIN
    CREATE INDEX IX_MasterStoryRuns_ExpiresAt
        ON dbo.MasterStoryRuns (ExpiresAt)
        WHERE ExpiresAt IS NOT NULL;
END;
GO
