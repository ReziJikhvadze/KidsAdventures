using System.Text;
using System.Text.Json;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story.Prompts;

namespace AdventurePacks.Api.Services.Story.Composite;

/// <summary>
/// The shape the composite pipeline's PLAN BOOK call must answer in —
/// <see cref="BekiBookPlanSchema"/> with the fields the locked MVP decisions forbid removed.
///
/// A third builder rather than a parameter on the second, for the reason the second gives for not
/// being a parameter on the first: the A5 and flow-misho schemas are what every book in production
/// is written against, and "it only changes when the flag is set" is a promise the next edit
/// breaks. Three builders cannot drift into each other.
///
/// Three removals, each one a locked decision:
///
/// - <c>titleEn</c> — Georgian only. There is no English title in this book.
/// - <c>spreads[].textEn</c> — Georgian only. There is no English copy in this book.
/// - <c>characterLock</c> — the child's likeness. It is the paragraph describing a real child's
///   face, hair, eyes, skin and build, written from the uploaded photograph, and §3 puts the
///   child's visual identity entirely in the image stage's hands. Nothing in the composite path
///   reads it: the outfit is locked by the Visual Scenario and the face by the photograph itself.
///
/// <c>worldLock</c> stays. It is the counterpart to the character lock for the *place* — palette,
/// light, terrain, one landmark — and it carries nothing about the child, so nothing in it is
/// derived from a photograph.
///
/// The per-spread <c>illustration</c> briefs stay as well. They are not sent to the image model on
/// this path — the Visual Scenario writes what the image model receives — but they are the story's
/// own account of what each spread shows, and that is context worth having beside the Georgian
/// text when the scenario is planned and when a book is reviewed.
///
/// A note for the code that deserialises this. <see cref="MasterStory"/> still declares
/// <c>CharacterLock</c> as required, because every A5 book in storage has one and the record
/// cannot be relaxed without breaking those. A composite plan has none, so the reader on this path
/// must supply <see cref="string.Empty"/> for it rather than expecting the model to fill a field
/// this schema does not offer.
/// </summary>
/// <summary>
/// What a composite plan must be true of, beyond the shape a schema can state.
///
/// One rule so far, and it exists because the illustration contract has no way to express its
/// opposite. <c>visual_scenario_v2.schema.json</c> requires a non-empty <c>beki_action</c> on every
/// one of the eight spreads, and the composite pipeline composites one approved pose per spread
/// from it — there is no representation anywhere in the contract for a page without Beki. So the
/// pictures carry her on all eight whatever the plan says, and a plan that listed her on five would
/// ship a book whose stored cast list contradicts its own illustrations: an operator reading the
/// record would be told the child is alone on spread four, and the printed spread four has Beki in
/// it.
///
/// Reported as a problem rather than repaired, and reported through the same corrective retry the
/// rest of the plan's faults use. Quietly adding "beki" to three spreads' cast lists would make the
/// record agree with the pictures while leaving the prose written for a child who was alone —
/// which is the same contradiction one layer down, where nothing checks for it.
///
/// Separate from <see cref="BekiPlanValidator"/> on purpose: that validator holds every A5 and
/// flow-misho book in production to Beki on the first spread, the last, and three others, and this
/// path's stricter rule must not become theirs.
/// </summary>
public static class CompositePlanRules
{
    /// <summary>The spread cast id the pipeline and the validator both key on.</summary>
    public const string BekiId = BekiPlanValidator.BekiId;

    /// <summary>
    /// Every reason a composite plan cannot be drawn, in the words the corrective retry is sent.
    /// Empty means the plan is usable.
    /// </summary>
    public static IReadOnlyList<string> Problems(
        MasterStory plan,
        int spreadCount = BookFormat.SpreadCount,
        string? ageBand = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var problems = new List<string>();

        var outline = plan.Concept?.Outline ?? [];
        if (outline.Count != spreadCount)
        {
            problems.Add(
                $"The story outline must contain exactly {spreadCount} ordered narrative beats "
                + $"(beginning, development and ending); it contains {outline.Count}.");
        }

        for (var index = 0; index < outline.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(outline[index]))
            {
                problems.Add($"Story outline beat {index + 1} is empty.");
            }
        }

        AddDuplicateProblems(
            outline.Select((text, index) => (Number: index + 1, Text: text)),
            "Story outline beats", problems);

        AddDuplicateProblems(
            (plan.Spreads ?? []).Select(spread => (spread.Number, spread.Text)),
            "Story spreads", problems);

        var maxWords = ageBand switch
        {
            null => (int?)null,
            "1-2" => 25,
            "3-5" => 35,
            "6+" => 45,
            _ => throw new ArgumentOutOfRangeException(
                nameof(ageBand), ageBand, "Expected one of the locked age bands: 1-2, 3-5, 6+.")
        };

