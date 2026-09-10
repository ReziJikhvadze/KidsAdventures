SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
  Whose preview this is, and which child it was written for.

  UserId has existed since 028, but only fulfilment ever wrote it: a run belonged to nobody until
  a book was paid for. That was true of the flow it was built for - the first preview happens
  before anyone signs in - and false of every one after it, where the parent is signed in the
  whole time and the story is being written for a child the account already holds. Their own
  space could not show the preview because nothing in the database said it was theirs.

  So the column is written at the start too, when the caller arrives with a token, and
  CharacterId is added beside it because the space is organised by child: a preview with no hero
  is a card that cannot be filed under the one it was made for.

  Neither column changes what a guest leaves behind. An anonymous preview still carries no owner
  and still expires, and a signed-in parent's unpaid preview keeps its expiry as well - being
  able to see it is not the same as keeping the child's photograph indefinitely.
*/

IF COL_LENGTH(N'dbo.MasterStoryRuns', N'CharacterId') IS NULL
BEGIN
    ALTER TABLE dbo.MasterStoryRuns
        ADD CharacterId UNIQUEIDENTIFIER NULL;
END;
GO

/*
  The parent's own previews, newest first.

  Filtered on the column being present because the great majority of rows are anonymous and never
  read this way: the index covers the accounts that have one and stays out of the way of the
  guests that do not.
*/
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_MasterStoryRuns_UserId_CreatedAt'
      AND object_id = OBJECT_ID(N'dbo.MasterStoryRuns'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_MasterStoryRuns_UserId_CreatedAt
        ON dbo.MasterStoryRuns (UserId, CreatedAt DESC)
        INCLUDE (PackId, Status, CharacterId)
        WHERE UserId IS NOT NULL;
END;
GO
