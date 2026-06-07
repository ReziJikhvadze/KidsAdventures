IF COL_LENGTH(N'dbo.Users', N'BookCredits') IS NULL
BEGIN
    ALTER TABLE dbo.Users
        ADD BookCredits INT NOT NULL
            CONSTRAINT DF_Users_BookCredits DEFAULT (0);
END;
GO

IF COL_LENGTH(N'dbo.AdventurePacks', N'PdfCreditCharged') IS NULL
BEGIN
    ALTER TABLE dbo.AdventurePacks
        ADD PdfCreditCharged BIT NOT NULL
            CONSTRAINT DF_AdventurePacks_PdfCreditCharged DEFAULT (0);
END;
GO

IF OBJECT_ID(N'dbo.BookCreditPurchases', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BookCreditPurchases
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_BookCreditPurchases PRIMARY KEY,
        UserId UNIQUEIDENTIFIER NOT NULL,
        StripeSessionId NVARCHAR(128) NOT NULL,
        CreditsAdded INT NOT NULL,
        PlanType NVARCHAR(32) NOT NULL,
        CreatedAt DATETIME2 NOT NULL
    );

    CREATE UNIQUE INDEX UX_BookCreditPurchases_StripeSessionId
        ON dbo.BookCreditPurchases (StripeSessionId);
END;
GO
