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
        required = new[] { "title", "outline" },
        properties = new Dictionary<string, object>
        {
            ["title"] = Text("The book's title."),
            ["outline"] = new
            {
                type = "array",
                description = "Five to eight beats, in order.",
                items = new { type = "string" }
            }
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
                "The read-aloud text for this scene: three to five short sentences. It has a page "
                + "to itself, but filling the page is not the goal — a child follows the picture "
                + "and the voice, not the length of the paragraph."),
            ["illustration"] = IllustrationSchema(
                "The picture for this scene, on the facing page.")
        }
    };

    private static object IllustrationSchema(string description) => new
    {
        type = "object",
        description,
        additionalProperties = false,
        required = new[] { "prompt", "negativePrompt" },
        properties = new Dictionary<string, object>
        {
            // The scene used to be broken out into moment, action, emotion, environment, shot and
            // a list of essential details, and then again into the prompt below. Nothing ever read
            // the six — they were planning written down. Multiplied across nine illustrations they
            // were most of what the call had to produce, and output length is what a reader waits
            // on. The planning still has to happen; it just does not have to be typed out. What it
            // must contain is stated here instead.
            ["prompt"] = new
            {
                type = "string",
                description =
                    "ENGLISH ONLY. The finished image prompt, beginning with the characterLock "
                    + "text repeated verbatim. Then, in this order: which moment of the text is "
                    + "shown, the main action, what the characters feel, the place with its "
                    + "objects, weather and time of day, the shot and camera angle, the details "
                    + "the picture needs for the story to read without words, then lighting, "
                    + "style and format. The picture has its own page, so it may fill the frame — "
                    + "no space needs to be reserved for text."
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
