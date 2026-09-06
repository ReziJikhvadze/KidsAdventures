SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
  How many printed copies of the book this order is for.

  One parcel, this many books in it — a family ordering a copy for each set of grandparents was
  otherwise three separate journeys through the same form. Only a printed order can have more
  than one: a digital book is a file, and the column stays 1 for it and for a print upgrade.

  NOT NULL with a default of 1, because every order taken before today was for one copy and
  "unknown" is not a possible answer to how many books were bought.
*/

IF COL_LENGTH(N'dbo.Orders', N'Quantity') IS NULL
BEGIN
    ALTER TABLE dbo.Orders
        ADD Quantity INT NOT NULL CONSTRAINT DF_Orders_Quantity DEFAULT (1);
END;
GO
