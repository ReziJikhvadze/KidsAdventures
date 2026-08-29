/*
    When a generation job last said anything about this pack.

    A pack has only ever carried CreatedAt: the status and progress writes touch no timestamp at
    all, so a job that stopped existing mid-book left a row in GeneratingStory that nothing could
    tell apart from a book being drawn right now. One did — pack a9f342cc-780f-4b59-ba5b-35f964ec869e
    stalled after its first spread and sat in GeneratingStory permanently, paid for, with the
    parent's progress bar at 20%.

    NULL is the honest value for every row written before this column existed, and for a pack that
    has never been claimed. The sweep therefore falls back to CreatedAt when it is NULL, which is
    what lets it reach the rows that are already stuck.

    The index is the sweep's: every five minutes it asks for the non-terminal statuses whose
    heartbeat has gone quiet, and CreatedAt rides along so the NULL fallback is answered from the
    index rather than from the table — a table whose rows each carry an entire book in
    GeneratedJson.
*/
IF COL_LENGTH(N'dbo.AdventurePacks', N'GenerationHeartbeatUtc') IS NULL
BEGIN
    ALTER TABLE dbo.AdventurePacks ADD GenerationHeartbeatUtc DATETIME2(3) NULL;
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_AdventurePacks_Status_GenerationHeartbeatUtc'
      AND object_id = OBJECT_ID(N'dbo.AdventurePacks'))
BEGIN
    CREATE INDEX IX_AdventurePacks_Status_GenerationHeartbeatUtc
        ON dbo.AdventurePacks (Status, GenerationHeartbeatUtc)
        INCLUDE (CreatedAt);
END;
GO

/*
    The same question asked of a preview run, which needs no new column: MasterStoryRuns has
    carried UpdatedAt since 028 and every write in its repository sets it. Only the index is
    missing, and for the same reason as above — the sweep reads by status and staleness, and the
    table's other columns are two whole books wide.
*/
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_MasterStoryRuns_Status_UpdatedAt'
      AND object_id = OBJECT_ID(N'dbo.MasterStoryRuns'))
BEGIN
    CREATE INDEX IX_MasterStoryRuns_Status_UpdatedAt
        ON dbo.MasterStoryRuns (Status, UpdatedAt);
END;
GO
