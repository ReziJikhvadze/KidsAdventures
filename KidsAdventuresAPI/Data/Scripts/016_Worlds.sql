SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
  The six worlds of the adventure map, and each character's progress through them.

  Progress is tracked per character rather than per user, because one parent
  account can hold several children and each child has their own map.
*/

IF OBJECT_ID(N'dbo.Worlds', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Worlds
    (
        Id        NVARCHAR(32)  NOT NULL CONSTRAINT PK_Worlds PRIMARY KEY,
        Name      NVARCHAR(128) NOT NULL,
        SortOrder INT           NOT NULL,
        IsActive  BIT           NOT NULL CONSTRAINT DF_Worlds_IsActive DEFAULT (1)
    );
END;
GO

MERGE dbo.Worlds AS target
USING (VALUES
    (N'dinosaurs',  N'დინოზავრები',      1),
    (N'space',      N'კოსმოსი',          2),
    (N'pirates',    N'მეკობრეები',       3),
    (N'animals',    N'ცხოველები',        4),
    (N'airplanes',  N'თვითმფრინავები',   5),
    (N'magic',      N'მაგიური სამყარო',  6)
) AS source (Id, Name, SortOrder)
ON target.Id = source.Id
WHEN MATCHED AND (target.Name <> source.Name OR target.SortOrder <> source.SortOrder)
    THEN UPDATE SET Name = source.Name, SortOrder = source.SortOrder
WHEN NOT MATCHED BY TARGET
    THEN INSERT (Id, Name, SortOrder, IsActive) VALUES (source.Id, source.Name, source.SortOrder, 1);
GO

IF OBJECT_ID(N'dbo.UserWorldProgress', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserWorldProgress
    (
        Id          UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_UserWorldProgress PRIMARY KEY,
        UserId      UNIQUEIDENTIFIER NOT NULL,
        CharacterId UNIQUEIDENTIFIER NOT NULL,
        WorldId     NVARCHAR(32)     NOT NULL,
        -- Locked: not reachable yet. Unlocked: reachable, no finished book.
        -- Completed: a paid book set here. Next is derived, never stored.
        State       NVARCHAR(16)     NOT NULL
            CONSTRAINT DF_UserWorldProgress_State DEFAULT (N'Locked'),
        BookId      UNIQUEIDENTIFIER NULL,
        UnlockedAt  DATETIME2        NULL,
        CompletedAt DATETIME2        NULL,
        CreatedAt   DATETIME2        NOT NULL
            CONSTRAINT DF_UserWorldProgress_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_UserWorldProgress_Users
            FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE CASCADE,
        CONSTRAINT FK_UserWorldProgress_Characters
            FOREIGN KEY (CharacterId) REFERENCES dbo.Characters (Id),
        CONSTRAINT FK_UserWorldProgress_Worlds
            FOREIGN KEY (WorldId) REFERENCES dbo.Worlds (Id),
        CONSTRAINT FK_UserWorldProgress_AdventurePacks
            FOREIGN KEY (BookId) REFERENCES dbo.AdventurePacks (Id),
        CONSTRAINT CK_UserWorldProgress_State
            CHECK (State IN (N'Locked', N'Unlocked', N'Completed'))
    );

    CREATE UNIQUE INDEX UX_UserWorldProgress_Character_World
        ON dbo.UserWorldProgress (CharacterId, WorldId);
END;
GO

/* Mark worlds that existing completed books already opened. */
INSERT INTO dbo.UserWorldProgress
    (Id, UserId, CharacterId, WorldId, State, BookId, UnlockedAt, CompletedAt)
SELECT
    NEWID(),
    p.UserId,
    p.PrimaryCharacterId,
    p.WorldId,
    N'Completed',
    MIN(p.Id),
    MIN(p.CreatedAt),
    MIN(p.CreatedAt)
FROM dbo.AdventurePacks AS p
INNER JOIN dbo.Worlds AS w ON w.Id = p.WorldId
WHERE p.PrimaryCharacterId IS NOT NULL
  AND p.Status = N'Completed'
  AND NOT EXISTS (
      SELECT 1 FROM dbo.UserWorldProgress AS up
      WHERE up.CharacterId = p.PrimaryCharacterId AND up.WorldId = p.WorldId)
GROUP BY p.UserId, p.PrimaryCharacterId, p.WorldId;
GO
