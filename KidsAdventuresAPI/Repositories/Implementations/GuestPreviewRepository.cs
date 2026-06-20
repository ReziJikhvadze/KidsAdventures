using AdventurePacks.Api.Repositories.Interfaces;

namespace AdventurePacks.Api.Repositories.Implementations;

public sealed class GuestPreviewRepository(ISqlConnectionFactory connectionFactory) : IGuestPreviewRepository
{
    private const string Columns = """
        Id, StoryId, PreviewUsed, Redeemed, RedeemedByUserId, ClientKey, ChildName, Theme, CreatedAt, RedeemedAt
        """;

    public async Task CreateAsync(GuestPreview preview, CancellationToken cancellationToken)
    {
        const string sql = """
                           INSERT INTO GuestPreviews (
                               Id, StoryId, PreviewUsed, Redeemed, RedeemedByUserId, ClientKey, ChildName, Theme, CreatedAt, RedeemedAt)
                           VALUES (
                               @Id, @StoryId, @PreviewUsed, @Redeemed, @RedeemedByUserId, @ClientKey, @ChildName, @Theme, @CreatedAt, @RedeemedAt);
                           """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            preview.Id,
            preview.StoryId,
            preview.PreviewUsed,
            preview.Redeemed,
            preview.RedeemedByUserId,
            preview.ClientKey,
            preview.ChildName,
            preview.Theme,
            preview.CreatedAt,
            preview.RedeemedAt
        }, cancellationToken: cancellationToken));
    }

    public async Task<GuestPreview?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var sql = $"""
                     SELECT TOP 1 {Columns}
                     FROM GuestPreviews
                     WHERE Id = @Id;
                     """;
        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<GuestPreviewRow>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
        return row is null ? null : Map(row);
    }

    public async Task<GuestPreview?> GetByStoryIdAsync(Guid storyId, CancellationToken cancellationToken)
    {
        var sql = $"""
                     SELECT TOP 1 {Columns}
                     FROM GuestPreviews
                     WHERE StoryId = @StoryId
                     ORDER BY CreatedAt DESC;
                     """;
        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<GuestPreviewRow>(
            new CommandDefinition(sql, new { StoryId = storyId }, cancellationToken: cancellationToken));
        return row is null ? null : Map(row);
    }

    public async Task<bool> TryRedeemAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE GuestPreviews
                           SET Redeemed = 1,
                               RedeemedByUserId = @UserId,
                               RedeemedAt = SYSUTCDATETIME()
                           WHERE Id = @Id
                             AND Redeemed = 0;
                           """;
        using var connection = connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(
            new CommandDefinition(sql, new { Id = id, UserId = userId }, cancellationToken: cancellationToken));
        return affected > 0;
    }

    private static GuestPreview Map(GuestPreviewRow row) => new()
    {
        Id = row.Id,
        StoryId = row.StoryId,
        PreviewUsed = row.PreviewUsed,
        Redeemed = row.Redeemed,
        RedeemedByUserId = row.RedeemedByUserId,
        ClientKey = row.ClientKey,
        ChildName = row.ChildName,
        Theme = row.Theme,
        CreatedAt = row.CreatedAt,
        RedeemedAt = row.RedeemedAt
    };

    private sealed class GuestPreviewRow
    {
        public Guid Id { get; set; }
        public Guid StoryId { get; set; }
        public bool PreviewUsed { get; set; }
        public bool Redeemed { get; set; }
        public Guid? RedeemedByUserId { get; set; }
        public string? ClientKey { get; set; }
        public string? ChildName { get; set; }
        public string? Theme { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? RedeemedAt { get; set; }
    }
}
