/*
    The release policy, the alarms it produces, and the column that says which pipeline drew a book.

    Owner ruling, 2026-09-01: books are generated and delivered to parents almost always; problems
    become admin alarms to check later, not blocks; human visual review is skipped by default and can
    be turned on from admin; every check is admin-markable as a BLOCKER or a FLAG. Nothing in here
    changes what the SUPPLIER is told — the handback verdict is still computed from the raw gate
    results (amendment B1's truth split). What it changes is whether the machinery is allowed to keep
    a paid book away from the family who bought it.

    Three objects:

      BekiReleaseChecks  — one row per (check, deliverable class). The class is B2's correction: two
                           of the sixteen supplier gates (RENDER_VALIDATION, QR) aggregate evidence
                           from artifacts that belong to different deliverables, so "blocker" has to
                           be sayable about the printer's files and "flag" about the reading copy at
                           the same time. 'all' is the wildcard row and is what almost every check
                           uses.

      BekiAlarms         — one row per waived incident, deduplicated on (PackId, CheckId, EvidenceKey)
                           so a book that is re-evaluated four times pages nobody four times. B4: a
                           re-raise moves LastSeenUtc; a re-raise of a reviewed alarm REOPENS it, and
                           keeps ReviewedBy so the record still says who had looked.

      AdventurePacks.GenerationPipeline — B5's durable discriminator. Until now nothing on a book said
                           whether it was drawn by the Beki composite pipeline or the legacy per-page
                           flow, and three separate decisions (BookReady, the download refusal, the
                           legacy auto-illustration trigger) were guessing.

    B8 hardening: the whole script is ONE batch inside ONE transaction, opened with an application
    lock, so two instances starting at once serialize rather than racing each other's CREATEs. It is
    a single batch on purpose — the migrator splits on GO and runs each batch separately, and a
    transaction that spans batches is a transaction that can be left open by a batch that fails. The
    two statements that must not be compiled before their column exists (the backfill and the index
    on the new column) go through EXEC, which is the standard way to defer name resolution inside a
    batch that just added the column.
*/
SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

/*
    Serialise racing instances. App Service restarts on every deploy and can bring two instances up
    together, and both would run this script: without the lock they interleave between the
    IF NOT EXISTS and the CREATE, and one of them fails the deployment on a duplicate object.

    Transaction-scoped, so it is released by the COMMIT below or by the rollback of a failure — there
    is no path that leaves the lock held. Sixty seconds is generous: everything below is metadata
    work plus one backfill.
*/
DECLARE @lock INT;

EXEC @lock = sp_getapplock
    @Resource = N'AdventurePacks:035_BekiReleasePolicy',
    @LockMode = N'Exclusive',
    @LockOwner = N'Transaction',
    @LockTimeout = 60000;

IF @lock < 0
BEGIN
    ROLLBACK TRANSACTION;
    THROW 50035, N'035_BekiReleasePolicy could not take the application lock; another instance is applying it.', 1;
END;

-- ==================================================================================================
-- The policy table
-- ==================================================================================================

