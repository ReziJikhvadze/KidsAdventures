-- Where the printable copy of a book is stored.
--
-- A book is rendered twice: the reading copy a parent downloads, and a print copy carrying the
-- blank leaves saddle-stitch needs. Both go to blob storage, but only the reading copy had a
-- column, so the admin print view derived the other one by rewriting ".pdf" into "-print.pdf".
--
-- That rewrite is a guess, and for every book made before the split it is a wrong one: those
-- have no print file, and the binder was being sent a link to a blob that does not exist.
-- Recording the url that was actually uploaded removes the guess. NULL means "no print copy",
-- which is exactly what the older books should say, and the reading copy is used instead.
IF COL_LENGTH(N'dbo.AdventurePacks', N'PrintPdfUrl') IS NULL
BEGIN
    ALTER TABLE dbo.AdventurePacks
        ADD PrintPdfUrl NVARCHAR(2048) NULL;
END;
GO
