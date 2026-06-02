using AdventurePacks.Api.Repositories.Interfaces;

namespace AdventurePacks.Api.Repositories.Implementations;

public sealed class AdventurePackRepository(ISqlConnectionFactory connectionFactory) : IAdventurePackRepository
{
    public async Task<Guid> CreatePendingAsync(AdventurePack pack, CancellationToken cancellationToken)
    {
        const string sql = """
                           INSERT INTO AdventurePacks (Id, UserId, ChildId, Theme, Status, GeneratedJson, PdfUrl, ErrorMessage, CreatedAt)
                           VALUES (@Id, @UserId, @ChildId, @Theme, @Status, @GeneratedJson, @PdfUrl, @ErrorMessage, @CreatedAt);
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
            pack.CreatedAt
        }, cancellationToken: cancellationToken));
        return pack.Id;
    }

    public async Task<AdventurePack?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT TOP 1 Id, UserId, ChildId, Theme, Status, GeneratedJson, PdfUrl, ErrorMessage, CreatedAt
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
        const string sql = """
                           SELECT TOP 1 Id, UserId, ChildId, Theme, Status, GeneratedJson, PdfUrl, ErrorMessage, CreatedAt
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
        const string sql = """
                           SELECT Id, UserId, ChildId, Theme, Status, GeneratedJson, PdfUrl, ErrorMessage, CreatedAt
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
                             AND CreatedAt < @UtcMonthEnd;
                           """;
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { UserId = userId, UtcMonthStart = utcMonthStart, UtcMonthEnd = utcMonthEnd }, cancellationToken: cancellationToken));
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
        CreatedAt = row.CreatedAt
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
        public DateTime CreatedAt { get; set; }
    }
}
