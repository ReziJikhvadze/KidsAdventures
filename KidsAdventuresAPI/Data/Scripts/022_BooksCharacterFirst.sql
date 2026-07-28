SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
  A book now keys off a Character, not a Child.

  Script 015 added PrimaryCharacterId, but ChildId was still NOT NULL with a foreign
  key into the legacy Children table. A parent who signs up today creates Characters
  and never gets a Children row, so every new book would violate that key. ChildId
  becomes nullable and loses its constraint, staying only as a breadcrumb back to
  pre-Characters data.

  PrimaryCharacterId is not made NOT NULL: rows that predate Characters and whose
  child was already deleted have nothing to point at, and rewriting history to
  satisfy a constraint would be worse than tolerating the null.
*/

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_AdventurePacks_Children_ChildId')
BEGIN
    ALTER TABLE dbo.AdventurePacks DROP CONSTRAINT FK_AdventurePacks_Children_ChildId;
END;
GO

IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.AdventurePacks')
      AND name = N'ChildId'
      AND is_nullable = 0)
BEGIN
    -- The index has to go first; ALTER COLUMN cannot touch an indexed column.
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AdventurePacks_ChildId' AND object_id = OBJECT_ID(N'dbo.AdventurePacks'))
    BEGIN
        DROP INDEX IX_AdventurePacks_ChildId ON dbo.AdventurePacks;
    END;

    ALTER TABLE dbo.AdventurePacks ALTER COLUMN ChildId UNIQUEIDENTIFIER NULL;

    CREATE INDEX IX_AdventurePacks_ChildId ON dbo.AdventurePacks (ChildId);
END;
GO

/*
  Series identity moves to the hero character.

  Script 015 seeded SeriesId from ChildId as a stopgap. A series belongs to the child
  the books are about, and that child is now a Character, so realign the two. Without
  this, a legacy book and a new book about the same child would sit in different
  series and the adventure map would show two spines for one child.
*/
UPDATE p
SET p.SeriesId = p.PrimaryCharacterId
FROM dbo.AdventurePacks AS p
WHERE p.PrimaryCharacterId IS NOT NULL
  AND (p.SeriesId IS NULL OR p.SeriesId <> p.PrimaryCharacterId);
GO

/*
  Renumber each series so SequenceNumber reflects creation order. Script 015 defaulted
  every row to 1, which would make "chapter 3" ambiguous the moment a child has more
  than one book.
*/
WITH ordered AS (
    SELECT
        Id,
        ROW_NUMBER() OVER (PARTITION BY SeriesId ORDER BY CreatedAt ASC, Id ASC) AS Position
    FROM dbo.AdventurePacks
    WHERE SeriesId IS NOT NULL
)
UPDATE p
SET p.SequenceNumber = o.Position
FROM dbo.AdventurePacks AS p
INNER JOIN ordered AS o ON o.Id = p.Id
WHERE p.SequenceNumber <> o.Position;
GO

/*
  Books bought under the credit wallet keep what their owner paid for.

  PdfCreditCharged meant "a credit was spent to illustrate this pack", which under the
  new model is exactly a fully unlocked digital book. Orders never existed for these,
  so the entitlement has to be carried on the book itself.
*/
UPDATE dbo.AdventurePacks
SET AccessLevel = N'Full'
WHERE AccessLevel = N'Preview'
  AND PdfCreditCharged = 1;
GO
