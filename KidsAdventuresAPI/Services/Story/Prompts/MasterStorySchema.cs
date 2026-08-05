using System.Text.Json;
using AdventurePacks.Api.Domain.Story;

namespace AdventurePacks.Api.Services.Story.Prompts;

/// <summary>
/// The shape the master call must answer in.
///
/// The operator's original prompt asked for sections A to I as readable prose, which is right
/// for a person and wrong for a program: a heading can be renamed, reordered or quietly dropped,
/// and nothing notices until a book is missing its illustrations. The content is unchanged — the
/// same concept, story and prompts — but the container is a schema the provider enforces
/// rather than a layout the model is asked to remember.
/// </summary>
public static class MasterStorySchema
{
    public const string Name = "master_story";

    public static JsonElement Build(int spreadCount = BookFormat.SpreadCount)
    {
        var schema = new
        {
            type = "object",
            additionalProperties = false,
            required = new[] { "concept", "spreads", "characterLock", "cover" },
            properties = new Dictionary<string, object>
            {
                ["concept"] = ConceptSchema(),
                ["spreads"] = new
                {
                    type = "array",
                    description =
                        $"Exactly {spreadCount} spreads, numbered 1 to {spreadCount}, in order. "
                        + "Each is one scene: a picture on one page and its words on the facing page.",
                    items = SpreadSchema()
                },
                ["characterLock"] = new
                {
                    type = "string",
                    description =
                        "ENGLISH ONLY. One paragraph fixing every recurring character's appearance: "
                        + "face, hair, eyes, skin, build, and the exact clothing worn in every scene. "
                        + "This text must appear verbatim at the start of every illustration prompt, "
                        + "including the cover."
                },
                ["cover"] = IllustrationSchema(
                    "The cover illustration. It carries no story text, so compose it as a portrait "
                    + "of the hero in this world, with calm space where a title will be placed.")
            }
        };

        return JsonSerializer.SerializeToElement(schema, StoryJson.Options);
    }

    private static object ConceptSchema() => new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "title", "logline", "learningGoal", "heroDescription", "outline", "ageRationale" },
        properties = new Dictionary<string, object>
        {
            ["title"] = Text("The book's title."),
            ["logline"] = Text("The whole story in one sentence."),
            ["learningGoal"] = Text("The skill this book is built around."),
            ["heroDescription"] = Text("Who the hero is, briefly."),
            ["outline"] = new
            {
                type = "array",
                description = "Five to eight beats, in order.",
                items = new { type = "string" }
            },
            ["ageRationale"] = Text("Why this suits the child's age. Written for a parent to read.")
        }
    };

    private static object SpreadSchema() => new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "number", "title", "caption", "text", "illustration" },
        properties = new Dictionary<string, object>
        {
            ["number"] = new { type = "integer", description = "1-based scene number." },
            ["title"] = Text("Short heading for this scene."),
            ["caption"] = Text("The short line beside the picture. Two to five words."),
            ["text"] = Text(
                "The read-aloud text for this scene. It has a whole page to itself, so it can be "
                + "a little longer than a caption-sized line, while still suiting the child's age."),
            ["illustration"] = IllustrationSchema(
                "The picture for this scene, on the facing page.")
        }
    };

    private static object IllustrationSchema(string description) => new
    {
        type = "object",
        description,
        additionalProperties = false,
        required = new[]
        {
            "moment", "action", "emotion", "environment",
            "shot", "essentialDetails", "prompt", "negativePrompt"
        },
        properties = new Dictionary<string, object>
        {
            ["moment"] = Text("Which moment of the text this picture shows."),
            ["action"] = Text("The main action."),
            ["emotion"] = Text("What the characters are feeling."),
            ["environment"] = Text("Place, objects, weather, time of day."),
            ["shot"] = Text("Full body, medium shot or close-up, plus the camera angle."),
            ["essentialDetails"] = new
            {
                type = "array",
                description = "What the picture must contain for the story to read without words.",
                items = new { type = "string" }
            },
            ["prompt"] = new
            {
                type = "string",
                description =
                    "ENGLISH ONLY. The finished image prompt, beginning with the characterLock "
                    + "text repeated verbatim, then scene, emotion, environment, composition, "
                    + "lighting, style and format. The picture has its own page, so it may fill "
                    + "the frame — no space needs to be reserved for text."
            },
            ["negativePrompt"] = new
            {
                type = "string",
                description = "ENGLISH ONLY. What must not appear, identity drift first."
            }
        }
    };

    private static object Text(string description) => new { type = "string", description };

    /// <summary>
    /// The negative prompt every illustration falls back to, so a page is never sent without
    /// one. Identity failures lead because they are the ones a parent notices immediately.
    /// </summary>
    public const string DefaultNegativePrompt =
        "changed identity, generic face, excessive facial stylization, inaccurate facial "
        + "proportions, different eye shape, different nose, different hairstyle, different skin "
        + "tone, incorrect age, altered body type, unrealistic body proportions, changed clothing, "
        + "extra accessories, asymmetrical eyes, distorted face, malformed hands, extra fingers, "
        + "missing fingers, duplicate person, blurry face, low detail, frightening expression, "
        + "text, captions, watermark, logo";
}
