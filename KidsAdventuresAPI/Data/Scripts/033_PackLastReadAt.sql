/*
    When a book was last opened in the reader.

    The shelf ranks a book's three actions by what is left to want: before the story has been
    read, reading it is the point; afterwards the free things are spent and the printed copy is
    the only thing the card can still offer. That signal lived in the browser's localStorage, so
    it was per-device — the same book counted as unread on a parent's phone after they had read
    it on a laptop.
*/
IF COL_LENGTH('dbo.AdventurePacks', 'LastReadAt') IS NULL
BEGIN
    ALTER TABLE dbo.AdventurePacks ADD LastReadAt DATETIME2(3) NULL;
END;
GO
