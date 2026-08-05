using AdventurePacks.Api.Domain.Story;

namespace AdventurePacks.Api.Services;

internal static class AdventureStoryConstants
{
    /// <summary>
    /// The most pages a book may have. Sixteen, because a book is eight spreads and a spread
    /// prints as a picture and the page of prose facing it.
    ///
    /// This used to be six, and it was the reason a sixteen-page book still arrived as six: the
    /// clamp below is applied to every book, so a longer story was trimmed on the way in and the
    /// pages past the sixth were never illustrated.
    /// </summary>
    public const int MaxPageCount = BookFormat.PageCount;

    /// <summary>
    /// What the older prompt asks for when it has to write a story from scratch. Left where it
    /// was on purpose: that prompt illustrates every page it writes, so raising it would double
    /// what a fallback book costs to produce without making it a spread book.
    /// </summary>
    public const int LegacyPageCount = 6;

    /// <summary>How many pages are illustrated for free as the one-time welcome perk (new registrations only).</summary>
    public const int WelcomeGiftPageCount = 1;
    public const int PreviewIllustrationStaleMinutes = 12;

    /// <summary>Legacy alias — full paid/monthly stories.</summary>
    public const int PageCount = LegacyPageCount;

    /// <summary>
    /// The free "welcome" perk is one free sample illustration on the first book, handled
    /// separately, so the legacy welcome flag no longer shortens the story.
    /// </summary>
    public static int ResolvePageCount(int storyPageCount, bool isWelcomeGiftStory)
    {
        _ = isWelcomeGiftStory;
        var stored = storyPageCount > 0 ? storyPageCount : MaxPageCount;
        return Math.Min(stored, MaxPageCount);
    }
}
