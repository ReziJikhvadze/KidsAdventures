SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
  Series memory — what a child's world remembers between books.

  A series is the hero (AdventurePacks.SeriesId = the primary character's id), so a child
  carries one memory no matter which world each book visits.

  The memory is a DISTILLED snapshot, not the previous story's text. Feeding whole stories
  forward would make the prompt grow without bound and, by book four or five, cost more than
  the book earns while burying the details that actually matter. Instead each finished book is
  reduced once to a small JSON document — companions met, memories that mattered, the hero's
  standing goal, how each world was left — and that document is what the next book reads.

  One row per series. The snapshot is rewritten in place, so it stays a constant-size input
  however long the series runs.
*/

IF OBJECT_ID(N'dbo.SeriesMemories', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SeriesMemories
    (
        SeriesId    UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_SeriesMemories PRIMARY KEY,
        UserId      UNIQUEIDENTIFIER NOT NULL,

        -- Distilled snapshot: { companions[], memories[], goal, worlds[] }.
        MemoryJson  NVARCHAR(MAX)    NOT NULL,

        -- Human-readable rendering handed to the story prompt, already in the book's language.
        MemoryText  NVARCHAR(MAX)    NULL,

        -- Guards against distilling the same book twice when a job is retried.
        LastBookId  UNIQUEIDENTIFIER NULL,
        BookCount   INT              NOT NULL
            CONSTRAINT DF_SeriesMemories_BookCount DEFAULT (0),

        CreatedAt   DATETIME2        NOT NULL
            CONSTRAINT DF_SeriesMemories_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt   DATETIME2        NOT NULL
            CONSTRAINT DF_SeriesMemories_UpdatedAt DEFAULT (SYSUTCDATETIME())
    );

    CREATE INDEX IX_SeriesMemories_UserId ON dbo.SeriesMemories (UserId);
END;
GO
