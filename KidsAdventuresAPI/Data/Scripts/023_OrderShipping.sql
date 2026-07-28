SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
  The delivery address a print order was placed with, frozen at checkout.

  Kept separate from DraftJson because the two answer different questions and have
  different lifetimes: DraftJson says what story to write and is read once, at
  generation; ShippingJson says where the parcel goes and must still be readable
  when operations picks the order up days later. A print upgrade has no story
  draft at all, so overloading one column would have meant two shapes in one field.
*/

IF COL_LENGTH(N'dbo.Orders', N'ShippingJson') IS NULL
BEGIN
    ALTER TABLE dbo.Orders ADD ShippingJson NVARCHAR(MAX) NULL;
END;
GO
