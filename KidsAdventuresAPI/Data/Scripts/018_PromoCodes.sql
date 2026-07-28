SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
  Promo codes and their redemptions.

  A code is either a percentage off or a full discount; the check constraint keeps
  those mutually exclusive so a total can never be computed two ways. GIFT100
  produces a zero-total order, which the checkout handles without ever reaching
  the payment provider.
*/

IF OBJECT_ID(N'dbo.PromoCodes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PromoCodes
    (
        Id              UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_PromoCodes PRIMARY KEY,
        Code            NVARCHAR(64)     NOT NULL,
        Description     NVARCHAR(256)    NULL,
        PercentOff      INT              NULL,
        IsFullDiscount  BIT              NOT NULL
            CONSTRAINT DF_PromoCodes_IsFullDiscount DEFAULT (0),
        MaxRedemptions  INT              NULL,
        RedemptionCount INT              NOT NULL
            CONSTRAINT DF_PromoCodes_RedemptionCount DEFAULT (0),
        -- When set, a code may only be used once per user account.
        OncePerUser     BIT              NOT NULL
            CONSTRAINT DF_PromoCodes_OncePerUser DEFAULT (1),
        StartsAt        DATETIME2        NULL,
        ExpiresAt       DATETIME2        NULL,
        IsActive        BIT              NOT NULL
            CONSTRAINT DF_PromoCodes_IsActive DEFAULT (1),
        CreatedAt       DATETIME2        NOT NULL
            CONSTRAINT DF_PromoCodes_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT CK_PromoCodes_Discount
            CHECK ((IsFullDiscount = 1 AND PercentOff IS NULL)
                OR (IsFullDiscount = 0 AND PercentOff BETWEEN 1 AND 100))
    );

    CREATE UNIQUE INDEX UX_PromoCodes_Code ON dbo.PromoCodes (Code);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Orders_PromoCodes')
BEGIN
    ALTER TABLE dbo.Orders
        ADD CONSTRAINT FK_Orders_PromoCodes
            FOREIGN KEY (PromoCodeId) REFERENCES dbo.PromoCodes (Id);
END;
GO

IF OBJECT_ID(N'dbo.PromoRedemptions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PromoRedemptions
    (
        Id            UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_PromoRedemptions PRIMARY KEY,
        PromoCodeId   UNIQUEIDENTIFIER NOT NULL,
        UserId        UNIQUEIDENTIFIER NOT NULL,
        OrderId       UNIQUEIDENTIFIER NOT NULL,
        DiscountMinor INT              NOT NULL,
        RedeemedAt    DATETIME2        NOT NULL
            CONSTRAINT DF_PromoRedemptions_RedeemedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_PromoRedemptions_PromoCodes
            FOREIGN KEY (PromoCodeId) REFERENCES dbo.PromoCodes (Id),
        CONSTRAINT FK_PromoRedemptions_Users
            FOREIGN KEY (UserId) REFERENCES dbo.Users (Id),
        CONSTRAINT FK_PromoRedemptions_Orders
            FOREIGN KEY (OrderId) REFERENCES dbo.Orders (Id)
    );

    -- One redemption row per order keeps webhook replays from double-counting.
    CREATE UNIQUE INDEX UX_PromoRedemptions_OrderId ON dbo.PromoRedemptions (OrderId);
    CREATE INDEX IX_PromoRedemptions_PromoCodeId_UserId
        ON dbo.PromoRedemptions (PromoCodeId, UserId);
END;
GO

MERGE dbo.PromoCodes AS target
USING (VALUES
    (N'MAGIC20',  N'20% ფასდაკლება', 20,   CAST(0 AS BIT)),
    (N'GIFT100',  N'უფასო წიგნი',    NULL, CAST(1 AS BIT))
) AS source (Code, Description, PercentOff, IsFullDiscount)
ON target.Code = source.Code
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, Code, Description, PercentOff, IsFullDiscount, IsActive)
    VALUES (NEWID(), source.Code, source.Description, source.PercentOff, source.IsFullDiscount, 1);
GO
