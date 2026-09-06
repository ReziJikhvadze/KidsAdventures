SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
  Whether a printed order is to be wrapped as a gift, +5 GEL.

  On the order rather than on the print order, because it is priced: the five lari is part of
  SubtotalMinor and the row has to be able to say why. The print queue reads it across the join
  it already makes, so the person packing the parcel sees it beside the address.

  NOT NULL with a default of 0: every order taken before today was not wrapped, and a nullable
  flag would make "no" and "nobody asked" two different answers to a question with one.
*/

IF COL_LENGTH(N'dbo.Orders', N'GiftWrap') IS NULL
BEGIN
    ALTER TABLE dbo.Orders
        ADD GiftWrap BIT NOT NULL CONSTRAINT DF_Orders_GiftWrap DEFAULT (0);
END;
GO
