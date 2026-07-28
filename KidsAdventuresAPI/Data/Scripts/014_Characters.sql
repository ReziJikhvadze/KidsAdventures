SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
  Generalises Children and FamilyMembers into a single Characters table.

  A book now stars up to three characters, any of which may be a child, an adult,
  an animal or a fantasy figure, so the old "one child plus up to six family
  members" split no longer holds. Children and FamilyMembers are left in place
  and backfilled from, because AdventurePacks still carries a ChildId foreign key;
  Characters.LegacyChildId / LegacyFamilyMemberId keep the two models joined until
  the legacy tables are dropped.
*/

IF OBJECT_ID(N'dbo.Characters', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Characters
    (
        Id                    UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_Characters PRIMARY KEY,
        UserId                UNIQUEIDENTIFIER NOT NULL,
        Name                  NVARCHAR(100)    NOT NULL,
        BirthDate             DATE             NULL,
        Gender                NVARCHAR(16)     NULL,
        EyeColor              NVARCHAR(24)     NULL,
        CharacterType         NVARCHAR(16)     NOT NULL
            CONSTRAINT DF_Characters_CharacterType DEFAULT (N'child'),
        Relationship          NVARCHAR(100)    NULL,
        IsPrimary             BIT              NOT NULL
            CONSTRAINT DF_Characters_IsPrimary DEFAULT (0),
        PhotoUrl              NVARCHAR(512)    NULL,
        -- Cached appearance prompt, so a re-generated illustration keeps the same face.
        AppearanceDescription NVARCHAR(MAX)    NULL,
        AppearancePhotoUrl    NVARCHAR(512)    NULL,
        LegacyChildId         UNIQUEIDENTIFIER NULL,
        LegacyFamilyMemberId  UNIQUEIDENTIFIER NULL,
        CreatedAt             DATETIME2        NOT NULL
            CONSTRAINT DF_Characters_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt             DATETIME2        NOT NULL
            CONSTRAINT DF_Characters_UpdatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_Characters_Users_UserId
            FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE CASCADE,
        CONSTRAINT CK_Characters_CharacterType
            CHECK (CharacterType IN (N'child', N'adult', N'animal', N'fantasy')),
        CONSTRAINT CK_Characters_Gender
            CHECK (Gender IS NULL OR Gender IN (N'girl', N'boy'))
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Characters_UserId_IsPrimary' AND object_id = OBJECT_ID(N'dbo.Characters'))
BEGIN
    CREATE INDEX IX_Characters_UserId_IsPrimary ON dbo.Characters (UserId, IsPrimary) INCLUDE (Name);
END;
GO

-- Filtered uniques make the backfill below safe to re-run on every startup.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Characters_LegacyChildId' AND object_id = OBJECT_ID(N'dbo.Characters'))
BEGIN
    CREATE UNIQUE INDEX UX_Characters_LegacyChildId
        ON dbo.Characters (LegacyChildId) WHERE LegacyChildId IS NOT NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Characters_LegacyFamilyMemberId' AND object_id = OBJECT_ID(N'dbo.Characters'))
BEGIN
    CREATE UNIQUE INDEX UX_Characters_LegacyFamilyMemberId
        ON dbo.Characters (LegacyFamilyMemberId) WHERE LegacyFamilyMemberId IS NOT NULL;
END;
GO

/*
  Backfill the primary character for each existing child. Children only stored an
  integer age, so the birth date is reconstructed as "age years ago today"; that
  keeps the age band the story generator reads while giving the new UI a date to
  edit.
*/
INSERT INTO dbo.Characters
    (Id, UserId, Name, BirthDate, CharacterType, IsPrimary, PhotoUrl,
     AppearanceDescription, AppearancePhotoUrl, LegacyChildId, CreatedAt, UpdatedAt)
SELECT
    NEWID(),
    ch.UserId,
    ch.Name,
    DATEADD(YEAR, -ch.Age, CAST(SYSUTCDATETIME() AS DATE)),
    N'child',
    1,
    ch.PhotoUrl,
    ch.AppearanceDescription,
    ch.AppearancePhotoUrl,
    ch.Id,
    ch.CreatedAt,
    SYSUTCDATETIME()
FROM dbo.Children AS ch
WHERE NOT EXISTS (SELECT 1 FROM dbo.Characters AS c WHERE c.LegacyChildId = ch.Id);
GO

/*
  Backfill supporting characters. Legacy relationships were free text in English,
  so type is inferred from the common values and falls back to 'adult', which is
  the safest default for a relative.
*/
INSERT INTO dbo.Characters
    (Id, UserId, Name, CharacterType, Relationship, IsPrimary, PhotoUrl,
     LegacyFamilyMemberId, CreatedAt, UpdatedAt)
SELECT
    NEWID(),
    ch.UserId,
    fm.Name,
    CASE
        WHEN LOWER(fm.Relationship) IN (N'dog', N'cat', N'pet', N'puppy', N'kitten', N'hamster', N'rabbit') THEN N'animal'
        WHEN LOWER(fm.Relationship) IN (N'sister', N'brother', N'cousin', N'friend', N'twin') THEN N'child'
        ELSE N'adult'
    END,
    fm.Relationship,
    0,
    fm.PhotoUrl,
    fm.Id,
    fm.CreatedAt,
    SYSUTCDATETIME()
FROM dbo.FamilyMembers AS fm
INNER JOIN dbo.Children AS ch ON ch.Id = fm.ChildId
WHERE NOT EXISTS (SELECT 1 FROM dbo.Characters AS c WHERE c.LegacyFamilyMemberId = fm.Id);
GO
