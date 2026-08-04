SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
  Beki visual pipeline — identity, visual bible, and one row per generated asset.

  The old flow kept nothing: a prompt was assembled inline, an image came back, and the
  reasoning behind it was gone. That makes "why does this page look wrong?" unanswerable
  and "regenerate exactly this page" impossible. Everything here exists to make an
  illustration explainable after the fact.

  Note on children's photographs: BekiChildIdentity stores the *derived* structured
  description, never the photo. The photo stays in blob storage under its existing
  retention rules, and the identity spec is what the pipeline actually consumes.
*/

IF OBJECT_ID(N'dbo.BekiChildIdentity', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BekiChildIdentity
    (
        Id                  UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_BekiChildIdentity PRIMARY KEY,

        CharacterId         UNIQUEIDENTIFIER NOT NULL,

        -- good | usable_with_limits | insufficient. 'insufficient' means the parent is
        -- asked to re-upload rather than a generic child being silently invented.
        ReferenceQuality    NVARCHAR(32)     NOT NULL,

        -- The Character Identity Spec produced by the analyzer.
        IdentityJson        NVARCHAR(MAX)    NOT NULL,

        -- Which photo produced this, so a re-upload can invalidate the spec.
        PhotoReference      NVARCHAR(400)    NULL,

        AnalyzerPromptVersion NVARCHAR(100)  NULL,
        AnalyzerModel       NVARCHAR(100)    NULL,

        -- Monotonic per character: a new photo supersedes rather than overwrites, because
        -- already-printed books were generated against the older spec.
        Version             INT              NOT NULL
            CONSTRAINT DF_BekiChildIdentity_Version DEFAULT 1,

        CreatedAt           DATETIME2(3)     NOT NULL
            CONSTRAINT DF_BekiChildIdentity_CreatedAt DEFAULT SYSUTCDATETIME()
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BekiChildIdentity_Character')
BEGIN
    CREATE INDEX IX_BekiChildIdentity_Character
        ON dbo.BekiChildIdentity (CharacterId, Version DESC);
END;
GO

/*
  The Visual Bible: one per book. It fixes the hero's outfit, Beki's canonical lock,
  guest locks, world palette and composition defaults, so that twelve separately
  generated images belong to the same book.
*/
IF OBJECT_ID(N'dbo.BekiVisualBible', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BekiVisualBible
    (
        Id                UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_BekiVisualBible PRIMARY KEY,

        StoryId           UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT FK_BekiVisualBible_Story
                REFERENCES dbo.BekiStories (Id) ON DELETE CASCADE,

        BibleJson         NVARCHAR(MAX)    NOT NULL,

        -- Denormalised: every page prompt restates the outfit, and it is the first thing
        -- to check when a child appears to change clothes mid-book.
        OutfitId          NVARCHAR(100)    NULL,

        IdentityId        UNIQUEIDENTIFIER NULL
            CONSTRAINT FK_BekiVisualBible_Identity
                REFERENCES dbo.BekiChildIdentity (Id),

        BiblePromptVersion NVARCHAR(100)   NULL,
        BibleModel        NVARCHAR(100)    NULL,
        Version           INT              NOT NULL
            CONSTRAINT DF_BekiVisualBible_Version DEFAULT 1,

        CreatedAt         DATETIME2(3)     NOT NULL
            CONSTRAINT DF_BekiVisualBible_CreatedAt DEFAULT SYSUTCDATETIME()
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BekiVisualBible_Story')
BEGIN
    CREATE INDEX IX_BekiVisualBible_Story ON dbo.BekiVisualBible (StoryId, Version DESC);
END;
GO

/*
  One row per generated image: the hero anchor, the cover, and each of the twelve pages.

  The anchor is stored here rather than in its own table because it goes through exactly
  the same generate -> review -> repair lifecycle as a page, and giving it a separate
  table would mean duplicating that lifecycle in two places.
*/
IF OBJECT_ID(N'dbo.BekiVisualAssets', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BekiVisualAssets
    (
        Id                UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_BekiVisualAssets PRIMARY KEY,

        StoryId           UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT FK_BekiVisualAssets_Story
                REFERENCES dbo.BekiStories (Id) ON DELETE CASCADE,

        -- hero_anchor | cover | page
        AssetType         NVARCHAR(24)     NOT NULL,

        -- 1..12 for pages; NULL for the anchor and the cover.
        PageNumber        INT              NULL,

        /*
          pending | generating | review_pending | repair_pending | approved | failed
          Pages must not start before the anchor reaches 'approved': the anchor is what
          every later page is matched against.
        */
        Status            NVARCHAR(32)     NOT NULL
            CONSTRAINT DF_BekiVisualAssets_Status DEFAULT N'pending',

        BlobUrl           NVARCHAR(1000)   NULL,

        -- The scene spec this image was generated from (page-scene-v1).
        SceneSpecJson     NVARCHAR(MAX)    NULL,

        -- The exact final prompt sent to the image model. The single most useful field
        -- when an illustration is wrong and nobody can say why.
        FinalPromptText   NVARCHAR(MAX)    NULL,

        -- visual-review-v1 output, including per-dimension scores.
        ReviewJson        NVARCHAR(MAX)    NULL,
        ReviewDecision    NVARCHAR(24)     NULL,   -- approve | repair | regenerate

        RepairAttempts    INT              NOT NULL
            CONSTRAINT DF_BekiVisualAssets_RepairAttempts DEFAULT 0,
        RegenerationAttempts INT           NOT NULL
            CONSTRAINT DF_BekiVisualAssets_RegenAttempts DEFAULT 0,

        -- Every reference version that shaped this image. Change any one of them and the
        -- output changes, so a reproduction needs all of them.
        VisualBibleId     UNIQUEIDENTIFIER NULL
            CONSTRAINT FK_BekiVisualAssets_Bible REFERENCES dbo.BekiVisualBible (Id),
        IdentityId        UNIQUEIDENTIFIER NULL
            CONSTRAINT FK_BekiVisualAssets_Identity REFERENCES dbo.BekiChildIdentity (Id),
        HeroAnchorAssetId UNIQUEIDENTIFIER NULL,
        BekiAssetVersion  NVARCHAR(100)    NULL,
        PromptVersion     NVARCHAR(100)    NULL,
        ImageModel        NVARCHAR(100)    NULL,
        ImageQuality      NVARCHAR(32)     NULL,
        ImageSize         NVARCHAR(32)     NULL,

        FailureReason     NVARCHAR(500)    NULL,
        LatencyMs         INT              NULL,

        CreatedAt         DATETIME2(3)     NOT NULL
            CONSTRAINT DF_BekiVisualAssets_CreatedAt DEFAULT SYSUTCDATETIME(),
        ApprovedAt        DATETIME2(3)     NULL
    );
END;
GO

/*
  Idempotency: one asset per slot per story. Without this a retried job silently pays
  for a second copy of page 7 and leaves two candidates with no way to choose.
  Filtered on PageNumber because the anchor and cover both carry NULL.
*/
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_BekiVisualAssets_Page')
BEGIN
    CREATE UNIQUE INDEX UX_BekiVisualAssets_Page
        ON dbo.BekiVisualAssets (StoryId, AssetType, PageNumber)
        WHERE PageNumber IS NOT NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_BekiVisualAssets_Singleton')
BEGIN
    CREATE UNIQUE INDEX UX_BekiVisualAssets_Singleton
        ON dbo.BekiVisualAssets (StoryId, AssetType)
        WHERE PageNumber IS NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BekiVisualAssets_Status')
BEGIN
    CREATE INDEX IX_BekiVisualAssets_Status ON dbo.BekiVisualAssets (Status, CreatedAt);
END;
GO