IF OBJECT_ID(N'dbo.BekiReleaseChecks', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BekiReleaseChecks
    (
        CheckId          NVARCHAR(64)  NOT NULL,
        /*
            'all' is the wildcard, and it is a real row rather than a NULL: a primary key with a
            nullable member is a key that cannot be looked up with an equality, and every read of
            this table is an equality on the pair.
        */
        DeliverableClass NVARCHAR(16)  NOT NULL CONSTRAINT DF_BekiReleaseChecks_Class DEFAULT N'all',
        Severity         NVARCHAR(16)  NOT NULL,
        UpdatedBy        NVARCHAR(256) NULL,
        UpdatedAtUtc     DATETIME2(3)  NULL,
        CONSTRAINT PK_BekiReleaseChecks PRIMARY KEY (CheckId, DeliverableClass)
    );
END;

/*
    Two words and no others. The severity is read by code that branches on it, and a row saying
    'BLOCKER ' or 'warn' would be a row whose meaning is decided by whichever comparison happens to
    run — the constraint is what makes SeverityOf's fallback to a code default mean "no row", rather
    than "a row nobody can read".
*/
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_BekiReleaseChecks_Severity')
BEGIN
    ALTER TABLE dbo.BekiReleaseChecks
        ADD CONSTRAINT CK_BekiReleaseChecks_Severity CHECK (Severity IN (N'blocker', N'flag'));
END;

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_BekiReleaseChecks_Class')
BEGIN
    ALTER TABLE dbo.BekiReleaseChecks
        ADD CONSTRAINT CK_BekiReleaseChecks_Class
            CHECK (DeliverableClass IN (N'all', N'shared', N'press', N'digital', N'package'));
END;

-- ==================================================================================================
-- The alarms
-- ==================================================================================================

IF OBJECT_ID(N'dbo.BekiAlarms', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BekiAlarms
    (
        Id            UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_BekiAlarms PRIMARY KEY,
        PackId        UNIQUEIDENTIFIER NOT NULL,
        OrderId       UNIQUEIDENTIFIER NULL,
        UserId        UNIQUEIDENTIFIER NOT NULL,
        CheckId       NVARCHAR(64)     NOT NULL,
        Severity      NVARCHAR(16)     NOT NULL,
        Detail        NVARCHAR(MAX)    NOT NULL,
        /*
            The blob a reviewer opens — the refused spread, the evidence document, the release-gates
            record. A name rather than bytes: the artifact is already in storage under a name this
            deployment builds deterministically, and a copy in SQL would be a second truth that ages.
        */
        EvidenceBlob  NVARCHAR(400)    NULL,
        /*
            What makes two raisings the same incident (B4). The SHA-256 of the evidence blob when
            there is one, or an attempt discriminator — the spread number and attempt count — when
            there is not. Never null: a null here would make the unique index below stop deduplicating
            exactly the alarms that repeat most.
        */
        EvidenceKey   NVARCHAR(128)    NOT NULL,
        CreatedAtUtc  DATETIME2(3)     NOT NULL CONSTRAINT DF_BekiAlarms_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        LastSeenUtc   DATETIME2(3)     NOT NULL CONSTRAINT DF_BekiAlarms_LastSeenUtc DEFAULT SYSUTCDATETIME(),
        ReviewedBy    NVARCHAR(256)    NULL,
        ReviewedAtUtc DATETIME2(3)     NULL,
        Resolution    NVARCHAR(32)     NULL
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_BekiAlarms_Severity')
BEGIN
    ALTER TABLE dbo.BekiAlarms
        ADD CONSTRAINT CK_BekiAlarms_Severity CHECK (Severity IN (N'blocker', N'flag'));
END;

/*
    A resolution is one of four words, or nothing at all.

    'acknowledged' — seen, nothing to do yet. 'fixed' — the underlying fault was corrected.
    'wont_fix' — judged acceptable for this book. 'false_alarm' — the check was wrong.
    Free text here would make the alarms list unfilterable within a month, which is the same as
    unreadable.
*/
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_BekiAlarms_Resolution')
BEGIN
    ALTER TABLE dbo.BekiAlarms
        ADD CONSTRAINT CK_BekiAlarms_Resolution
            CHECK (Resolution IS NULL
                   OR Resolution IN (N'acknowledged', N'fixed', N'wont_fix', N'false_alarm'));
END;

-- The deduplication key itself. Unique, because the re-raise path depends on there being at most
-- one row to move LastSeenUtc on.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'UX_BekiAlarms_Pack_Check_Evidence'
      AND object_id = OBJECT_ID(N'dbo.BekiAlarms'))
BEGIN
    CREATE UNIQUE INDEX UX_BekiAlarms_Pack_Check_Evidence
        ON dbo.BekiAlarms (PackId, CheckId, EvidenceKey);
END;

/*
    The console's own query: the open alarms, newest first. Filtered, because the interesting set is
    the small one — an alarm that has been reviewed is history, and an index that carried every
    reviewed alarm would grow without bound while answering a question about the ones that are not.
*/
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_BekiAlarms_Open'
      AND object_id = OBJECT_ID(N'dbo.BekiAlarms'))
BEGIN
    CREATE INDEX IX_BekiAlarms_Open
        ON dbo.BekiAlarms (LastSeenUtc DESC)
        INCLUDE (PackId, OrderId, UserId, CheckId, Severity)
        WHERE ReviewedAtUtc IS NULL;
END;

-- Per-book lookup, for the order page that shows one book's alarms beside its gates.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_BekiAlarms_PackId'
      AND object_id = OBJECT_ID(N'dbo.BekiAlarms'))
BEGIN
    CREATE INDEX IX_BekiAlarms_PackId ON dbo.BekiAlarms (PackId, LastSeenUtc DESC);
END;

-- ==================================================================================================
-- B5: which pipeline drew this book
-- ==================================================================================================

IF COL_LENGTH(N'dbo.AdventurePacks', N'GenerationPipeline') IS NULL
BEGIN
    /*
        NOT NULL with a default, rather than nullable.

        Every consumer of this column is a branch — BookReady, the download refusal, the legacy
        auto-illustration trigger — and a three-valued answer would give each of them a third
        behaviour nobody designed. 'legacy' is the honest value for a row written before the column
        existed AND the safe one: the legacy readiness rule is the one those books have always been
        judged by, so a backfill that missed a book changes nothing about it.
    */
    ALTER TABLE dbo.AdventurePacks
        ADD GenerationPipeline NVARCHAR(16) NOT NULL
            CONSTRAINT DF_AdventurePacks_GenerationPipeline DEFAULT N'legacy';
END;

