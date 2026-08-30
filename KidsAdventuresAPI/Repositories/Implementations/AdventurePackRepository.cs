using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Story;

namespace AdventurePacks.Api.Repositories.Implementations;

public sealed class AdventurePackRepository(ISqlConnectionFactory connectionFactory) : IAdventurePackRepository
{
    private const string PackColumns = """
        Id, UserId, ChildId, Theme, Status, GeneratedJson, PdfUrl, PrintPdfUrl, ErrorMessage,
        OptionalStoryNotes, StoryLanguage, ProgressMessage, ProgressPercent, PdfCreditCharged,
        PreviewIllustrationUrl, PreviewIllustrationStatus, PreviewIllustrationUpdatedAt,
        StoryPageCount, IsWelcomeGiftStory, CreatedAt,
        SeriesId, SequenceNumber, ContinuesFromBookId, AccessLevel, WorldId,
        PrimaryCharacterId, Title, CoverImageUrl, HasPrintEntitlement, LastReadAt,
        GenerationHeartbeatUtc
        """;

    /// <summary>
    /// The same columns without GeneratedJson, for queries that return many books.
    ///
    /// GeneratedJson holds an entire book. A shelf listing every book a family owns was reading
    /// all of them out of SQL and mapping them into memory to render covers and titles — which
    /// is the one thing the story is not needed for. A sixteen-page book made that noticeably
    /// worse, and it grows with every book bought.
    ///
    /// Everything that actually reads a story fetches one book by id, and those still get it.
    /// </summary>
    private const string PackListColumns = """
        Id, UserId, ChildId, Theme, Status, PdfUrl, PrintPdfUrl, ErrorMessage,
        OptionalStoryNotes, StoryLanguage, ProgressMessage, ProgressPercent, PdfCreditCharged,
        PreviewIllustrationUrl, PreviewIllustrationStatus, PreviewIllustrationUpdatedAt,
        StoryPageCount, IsWelcomeGiftStory, CreatedAt,
        SeriesId, SequenceNumber, ContinuesFromBookId, AccessLevel, WorldId,
        PrimaryCharacterId, Title, CoverImageUrl, HasPrintEntitlement, LastReadAt,
        GenerationHeartbeatUtc
        """;

