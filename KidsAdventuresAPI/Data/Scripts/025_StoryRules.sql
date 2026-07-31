SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
  Story rules — the age x theme matrix behind the admin panel.

  Until now the only age tuning lived in AdventurePromptTexts as three hardcoded blocks
  (3-5, 6-9, 10-13), identical for every world. This table lets an operator tune each
  combination without a deploy.

  Deliberately additive: every tuning column is nullable, and the prompt builder only
  emits a line for the columns that are set. A freshly migrated database therefore has
  18 empty cells and produces byte-identical prompts to before — nothing changes until
  someone actually edits a cell.

  Resolution order is exact cell, then the theme-wide row for that age band, then the
  built-in locale text. That means an operator can tune one age band across all worlds
  with a single row, and override a single world where it matters.
*/

IF OBJECT_ID(N'dbo.StoryRules', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StoryRules
    (
        Id               UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_StoryRules PRIMARY KEY,

        -- '3-5' | '6-9' | '10-13'
        AgeBand          NVARCHAR(16)     NOT NULL,

        -- ThemeType name, or NULL meaning "every world in this age band".
        Theme            NVARCHAR(32)     NULL,

        -- Tuning. NULL means "leave this to the built-in guidance".
        MaxWordsPerPage  INT              NULL,
        MaxSentenceWords INT              NULL,

        -- 'simple' | 'standard' | 'rich'
        VocabularyLevel  NVARCHAR(16)     NULL,

        -- 0 = nothing tense at all, 3 = real jeopardy. Guides how much peril is allowed.
        ScarinessLimit   INT              NULL
            CONSTRAINT CK_StoryRules_Scariness CHECK (ScarinessLimit BETWEEN 0 AND 3),

        -- Free-text guidance appended verbatim to the story prompt for this cell.
        ExtraGuidance    NVARCHAR(1000)   NULL,

        IsActive         BIT              NOT NULL
            CONSTRAINT DF_StoryRules_IsActive DEFAULT (1),

        UpdatedByUserId  UNIQUEIDENTIFIER NULL,
        CreatedAt        DATETIME2        NOT NULL
            CONSTRAINT DF_StoryRules_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt        DATETIME2        NOT NULL
            CONSTRAINT DF_StoryRules_UpdatedAt DEFAULT (SYSUTCDATETIME())
    );

    -- One row per cell. The filtered index covers the theme-wide rows, where Theme is NULL
    -- and a plain unique index would allow duplicates.
    CREATE UNIQUE INDEX UX_StoryRules_Band_Theme
        ON dbo.StoryRules (AgeBand, Theme) WHERE Theme IS NOT NULL;

    CREATE UNIQUE INDEX UX_StoryRules_Band_AllThemes
        ON dbo.StoryRules (AgeBand) WHERE Theme IS NULL;
END;
GO

/*
  Seed the full 3 x 6 grid plus the three theme-wide rows, all with empty tuning, so the
  admin renders a complete matrix on first load and every cell has a stable id to PUT to.
*/
MERGE dbo.StoryRules AS target
USING (
    SELECT band.AgeBand, theme.Theme
    FROM (VALUES (N'3-5'), (N'6-9'), (N'10-13')) AS band(AgeBand)
    CROSS JOIN (VALUES
        (N'Dinosaurs'), (N'Space'), (N'Pirates'),
        (N'Animals'), (N'Airplanes'), (N'Magic'), (NULL)
    ) AS theme(Theme)
) AS source
ON target.AgeBand = source.AgeBand
   AND ISNULL(target.Theme, N'*') = ISNULL(source.Theme, N'*')
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, AgeBand, Theme) VALUES (NEWID(), source.AgeBand, source.Theme);
GO
