SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
  Whether the family's copy and the printer's copy have been released, on the row that is asked.

  Until now the answer was "PdfUrl is not null". That column holds a blob url, and it only ever
  answered the release question as a side effect: it was written at the moment the release report
  permitted publication, and nulled when that permission was revoked. No PDF of a book is kept any
  more - each is composed for the click that asks for it - so the column is null on every book and
  the question it used to answer has no answer at all.

  What that costs is not theoretical. The admin list marks every finished book as withheld, the
  awaiting-review filter matches all of them, an approval raises a blocker alarm saying the family
  received nothing when they received their book, and a print order cannot leave the queue because
  the guard against shipping an unapproved book reads the same empty column.

  A bit apiece, written where the url used to be written, restores exactly what was lost and
  nothing else. Backfilled from the urls, so every book made before today keeps the answer it
  already had; a book finished since the files stopped being kept has neither url nor bit, and the
  reconciliation sweep sets it on its next pass from the verdict, which is where the truth has been
  all along.
*/

IF COL_LENGTH(N'dbo.AdventurePacks', N'CustomerPdfReleased') IS NULL
BEGIN
    ALTER TABLE dbo.AdventurePacks
        ADD CustomerPdfReleased BIT NOT NULL
            CONSTRAINT DF_AdventurePacks_CustomerPdfReleased DEFAULT 0;
END;
GO

IF COL_LENGTH(N'dbo.AdventurePacks', N'PressFilesReleased') IS NULL
BEGIN
    ALTER TABLE dbo.AdventurePacks
        ADD PressFilesReleased BIT NOT NULL
            CONSTRAINT DF_AdventurePacks_PressFilesReleased DEFAULT 0;
END;
GO

-- A book still carrying a url was released under the old rule, and says so.
UPDATE dbo.AdventurePacks
SET CustomerPdfReleased = 1
WHERE CustomerPdfReleased = 0
  AND PdfUrl IS NOT NULL;
GO

UPDATE dbo.AdventurePacks
SET PressFilesReleased = 1
WHERE PressFilesReleased = 0
  AND PrintPdfUrl IS NOT NULL;
GO
