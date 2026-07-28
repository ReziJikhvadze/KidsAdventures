SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
  Extends AdventurePacks into the book model.

  A book is now part of a series: it belongs to a world, may continue from an
  earlier book, and is only fully readable once its order is paid. AccessLevel is
  the gate — 'Preview' means cover plus page one, 'Full' means all seven pages —
  which replaces the old "story is free, PDF costs a credit" arrangement.
*/

IF COL_LENGTH(N'dbo.AdventurePacks', N'SeriesId') IS NULL
BEGIN
    ALTER TABLE dbo.AdventurePacks ADD SeriesId UNIQUEIDENTIFIER NULL;
END;
GO

IF COL_LENGTH(N'dbo.AdventurePacks', N'SequenceNumber') IS NULL
BEGIN
    ALTER TABLE dbo.AdventurePacks
        ADD SequenceNumber INT NOT NULL
            CONSTRAINT DF_AdventurePacks_SequenceNumber DEFAULT (1);
END;
GO

IF COL_LENGTH(N'dbo.AdventurePacks', N'ContinuesFromBookId') IS NULL
BEGIN
    ALTER TABLE dbo.AdventurePacks ADD ContinuesFromBookId UNIQUEIDENTIFIER NULL;
END;
GO

IF COL_LENGTH(N'dbo.AdventurePacks', N'AccessLevel') IS NULL
BEGIN
    ALTER TABLE dbo.AdventurePacks
        ADD AccessLevel NVARCHAR(16) NOT NULL
            CONSTRAINT DF_AdventurePacks_AccessLevel DEFAULT (N'Preview');
END;
GO

IF COL_LENGTH(N'dbo.AdventurePacks', N'WorldId') IS NULL
BEGIN
    ALTER TABLE dbo.AdventurePacks ADD WorldId NVARCHAR(32) NULL;
END;
GO

IF COL_LENGTH(N'dbo.AdventurePacks', N'PrimaryCharacterId') IS NULL
BEGIN
    ALTER TABLE dbo.AdventurePacks ADD PrimaryCharacterId UNIQUEIDENTIFIER NULL;
END;
GO

IF COL_LENGTH(N'dbo.AdventurePacks', N'Title') IS NULL
BEGIN
    ALTER TABLE dbo.AdventurePacks ADD Title NVARCHAR(256) NULL;
END;
GO

IF COL_LENGTH(N'dbo.AdventurePacks', N'CoverImageUrl') IS NULL
BEGIN
    ALTER TABLE dbo.AdventurePacks ADD CoverImageUrl NVARCHAR(512) NULL;
END;
GO

/* Whether the print run for this book has been paid for, independent of PrintOrders rows. */
IF COL_LENGTH(N'dbo.AdventurePacks', N'HasPrintEntitlement') IS NULL
BEGIN
    ALTER TABLE dbo.AdventurePacks
        ADD HasPrintEntitlement BIT NOT NULL
            CONSTRAINT DF_AdventurePacks_HasPrintEntitlement DEFAULT (0);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_AdventurePacks_Characters_PrimaryCharacterId')
BEGIN
    ALTER TABLE dbo.AdventurePacks
        ADD CONSTRAINT FK_AdventurePacks_Characters_PrimaryCharacterId
            FOREIGN KEY (PrimaryCharacterId) REFERENCES dbo.Characters (Id);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_AdventurePacks_AdventurePacks_ContinuesFrom')
BEGIN
    ALTER TABLE dbo.AdventurePacks
        ADD CONSTRAINT FK_AdventurePacks_AdventurePacks_ContinuesFrom
            FOREIGN KEY (ContinuesFromBookId) REFERENCES dbo.AdventurePacks (Id);
END;
GO

/* Existing packs predate worlds; map their theme across and open them fully,
   since they were already paid for under the old credit model. */
UPDATE dbo.AdventurePacks
SET WorldId = LOWER(Theme)
WHERE WorldId IS NULL
  AND Theme IS NOT NULL;
GO

UPDATE dbo.AdventurePacks
SET AccessLevel = N'Full'
WHERE AccessLevel = N'Preview'
  AND Status = N'Completed';
GO

UPDATE p
SET p.PrimaryCharacterId = c.Id
FROM dbo.AdventurePacks AS p
INNER JOIN dbo.Characters AS c ON c.LegacyChildId = p.ChildId
WHERE p.PrimaryCharacterId IS NULL;
GO

/* One series per child, so the adventure map has a spine to hang books off. */
UPDATE p
SET p.SeriesId = p.ChildId
FROM dbo.AdventurePacks AS p
WHERE p.SeriesId IS NULL;
GO

/*
  Join table capping a book at three characters. The unique index on
  (BookId, Position) combined with the 1-3 check constraint is what enforces the
  cap in the database rather than only in application code.
*/
IF OBJECT_ID(N'dbo.BookCharacters', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BookCharacters
    (
        BookId      UNIQUEIDENTIFIER NOT NULL,
        CharacterId UNIQUEIDENTIFIER NOT NULL,
        Position    TINYINT          NOT NULL,
        CreatedAt   DATETIME2        NOT NULL
            CONSTRAINT DF_BookCharacters_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_BookCharacters PRIMARY KEY (BookId, CharacterId),
        CONSTRAINT FK_BookCharacters_AdventurePacks
            FOREIGN KEY (BookId) REFERENCES dbo.AdventurePacks (Id) ON DELETE CASCADE,
        CONSTRAINT FK_BookCharacters_Characters
            FOREIGN KEY (CharacterId) REFERENCES dbo.Characters (Id),
        CONSTRAINT CK_BookCharacters_Position CHECK (Position BETWEEN 1 AND 3)
    );

    CREATE UNIQUE INDEX UX_BookCharacters_BookId_Position
        ON dbo.BookCharacters (BookId, Position);
END;
GO

/* Every existing pack starred its child; record that as position 1. */
INSERT INTO dbo.BookCharacters (BookId, CharacterId, Position, CreatedAt)
SELECT p.Id, p.PrimaryCharacterId, 1, p.CreatedAt
FROM dbo.AdventurePacks AS p
WHERE p.PrimaryCharacterId IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM dbo.BookCharacters AS bc WHERE bc.BookId = p.Id);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AdventurePacks_SeriesId_SequenceNumber' AND object_id = OBJECT_ID(N'dbo.AdventurePacks'))
BEGIN
    CREATE INDEX IX_AdventurePacks_SeriesId_SequenceNumber
        ON dbo.AdventurePacks (SeriesId, SequenceNumber);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AdventurePacks_PrimaryCharacterId' AND object_id = OBJECT_ID(N'dbo.AdventurePacks'))
BEGIN
    CREATE INDEX IX_AdventurePacks_PrimaryCharacterId
        ON dbo.AdventurePacks (PrimaryCharacterId, CreatedAt);
END;
GO
