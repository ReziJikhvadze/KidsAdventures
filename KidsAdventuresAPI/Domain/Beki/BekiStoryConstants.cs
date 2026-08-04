namespace AdventurePacks.Api.Domain.Beki;

/// <summary>
/// Product rules that the deterministic validator enforces. These are not stylistic
/// preferences: each one exists because the prompt promises it and a paying parent would
/// notice if it broke.
/// </summary>
public static class BekiStoryConstants
{
    /// <summary>Interior pages. The cover is separate and never counted here.</summary>
    public const int PageCount = 12;

    /// <summary>Beki guides; Beki does not co-star. Fewer than 3 and the friendship never lands.</summary>
    public const int MinBekiPages = 3;
    public const int MaxBekiPages = 5;

    /// <summary>The Extra Wish must run through the book, not wave from a single page.</summary>
    public const int MinExtraWishBeats = 3;

    public const string SchemaVersion = "1.0";

    /// <summary>A book is the start of a series, so it never signs off.</summary>
    public static readonly string[] ForbiddenEndings =
    [
        "დასასრული",
        "The End",
        "THE END",
        "ბოლო",
    ];

    public static readonly string[] AgeBands = ["2-4", "5-7", "8-10"];

    public static readonly string[] ContinuationModes =
    [
        "first_book",
        "continue_previous_chapter",
        "new_adventure_same_universe",
        "new_world_with_existing_relationships",
    ];

    public static readonly string[] ThirdPartyModes = ["licensed", "private_test", "originalize", "exclude"];

    public static readonly string[] PageTurnFunctions =
    [
        "invitation", "curiosity", "choice", "discovery", "consequence", "relationship",
        "humor", "setback", "reveal", "resolution", "continuation_reveal",
    ];

    public static readonly string[] ReviewStatuses =
    [
        "approved_without_changes", "revised", "needs_human_review",
    ];

    /// <summary>
    /// Word budget per page. Enforced with slack because these are quality targets, not a
    /// reason to pad or truncate — the prompt says so, and a validator that rejected a good
    /// 78-word page for a 5–7 year old would be fighting the writing rather than guarding it.
    /// </summary>
    public static (int Min, int Max) WordRangeFor(string ageBand) => ageBand switch
    {
        "2-4" => (20, 45),
        "5-7" => (40, 75),
        _ => (65, 110),
    };

    /// <summary>How far outside the target range a page may drift before it is a real defect.</summary>
    public const double WordCountTolerance = 0.5;

    /// <summary>Active supporting cast in one scene, excluding the child and Beki.</summary>
    public static int MaxSupportingCastFor(string ageBand) => ageBand switch
    {
        "2-4" => 2,
        "5-7" => 3,
        _ => 4,
    };

    public static string AgeBandFor(int age) => age switch
    {
        <= 4 => "2-4",
        <= 7 => "5-7",
        _ => "8-10",
    };
}