        foreach (var spread in (plan.Spreads ?? []).OrderBy(spread => spread.Number))
        {
            var characters = spread.Characters ?? [];
            if (!characters.Any(id => id.Equals("child", StringComparison.OrdinalIgnoreCase)))
            {
                problems.Add(
                    $"Spread {spread.Number} does not list \"child\" in its characters; the child "
                    + "must remain the visible main hero throughout the eight-spread story.");
            }

            var present = (spread.Characters ?? [])
                .Any(id => id.Equals(BekiId, StringComparison.OrdinalIgnoreCase));

            if (!present)
            {
                problems.Add(
                    $"Spread {spread.Number} does not list \"{BekiId}\" in its characters. This "
                    + $"book's illustrations carry Beki on all {spreadCount} spreads, because the "
                    + "Visual Scenario contract has no way to describe a spread without her, so a "
                    + "spread planned without Beki would be printed with her.");
            }

            if (!string.IsNullOrEmpty(spread.Title) || !string.IsNullOrEmpty(spread.Caption))
            {
                problems.Add(
                    $"Spread {spread.Number} must have empty title and caption fields; the canonical "
                    + "interior prints story copy only.");
            }

            if (maxWords is { } limit && WordCount(spread.Text) > limit)
            {
                problems.Add(
                    $"Spread {spread.Number} has {WordCount(spread.Text)} Georgian words; the maximum "
                    + $"for age band {ageBand} is {limit}, before illustration generation.");
            }
        }

        return problems;
    }

    private static void AddDuplicateProblems(
        IEnumerable<(int Number, string Text)> values,
        string label,
        List<string> problems)
    {
        var duplicates = values
            .Where(value => !string.IsNullOrWhiteSpace(value.Text))
            .GroupBy(value => Normalize(value.Text), StringComparer.Ordinal)
            .Where(group => group.Key.Length > 0 && group.Count() > 1)
            .Select(group => string.Join(", ", group.Select(value => value.Number).Order()))
            .ToList();

        foreach (var numbers in duplicates)
        {
            problems.Add($"{label} {numbers} duplicate the same text.");
        }
    }

    private static string Normalize(string? text) => string.Concat(
        (text ?? string.Empty)
            .Normalize(NormalizationForm.FormC)
            .Where(character => char.IsLetterOrDigit(character)))
        .ToLowerInvariant();

    private static int WordCount(string? text)
    {
        var count = 0;
        var insideWord = false;
        foreach (var character in text ?? string.Empty)
        {
            if (char.IsLetterOrDigit(character))
            {
                if (!insideWord) count++;
                insideWord = true;
            }
            else
            {
                insideWord = false;
            }
        }

        return count;
    }
}

public static class CompositeStorySchema
{
    public const string Version = "composite-story-schema-v1";

    public const string Name = "composite_book_plan";

    public static JsonElement Build(int spreadCount = BookFormat.SpreadCount)
    {
        var schema = new
        {
            type = "object",
            additionalProperties = false,
            // "titleEn" and "characterLock" are absent from this list, and that absence is the
            // point: a required field is the only way to be sure a model returns one, so removing
            // them from `required` and from `properties` is how they stop existing at all.
            required = new[] { "concept", "cast", "objects", "spreads", "worldLock", "cover" },
            properties = new Dictionary<string, object>
            {
                ["concept"] = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[] { "title", "outline" },
                    properties = new Dictionary<string, object>
                    {
                        ["title"] = Text("The book's title, in Georgian. This book has no other title."),
                        ["outline"] = new
                        {
                            type = "array",
                            description = $"The {spreadCount} beats, in order, one short line each.",
                            items = new { type = "string" }
                        }
                    }
                },
                ["cast"] = new
                {
                    type = "array",
                    description =
                        "Only as many recurring supporting characters as the story actually needs — "
                        + "none is a valid answer. The child is the hero and is never listed here, "
                        + "and neither is Beki.",
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
                    "The cover scene: the child in one inviting moment from this world. Beki is "
                    + "added later from approved artwork and is never drawn here. Typography is "
                    + "never drawn.")
            }
        };

        return JsonSerializer.SerializeToElement(schema, StoryJson.Options);
    }

    private static object SpreadSchema() => new
    {
        type = "object",
        additionalProperties = false,
        // "textEn" is gone from this list. The rest is the Beki plan schema's own set: `number`,
        // `title` and `caption` remain because the record they land in still requires them.
        required = new[] { "number", "title", "caption", "text", "characters", "objects", "illustration" },
        properties = new Dictionary<string, object>
        {
            ["number"] = new { type = "integer", description = "1-based spread number." },
            // The handoff is explicit that spreads carry no titles. Asked for and required anyway,
            // as empty strings: the record they land in still needs them, and a model told to omit
            // a required field returns nothing at all.
            ["title"] = Text("Always an empty string. This book prints no page titles."),
            ["caption"] = Text("Always an empty string. This book prints no captions."),
            ["text"] = Text(
                "The Georgian story text for this spread. One clear story moment. Keep it within "
                + "the word budget the instructions give for this book's age band."),
            ["characters"] = new
            {
                type = "array",
                description =
                    "Who appears in this spread: \"child\", plus \"beki\" when Beki is present, "
                    + "plus the id of every cast member present. Nobody who is not visible in the "
                    + "illustration.",
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
                    + "instruction, no text side or fold, and nothing about what the child or Beki "
                    + "look like — every one of those is added by our code or fixed by approved "
                    + "artwork. Draw only what this spread's own text says is there."
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
