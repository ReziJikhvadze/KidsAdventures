namespace AdventurePacks.Api.Domain.Story;

/// <summary>
/// Thresholds derived from length rather than written down.
///
/// A craft rule that says "at least four emotions" is really saying "at least four emotions in
/// a twelve page book" — and quietly becomes wrong the day someone ships an eight page board
/// book or a twenty page chapter. Every judgement of that shape lives here, expressed as a
/// function of page count, so adding a new length is a product decision rather than a code
/// change.
/// </summary>
public static class StoryScale
{
    /// <summary>Below this a book is too short for distribution rules to say anything useful.</summary>
    public const int MinimumMeaningfulLength = 6;

    /// <summary>
    /// How many distinct feelings a book of this length needs before it reads as varied.
    /// Roughly one new emotion per three pages, floored so short books stay achievable and
    /// capped so a long book is not asked for more feelings than a child can follow.
    /// </summary>
    public static int MinimumDistinctEmotions(int pageCount) =>
        Math.Clamp(pageCount / 3, 3, 7);

    /// <summary>
    /// Deliberate surprises expected. One per four pages: enough that no stretch is
    /// predictable, few enough that the book is not exhausting.
    /// </summary>
    public static int MinimumSurprises(int pageCount) =>
        Math.Clamp(pageCount / 4, 2, 5);

    /// <summary>
    /// How many identical pages in a row before a run reads as monotony. Short books cannot
    /// afford three the same; long books can absorb one more before it registers.
    /// </summary>
    public static int MaximumSameRun(int pageCount) =>
        pageCount >= 16 ? 4 : 3;

    /// <summary>
    /// Whether a book is long enough for distribution rules — "needs a quiet page", "needs a
    /// joke" — to be fair. A four page book that is all wonder is a poem, not a failure.
    /// </summary>
    public static bool SupportsDistributionRules(int pageCount) =>
        pageCount >= MinimumMeaningfulLength + 2;

    /// <summary>Read-aloud word budget for a page, by the age it was written for.</summary>
    public static (int Min, int Max) PageWordBudget(int childAge) => childAge switch
    {
        <= 4 => (6, 28),
        <= 6 => (10, 45),
        <= 8 => (18, 70),
        _ => (25, 95)
    };
}
