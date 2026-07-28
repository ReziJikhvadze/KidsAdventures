SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
  Orders replace the book-credit wallet.

  Money is stored in tetri (GEL minor units) as integers so a total is never the
  result of float arithmetic. The unique index on ProviderSessionId is the
  idempotency key for webhook fulfilment, reusing the pattern already proven by
  BookCreditPurchases.
*/

IF OBJECT_ID(N'dbo.Orders', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Orders
    (
        Id                      UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_Orders PRIMARY KEY,
        UserId                  UNIQUEIDENTIFIER NOT NULL,
        BookId                  UNIQUEIDENTIFIER NULL,
        -- NewBook: pay, then generate. PrintUpgrade: reprint an existing book.
        Type                    NVARCHAR(24)     NOT NULL,
        Package                 NVARCHAR(16)     NOT NULL,
        Currency                CHAR(3)          NOT NULL
            CONSTRAINT DF_Orders_Currency DEFAULT ('GEL'),
        SubtotalMinor           INT              NOT NULL,
        DiscountMinor           INT              NOT NULL
            CONSTRAINT DF_Orders_DiscountMinor DEFAULT (0),
        TotalMinor              INT              NOT NULL,
        PromoCodeId             UNIQUEIDENTIFIER NULL,
        Status                  NVARCHAR(16)     NOT NULL
            CONSTRAINT DF_Orders_Status DEFAULT (N'Pending'),
        Provider                NVARCHAR(32)     NOT NULL
            CONSTRAINT DF_Orders_Provider DEFAULT (N'Stripe'),
        ProviderSessionId       NVARCHAR(256)    NULL,
        ProviderPaymentIntentId NVARCHAR(256)    NULL,
        -- Snapshot of the create-journey draft, so fulfilment can generate the
        -- book without the client having to re-send anything after payment.
        DraftJson               NVARCHAR(MAX)    NULL,
        FailureReason           NVARCHAR(512)    NULL,
        CreatedAt               DATETIME2        NOT NULL
            CONSTRAINT DF_Orders_CreatedAt DEFAULT (SYSUTCDATETIME()),
        PaidAt                  DATETIME2        NULL,
        FulfilledAt             DATETIME2        NULL,
        CONSTRAINT FK_Orders_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (Id),
        CONSTRAINT FK_Orders_AdventurePacks FOREIGN KEY (BookId) REFERENCES dbo.AdventurePacks (Id),
        CONSTRAINT CK_Orders_Type CHECK (Type IN (N'NewBook', N'PrintUpgrade')),
        CONSTRAINT CK_Orders_Package CHECK (Package IN (N'Digital', N'Print')),
        CONSTRAINT CK_Orders_Status
            CHECK (Status IN (N'Pending', N'Paid', N'Fulfilled', N'Failed', N'Cancelled', N'Refunded')),
        CONSTRAINT CK_Orders_Totals
            CHECK (SubtotalMinor >= 0 AND DiscountMinor >= 0 AND TotalMinor >= 0
                   AND TotalMinor = SubtotalMinor - DiscountMinor)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Orders_ProviderSessionId' AND object_id = OBJECT_ID(N'dbo.Orders'))
BEGIN
    CREATE UNIQUE INDEX UX_Orders_ProviderSessionId
        ON dbo.Orders (ProviderSessionId) WHERE ProviderSessionId IS NOT NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Orders_UserId_CreatedAt' AND object_id = OBJECT_ID(N'dbo.Orders'))
BEGIN
    CREATE INDEX IX_Orders_UserId_CreatedAt ON dbo.Orders (UserId, CreatedAt DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Orders_Status' AND object_id = OBJECT_ID(N'dbo.Orders'))
BEGIN
    CREATE INDEX IX_Orders_Status ON dbo.Orders (Status) INCLUDE (BookId, Type, PaidAt);
END;
GO
