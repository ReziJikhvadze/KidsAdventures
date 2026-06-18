namespace AdventurePacks.Api.Services;

internal static class AdventureStoryConstants
{
    public const int FullPageCount = 6;
    public const int WelcomeGiftPageCount = 2;
    public const int PreviewIllustrationStaleMinutes = 12;

    /// <summary>Legacy alias — full paid/monthly stories.</summary>
    public const int PageCount = FullPageCount;

    /// <summary>
    /// Every book is now a full 6-page TEXT story. The free "welcome" perk is 2 free sample illustrations on the
    /// first book (handled separately), so the legacy welcome flag no longer shortens the story.
    /// </summary>
    public static int ResolvePageCount(int storyPageCount, bool isWelcomeGiftStory)
    {
        _ = isWelcomeGiftStory;
        var stored = storyPageCount > 0 ? storyPageCount : FullPageCount;
        return Math.Min(stored, FullPageCount);
    }
}
