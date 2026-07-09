namespace AdventurePacks.Api.Services;

internal static class AdventureStoryConstants
{
    public const int FullPageCount = 6;

    /// <summary>
    /// How many free fully-illustrated books a new account receives (the welcome gift).
    /// The gift paints every page of that one book — not a single sample page.
    /// </summary>
    public const int WelcomeGiftBookCount = 1;

    /// <summary>Legacy alias for <see cref="WelcomeGiftBookCount"/> (entitlement counter, not page count).</summary>
    public const int WelcomeGiftPageCount = WelcomeGiftBookCount;

    public const int PreviewIllustrationStaleMinutes = 12;

    /// <summary>Legacy alias — full paid/monthly stories.</summary>
    public const int PageCount = FullPageCount;

    /// <summary>
    /// Every book is a full 6-page story. The welcome gift marks the first book as fully illustrated for free;
    /// it never shortens the story length.
    /// </summary>
    public static int ResolvePageCount(int storyPageCount, bool isWelcomeGiftStory)
    {
        _ = isWelcomeGiftStory;
        var stored = storyPageCount > 0 ? storyPageCount : FullPageCount;
        return Math.Min(stored, FullPageCount);
    }
}
