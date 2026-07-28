namespace AdventurePacks.Api.Domain.Entities;

/// <summary>One of the six destinations on the adventure map.</summary>
public sealed class World
{
    /// <summary>Stable slug shared with the frontend, e.g. "dinosaurs".</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Georgian display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Map order, which also decides which world unlocks next.</summary>
    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>One character's standing in one world.</summary>
public sealed class UserWorldProgress
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid CharacterId { get; set; }
    public string WorldId { get; set; } = string.Empty;

    /// <summary>Never <see cref="WorldState.Next"/>; that state is derived, not stored.</summary>
    public WorldState State { get; set; } = WorldState.Locked;

    /// <summary>The book that completed this world.</summary>
    public Guid? BookId { get; set; }

    public DateTime? UnlockedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
