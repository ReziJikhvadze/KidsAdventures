namespace AdventurePacks.Api.Domain.Models;

public sealed class ProblemDefinition
{
    /// <summary>choice_consequence | sequence_match</summary>
    public string InteractionType { get; set; } = "choice_consequence";

    public string? Prompt { get; set; }

    /// <summary>Interaction-specific config (options, items, correct order, etc.).</summary>
    public string? ConfigJson { get; set; }
}
