/*
  004_VerifyMigration.sql
  -----------------------
  Read-only checks after migration. Review result sets in SSMS / Azure Data Studio.
*/

SET NOCOUNT ON;
GO

PRINT '=== Table existence ===';
SELECT
    t.name AS TableName,
    CASE WHEN t.name IS NOT NULL THEN 'YES' ELSE 'NO' END AS ExistsFlag
FROM (VALUES
    (N'Users'),
    (N'AspNetUsers'),
    (N'Children'),
    (N'FamilyMembers'),
    (N'AdventurePacks'),
    (N'Subscriptions')
) AS expected(name)
LEFT JOIN sys.tables AS t ON t.name = expected.name AND t.schema_id = SCHEMA_ID(N'dbo');
GO

PRINT '=== Row counts ===';
IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
    SELECT N'Users' AS TableName, COUNT(*) AS RowCount FROM dbo.Users;

IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NOT NULL
    SELECT N'AspNetUsers' AS TableName, COUNT(*) AS RowCount FROM dbo.AspNetUsers;

IF OBJECT_ID(N'dbo.Children', N'U') IS NOT NULL
    SELECT N'Children' AS TableName, COUNT(*) AS RowCount FROM dbo.Children;

IF OBJECT_ID(N'dbo.FamilyMembers', N'U') IS NOT NULL
    SELECT N'FamilyMembers' AS TableName, COUNT(*) AS RowCount FROM dbo.FamilyMembers;

IF OBJECT_ID(N'dbo.AdventurePacks', N'U') IS NOT NULL
    SELECT N'AdventurePacks' AS TableName, COUNT(*) AS RowCount FROM dbo.AdventurePacks;

IF OBJECT_ID(N'dbo.Subscriptions', N'U') IS NOT NULL
    SELECT N'Subscriptions' AS TableName, COUNT(*) AS RowCount FROM dbo.Subscriptions;
GO

PRINT '=== AspNetUsers not yet in Users ===';
IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
BEGIN
    SELECT a.Id, a.Email, a.SubscriptionType, a.CreatedAt
    FROM dbo.AspNetUsers AS a
    LEFT JOIN dbo.Users AS u ON u.Id = a.Id
    WHERE u.Id IS NULL;
END
ELSE
    SELECT N'(skip — table missing)' AS Note;
GO

PRINT '=== Orphan UserId in Children ===';
IF OBJECT_ID(N'dbo.Children', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
BEGIN
    SELECT c.Id, c.UserId, c.Name
    FROM dbo.Children AS c
    LEFT JOIN dbo.Users AS u ON u.Id = c.UserId
    WHERE u.Id IS NULL;
END
ELSE
    SELECT N'(skip — table missing)' AS Note;
GO

PRINT '=== Foreign keys still pointing to AspNetUsers ===';
SELECT
    OBJECT_NAME(fk.parent_object_id) AS ChildTable,
    fk.name AS ForeignKeyName,
    rt.name AS ReferencedTable
FROM sys.foreign_keys AS fk
INNER JOIN sys.tables AS rt ON fk.referenced_object_id = rt.object_id
WHERE rt.name = N'AspNetUsers';
GO

PRINT '=== Foreign keys pointing to Users ===';
SELECT
    OBJECT_NAME(fk.parent_object_id) AS ChildTable,
    fk.name AS ForeignKeyName,
    rt.name AS ReferencedTable
FROM sys.foreign_keys AS fk
INNER JOIN sys.tables AS rt ON fk.referenced_object_id = rt.object_id
WHERE rt.name = N'Users';
GO

PRINT 'Verification script finished.';
GO
