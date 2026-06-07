IF COL_LENGTH(N'dbo.Users', N'EmailConfirmed') IS NULL
BEGIN
    ALTER TABLE dbo.Users
        ADD EmailConfirmed BIT NOT NULL
            CONSTRAINT DF_Users_EmailConfirmed DEFAULT (0);
END;
GO

IF COL_LENGTH(N'dbo.Users', N'EmailConfirmationToken') IS NULL
BEGIN
    ALTER TABLE dbo.Users
        ADD EmailConfirmationToken NVARCHAR(128) NULL;
END;
GO

IF COL_LENGTH(N'dbo.Users', N'EmailConfirmationExpiresAt') IS NULL
BEGIN
    ALTER TABLE dbo.Users
        ADD EmailConfirmationExpiresAt DATETIME2 NULL;
END;
GO

UPDATE dbo.Users
SET EmailConfirmed = 1
WHERE Email IN (N'demo@adventurepacks.com', N'premium@adventurepacks.com')
  AND EmailConfirmed = 0;
GO
