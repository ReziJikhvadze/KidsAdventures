using AdventurePacks.Api.Repositories.Interfaces;

namespace AdventurePacks.Api.Repositories.Implementations;

public sealed class AdventurePackRepository(ISqlConnectionFactory connectionFactory) : IAdventurePackRepository
{
    private const string PackColumns = """
        Id, UserId, ChildId, Theme, Status, GeneratedJson, PdfUrl, ErrorMessage,
        OptionalStoryNotes, StoryLanguage, ProgressMessage, PdfCreditCharged,
        PreviewIllustrationUrl, PreviewIllustrationStatus, PreviewIllustrationUpdatedAt,
        StoryPageCount, IsWelcomeGiftStory, CreatedAt,
        SeriesId, SequenceNumber, ContinuesFromBookId, AccessLevel, WorldId,
        PrimaryCharacterId, Title, CoverImageUrl, HasPrintEntitlement
        """;

    public async Task<Guid> CreatePendingAsync(AdventurePack pack, CancellationToken cancellationToken)
    {
        const string sql = """
                           INSERT INTO AdventurePacks (
                               Id, UserId, ChildId, Theme, Status, GeneratedJson, PdfUrl, ErrorMessage,
                               OptionalStoryNotes, StoryLanguage, ProgressMessage, PdfCreditCharged,
                               PreviewIllustrationUrl, PreviewIllustrationStatus, StoryPageCount, IsWelcomeGiftStory, CreatedAt,
                               SeriesId, SequenceNumber, ContinuesFromBookId, AccessLevel, WorldId,
                               PrimaryCharacterId, Title, CoverImageUrl, HasPrintEntitlement)
                           VALUES (
                               @Id, @UserId, @ChildId, @Theme, @Status, @GeneratedJson, @PdfUrl, @ErrorMessage,
                               @OptionalStoryNotes, @StoryLanguage, @ProgressMessage, @PdfCreditCharged,
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
            pack.ErrorMessage,
            pack.OptionalStoryNotes,
            pack.StoryLanguage,
            pack.ProgressMessage,
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
                   SELECT {PackColumns}
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
                     SELECT {PackColumns}
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

    public async Task<bool> UpdateStatusAsync(Guid id, AdventurePackStatus status, string? generatedJson, string? pdfUrl, string? errorMessage, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE AdventurePacks
                           SET Status = @Status,
                               GeneratedJson = @GeneratedJson,
                               PdfUrl = @PdfUrl,
                               ErrorMessage = @ErrorMessage
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

    public async Task UpdateProgressMessageAsync(Guid id, string? progressMessage, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE AdventurePacks
                           SET ProgressMessage = @ProgressMessage
                           WHERE Id = @Id;
                           """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = id, ProgressMessage = progressMessage }, cancellationToken: cancellationToken));
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
        ErrorMessage = row.ErrorMessage,
        OptionalStoryNotes = row.OptionalStoryNotes,
        StoryLanguage = row.StoryLanguage,
        ProgressMessage = row.ProgressMessage,
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
        HasPrintEntitlement = row.HasPrintEntitlement
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
        public string? ErrorMessage { get; set; }
        public string? OptionalStoryNotes { get; set; }
        public string? StoryLanguage { get; set; }
        public string? ProgressMessage { get; set; }
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
    }
}
