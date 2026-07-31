namespace AdventurePacks.Api.Domain.Entities;

/// <summary>
/// What a child's series remembers between books. One row per series (the series is the hero),
/// rewritten in place after every finished book so it never grows with the shelf.
/// </summary>
public sealed class SeriesMemory
{
    /// <summary>The hero character's id — <see cref="AdventurePack.SeriesId"/>.</summary>
    public Guid SeriesId { get; set; }

    public Guid UserId { get; set; }

    /// <summary>Distilled snapshot as JSON; see <see cref="Models.SeriesMemorySnapshot"/>.</summary>
    public string MemoryJson { get; set; } = "{}";

    /// <summary>The snapshot rendered for the story prompt, already in the book's language.</summary>
    public string? MemoryText { get; set; }

    /// <summary>Last book folded into the snapshot. Keeps a retried job from double-counting.</summary>
    public Guid? LastBookId { get; set; }

    public int BookCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
