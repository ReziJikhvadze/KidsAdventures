IF OBJECT_ID(N'dbo.GuestPreviews', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.GuestPreviews
    (
        Id               UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_GuestPreviews PRIMARY KEY,
        StoryId          UNIQUEIDENTIFIER NOT NULL,
        PreviewUsed      BIT              NOT NULL CONSTRAINT DF_GuestPreviews_PreviewUsed DEFAULT (1),
        Redeemed         BIT              NOT NULL CONSTRAINT DF_GuestPreviews_Redeemed DEFAULT (0),
        RedeemedByUserId UNIQUEIDENTIFIER NULL,
        ClientKey        NVARCHAR(128)    NULL,
        ChildName        NVARCHAR(128)    NULL,
        Theme            NVARCHAR(64)     NULL,
        CreatedAt        DATETIME2        NOT NULL CONSTRAINT DF_GuestPreviews_CreatedAt DEFAULT (SYSUTCDATETIME()),
        RedeemedAt       DATETIME2        NULL
    );
END;
GO

-- Look up a preview by the story it produced (fallback when only the storyId travels with the client).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_GuestPreviews_StoryId' AND object_id = OBJECT_ID(N'dbo.GuestPreviews'))
BEGIN
    CREATE INDEX IX_GuestPreviews_StoryId ON dbo.GuestPreviews (StoryId);
END;
GO