    public async Task<Guid> CreatePendingAsync(AdventurePack pack, CancellationToken cancellationToken)
    {
        const string sql = """
                           INSERT INTO AdventurePacks (
                               Id, UserId, ChildId, Theme, Status, GeneratedJson, PdfUrl, PrintPdfUrl, ErrorMessage,
                               OptionalStoryNotes, StoryLanguage, ProgressMessage, ProgressPercent, PdfCreditCharged,
                               PreviewIllustrationUrl, PreviewIllustrationStatus, StoryPageCount, IsWelcomeGiftStory, CreatedAt,
                               SeriesId, SequenceNumber, ContinuesFromBookId, AccessLevel, WorldId,
                               PrimaryCharacterId, Title, CoverImageUrl, HasPrintEntitlement)
                           VALUES (
                               @Id, @UserId, @ChildId, @Theme, @Status, @GeneratedJson, @PdfUrl, @PrintPdfUrl, @ErrorMessage,
                               @OptionalStoryNotes, @StoryLanguage, @ProgressMessage, @ProgressPercent, @PdfCreditCharged,
                               @PreviewIllustrationUrl, @PreviewIllustrationStatus, @StoryPageCount, @IsWelcomeGiftStory, @CreatedAt,
                               @SeriesId, @SequenceNumber, @ContinuesFromBookId, @AccessLevel, @WorldId,
                               @PrimaryCharacterId, @Title, @CoverImageUrl, @HasPrintEntitlement);
                           """;
        pack.Id = pack.Id == Guid.Empty ? Guid.NewGuid() : pack.Id;

        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            pack.Id,
            pack.UserId,
            pack.ChildId,
            Theme = pack.Theme.ToString(),
            Status = pack.Status.ToString(),
            pack.GeneratedJson,
            pack.PdfUrl,
            pack.PrintPdfUrl,
            pack.ErrorMessage,
            pack.OptionalStoryNotes,
            pack.StoryLanguage,
            pack.ProgressMessage,
            pack.ProgressPercent,
            pack.PdfCreditCharged,
            pack.PreviewIllustrationUrl,
            PreviewIllustrationStatus = pack.PreviewIllustrationStatus.ToString(),
            pack.StoryPageCount,
            pack.IsWelcomeGiftStory,
            pack.CreatedAt,
            pack.SeriesId,
            pack.SequenceNumber,
            pack.ContinuesFromBookId,
            AccessLevel = pack.AccessLevel.ToString(),
            pack.WorldId,
            pack.PrimaryCharacterId,
            pack.Title,
            pack.CoverImageUrl,
            pack.HasPrintEntitlement
        }, cancellationToken: cancellationToken));
        return pack.Id;
    }

    public async Task<IReadOnlyList<AdventurePack>> GetByCharacterIdAsync(
        Guid characterId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        // Matches on either side of the cast: the hero the book is about, or a
        // supporting role, so a sibling's appearances show up on their shelf too.
        var sql = $"""
                   SELECT {PackListColumns}
                   FROM AdventurePacks AS p
                   WHERE p.UserId = @UserId
                     AND (
                         p.PrimaryCharacterId = @CharacterId
                         OR EXISTS (SELECT 1 FROM dbo.BookCharacters AS bc
                                    WHERE bc.BookId = p.Id AND bc.CharacterId = @CharacterId)
                     )
                   ORDER BY p.SequenceNumber ASC, p.CreatedAt ASC;
                   """;
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<AdventurePackRow>(
            new CommandDefinition(sql, new { UserId = userId, CharacterId = characterId }, cancellationToken: cancellationToken));
        return rows.Select(Map).ToList();
    }

    public async Task<int> GetNextSequenceNumberAsync(Guid seriesId, CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT ISNULL(MAX(SequenceNumber), 0) + 1
                           FROM AdventurePacks
                           WHERE SeriesId = @SeriesId;
                           """;
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, new { SeriesId = seriesId }, cancellationToken: cancellationToken));
    }

    public async Task<bool> SetAccessLevelAsync(Guid id, BookAccessLevel accessLevel, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE AdventurePacks
                           SET AccessLevel = @AccessLevel
                           WHERE Id = @Id;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = id, AccessLevel = accessLevel.ToString() },
            cancellationToken: cancellationToken));
        return affected > 0;
    }

    /// <summary>
    /// Stamps the book as read. Idempotent by intention rather than by guard: re-reading a book
    /// moves the stamp forward, which is what "last read" means.
    /// </summary>
    public async Task<bool> MarkReadAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE AdventurePacks
                           SET LastReadAt = SYSUTCDATETIME()
                           WHERE Id = @Id AND UserId = @UserId;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            sql, new { Id = id, UserId = userId }, cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<bool> SetPrintEntitlementAsync(Guid id, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE AdventurePacks
                           SET HasPrintEntitlement = 1
                           WHERE Id = @Id;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task UpdateBookPresentationAsync(
        Guid id,
        string? title,
        string? coverImageUrl,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE AdventurePacks
                           SET Title = COALESCE(@Title, Title),
                               CoverImageUrl = COALESCE(@CoverImageUrl, CoverImageUrl)
                           WHERE Id = @Id;
                           """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = id, Title = title, CoverImageUrl = coverImageUrl },
            cancellationToken: cancellationToken));
    }

    public async Task<AdventurePack?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var sql = $"""
                     SELECT TOP 1 {PackColumns}
                     FROM AdventurePacks
                     WHERE Id = @Id AND UserId = @UserId;
                     """;
        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<AdventurePackRow>(
            new CommandDefinition(sql, new { Id = id, UserId = userId }, cancellationToken: cancellationToken));
        return row is null ? null : Map(row);
    }

    public async Task<AdventurePack?> GetByIdNoOwnershipAsync(Guid id, CancellationToken cancellationToken)
    {
        var sql = $"""
                     SELECT TOP 1 {PackColumns}
                     FROM AdventurePacks
                     WHERE Id = @Id;
                     """;
        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<AdventurePackRow>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<AdventurePack>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var sql = $"""
                     SELECT {PackListColumns}
                     FROM AdventurePacks
                     WHERE UserId = @UserId
                     ORDER BY CreatedAt DESC;
                     """;
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<AdventurePackRow>(
            new CommandDefinition(sql, new { UserId = userId }, cancellationToken: cancellationToken));
        return rows.Select(Map).ToList();
    }

    public async Task<int> CountForMonthAsync(Guid userId, DateTime utcMonthStart, DateTime utcMonthEnd, CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT COUNT(1)
                           FROM AdventurePacks
                           WHERE UserId = @UserId
                             AND CreatedAt >= @UtcMonthStart
                             AND CreatedAt < @UtcMonthEnd
                             AND Status <> @FailedStatus
                             AND IsWelcomeGiftStory = 0;
                           """;
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            sql,
            new
            {
                UserId = userId,
                UtcMonthStart = utcMonthStart,
                UtcMonthEnd = utcMonthEnd,
                FailedStatus = AdventurePackStatus.Failed.ToString()
            },
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// The unconditional status write, now also stamping the generation heartbeat.
    ///
    /// The stamp is here rather than in a separate call because this is where a job says it is
    /// alive: the claim comes through here, and so does every phase change. A pack whose row has
    /// not been touched for longer than the whole generation budget is a pack whose job is gone,
    /// and before this column there was nothing on the row that could say so.
    /// </summary>
    public async Task<bool> UpdateStatusAsync(Guid id, AdventurePackStatus status, string? generatedJson, string? pdfUrl, string? errorMessage, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE AdventurePacks
                           SET Status = @Status,
                               GeneratedJson = @GeneratedJson,
                               PdfUrl = @PdfUrl,
                               ErrorMessage = @ErrorMessage,
                               GenerationHeartbeatUtc = SYSUTCDATETIME()
                           WHERE Id = @Id;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = id,
            Status = status.ToString(),
            GeneratedJson = generatedJson,
            PdfUrl = pdfUrl,
            ErrorMessage = errorMessage
        }, cancellationToken: cancellationToken));
        return affected > 0;
    }

    /// <summary>
    /// The same write, conditional on the pack still being in the status the caller last left it
    /// in. False means somebody else moved it first, and the caller's own answer is stale.
    ///
    /// This is what stops a revived job from overwriting a sweep's verdict. The sweep fails a pack
    /// whose job has been silent for longer than the budget plus a grace period; if that job is in
    /// fact still alive somewhere — a machine that came back, a network partition that healed — it
    /// finishes minutes later and writes Completed over the Failed. The pack would then be
    /// complete, the parent would see a book, and nothing would record that it took forty minutes
    /// and was declared lost. The reverse race is worse: the sweep failing a pack the moment after
    /// it completed.
    ///
    /// Both are the same race, and the fix for both is that the last writer has to say what it
    /// believes the row says.
    /// </summary>
    public async Task<bool> TryUpdateStatusAsync(
        Guid id,
        AdventurePackStatus expectedStatus,
        AdventurePackStatus status,
        string? generatedJson,
        string? pdfUrl,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE AdventurePacks
                           SET Status = @Status,
                               GeneratedJson = @GeneratedJson,
                               PdfUrl = @PdfUrl,
                               ErrorMessage = @ErrorMessage,
                               GenerationHeartbeatUtc = SYSUTCDATETIME()
                           WHERE Id = @Id AND Status = @ExpectedStatus;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = id,
            ExpectedStatus = expectedStatus.ToString(),
            Status = status.ToString(),
            GeneratedJson = generatedJson,
            PdfUrl = pdfUrl,
            ErrorMessage = errorMessage
        }, cancellationToken: cancellationToken));
        return affected > 0;
    }

    /// <summary>
    /// The packs whose generation job has gone quiet: still in a working status, and last heard
    /// from before the cutoff.
    ///
    /// <c>COALESCE(GenerationHeartbeatUtc, CreatedAt)</c> is the whole point of the query. The
    /// heartbeat column arrived after the books that are already stuck, and a NULL that read as
    /// "recent" would leave exactly those books unreachable — including the one that motivated
    /// this. A pack created hours ago and never claimed is not lost either way; it is Pending, and
    /// Pending is not a status this asks for.
    ///
    /// GeneratedJson is deliberately not selected: a stuck book is still a whole book on that row,
    /// and the sweep only needs to know which rows and what they say their status is.
    /// </summary>
    public async Task<IReadOnlyList<StaleGenerationPack>> ListStaleGenerationAsync(
        DateTime cutoffUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT TOP (@Limit)
                                  Id,
                                  Status,
                                  CreatedAt,
                                  GenerationHeartbeatUtc
                           FROM AdventurePacks
                           WHERE Status IN @Statuses
                             AND COALESCE(GenerationHeartbeatUtc, CreatedAt) < @CutoffUtc
                           ORDER BY COALESCE(GenerationHeartbeatUtc, CreatedAt);
                           """;

        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<StaleGenerationPackRow>(new CommandDefinition(
            sql,
            new
            {
                Limit = limit,
                CutoffUtc = cutoffUtc,
                Statuses = StaleGenerationStatuses.Select(status => status.ToString()).ToArray()
            },
            cancellationToken: cancellationToken));

        return rows
            .Select(row => new StaleGenerationPack(
                row.Id,
                Enum.Parse<AdventurePackStatus>(row.Status),
                row.CreatedAt,
                row.GenerationHeartbeatUtc))
            .ToList();
    }

    /// <summary>
    /// The statuses a Beki or legacy generation job leaves behind <em>while it is running</em>, and
    /// the only ones the sweep will fail.
    ///
    /// Pending and StoryReady are deliberately excluded, and not because nothing goes wrong there.
    /// They are the statuses a pack holds while its job sits in the Hangfire queue — a pack is
    /// created Pending, adopts its previewed story into StoryReady, and only then is generation
    /// enqueued. Queue latency is unbounded by design: with eight paid books drawing at eleven
    /// minutes each, the ninth waits well past any silence limit while being perfectly healthy.
    /// Sweeping those statuses would fail books whose only fault is being behind other books,
    /// which is worse than the stall it would catch.
    /// </summary>
    public static readonly IReadOnlyList<AdventurePackStatus> StaleGenerationStatuses =
    [
        AdventurePackStatus.GeneratingStory,
        AdventurePackStatus.GeneratingPdf
    ];

    /// <summary>
    /// Fails one stalled pack — only if it is still in the status the sweep saw, <em>and</em> still
    /// as silent as the sweep judged it to be.
    ///
    /// The status alone is not enough, and the gap it leaves is not theoretical. The sweep reads a
    /// batch, then writes each row in turn; in between, a job that was merely slow rather than dead
    /// delivers a spread. That write refreshes the heartbeat and leaves the status exactly where it
    /// was — <c>GeneratingStory</c> — so a status-only compare-and-set still matches, and a book
    /// that had just proved it was alive gets buried by a verdict formed before it spoke. Repeating
    /// the staleness test inside the UPDATE closes that window completely: the row has to be stale
    /// at the moment of the write, not merely at the moment of the read.
    ///
    /// The cutoff is the caller's own, so the two halves of one sweep pass judge by one clock.
    ///
    /// The book's own content columns are left exactly as the dead job left them, because a pack
    /// that stalled on spread seven still has seven spreads and a manifest, and a sweep that
    /// blanked GeneratedJson would destroy the evidence and any chance of a later resume. The
    /// heartbeat is stamped so the row does not keep being re-read by the next sweep before the
    /// status write is visible.
    ///
    /// The two progress columns are the exception, and they are written for the parent rather than
    /// for the sweep. Left alone they keep whatever the dead job last wrote — "იხატება მე-2
    /// გვერდი", 18% — which is a promise the row can no longer keep, on the screen the family is
    /// watching. So the same UPDATE that records the verdict replaces the line and clears the bar,
    /// in one write, because a row that is Failed with a percentage on it is a row somebody's
    /// loader will keep animating.
    /// </summary>
    public async Task<bool> TryFailStaleGenerationAsync(
        Guid id,
        AdventurePackStatus expectedStatus,
        DateTime cutoffUtc,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE AdventurePacks
                           SET Status = @Status,
                               ErrorMessage = @ErrorMessage,
                               ProgressMessage = @ProgressMessage,
                               ProgressPercent = NULL,
                               GenerationHeartbeatUtc = SYSUTCDATETIME()
                           WHERE Id = @Id
                             AND Status = @ExpectedStatus
                             AND COALESCE(GenerationHeartbeatUtc, CreatedAt) < @CutoffUtc;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = id,
            ExpectedStatus = expectedStatus.ToString(),
            CutoffUtc = cutoffUtc,
            Status = AdventurePackStatus.Failed.ToString(),
            ErrorMessage = Truncate(errorMessage),
            ProgressMessage = ParentFacingFailure.ProgressLine
        }, cancellationToken: cancellationToken));
        return affected > 0;
    }

    /// <summary>
    /// A job's own verdict on the book it was making: Failed, but only while the pack is still
    /// where that job left it.
    ///
    /// Distinct from <see cref="TryFailStaleGenerationAsync"/> by what it does <em>not</em> check —
    /// this writer knows the book is finished because it is the one that was making it, so
    /// staleness is beside the point. Distinct from <see cref="TryUpdateStatusAsync"/> by what it
    /// does not touch: the story, the PDF url and the rest stay exactly as they are, which is both
    /// safer than writing back a copy read minutes ago and the only way to record a verdict when
    /// the row could not be read at all.
    ///
    /// Progress is not part of "the rest". A terminal write leaves nothing running, so the line and
    /// the percentage are replaced with the parent-safe apology in the same statement — the legacy
    /// pipeline has always overwritten them on failure, and the Beki path's not doing so is why a
    /// paid parent watched a dead book sit at 18%.
    /// </summary>
    public async Task<bool> TryFailAsync(
        Guid id,
        AdventurePackStatus expectedStatus,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE AdventurePacks
                           SET Status = @Status,
                               ErrorMessage = @ErrorMessage,
                               ProgressMessage = @ProgressMessage,
                               ProgressPercent = NULL,
                               GenerationHeartbeatUtc = SYSUTCDATETIME()
                           WHERE Id = @Id AND Status = @ExpectedStatus;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = id,
            ExpectedStatus = expectedStatus.ToString(),
            Status = AdventurePackStatus.Failed.ToString(),
            ErrorMessage = Truncate(errorMessage),
            ProgressMessage = ParentFacingFailure.ProgressLine
        }, cancellationToken: cancellationToken));
        return affected > 0;
    }

    /// <summary>ErrorMessage is NVARCHAR(2048); a model's complaint can be longer than that.</summary>
    private static string Truncate(string message) =>
        message.Length <= 2000 ? message : message[..2000];

    public async Task UpdatePrintPdfUrlAsync(Guid id, string? printPdfUrl, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE AdventurePacks
                           SET PrintPdfUrl = @PrintPdfUrl
                           WHERE Id = @Id;
                           """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = id, PrintPdfUrl = printPdfUrl },
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// The message-only progress write, and the heartbeat with it — for the same reason as
    /// <see cref="UpdateProgressAsync"/>. The two exist only because one carries a percentage;
    /// a job that says something through either is equally alive, and a sweep that recognised
    /// only one of them would fail books the other kind of job was still drawing.
    /// </summary>
    public async Task UpdateProgressMessageAsync(Guid id, string? progressMessage, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE AdventurePacks
                           SET ProgressMessage = @ProgressMessage,
                               GenerationHeartbeatUtc = SYSUTCDATETIME()
                           WHERE Id = @Id;
                           """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = id, ProgressMessage = progressMessage }, cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Progress, and the heartbeat that goes with it.
    ///
    /// This is the call a long job makes most often — once per delivered spread — so it is the one
    /// that keeps the sweep off a book that is genuinely being drawn. A job that is still
    /// delivering pages is alive whatever its status column says.
    /// </summary>
    public async Task UpdateProgressAsync(Guid id, string? progressMessage, int? progressPercent, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE AdventurePacks
                           SET ProgressMessage = @ProgressMessage,
                               ProgressPercent = @ProgressPercent,
                               GenerationHeartbeatUtc = SYSUTCDATETIME()
                           WHERE Id = @Id;
                           """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = id, ProgressMessage = progressMessage, ProgressPercent = progressPercent },
            cancellationToken: cancellationToken));
    }

    public async Task SetPdfCreditChargedAsync(Guid id, bool charged, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE AdventurePacks
                           SET PdfCreditCharged = @PdfCreditCharged
                           WHERE Id = @Id;
                           """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = id,
            PdfCreditCharged = charged
        }, cancellationToken: cancellationToken));
    }

    public async Task UpdatePreviewIllustrationAsync(
        Guid id,
        PreviewIllustrationStatus status,
        string? illustrationUrl,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE AdventurePacks
                           SET PreviewIllustrationStatus = @PreviewIllustrationStatus,
                               PreviewIllustrationUrl = @PreviewIllustrationUrl,
                               PreviewIllustrationUpdatedAt = SYSUTCDATETIME()
                           WHERE Id = @Id;
                           """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = id,
            PreviewIllustrationStatus = status.ToString(),
            PreviewIllustrationUrl = illustrationUrl
        }, cancellationToken: cancellationToken));
    }

    public async Task<bool> TryClaimPreviewIllustrationGenerationAsync(
        Guid id,
        int staleAfterMinutes,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE AdventurePacks
                           SET PreviewIllustrationStatus = @Generating,
                               PreviewIllustrationUpdatedAt = SYSUTCDATETIME()
                           WHERE Id = @Id
                             AND (
                                 PreviewIllustrationStatus IN (@None, @Failed)
                                 OR (
                                     PreviewIllustrationStatus = @Generating
                                     AND (
                                         PreviewIllustrationUpdatedAt IS NULL
                                         OR PreviewIllustrationUpdatedAt < DATEADD(minute, -@StaleAfterMinutes, SYSUTCDATETIME())
                                     )
                                 )
                             );
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = id,
            StaleAfterMinutes = staleAfterMinutes,
            Generating = PreviewIllustrationStatus.Generating.ToString(),
            None = PreviewIllustrationStatus.None.ToString(),
            Failed = PreviewIllustrationStatus.Failed.ToString()
        }, cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task TouchPreviewIllustrationHeartbeatAsync(Guid id, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE AdventurePacks
                           SET PreviewIllustrationUpdatedAt = SYSUTCDATETIME()
                           WHERE Id = @Id
                             AND PreviewIllustrationStatus = @Generating;
                           """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = id,
            Generating = PreviewIllustrationStatus.Generating.ToString()
        }, cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateGeneratedJsonAsync(Guid id, string generatedJson, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE AdventurePacks
                           SET GeneratedJson = @GeneratedJson
                           WHERE Id = @Id;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = id,
            GeneratedJson = generatedJson
        }, cancellationToken: cancellationToken));
        return affected > 0;
    }

    private static AdventurePack Map(AdventurePackRow row) => new()
    {
        Id = row.Id,
        UserId = row.UserId,
        ChildId = row.ChildId,
        Theme = Enum.Parse<ThemeType>(row.Theme),
        Status = Enum.Parse<AdventurePackStatus>(row.Status),
        GeneratedJson = row.GeneratedJson,
        PdfUrl = row.PdfUrl,
        PrintPdfUrl = row.PrintPdfUrl,
        ErrorMessage = row.ErrorMessage,
        OptionalStoryNotes = row.OptionalStoryNotes,
        StoryLanguage = row.StoryLanguage,
        ProgressMessage = row.ProgressMessage,
        ProgressPercent = row.ProgressPercent,
        PdfCreditCharged = row.PdfCreditCharged,
        PreviewIllustrationUrl = row.PreviewIllustrationUrl,
        PreviewIllustrationStatus = Enum.Parse<PreviewIllustrationStatus>(row.PreviewIllustrationStatus),
        PreviewIllustrationUpdatedAt = row.PreviewIllustrationUpdatedAt,
        StoryPageCount = row.StoryPageCount,
        IsWelcomeGiftStory = row.IsWelcomeGiftStory,
        CreatedAt = row.CreatedAt,
        SeriesId = row.SeriesId,
        SequenceNumber = row.SequenceNumber,
        ContinuesFromBookId = row.ContinuesFromBookId,
        AccessLevel = Enum.TryParse<BookAccessLevel>(row.AccessLevel, out var accessLevel)
            ? accessLevel
            : BookAccessLevel.Preview,
        WorldId = row.WorldId,
        PrimaryCharacterId = row.PrimaryCharacterId,
        Title = row.Title,
        CoverImageUrl = row.CoverImageUrl,
        HasPrintEntitlement = row.HasPrintEntitlement,
        LastReadAt = row.LastReadAt,
        GenerationHeartbeatUtc = row.GenerationHeartbeatUtc
    };

    private sealed class AdventurePackRow
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid ChildId { get; set; }
        public string Theme { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? GeneratedJson { get; set; }
        public string? PdfUrl { get; set; }
        public string? PrintPdfUrl { get; set; }
        public string? ErrorMessage { get; set; }
        public string? OptionalStoryNotes { get; set; }
        public string? StoryLanguage { get; set; }
        public string? ProgressMessage { get; set; }
        public int? ProgressPercent { get; set; }
        public bool PdfCreditCharged { get; set; }
        public string? PreviewIllustrationUrl { get; set; }
        public string PreviewIllustrationStatus { get; set; } = "None";
        public DateTime? PreviewIllustrationUpdatedAt { get; set; }
        public int StoryPageCount { get; set; } = 6;
        public bool IsWelcomeGiftStory { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid? SeriesId { get; set; }
        public int SequenceNumber { get; set; } = 1;
        public Guid? ContinuesFromBookId { get; set; }
        public string AccessLevel { get; set; } = "Preview";
        public string? WorldId { get; set; }
        public Guid? PrimaryCharacterId { get; set; }
        public string? Title { get; set; }
        public string? CoverImageUrl { get; set; }
        public bool HasPrintEntitlement { get; set; }
        public DateTime? LastReadAt { get; set; }
        public DateTime? GenerationHeartbeatUtc { get; set; }
    }

    private sealed class StaleGenerationPackRow
    {
        public Guid Id { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? GenerationHeartbeatUtc { get; set; }
    }
}
