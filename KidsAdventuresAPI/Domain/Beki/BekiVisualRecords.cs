namespace AdventurePacks.Api.Domain.Beki;

/// <summary>A row in <c>dbo.BekiChildIdentity</c> — the derived spec, never the photo.</summary>
public sealed class BekiIdentityRecord
{
    public Guid Id { get; set; }
    public Guid CharacterId { get; set; }
    public string ReferenceQuality { get; set; } = string.Empty;
    public string IdentityJson { get; set; } = string.Empty;
    public string? PhotoReference { get; set; }
    public string? AnalyzerPromptVersion { get; set; }
    public string? AnalyzerModel { get; set; }
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A row in <c>dbo.BekiVisualBible</c> — one per book.</summary>
public sealed class BekiVisualBibleRecord
{
    public Guid Id { get; set; }
    public Guid StoryId { get; set; }
    public string BibleJson { get; set; } = string.Empty;
    public string? OutfitId { get; set; }
    public Guid? IdentityId { get; set; }
    public string? BiblePromptVersion { get; set; }
    public string? BibleModel { get; set; }
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A row in <c>dbo.BekiVisualAssets</c> — the anchor, the cover, or one page.</summary>
public sealed class BekiVisualAssetRecord
{
    public Guid Id { get; set; }
    public Guid StoryId { get; set; }

    /// <summary>See <see cref="BekiAssetType"/>.</summary>
    public string AssetType { get; set; } = string.Empty;

    /// <summary>1..12 for pages; null for the anchor and cover.</summary>
    public int? PageNumber { get; set; }

    /// <summary>See <see cref="BekiAssetStatus"/>.</summary>
    public string Status { get; set; } = BekiAssetStatus.Pending;

    public string? BlobUrl { get; set; }
    public string? SceneSpecJson { get; set; }

    /// <summary>The exact prompt sent to the image model — the most useful field when a page is wrong.</summary>
    public string? FinalPromptText { get; set; }

    public string? ReviewJson { get; set; }
    public string? ReviewDecision { get; set; }
    public int RepairAttempts { get; set; }
    public int RegenerationAttempts { get; set; }
    public Guid? VisualBibleId { get; set; }
    public Guid? IdentityId { get; set; }
    public Guid? HeroAnchorAssetId { get; set; }
    public string? BekiAssetVersion { get; set; }
    public string? PromptVersion { get; set; }
    public string? ImageModel { get; set; }
    public string? ImageQuality { get; set; }
    public string? ImageSize { get; set; }
    public string? FailureReason { get; set; }
    public int? LatencyMs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAt { get; set; }
}

public static class BekiAssetType
{
    public const string HeroAnchor = "hero_anchor";
    public const string Cover = "cover";
    public const string Page = "page";
}

public static class BekiAssetStatus
{
    public const string Pending = "pending";
    public const string Generating = "generating";
    public const string ReviewPending = "review_pending";
    public const string RepairPending = "repair_pending";
    public const string Approved = "approved";
    public const string Failed = "failed";
}
