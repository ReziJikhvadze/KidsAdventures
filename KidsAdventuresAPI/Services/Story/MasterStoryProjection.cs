using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Domain.Story;

namespace AdventurePacks.Api.Services.Story;

/// <summary>
/// Turns the book the model wrote into the shape the app already stores, renders and prints.
///
/// A spread becomes two pages, in reading order: the picture first, then the prose facing it.
/// That order is what keeps text off a child's face — the complaint that started this — because
/// the words are never asked to share a page with the illustration.
///
/// The projection is a pure function of the master story. Nothing here invents, shortens or
/// rewrites: creativity was the model's job, and this code's only job is to be faithful about it.
/// </summary>
public static class MasterStoryProjection
{
    public static AdventureContentDto ToContent(MasterStory story, string childName, string theme)
    {
        var pages = new List<StoryPageDto>(story.Spreads.Count * 2);

        foreach (var spread in story.Spreads.OrderBy(s => s.Number))
        {
            // The illustration page. Its caption is the only text on it, sitting under the
            // picture rather than over it.
            pages.Add(new StoryPageDto
            {
                Title = spread.Title,
                Caption = spread.Caption,
                Content = string.Empty,
                IsTextOnlyPage = false,
                ImagePrompt = IllustrationPrompt.Compose(
                    story.CharacterLock, spread.Illustration.Scene, spread.Illustration.Avoid),
                NegativePrompt = null
            });

            // The facing text page. It carries the read-aloud prose and never an image, which is
            // what gives the words a whole page to breathe.
            pages.Add(new StoryPageDto
            {
                Title = spread.Title,
                Caption = spread.Caption,
                Content = spread.Text,
                IsTextOnlyPage = true
            });
        }

        return new AdventureContentDto
        {
            Title = story.Concept.Title,
            Theme = theme,
            ChildName = childName,
            CharacterLock = story.CharacterLock,
            StoryPages = pages
        };
    }

    /// <summary>
    /// Which pages carry a picture, in the order they should be drawn.
    ///
    /// Page 0 comes first and alone, because it is the anchor every later illustration is matched
    /// against; drawing it in parallel with the others would leave them nothing to match.
    /// </summary>
    public static IReadOnlyList<int> IllustratablePageIndexes(AdventureContentDto content) =>
        content.StoryPages
            .Select((page, index) => (page, index))
            .Where(x => !x.page.IsTextOnlyPage)
            .Select(x => x.index)
            .ToList();
}
