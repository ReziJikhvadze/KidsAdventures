namespace AdventurePacks.Api.Domain.Entities;

/// <summary>
/// One cell of the age x theme matrix. Every tuning field is optional; an unset field means
/// "keep the built-in guidance", which is why an untouched matrix changes nothing.
/// </summary>
public sealed class StoryRule
{
    public Guid Id { get; set; }

    /// <summary>See <see cref="StoryAgeBands"/>.</summary>
    public string AgeBand { get; set; } = string.Empty;

    /// <summary><see cref="ThemeType"/> name, or null for every world in this age band.</summary>
    public string? Theme { get; set; }

    public int? MaxWordsPerPage { get; set; }

    public int? MaxSentenceWords { get; set; }

    /// <summary>simple | standard | rich</summary>
    public string? VocabularyLevel { get; set; }

    /// <summary>0 = nothing tense, 3 = real jeopardy.</summary>
    public int? ScarinessLimit { get; set; }

    public string? ExtraGuidance { get; set; }

    public bool IsActive { get; set; } = true;

    public Guid? UpdatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

public static class StoryAgeBands
{
    public const string Young = "3-5";
    public const string Middle = "6-9";
    public const string Older = "10-13";

    public static readonly string[] All = [Young, Middle, Older];

    /// <summary>
    /// Mirrors the thresholds the prompt builder has always used, so a child does not change
    /// band just because the matrix now exists.
    /// </summary>
    public static string ForAge(int age) => age switch
    {
        <= 5 => Young,
        <= 9 => Middle,
        _ => Older,
    };

    public static bool IsValid(string? band) => All.Contains(band);
}
