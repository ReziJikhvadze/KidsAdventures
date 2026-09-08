SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
  Whether this parent has said yes to hearing from us.

  Separate from accepting the terms, and it has to be: the terms are the condition of making a
  book at all, and marketing is a favour the parent does us. Consent that cannot be withdrawn is
  not consent, which is why this is a column rather than a tick that lives and dies in a browser
  tab — the parent's own space reads it back and can turn it off.

  NOT NULL with a default of 0, because silence is not agreement. Every account taken before
  today was never asked, and "never asked" and "said no" are the same permission to send nothing.
*/

IF COL_LENGTH(N'dbo.Users', N'MarketingConsent') IS NULL
BEGIN
    ALTER TABLE dbo.Users
        ADD MarketingConsent BIT NOT NULL CONSTRAINT DF_Users_MarketingConsent DEFAULT (0);
END;
GO
