SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
  The birth date a book was written from.

  Age was arriving as a number the browser had worked out, and the run kept only that number.
  A book came back written for a one-year-old when the parent was certain they had entered 2023,
  and there was no way to tell which of them was wrong: the row held the conclusion and not the
  evidence, and both age calculations — the browser's and ours — are correct when read.

  So the date travels now and the age is derived from it here. Nullable because a parent may not
  have given one, and because every run written before this column existed has none.
*/

IF COL_LENGTH(N'dbo.MasterStoryRuns', N'BirthDate') IS NULL
BEGIN
    ALTER TABLE dbo.MasterStoryRuns ADD BirthDate DATE NULL;
END;
GO
