SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
  Which prompt variant wrote a book.

  Two prompts are only comparable if you can tell them apart afterwards. Without this the
  question "were the books written under v2 better" has no answer that does not involve
  remembering when the setting was changed.
*/

IF COL_LENGTH(N'dbo.MasterStoryRuns', N'PromptVersion') IS NULL
BEGIN
    ALTER TABLE dbo.MasterStoryRuns ADD PromptVersion NVARCHAR(10) NULL;
END;
GO
