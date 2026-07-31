namespace AdventurePacks.Api.Domain.Models;

/// <summary>
/// The shape the distiller is asked to produce. Keeping it small and typed is the whole point:
/// the next book's prompt gets a handful of concrete facts rather than a growing wall of prose.
/// </summary>
public sealed class SeriesMemorySnapshot
{
    /// <summary>Friends, animals and guides the hero has met and may meet again.</summary>
    public List<SeriesCompanion> Companions { get; set; } = [];

    /// <summary>Moments worth calling back to, newest first.</summary>
    public List<string> Memories { get; set; } = [];

    /// <summary>The thread running through the series — what the hero is still reaching for.</summary>
    public string? Goal { get; set; }

    /// <summary>Where the hero has been and how each place was left.</summary>
    public List<SeriesWorldNote> Worlds { get; set; } = [];

    /// <summary>Traits the stories have established about the hero (brave, curious, afraid of the dark).</summary>
    public List<string> HeroTraits { get; set; } = [];
}

public sealed class SeriesCompanion
{
    public string Name { get; set; } = string.Empty;

    /// <summary>"a small dinosaur", "the lighthouse keeper" — enough to write them back in.</summary>
    public string? Description { get; set; }

    /// <summary>How they came into the story, so a return feels earned.</summary>
    public string? MetIn { get; set; }
}

public sealed class SeriesWorldNote
{
    public string WorldId { get; set; } = string.Empty;

    /// <summary>The state the last book left it in.</summary>
    public string? LeftAs { get; set; }
}
