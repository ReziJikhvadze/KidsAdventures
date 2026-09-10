SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
  The numbers a parent has already proved they can reach.

  A printed book is posted to a phone number as much as to a street: the courier calls before
  they climb the stairs, and a digit typed wrong is a parcel that comes back. So checkout now
  sends four digits to the recipient's handset and will not take payment until they come back.

  Asking that of the same number twice would be the wrong lesson to draw from it. A parent who
  orders a second book for the same child, to the same grandmother, has already proved the
  number - and being asked again reads as the shop having forgotten them. One row per parent per
  number, written the first time it is proved, and read before the panel is ever shown.

  Keyed by the parent as well as the number on purpose. A number one account proved says nothing
  about another account's right to post to it, and the pair is what the checkout gate asks for.
  Phone numbers are stored in the same normalised form the auth challenges use (+995 and nine
  digits), so a number typed with spaces one day and without them the next is one row.
*/

IF OBJECT_ID(N'dbo.VerifiedRecipientPhones', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.VerifiedRecipientPhones
    (
        UserId      UNIQUEIDENTIFIER NOT NULL,
        PhoneNumber NVARCHAR(32)     NOT NULL,
        VerifiedAt  DATETIME2(3)     NOT NULL CONSTRAINT DF_VerifiedRecipientPhones_VerifiedAt DEFAULT SYSUTCDATETIME(),

        CONSTRAINT PK_VerifiedRecipientPhones PRIMARY KEY CLUSTERED (UserId, PhoneNumber),
        CONSTRAINT FK_VerifiedRecipientPhones_Users FOREIGN KEY (UserId)
            REFERENCES dbo.Users (Id) ON DELETE CASCADE
    );
END;
GO

/*
  And the purpose that carries those four digits.

  A challenge row says what its secret is for, and the column is guarded by a CHECK naming the
  two purposes that existed when it was written - a magic link and a sign-in code. Checkout's
  code is a third: it proves a handset a parcel is going to and entitles nobody to an account,
  which is exactly why it may not share a purpose with the sign-in code. Sharing one would let a
  code issued at checkout be posted to the sign-in endpoint and taken as the account holder's.

  So the constraint learns a third name rather than being dropped. It is doing real work: the
  purposes are written as text by the repository, and a typo in an enum name would otherwise
  become a challenge nobody can ever find again.
*/

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_AuthChallenges_Purpose')
BEGIN
    ALTER TABLE dbo.AuthChallenges DROP CONSTRAINT CK_AuthChallenges_Purpose;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_AuthChallenges_Purpose')
BEGIN
    ALTER TABLE dbo.AuthChallenges
        ADD CONSTRAINT CK_AuthChallenges_Purpose
            CHECK (Purpose IN (N'MagicLink', N'PhoneOtp', N'RecipientPhone'));
END;
GO
