using AdventurePacks.Api.Domain.Beki;
using AdventurePacks.Api.Repositories.Interfaces;

namespace AdventurePacks.Api.Repositories.Implementations;

public sealed class BekiVisualRepository(ISqlConnectionFactory connectionFactory) : IBekiVisualRepository
{
    // ---- identity -------------------------------------------------------

    public async Task<Guid> SaveIdentityAsync(BekiIdentityRecord record, CancellationToken cancellationToken)
    {
        // A new photo supersedes rather than overwrites: books already printed were
        // generated against the older spec, and their provenance must stay truthful.
        const string sql = """
                           INSERT INTO dbo.BekiChildIdentity (
                               Id, CharacterId, ReferenceQuality, IdentityJson, PhotoReference,
                               AnalyzerPromptVersion, AnalyzerModel, Version, CreatedAt)
                           SELECT @Id, @CharacterId, @ReferenceQuality, @IdentityJson, @PhotoReference,
                                  @AnalyzerPromptVersion, @AnalyzerModel,
                                  ISNULL((SELECT MAX(Version) FROM dbo.BekiChildIdentity WHERE CharacterId = @CharacterId), 0) + 1,
                                  SYSUTCDATETIME();
                           """;

        record.Id = record.Id == Guid.Empty ? Guid.NewGuid() : record.Id;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, record, cancellationToken: cancellationToken));
        return record.Id;
    }

    public async Task<BekiIdentityRecord?> GetLatestIdentityAsync(Guid characterId, CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT TOP 1 Id, CharacterId, ReferenceQuality, IdentityJson, PhotoReference,
                                  AnalyzerPromptVersion, AnalyzerModel, Version, CreatedAt
                           FROM dbo.BekiChildIdentity
                           WHERE CharacterId = @CharacterId
                           ORDER BY Version DESC;
                           """;
        using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<BekiIdentityRecord>(
            new CommandDefinition(sql, new { CharacterId = characterId }, cancellationToken: cancellationToken));
    }

    // ---- visual bible ---------------------------------------------------

    public async Task<Guid> SaveVisualBibleAsync(BekiVisualBibleRecord record, CancellationToken cancellationToken)
    {
        const string sql = """
                           INSERT INTO dbo.BekiVisualBible (
                               Id, StoryId, BibleJson, OutfitId, IdentityId,
                               BiblePromptVersion, BibleModel, Version, CreatedAt)
                           SELECT @Id, @StoryId, @BibleJson, @OutfitId, @IdentityId,
                                  @BiblePromptVersion, @BibleModel,
                                  ISNULL((SELECT MAX(Version) FROM dbo.BekiVisualBible WHERE StoryId = @StoryId), 0) + 1,
                                  SYSUTCDATETIME();
                           """;

        record.Id = record.Id == Guid.Empty ? Guid.NewGuid() : record.Id;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, record, cancellationToken: cancellationToken));
        return record.Id;
    }

    public async Task<BekiVisualBibleRecord?> GetVisualBibleAsync(Guid storyId, CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT TOP 1 Id, StoryId, BibleJson, OutfitId, IdentityId,
                                  BiblePromptVersion, BibleModel, Version, CreatedAt
                           FROM dbo.BekiVisualBible
                           WHERE StoryId = @StoryId
                           ORDER BY Version DESC;
                           """;
        using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<BekiVisualBibleRecord>(
            new CommandDefinition(sql, new { StoryId = storyId }, cancellationToken: cancellationToken));
    }

    // ---- assets ---------------------------------------------------------

    private const string AssetColumns = """
        Id, StoryId, AssetType, PageNumber, Status, BlobUrl, SceneSpecJson, FinalPromptText,
        ReviewJson, ReviewDecision, RepairAttempts, RegenerationAttempts, VisualBibleId,
        IdentityId, HeroAnchorAssetId, BekiAssetVersion, PromptVersion, ImageModel,
        ImageQuality, ImageSize, FailureReason, LatencyMs, CreatedAt, ApprovedAt
        """;

    /// <summary>
    /// Claims an asset slot. The unique indexes on (StoryId, AssetType, PageNumber) make
    /// this the idempotency point: a retried job finds the row already there and returns
    /// null rather than paying to draw page 7 a second time.
    /// </summary>
    public async Task<Guid?> TryClaimAssetAsync(
        Guid storyId,
        string assetType,
        int? pageNumber,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           IF NOT EXISTS (
                               SELECT 1 FROM dbo.BekiVisualAssets
                               WHERE StoryId = @StoryId AND AssetType = @AssetType
                                 AND ((PageNumber IS NULL AND @PageNumber IS NULL) OR PageNumber = @PageNumber))
                           BEGIN
                               INSERT INTO dbo.BekiVisualAssets (Id, StoryId, AssetType, PageNumber, Status, CreatedAt)
                               VALUES (@Id, @StoryId, @AssetType, @PageNumber, N'generating', SYSUTCDATETIME());
                               SELECT @Id;
                           END
                           """;

        var id = Guid.NewGuid();
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            sql,
            new { Id = id, StoryId = storyId, AssetType = assetType, PageNumber = pageNumber },
            cancellationToken: cancellationToken));
    }

    public async Task CompleteAssetAsync(BekiVisualAssetRecord record, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE dbo.BekiVisualAssets
                           SET Status = @Status,
                               BlobUrl = @BlobUrl,
                               SceneSpecJson = @SceneSpecJson,
                               FinalPromptText = @FinalPromptText,
                               ReviewJson = @ReviewJson,
                               ReviewDecision = @ReviewDecision,
                               RepairAttempts = @RepairAttempts,
                               RegenerationAttempts = @RegenerationAttempts,
                               VisualBibleId = @VisualBibleId,
                               IdentityId = @IdentityId,
                               HeroAnchorAssetId = @HeroAnchorAssetId,
                               BekiAssetVersion = @BekiAssetVersion,
                               PromptVersion = @PromptVersion,
                               ImageModel = @ImageModel,
                               ImageQuality = @ImageQuality,
                               ImageSize = @ImageSize,
                               FailureReason = @FailureReason,
                               LatencyMs = @LatencyMs,
                               ApprovedAt = CASE WHEN @Status = N'approved' THEN SYSUTCDATETIME() ELSE ApprovedAt END
                           WHERE Id = @Id;
                           """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, record, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<BekiVisualAssetRecord>> GetAssetsAsync(Guid storyId, CancellationToken cancellationToken)
    {
        var sql = $"""
                   SELECT {AssetColumns}
                   FROM dbo.BekiVisualAssets
                   WHERE StoryId = @StoryId
                   ORDER BY CASE AssetType WHEN N'hero_anchor' THEN 0 WHEN N'cover' THEN 1 ELSE 2 END,
                            PageNumber;
                   """;
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<BekiVisualAssetRecord>(
            new CommandDefinition(sql, new { StoryId = storyId }, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task<BekiVisualAssetRecord?> GetAssetAsync(
        Guid storyId,
        string assetType,
        int? pageNumber,
        CancellationToken cancellationToken)
    {
        var sql = $"""
                   SELECT TOP 1 {AssetColumns}
                   FROM dbo.BekiVisualAssets
                   WHERE StoryId = @StoryId AND AssetType = @AssetType
                     AND ((PageNumber IS NULL AND @PageNumber IS NULL) OR PageNumber = @PageNumber);
                   """;
        using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<BekiVisualAssetRecord>(new CommandDefinition(
            sql,
            new { StoryId = storyId, AssetType = assetType, PageNumber = pageNumber },
            cancellationToken: cancellationToken));
    }
}
