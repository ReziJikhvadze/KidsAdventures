using System.Text.Json;
using AdventurePacks.Api.Domain.Story;

namespace AdventurePacks.Api.Services.Story.Composite;

/// <summary>The story writer owns supporting character designs, scoped to this book.</summary>
public static class CompositeStoryCharacters
{
    public static IReadOnlyList<string> ForPage(MasterStory story, int page)
    {
        var visible = story.Spreads.FirstOrDefault(spread => spread.Number == page)?.Characters ?? [];
        return (story.Cast ?? [])
            .Where(member => IsSupporting(member) && visible.Contains(member.Id, StringComparer.Ordinal))
            .Select(member => $"{member.Id} ({member.Name}): {member.VisualDescription.Trim()}")
            .Distinct(StringComparer.Ordinal).ToList();
    }

    public static string PlannerInput(MasterStory? story)
    {
        if (story is null) return string.Empty;
        var cast = (story.Cast ?? []).Where(IsSupporting).ToList();
        if (cast.Count == 0) return string.Empty;
        return "\nAUTHORITATIVE SUPPORTING CHARACTERS FROM THIS BOOK'S STORY WRITER\n"
            + "Preserve these designs exactly; stage them, do not redesign or replace them. "
            + "Use each character only on its listed pages. Include its design in recurring_elements "
            + "and use the same identity in every relevant child_world_scene. "
            + "These designs belong only to this book, not to the theme or other books.\n"
            + JsonSerializer.Serialize(cast.Select(member => new
            {
                id = member.Id, name = member.Name, visual_description = member.VisualDescription,
                pages = story.Spreads.Where(spread =>
                    (spread.Characters ?? []).Contains(member.Id, StringComparer.Ordinal))
                    .Select(spread => spread.Number).ToList()
            }), CompositeJson.Readable);
    }

    private static bool IsSupporting(StoryCastMember member) =>
        !string.IsNullOrWhiteSpace(member.Id)
        && !member.Id.Equals("child", StringComparison.OrdinalIgnoreCase)
        && !member.Id.Equals("beki", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(member.VisualDescription);
}
