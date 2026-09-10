SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
  How this parcel is being sent, on the row the courier is booked from.

  Delivery stopped being one flat promise: Tbilisi now chooses between three working days for
  7 GEL and five for nothing, and everywhere else is 8 GEL in five to seven. The choice is part
  of the address, so the order already keeps it inside ShippingJson with the rest of what the
  parent typed - but the print queue is a table, not that blob, and the shipping email, the
  parent's own tracking card and the admin console all read the window off this row.

  Without the column those three would answer from the city alone, which is the address's
  default: a parent who paid the seven lari for three days would be told five in the email that
  confirms the order. Under-promising is the safe direction to be wrong in generally, and the
  wrong direction to be wrong in when the difference is the thing they paid for.

  Nullable, and read as "whatever that address gets by default" when it is absent, so every
  order placed before today keeps answering exactly as it does now.
*/

IF COL_LENGTH(N'dbo.PrintOrders', N'DeliveryOption') IS NULL
BEGIN
    ALTER TABLE dbo.PrintOrders
        ADD DeliveryOption NVARCHAR(32) NULL;
END;
GO
