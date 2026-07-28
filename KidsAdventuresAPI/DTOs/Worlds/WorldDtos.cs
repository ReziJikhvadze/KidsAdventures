namespace AdventurePacks.Api.DTOs.Worlds;

public sealed class WorldResponse
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

/// <summary>One node on a child's adventure map.</summary>
public sealed class WorldNodeResponse
{
    public string WorldId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    /// <summary>"Locked", "Unlocked", "Completed" or "Next".</summary>
    public WorldState State { get; set; }

    /// <summary>True when a book can be started here right now.</summary>
    public bool CanStart { get; set; }

    /// <summary>The book that completed this world, when there is one.</summary>
    public Guid? BookId { get; set; }

    public string? BookTitle { get; set; }
    public string? CoverImageUrl { get; set; }
    public int? SequenceNumber { get; set; }
    public DateTime? CompletedAt { get; set; }
}

/// <summary>The whole map for one hero, plus what continuing the story would mean.</summary>
public sealed class AdventureMapResponse
{
    public Guid CharacterId { get; set; }
    public string CharacterName { get; set; } = string.Empty;

    /// <summary>True before the first book, when every world is still open to choose from.</summary>
    public bool IsFirstJourney { get; set; }

    public int CompletedCount { get; set; }
    public int TotalWorlds { get; set; }

    /// <summary>The world the map invites the child into next, or null once all are done.</summary>
    public string? NextWorldId { get; set; }

    public List<WorldNodeResponse> Worlds { get; set; } = [];

    /// <summary>What a continuation would pick up from.</summary>
    public ContinuationResponse? Continuation { get; set; }
}

public sealed class ContinuationResponse
{
    /// <summary>Book the next chapter continues from: the newest fully unlocked one.</summary>
    public Guid FromBookId { get; set; }

    public string? FromBookTitle { get; set; }
    public string? FromWorldId { get; set; }
    public int FromSequenceNumber { get; set; }

    /// <summary>The sequence number the next book in this series will take.</summary>
    public int NextSequenceNumber { get; set; }

    /// <summary>Suggested destination for the next chapter.</summary>
    public string? SuggestedWorldId { get; set; }

    /// <summary>Cast carried forward, hero first, so the UI can pre-tick them.</summary>
    public List<ContinuationCharacter> CarryForwardCharacters { get; set; } = [];
}

public sealed class ContinuationCharacter
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CharacterType { get; set; } = "child";
    public string? Relationship { get; set; }
    public bool IsPrimary { get; set; }
}
