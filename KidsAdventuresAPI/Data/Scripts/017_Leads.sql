IF OBJECT_ID(N'dbo.Leads', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Leads (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Leads PRIMARY KEY,
        Email NVARCHAR(256) NOT NULL,
        Source NVARCHAR(64) NULL,
        ChildName NVARCHAR(128) NULL,
        Theme NVARCHAR(64) NULL,
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_Leads_CreatedAt DEFAULT SYSUTCDATETIME(),
        EmailedAt DATETIME2 NULL
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Leads_Email' AND object_id = OBJECT_ID(N'dbo.Leads'))
BEGIN
    CREATE UNIQUE INDEX UX_Leads_Email ON dbo.Leads(Email);
END;
GO
