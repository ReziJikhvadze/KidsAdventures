namespace AdventurePacks.Api.Services;

internal static class AdventureStoryConstants
{
    public const int FullPageCount = 6;
    public const int WelcomeGiftPageCount = 2;
    public const int PreviewIllustrationStaleMinutes = 12;

    /// <summary>Legacy alias — full paid/monthly stories.</summary>
    public const int PageCount = FullPageCount;

    /// <summary>Never bill more than 6 text pages + 6 images for a full story (2 for welcome gift).</summary>
    public static int ResolvePageCount(int storyPageCount, bool isWelcomeGiftStory)
    {
        if (isWelcomeGiftStory)
        {
            return WelcomeGiftPageCount;
        }

        var stored = storyPageCount > 0 ? storyPageCount : FullPageCount;
        return Math.Min(stored, FullPageCount);
    }
}
