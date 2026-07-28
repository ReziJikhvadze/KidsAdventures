SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
  Phone numbers and passwordless sign-in.

  Sign-in is now an email magic link or a Georgian phone number plus a six-digit
  code, so PasswordHash becomes nullable: accounts created through either of
  those paths never have one. Existing password accounts keep working unchanged.
*/

IF COL_LENGTH(N'dbo.Users', N'PhoneNumber') IS NULL
BEGIN
    ALTER TABLE dbo.Users ADD PhoneNumber NVARCHAR(32) NULL;
END;
GO

IF COL_LENGTH(N'dbo.Users', N'PhoneConfirmed') IS NULL
BEGIN
    ALTER TABLE dbo.Users
        ADD PhoneConfirmed BIT NOT NULL
            CONSTRAINT DF_Users_PhoneConfirmed DEFAULT (0);
END;
GO

IF COL_LENGTH(N'dbo.Users', N'PreferredLanguage') IS NULL
BEGIN
    ALTER TABLE dbo.Users
        ADD PreferredLanguage NVARCHAR(16) NOT NULL
            CONSTRAINT DF_Users_PreferredLanguage DEFAULT (N'ka');
END;
GO

IF COL_LENGTH(N'dbo.Users', N'DisplayName') IS NULL
BEGIN
    ALTER TABLE dbo.Users ADD DisplayName NVARCHAR(128) NULL;
END;
GO

IF COL_LENGTH(N'dbo.Users', N'IsAdmin') IS NULL
BEGIN
    ALTER TABLE dbo.Users
        ADD IsAdmin BIT NOT NULL
            CONSTRAINT DF_Users_IsAdmin DEFAULT (0);
END;
GO

IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.Users')
      AND name = N'PasswordHash'
      AND is_nullable = 0)
BEGIN
    ALTER TABLE dbo.Users ALTER COLUMN PasswordHash NVARCHAR(512) NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Users_PhoneNumber' AND object_id = OBJECT_ID(N'dbo.Users'))
BEGIN
    CREATE UNIQUE INDEX UX_Users_PhoneNumber
        ON dbo.Users (PhoneNumber) WHERE PhoneNumber IS NOT NULL;
END;
GO

/*
  Pending magic links and OTP codes.

  Secrets are stored hashed, never in the clear, so a database leak cannot be
  replayed into account access. AttemptCount guards a code against brute force;
  the row is deleted or marked consumed once used.
*/
IF OBJECT_ID(N'dbo.AuthChallenges', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuthChallenges
    (
        Id            UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_AuthChallenges PRIMARY KEY,
        Purpose       NVARCHAR(24)     NOT NULL,
        -- Email address or E.164 phone number the challenge was sent to.
        Destination   NVARCHAR(256)    NOT NULL,
        SecretHash    NVARCHAR(256)    NOT NULL,
        UserId        UNIQUEIDENTIFIER NULL,
        AttemptCount  INT              NOT NULL
            CONSTRAINT DF_AuthChallenges_AttemptCount DEFAULT (0),
        MaxAttempts   INT              NOT NULL
            CONSTRAINT DF_AuthChallenges_MaxAttempts DEFAULT (5),
        ExpiresAt     DATETIME2        NOT NULL,
        ConsumedAt    DATETIME2        NULL,
        IpAddress     NVARCHAR(64)     NULL,
        CreatedAt     DATETIME2        NOT NULL
            CONSTRAINT DF_AuthChallenges_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_AuthChallenges_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (Id),
        CONSTRAINT CK_AuthChallenges_Purpose
            CHECK (Purpose IN (N'MagicLink', N'PhoneOtp'))
    );

    -- Newest unconsumed challenge per destination is the hot lookup.
    CREATE INDEX IX_AuthChallenges_Destination
        ON dbo.AuthChallenges (Purpose, Destination, CreatedAt DESC);
    CREATE INDEX IX_AuthChallenges_ExpiresAt ON dbo.AuthChallenges (ExpiresAt);
    CREATE UNIQUE INDEX UX_AuthChallenges_SecretHash ON dbo.AuthChallenges (SecretHash);
END;
GO

/* Existing accounts signed in with a password, so their email is already proven. */
UPDATE dbo.Users
SET DisplayName = LEFT(Email, CHARINDEX('@', Email + '@') - 1)
WHERE DisplayName IS NULL;
GO
