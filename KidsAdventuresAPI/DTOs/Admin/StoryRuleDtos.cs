namespace AdventurePacks.Api.DTOs.Admin;

public sealed class StoryRuleMatrixResponse
{
    /// <summary>Row headings, in display order.</summary>
    public IReadOnlyList<string> AgeBands { get; set; } = [];

    /// <summary>Column headings, in display order.</summary>
    public IReadOnlyList<string> Themes { get; set; } = [];

    /// <summary>
    /// Every cell, including untuned ones and the theme-wide rows (<c>Theme</c> null), so the
    /// grid can render complete and each cell has an id to save against.
    /// </summary>
    public IReadOnlyList<StoryRuleResponse> Cells { get; set; } = [];
}

public sealed class StoryRuleResponse
{
    public Guid Id { get; set; }
    public string AgeBand { get; set; } = string.Empty;

    /// <summary>Null means this row applies to every world in the age band.</summary>
    public string? Theme { get; set; }

    public int? MaxWordsPerPage { get; set; }
    public int? MaxSentenceWords { get; set; }
    public string? VocabularyLevel { get; set; }
    public int? ScarinessLimit { get; set; }
    public string? ExtraGuidance { get; set; }
    public bool IsActive { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// A full replacement of the cell's tuning. Null on a field clears it, handing that aspect
/// back to the built-in age guidance.
/// </summary>
public sealed class UpdateStoryRuleRequest
{
    public int? MaxWordsPerPage { get; set; }
    public int? MaxSentenceWords { get; set; }
    public string? VocabularyLevel { get; set; }
    public int? ScarinessLimit { get; set; }
    public string? ExtraGuidance { get; set; }
    public bool IsActive { get; set; } = true;
}
