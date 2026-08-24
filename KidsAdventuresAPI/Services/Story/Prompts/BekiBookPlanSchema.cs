using System.Text.Json;
using AdventurePacks.Api.Domain.Story;

namespace AdventurePacks.Api.Services.Story.Prompts;

/// <summary>
/// The shape the Beki PLAN BOOK call must answer in.
///
/// A separate builder rather than a flag on <see cref="MasterStorySchema"/>. That schema is what
/// every A5 book in production is written against; a parameter there would put a branch inside
/// the one function whose output must not move, and "it only changes when the flag is set" is a
/// promise that gets broken by the next edit. Two builders cannot drift into each other.
///
/// It deserialises into the same <see cref="MasterStory"/> record, using the optional Beki fields
/// on it. The A5 fields the record still requires — concept, characterLock, title, caption — are
/// asked for here too, because the record cannot be constructed without them. `caption` and the
/// per-spread `title` are the exception the handoff names ("do not create page titles"): they are
/// requested as empty strings so nothing downstream that reads them finds a null, and no title
/// is invented for a page that will not print one.
/// </summary>
public static class BekiBookPlanSchema
{
    public const string Name = "beki_book_plan";

    public static JsonElement Build(int spreadCount = BookFormat.SpreadCount)
    {
        var schema = new
        {
            type = "object",
            additionalProperties = false,
            required = new[] { "concept", "titleEn", "cast", "objects", "spreads", "characterLock", "worldLock", "cover" },
            properties = new Dictionary<string, object>
            {
                ["concept"] = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[] { "title", "outline" },
                    properties = new Dictionary<string, object>
                    {
                        ["title"] = Text("The book's title, in Georgian."),
                        ["outline"] = new
                        {
                            type = "array",
                            description = "The eight beats, in order, one short line each.",
                            items = new { type = "string" }
                        }
                    }
                },
                ["titleEn"] = Text("The same title in English."),
                ["cast"] = new
                {
                    type = "array",
                    description =
                        "Only as many recurring supporting characters as the story actually needs — "
                        + "none is a valid answer. The child is the hero and is never listed here.",
                    items = new
                    {
                        type = "object",
                        additionalProperties = false,
                        required = new[] { "id", "name", "visualDescription" },
                        properties = new Dictionary<string, object>
                        {
                            ["id"] = Text("char_01, char_02, … Stable for the whole book."),
                            ["name"] = Text("The character's name as the story says it."),
                            ["visualDescription"] = Text(
                                "ENGLISH ONLY. One short, concrete sentence an illustrator can draw "
                                + "from: species or kind, colour, size, one distinguishing feature. "
                                + "No personality, no backstory.")
                        }
                    }
                },
                ["objects"] = new
                {
                    type = "array",
                    description =
                        "Only as many recurring story objects as the story actually needs — "
                        + "none is a valid answer.",
                    items = new
                    {
                        type = "object",
                        additionalProperties = false,
                        required = new[] { "id", "name", "visualDescription" },
                        properties = new Dictionary<string, object>
                        {
                            ["id"] = Text("obj_01, obj_02, … Stable for the whole book."),
                            ["name"] = Text("The object's name as the story says it."),
                            ["visualDescription"] = Text(
                                "ENGLISH ONLY. One short, concrete sentence an illustrator can draw "
                                + "from: shape, material, colour, size. No personality.")
                        }
                    }
                },
                ["spreads"] = new
                {
                    type = "array",
                    description =
                        $"Exactly {spreadCount} spreads, numbered 1 to {spreadCount}, following the "
                        + "narrative rhythm: enter, discovery, action, complication, journey, reveal, "
                        + "emotional resolution, and an ending that satisfies while hinting that "
                        + "another adventure could follow.",
                    items = SpreadSchema()
                },
                ["characterLock"] = new
                {
                    type = "string",
                    description =
                        "ENGLISH ONLY. The child's unchanging appearance only — face, hair, eyes, "
                        + "skin, build and the clothing worn in every scene. Not the supporting "
                        + "cast: each of those carries its own visualDescription above."
                },
                ["worldLock"] = new
                {
                    type = "string",
                    description =
                        "ENGLISH ONLY. Two or three sentences fixing the constant look of this "
                        + "book's world: palette, quality of light, terrain or architecture, and "
                        + "one recurring landmark. No characters, no story events, no camera — "
                        + "this is repeated word for word into every illustration, so everything "
                        + "in it must be true of every spread."
                },
                ["cover"] = IllustrationSchema(
                    "The cover scene: the child as the hero of this world, with calm space where a "
                    + "title will be placed. Typography is never drawn.")
            }
        };

        return JsonSerializer.SerializeToElement(schema, StoryJson.Options);
    }

    private static object SpreadSchema() => new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "number", "title", "caption", "text", "textEn", "characters", "objects", "illustration" },
        properties = new Dictionary<string, object>
        {
            ["number"] = new { type = "integer", description = "1-based spread number." },
            // The handoff is explicit that spreads carry no titles. Asked for and required anyway,
            // as empty strings: the record they land in still needs them, and a model told to omit
            // a required field returns nothing at all.
            ["title"] = Text("Always an empty string. This book prints no page titles."),
            ["caption"] = Text("Always an empty string. This book prints no captions."),
            ["text"] = Text(
                "The Georgian story text for this spread. One clear story moment. For ages 2–4 keep "
                + "it very simple and short; for 5–8 richer but still easy to read aloud."),
            ["textEn"] = Text("The same spread in English — an equivalent, not a literal gloss."),
            ["characters"] = new
            {
                type = "array",
                description =
                    "Who appears in this spread: \"child\", plus the id of every cast member present. "
                    + "Nobody who is not visible in the illustration.",
                items = new { type = "string" }
            },
            ["objects"] = new
            {
                type = "array",
                description =
                    "Which recurring objects appear in this spread: the id of every object present. "
                    + "Nothing that is not visible in the illustration.",
                items = new { type = "string" }
            },
            ["illustration"] = IllustrationSchema("The single continuous illustration for this spread.")
        }
    };

    private static object IllustrationSchema(string description) => new
    {
        type = "object",
        description,
        additionalProperties = false,
        required = new[] { "scene", "avoid" },
        properties = new Dictionary<string, object>
        {
            ["scene"] = new
            {
                type = "string",
                description =
                    "ENGLISH ONLY. Only what should be visible in this illustration: the moment, the "
                    + "action, what the characters feel, the place with its objects, weather and time "
                    + "of day. No style, no format, no camera or shot instruction, no photograph "
                    + "instruction, and no permanent character appearance — every one of those is "
                    + "added by our code. Draw only what this spread's own text says is there."
            },
            ["avoid"] = new
            {
                type = "string",
                description =
                    "ENGLISH ONLY. Only what would go wrong in THIS picture. Empty when there is "
                    + "nothing particular."
            }
        }
    };

    private static object Text(string description) => new { type = "string", description };
}
