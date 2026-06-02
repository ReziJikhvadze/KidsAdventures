/*
  003_CleanupAspNetIdentityTables.sql  (MANUAL — run only after verifying migration)
  ---------------------------------------------------------------------------------
  Drops legacy ASP.NET Identity tables after you confirmed:
  - dbo.Users has all accounts
  - Login works with existing passwords
  - Children / AdventurePacks / Subscriptions reference dbo.Users

  BACK UP YOUR DATABASE BEFORE RUNNING THIS SCRIPT.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NULL
BEGIN
    PRINT 'AspNetUsers already removed. Nothing to clean up.';
    RETURN;
END
GO

IF EXISTS (
    SELECT 1
    FROM dbo.AspNetUsers AS a
    LEFT JOIN dbo.Users AS u ON u.Id = a.Id
    WHERE u.Id IS NULL
)
BEGIN
    RAISERROR('Abort: AspNetUsers contains Ids not present in dbo.Users. Run 002 first or fix data.', 16, 1);
    RETURN;
END
GO

PRINT 'Dropping ASP.NET Identity tables...';
GO

IF OBJECT_ID(N'dbo.AdventurePacks', N'U') IS NOT NULL
BEGIN
    DECLARE @PackFk SYSNAME;
    SELECT @PackFk = fk.name
    FROM sys.foreign_keys AS fk
    INNER JOIN sys.foreign_key_columns AS fkc ON fk.object_id = fkc.constraint_object_id
    INNER JOIN sys.tables AS pt ON fkc.referenced_object_id = pt.object_id
    WHERE fk.parent_object_id = OBJECT_ID(N'dbo.AdventurePacks')
      AND pt.name = N'AspNetUsers';

    IF @PackFk IS NOT NULL
        EXEC(N'ALTER TABLE dbo.AdventurePacks DROP CONSTRAINT [' + @PackFk + N'];');
END
GO

IF OBJECT_ID(N'dbo.Children', N'U') IS NOT NULL
BEGIN
    DECLARE @ChildFk SYSNAME;
    SELECT @ChildFk = fk.name
    FROM sys.foreign_keys AS fk
    INNER JOIN sys.foreign_key_columns AS fkc ON fk.object_id = fkc.constraint_object_id
    INNER JOIN sys.tables AS pt ON fkc.referenced_object_id = pt.object_id
    WHERE fk.parent_object_id = OBJECT_ID(N'dbo.Children')
      AND pt.name = N'AspNetUsers';

    IF @ChildFk IS NOT NULL
        EXEC(N'ALTER TABLE dbo.Children DROP CONSTRAINT [' + @ChildFk + N'];');
END
GO

IF OBJECT_ID(N'dbo.Subscriptions', N'U') IS NOT NULL
BEGIN
    DECLARE @SubFk SYSNAME;
    SELECT @SubFk = fk.name
    FROM sys.foreign_keys AS fk
    INNER JOIN sys.foreign_key_columns AS fkc ON fk.object_id = fkc.constraint_object_id
    INNER JOIN sys.tables AS pt ON fkc.referenced_object_id = pt.object_id
    WHERE fk.parent_object_id = OBJECT_ID(N'dbo.Subscriptions')
      AND pt.name = N'AspNetUsers';

    IF @SubFk IS NOT NULL
        EXEC(N'ALTER TABLE dbo.Subscriptions DROP CONSTRAINT [' + @SubFk + N'];');
END
GO

IF OBJECT_ID(N'dbo.AspNetUserTokens', N'U') IS NOT NULL DROP TABLE dbo.AspNetUserTokens;
IF OBJECT_ID(N'dbo.AspNetUserRoles', N'U') IS NOT NULL DROP TABLE dbo.AspNetUserRoles;
IF OBJECT_ID(N'dbo.AspNetUserLogins', N'U') IS NOT NULL DROP TABLE dbo.AspNetUserLogins;
IF OBJECT_ID(N'dbo.AspNetUserClaims', N'U') IS NOT NULL DROP TABLE dbo.AspNetUserClaims;
IF OBJECT_ID(N'dbo.AspNetRoleClaims', N'U') IS NOT NULL DROP TABLE dbo.AspNetRoleClaims;
IF OBJECT_ID(N'dbo.AspNetRoles', N'U') IS NOT NULL DROP TABLE dbo.AspNetRoles;
IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NOT NULL DROP TABLE dbo.AspNetUsers;
GO

PRINT '003_CleanupAspNetIdentityTables.sql completed.';
GO
