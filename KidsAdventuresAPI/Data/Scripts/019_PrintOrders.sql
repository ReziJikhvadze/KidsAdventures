SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
  Print fulfilment: the shipping address captured at checkout, plus the physical
  order that operations works through.

  Addresses are stored twice on purpose. UserAddresses is the reusable book the
  customer picks from ("use saved address"); PrintOrders keeps its own copy so a
  later edit to the saved address cannot silently rewrite where an already
  dispatched parcel was sent.
*/

IF OBJECT_ID(N'dbo.UserAddresses', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserAddresses
    (
        Id             UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_UserAddresses PRIMARY KEY,
        UserId         UNIQUEIDENTIFIER NOT NULL,
        RecipientName  NVARCHAR(128)    NOT NULL,
        RecipientPhone NVARCHAR(32)     NOT NULL,
        City           NVARCHAR(128)    NOT NULL,
        Region         NVARCHAR(128)    NULL,
        AddressLine1   NVARCHAR(256)    NOT NULL,
        AddressLine2   NVARCHAR(256)    NULL,
        PostalCode     NVARCHAR(32)     NULL,
        IsDefault      BIT              NOT NULL
            CONSTRAINT DF_UserAddresses_IsDefault DEFAULT (0),
        CreatedAt      DATETIME2        NOT NULL
            CONSTRAINT DF_UserAddresses_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_UserAddresses_Users
            FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE CASCADE
    );

    CREATE INDEX IX_UserAddresses_UserId ON dbo.UserAddresses (UserId, IsDefault DESC, CreatedAt DESC);
END;
GO

IF OBJECT_ID(N'dbo.PrintOrders', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PrintOrders
    (
        Id             UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_PrintOrders PRIMARY KEY,
        OrderId        UNIQUEIDENTIFIER NOT NULL,
        BookId         UNIQUEIDENTIFIER NOT NULL,
        UserId         UNIQUEIDENTIFIER NOT NULL,
        RecipientName  NVARCHAR(128)    NOT NULL,
        RecipientPhone NVARCHAR(32)     NOT NULL,
        City           NVARCHAR(128)    NOT NULL,
        Region         NVARCHAR(128)    NULL,
        AddressLine1   NVARCHAR(256)    NOT NULL,
        AddressLine2   NVARCHAR(256)    NULL,
        PostalCode     NVARCHAR(32)     NULL,
        Notes          NVARCHAR(512)    NULL,
        Status         NVARCHAR(24)     NOT NULL
            CONSTRAINT DF_PrintOrders_Status DEFAULT (N'AwaitingPrint'),
        TrackingCode   NVARCHAR(128)    NULL,
        CreatedAt      DATETIME2        NOT NULL
            CONSTRAINT DF_PrintOrders_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt      DATETIME2        NOT NULL
            CONSTRAINT DF_PrintOrders_UpdatedAt DEFAULT (SYSUTCDATETIME()),
        ShippedAt      DATETIME2        NULL,
        DeliveredAt    DATETIME2        NULL,
        CONSTRAINT FK_PrintOrders_Orders FOREIGN KEY (OrderId) REFERENCES dbo.Orders (Id),
        CONSTRAINT FK_PrintOrders_AdventurePacks FOREIGN KEY (BookId) REFERENCES dbo.AdventurePacks (Id),
        CONSTRAINT FK_PrintOrders_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (Id),
        CONSTRAINT CK_PrintOrders_Status
            CHECK (Status IN (N'AwaitingPrint', N'Printing', N'Shipped', N'Delivered', N'Cancelled'))
    );

    -- One print run per paid order; a webhook replay must not queue a second parcel.
    CREATE UNIQUE INDEX UX_PrintOrders_OrderId ON dbo.PrintOrders (OrderId);
    CREATE INDEX IX_PrintOrders_Status_CreatedAt ON dbo.PrintOrders (Status, CreatedAt);
    CREATE INDEX IX_PrintOrders_UserId ON dbo.PrintOrders (UserId, CreatedAt DESC);
END;
GO
