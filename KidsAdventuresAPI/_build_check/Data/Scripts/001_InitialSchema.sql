/*
  Target catalog (Azure): adventuresapi-database
  In SSMS / Azure Data Studio, select that database before running (F5).
*/

IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Users
    (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        Email NVARCHAR(256) NOT NULL,
        PasswordHash NVARCHAR(512) NOT NULL,
        SubscriptionType NVARCHAR(32) NOT NULL,
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_Users_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT UQ_Users_Email UNIQUE (Email)
    );
END;
GO

IF OBJECT_ID(N'dbo.Children', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Children
    (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        UserId UNIQUEIDENTIFIER NOT NULL,
        Name NVARCHAR(100) NOT NULL,
        Age INT NOT NULL,
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_Children_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_Children_Users_UserId FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE CASCADE
    );

    CREATE INDEX IX_Children_UserId_Name ON dbo.Children (UserId, Name);
END;
GO

IF OBJECT_ID(N'dbo.FamilyMembers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FamilyMembers
    (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        ChildId UNIQUEIDENTIFIER NOT NULL,
        Name NVARCHAR(100) NOT NULL,
        Relationship NVARCHAR(100) NOT NULL,
        PhotoUrl NVARCHAR(512) NULL,
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_FamilyMembers_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_FamilyMembers_Children_ChildId FOREIGN KEY (ChildId) REFERENCES dbo.Children (Id) ON DELETE CASCADE
    );

    CREATE INDEX IX_FamilyMembers_ChildId ON dbo.FamilyMembers (ChildId);
END;
GO

IF OBJECT_ID(N'dbo.Subscriptions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Subscriptions
    (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        UserId UNIQUEIDENTIFIER NOT NULL,
        StripeCustomerId NVARCHAR(100) NOT NULL,
        StripeSubscriptionId NVARCHAR(100) NOT NULL,
        PlanType NVARCHAR(32) NOT NULL,
        ActiveUntil DATETIME2 NOT NULL,
        CONSTRAINT FK_Subscriptions_Users_UserId FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE CASCADE
    );

    CREATE INDEX IX_Subscriptions_UserId ON dbo.Subscriptions (UserId);
END;
GO

IF OBJECT_ID(N'dbo.AdventurePacks', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AdventurePacks
    (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        UserId UNIQUEIDENTIFIER NOT NULL,
        ChildId UNIQUEIDENTIFIER NOT NULL,
        Theme NVARCHAR(64) NOT NULL,
        Status NVARCHAR(32) NOT NULL,
        GeneratedJson NVARCHAR(MAX) NULL,
        PdfUrl NVARCHAR(2048) NULL,
        ErrorMessage NVARCHAR(2048) NULL,
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_AdventurePacks_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_AdventurePacks_Users_UserId FOREIGN KEY (UserId) REFERENCES dbo.Users (Id),
        CONSTRAINT FK_AdventurePacks_Children_ChildId FOREIGN KEY (ChildId) REFERENCES dbo.Children (Id)
    );

    CREATE INDEX IX_AdventurePacks_UserId_CreatedAt ON dbo.AdventurePacks (UserId, CreatedAt);
    CREATE INDEX IX_AdventurePacks_ChildId ON dbo.AdventurePacks (ChildId);
END;
GO
