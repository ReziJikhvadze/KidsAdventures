/*
  002_MigrateFromAspNetIdentityToUsers.sql
  ----------------------------------------
  Use when upgrading from the old EF Core / ASP.NET Identity schema (AspNetUsers)
  to the Dapper Users table.

  Safe to re-run (idempotent).

  Prerequisites:
  - SQL Server database that previously used EF migrations, OR
  - You already ran 001 on a DB that still has AspNetUsers FKs.

  Does NOT drop AspNet tables. Run Manual/003_CleanupAspNetIdentityTables.sql after verifying.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- 1) Ensure Users table exists (same shape as 001)
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
    PRINT 'Creating dbo.Users...';

    CREATE TABLE dbo.Users
    (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        Email NVARCHAR(256) NOT NULL,
        PasswordHash NVARCHAR(512) NOT NULL,
        SubscriptionType NVARCHAR(32) NOT NULL,
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_Users_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT UQ_Users_Email UNIQUE (Email)
    );
END
ELSE
BEGIN
    PRINT 'dbo.Users already exists.';
END
GO

-- 2) Copy users from AspNetUsers (same Ids so child/pack FKs stay valid)
IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NOT NULL
BEGIN
    PRINT 'Migrating rows from dbo.AspNetUsers to dbo.Users...';

    INSERT INTO dbo.Users (Id, Email, PasswordHash, SubscriptionType, CreatedAt)
    SELECT
        u.Id,
        LOWER(LTRIM(RTRIM(u.Email))) AS Email,
        ISNULL(u.PasswordHash, N'') AS PasswordHash,
        CASE
            WHEN u.SubscriptionType IN (N'Free', N'Premium') THEN u.SubscriptionType
            ELSE N'Free'
        END AS SubscriptionType,
        ISNULL(u.CreatedAt, SYSUTCDATETIME()) AS CreatedAt
    FROM dbo.AspNetUsers AS u
    WHERE u.Email IS NOT NULL
      AND LTRIM(RTRIM(u.Email)) <> N''
      AND NOT EXISTS (SELECT 1 FROM dbo.Users AS x WHERE x.Id = u.Id);

    PRINT CONCAT('Users migrated (new rows this run): ', @@ROWCOUNT);
END
ELSE
BEGIN
    PRINT 'dbo.AspNetUsers not found — skipping user data copy.';
END
GO

-- 3) Repoint foreign keys from AspNetUsers -> Users
IF OBJECT_ID(N'dbo.Children', N'U') IS NOT NULL
BEGIN
    DECLARE @ChildrenFk SYSNAME;
    SELECT @ChildrenFk = fk.name
    FROM sys.foreign_keys AS fk
    INNER JOIN sys.foreign_key_columns AS fkc ON fk.object_id = fkc.constraint_object_id
    INNER JOIN sys.tables AS pt ON fkc.referenced_object_id = pt.object_id
    WHERE fk.parent_object_id = OBJECT_ID(N'dbo.Children')
      AND pt.name = N'AspNetUsers';

    IF @ChildrenFk IS NOT NULL
    BEGIN
        PRINT CONCAT('Dropping FK dbo.Children.', @ChildrenFk, ' (AspNetUsers)...');
        EXEC(N'ALTER TABLE dbo.Children DROP CONSTRAINT [' + @ChildrenFk + N'];');
    END

    IF NOT EXISTS (
        SELECT 1
        FROM sys.foreign_keys AS fk
        INNER JOIN sys.foreign_key_columns AS fkc ON fk.object_id = fkc.constraint_object_id
        INNER JOIN sys.tables AS pt ON fkc.referenced_object_id = pt.object_id
        WHERE fk.parent_object_id = OBJECT_ID(N'dbo.Children')
          AND pt.name = N'Users'
    )
    BEGIN
        PRINT 'Adding FK dbo.Children -> dbo.Users...';
        ALTER TABLE dbo.Children
            ADD CONSTRAINT FK_Children_Users_UserId
            FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE CASCADE;
    END
END
GO

IF OBJECT_ID(N'dbo.Subscriptions', N'U') IS NOT NULL
BEGIN
    DECLARE @SubscriptionsFk SYSNAME;
    SELECT @SubscriptionsFk = fk.name
    FROM sys.foreign_keys AS fk
    INNER JOIN sys.foreign_key_columns AS fkc ON fk.object_id = fkc.constraint_object_id
    INNER JOIN sys.tables AS pt ON fkc.referenced_object_id = pt.object_id
    WHERE fk.parent_object_id = OBJECT_ID(N'dbo.Subscriptions')
      AND pt.name = N'AspNetUsers';

    IF @SubscriptionsFk IS NOT NULL
    BEGIN
        PRINT CONCAT('Dropping FK dbo.Subscriptions.', @SubscriptionsFk, ' (AspNetUsers)...');
        EXEC(N'ALTER TABLE dbo.Subscriptions DROP CONSTRAINT [' + @SubscriptionsFk + N'];');
    END

    IF NOT EXISTS (
        SELECT 1
        FROM sys.foreign_keys AS fk
        INNER JOIN sys.foreign_key_columns AS fkc ON fk.object_id = fkc.constraint_object_id
        INNER JOIN sys.tables AS pt ON fkc.referenced_object_id = pt.object_id
        WHERE fk.parent_object_id = OBJECT_ID(N'dbo.Subscriptions')
          AND pt.name = N'Users'
    )
    BEGIN
        PRINT 'Adding FK dbo.Subscriptions -> dbo.Users...';
        ALTER TABLE dbo.Subscriptions
            ADD CONSTRAINT FK_Subscriptions_Users_UserId
            FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE CASCADE;
    END
END
GO

IF OBJECT_ID(N'dbo.AdventurePacks', N'U') IS NOT NULL
BEGIN
    DECLARE @PacksFk SYSNAME;
    SELECT @PacksFk = fk.name
    FROM sys.foreign_keys AS fk
    INNER JOIN sys.foreign_key_columns AS fkc ON fk.object_id = fkc.constraint_object_id
    INNER JOIN sys.tables AS pt ON fkc.referenced_object_id = pt.object_id
    WHERE fk.parent_object_id = OBJECT_ID(N'dbo.AdventurePacks')
      AND pt.name = N'AspNetUsers';

    IF @PacksFk IS NOT NULL
    BEGIN
        PRINT CONCAT('Dropping FK dbo.AdventurePacks.', @PacksFk, ' (AspNetUsers)...');
        EXEC(N'ALTER TABLE dbo.AdventurePacks DROP CONSTRAINT [' + @PacksFk + N'];');
    END

    IF NOT EXISTS (
        SELECT 1
        FROM sys.foreign_keys AS fk
        INNER JOIN sys.foreign_key_columns AS fkc ON fk.object_id = fkc.constraint_object_id
        INNER JOIN sys.tables AS pt ON fkc.referenced_object_id = pt.object_id
        WHERE fk.parent_object_id = OBJECT_ID(N'dbo.AdventurePacks')
          AND pt.name = N'Users'
    )
    BEGIN
        PRINT 'Adding FK dbo.AdventurePacks -> dbo.Users...';
        ALTER TABLE dbo.AdventurePacks
            ADD CONSTRAINT FK_AdventurePacks_Users_UserId
            FOREIGN KEY (UserId) REFERENCES dbo.Users (Id);
    END
END
GO

-- 4) Orphan check (UserIds in app tables with no matching Users row)
IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.Children', N'U') IS NOT NULL
    BEGIN
        IF EXISTS (
            SELECT 1
            FROM dbo.Children AS c
            LEFT JOIN dbo.Users AS u ON u.Id = c.UserId
            WHERE u.Id IS NULL
        )
        BEGIN
            PRINT 'WARNING: Children rows exist without a matching Users row. Inspect with Manual/004_VerifyMigration.sql';
        END
    END
END
GO

PRINT '002_MigrateFromAspNetIdentityToUsers.sql completed.';
GO
