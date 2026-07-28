SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
  Email becomes optional.

  A parent who signs in with a Georgian mobile number and a six-digit code never
  gives us an email address, so Email can no longer be the mandatory identity
  column. The UNIQUE constraint has to go with it: SQL Server's UNIQUE permits
  exactly one NULL, which would let the first phone-only account block every
  one after it. A filtered unique index keeps real addresses unique while
  ignoring the NULLs entirely.

  A table-level CHECK guarantees the account is still reachable by something.
*/

IF EXISTS (
    SELECT 1 FROM sys.key_constraints
    WHERE name = N'UQ_Users_Email' AND parent_object_id = OBJECT_ID(N'dbo.Users'))
BEGIN
    ALTER TABLE dbo.Users DROP CONSTRAINT UQ_Users_Email;
END;
GO

IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.Users')
      AND name = N'Email'
      AND is_nullable = 0)
BEGIN
    ALTER TABLE dbo.Users ALTER COLUMN Email NVARCHAR(256) NULL;
END;
GO

/* Empty strings would defeat the filtered index, so fold them into NULL first. */
UPDATE dbo.Users SET Email = NULL WHERE Email IS NOT NULL AND LTRIM(RTRIM(Email)) = N'';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Users_Email' AND object_id = OBJECT_ID(N'dbo.Users'))
BEGIN
    CREATE UNIQUE INDEX UX_Users_Email
        ON dbo.Users (Email) WHERE Email IS NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_Users_HasContact' AND parent_object_id = OBJECT_ID(N'dbo.Users'))
BEGIN
    ALTER TABLE dbo.Users WITH NOCHECK
        ADD CONSTRAINT CK_Users_HasContact
            CHECK (Email IS NOT NULL OR PhoneNumber IS NOT NULL);
END;
GO