/*
    The backfill, and the index on the new column, through EXEC.

    Deferred name resolution: SQL Server compiles a batch's statements against the catalogue as it
    stands when the batch starts, so a plain UPDATE naming GenerationPipeline in the same batch that
    just added it fails to compile — even though it would execute perfectly. EXEC compiles at
    execution time, when the column is there.

    THE PREDICATE IS A MIRROR OF C#. BookFormat.IsPrintPlan (Domain/Story/MasterStory.cs) is the one
    true definition of "this plan was written for the printing book format", and today it reads:

        promptVersion == "v5" || promptVersion == "v6"     (case-insensitive)

    Every new version of the printing flow joins that list, and a version added there without being
    added here would leave newly-backfilled books looking legacy. Nothing depends on this after the
    first run — BookFulfillmentService stamps the column in the same write that adopts or creates the
    pack from then on — so the coupling is a one-time read of a list that is documented in both
    places rather than a standing duplication.
*/
EXEC(N'
    UPDATE p
       SET p.GenerationPipeline = N''beki''
      FROM dbo.AdventurePacks AS p
     WHERE p.GenerationPipeline <> N''beki''
       AND EXISTS (
           SELECT 1
             FROM dbo.MasterStoryRuns AS r
            WHERE r.PackId = p.Id
              AND UPPER(LTRIM(RTRIM(ISNULL(r.PromptVersion, N'''')))) IN (N''V5'', N''V6'')
       );
');

EXEC(N'
    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N''IX_AdventurePacks_GenerationPipeline''
          AND object_id = OBJECT_ID(N''dbo.AdventurePacks''))
    BEGIN
        CREATE INDEX IX_AdventurePacks_GenerationPipeline
            ON dbo.AdventurePacks (GenerationPipeline, Status);
    END;
');

-- ==================================================================================================
-- The seeded defaults
-- ==================================================================================================

/*
    THESE ROWS AND THE CODE DEFAULTS MUST AGREE.

    BekiReleasePolicySnapshot.Defaults (Services/Story/BekiReleasePolicy.cs) answers for a check with
    no row at all — a fresh database, a check added by a later campaign, a row an operator deleted.
    If the two disagreed, a deployment's behaviour would depend on whether this script had run, which
    is the least debuggable kind of difference there is.

    Inserted rather than merged: an operator who has already flipped a check to blocker must keep
    their decision through the next deployment. Re-running this script is therefore a no-op on every
    row that exists.

    The shape of the defaults is the owner's ruling. Every pipeline quality check is a flag, human
    review is skipped, the shared and digital gates flag for parent publication — and the printer's
    files keep their blockers, because a press PDF is somebody else's money and a bad one is a reprint
    rather than a disappointment.
*/
DECLARE @defaults TABLE (CheckId NVARCHAR(64), DeliverableClass NVARCHAR(16), Severity NVARCHAR(16));

INSERT INTO @defaults (CheckId, DeliverableClass, Severity)
VALUES
    -- The pipeline's own quality refusals: B3's whitelist, and nothing else is policy-eligible.
    (N'centre_fold',           N'all',     N'flag'),
    (N'cover_bands',           N'all',     N'flag'),
    (N'image_qa',              N'all',     N'flag'),
    (N'qa_unreadable',         N'all',     N'flag'),

    -- The human gate. 'flag' means skipped; an admin turning it to 'blocker' restores the
    -- approve-before-publish flow for every book evaluated after the change.
    (N'human_review',          N'all',     N'flag'),

    -- The eight shared gates.
    (N'ASSET_LOCK',            N'all',     N'flag'),
    (N'EXACT_BEKI',            N'all',     N'flag'),
    (N'SINGLE_COVER_MASTER',   N'all',     N'flag'),
    (N'COVER_CONTINUITY',      N'all',     N'flag'),
    (N'INTERIOR_CONTINUITY',   N'all',     N'flag'),
    (N'TEXT_LAYER',            N'all',     N'flag'),
    (N'FONT_INTEGRITY',        N'all',     N'flag'),
    (N'VISUAL_QA',             N'all',     N'flag'),

    -- The reading copy's own gate, and the package's.
    (N'DIGITAL_GEOMETRY',      N'all',     N'flag'),
    (N'HANDBACK_COMPLETENESS', N'all',     N'flag'),

    -- The printer's four.
    (N'PRESS_GEOMETRY',        N'all',     N'blocker'),
    (N'PRESS_COLOR',           N'all',     N'blocker'),
    (N'PRESS_RESOLUTION',      N'all',     N'blocker'),
    (N'TEXT_COLOR_INTEGRITY',  N'all',     N'blocker'),

    /*
        B2's split. These two gates are answered per stored artifact, and the artifacts belong to
        different deliverables: the same RENDER_VALIDATION failure means "do not send this to the
        printer" about a press file and "tell somebody, later" about the reading copy.
    */
    (N'RENDER_VALIDATION',     N'press',   N'blocker'),
    (N'RENDER_VALIDATION',     N'digital', N'flag'),
    (N'QR',                    N'press',   N'blocker'),
    (N'QR',                    N'digital', N'flag');

INSERT INTO dbo.BekiReleaseChecks (CheckId, DeliverableClass, Severity, UpdatedBy, UpdatedAtUtc)
SELECT d.CheckId, d.DeliverableClass, d.Severity, N'seed:035', SYSUTCDATETIME()
  FROM @defaults AS d
 WHERE NOT EXISTS (
     SELECT 1
       FROM dbo.BekiReleaseChecks AS c
      WHERE c.CheckId = d.CheckId
        AND c.DeliverableClass = d.DeliverableClass
 );

COMMIT TRANSACTION;
