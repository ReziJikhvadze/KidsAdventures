/*
    The owner's rules of 2026-09-01, applied to the policy rows 035 seeded.

    Two rulings reach this table.

    Rule 5 — "we don't need additional reviews for images". A new check, `image_review`, decides
    whether the per-spread visual QA call is bought at all: 'blocker' is the reviewed loop this
    pipeline has always run, 'flag' — the default this script seeds — means no model is asked and the
    spread's stored QA record says REVIEW_SKIPPED_BY_POLICY in as many words. It reads like
    `human_review` rather than like the waivers: nothing is being shipped over a refusal, the check
    simply does not run, and nothing anywhere claims a page passed a review it never had.

    Rule 4 — the sizes we indicated for printing are correct. `PRESS_RESOLUTION` was measuring the
    approved artwork against a placement resolution the format does not require, and its refusal was
    withholding the printer's file on books whose art is the art we approved. It becomes a flag. Its
    three neighbours keep their blockers, because they are about what a press does with a file —
    geometry, colour, ink — rather than about a number we set ourselves.

    WHAT THIS SCRIPT DOES NOT DO is change what the supplier is told. The raw gate results are
    untouched: a failing PRESS_RESOLUTION still fails, a book whose spreads were never reviewed still
    answers REVIEW_SKIPPED_BY_POLICY on VISUAL_QA, both still make the handback verdict
    NOT_RELEASABLE, and both still raise an alarm. Severity decides publication to the family; it has
    never decided truth, and amendment B1's split is what keeps the two apart.

    Also seeded: `name_fidelity` as a blocker. It has been a code default since the observed defect of
    2026-09-01 (a child called ვეკო whose book was titled for ველო) and has never had a row, which is
    legal — BekiReleasePolicySnapshot answers for a check with no row — but leaves the admin table
    reading a default rather than a decision. It gets its row, at the severity the code already
    answers, so the two agree the way 035 requires.

    B8 hardening, as 035: one batch, one transaction, opened with an application lock so racing App
    Service instances serialise instead of interleaving. Every write is idempotent, and re-running is
    a no-op.
*/
SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

DECLARE @lock INT;

EXEC @lock = sp_getapplock
    @Resource = N'AdventurePacks:036_BekiImageReviewPolicy',
    @LockMode = N'Exclusive',
    @LockOwner = N'Transaction',
    @LockTimeout = 60000;

IF @lock < 0
BEGIN
    ROLLBACK TRANSACTION;
    THROW 50036, N'036_BekiImageReviewPolicy could not take the application lock; another instance is applying it.', 1;
END;

/*
    Nothing to do at all if 035 has not run.

    The migrator runs scripts in order, so this is defensive rather than expected — but a script that
    silently created the table itself would be a second definition of it, and one of the two would
    eventually be the stale one.
*/
IF OBJECT_ID(N'dbo.BekiReleaseChecks', N'U') IS NULL
BEGIN
    ROLLBACK TRANSACTION;
    THROW 50036, N'036_BekiImageReviewPolicy needs dbo.BekiReleaseChecks, which 035 creates.', 1;
END;

-- ==================================================================================================
-- The new rows
-- ==================================================================================================

/*
    Inserted only when absent, exactly as 035 seeds: a deployment where somebody has already set one
    of these keeps their decision through this migration.

    Both severities are 'blocker' or 'flag' and both classes are 'all', so CK_BekiReleaseChecks_Severity
    and CK_BekiReleaseChecks_Class accept every row below without amendment — this script introduces
    no new vocabulary, only new check ids.
*/
DECLARE @new TABLE (CheckId NVARCHAR(64), DeliverableClass NVARCHAR(16), Severity NVARCHAR(16));

INSERT INTO @new (CheckId, DeliverableClass, Severity)
VALUES
    -- Rule 5. 'flag' means the per-spread visual review is not bought; 'blocker' restores it for
    -- every book drawn after the change.
    (N'image_review',  N'all', N'flag'),

    -- The name the parent typed, spelled the way they typed it. A blocker, and the only one of ours.
    (N'name_fidelity', N'all', N'blocker');

INSERT INTO dbo.BekiReleaseChecks (CheckId, DeliverableClass, Severity, UpdatedBy, UpdatedAtUtc)
SELECT n.CheckId, n.DeliverableClass, n.Severity, N'seed:036', SYSUTCDATETIME()
  FROM @new AS n
 WHERE NOT EXISTS (
     SELECT 1
       FROM dbo.BekiReleaseChecks AS c
      WHERE c.CheckId = n.CheckId
        AND c.DeliverableClass = n.DeliverableClass
 );

-- ==================================================================================================
-- Rule 4: PRESS_RESOLUTION becomes a flag on every row nobody has touched
-- ==================================================================================================

/*
    THE PREDICATE IS "THE OPERATOR HAS NEVER TOUCHED THIS ROW", AND IT IS NOT `UpdatedBy IS NULL`.

    That is the obvious spelling and it would match nothing. 035 stamps every row it seeds with
    UpdatedBy = 'seed:035' rather than leaving it null — the column records who last decided the
    value, and "the migration did" is a real answer — so an untouched row is one whose UpdatedBy is
    still a seed marker. Null is accepted too, for a row inserted by some other hand before this
    convention existed.

    The distinction is the whole point of the statement. An operator who has deliberately made
    PRESS_RESOLUTION a blocker on this deployment must keep that decision through a deployment; a
    default they have never expressed an opinion about is ours to change.

    Every class row, not just 'all': PRESS_RESOLUTION is not one of the two per-artifact gates, so
    today it has exactly one row — but a deployment that has grown a per-class row for it should not
    end up with half of its rows on the new default and half on the old.
*/
UPDATE dbo.BekiReleaseChecks
   SET Severity     = N'flag',
       UpdatedBy    = N'seed:036',
       UpdatedAtUtc = SYSUTCDATETIME()
 WHERE CheckId = N'PRESS_RESOLUTION'
   AND Severity <> N'flag'
   AND (UpdatedBy IS NULL OR UpdatedBy LIKE N'seed:%');

COMMIT TRANSACTION;
