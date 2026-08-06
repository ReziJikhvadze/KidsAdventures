using System.Text.Json;
using AdventurePacks.Api.Domain.Story;

namespace AdventurePacks.Api.Services.Story.Prompts;

/// <summary>
/// The shape the architect call must answer in.
///
/// Small on purpose. The plan is decisions, not prose — a title, a refrain, the cast with the
/// scene each of them is allowed to enter, and one line per scene. Everything that takes words
/// to say belongs to the writer call.
/// </summary>
public static class StoryPlanSchema
{
    public const string Name = "story_plan";

    public static JsonElement Build(int spreadCount = BookFormat.SpreadCount)
    {
        var schema = new
        {
            type = "object",
            additionalProperties = false,
            required = new[] { "storyTitle", "refrainPhrase", "characterLock", "characterManifest", "outline" },
            properties = new Dictionary<string, object>
            {
                ["storyTitle"] = Text("The book's title, in the story's language."),
                ["refrainPhrase"] = Text(
                    "Two to four words in the story's language, rhythmic enough to say aloud. It "
                    + "will recur three times, so it must sound like something a character would "
                    + "say rather than a slogan."),
                ["characterLock"] = new
                {
                    type = "string",
                    description =
                        "ENGLISH ONLY. One paragraph describing every recurring character's "
                        + "appearance: face, hair, eyes, skin, build, and the exact clothing worn "
                        + "in every scene. Appearance only — no scene, no instruction about "
                        + "photographs. It is placed into every illustration prompt automatically."
                },
                ["characterManifest"] = new
                {
                    type = "array",
                    description =
                        "The hero is not listed. At most two secondary characters — a book for a "
                        + "small child cannot hold more.",
                    items = new
                    {
                        type = "object",
                        additionalProperties = false,
                        required = new[] { "name", "role", "introducedInSpread" },
                        properties = new Dictionary<string, object>
                        {
                            ["name"] = Text("The name used on every page they appear."),
                            ["role"] = Text("What they are to the hero, in a few words."),
                            ["introducedInSpread"] = new
                            {
                                type = "integer",
                                description =
                                    $"1 to {spreadCount}. The first scene this character may "
                                    + "appear in. They must not be named or present before it."
                            }
                        }
                    }
                },
                ["outline"] = new
                {
                    type = "array",
                    description = $"Exactly {spreadCount} scenes, numbered 1 to {spreadCount}, in order.",
                    items = new
                    {
                        type = "object",
                        additionalProperties = false,
                        required = new[] { "spreadNumber", "title", "plotSummary", "childAction" },
                        properties = new Dictionary<string, object>
                        {
                            ["spreadNumber"] = new { type = "integer", description = "1-based." },
                            ["title"] = Text("Short heading for this scene, in the story's language."),
                            ["plotSummary"] = Text("One short sentence. What happens, not how it is written."),
                            ["childAction"] = Text(
                                "What the child physically does or notices here — the skill shown "
                                + "as an action, never named.")
                        }
                    }
                }
            }
        };

        return JsonSerializer.SerializeToElement(schema, StoryJson.Options);
    }

    private static object Text(string description) => new { type = "string", description };
}
