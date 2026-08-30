using System.Text.Json;
using AdventurePacks.Api.Domain.Story;
using Json.Schema;

namespace AdventurePacks.Api.Services.Story.Composite;

/// <summary>
/// The vocabulary a rejected Visual Scenario is rejected in.
///
/// One code per rule rather than one string per message. The retry is allowed exactly once, and it
/// is sent the validator's error list: a code the model can be told about plainly ("a scene named
/// Beki") retries usefully, where a JSON Pointer into a schema does not. The codes are also what a
/// log can be counted by — which of these fires most often is the question that decides whether
/// the prompt or the schema needs the next edit.
/// </summary>
public static class VisualScenarioProblemCodes
{
    /// <summary>The response was not JSON at all.</summary>
    public const string MalformedJson = "MALFORMED_JSON";

    /// <summary>The supplied Draft 2020-12 schema rejected it. The detail names the location.</summary>
    public const string SchemaViolation = "SCHEMA_VIOLATION";

    /// <summary>A string the contract requires arrived empty or missing.</summary>
    public const string EmptyRequiredString = "EMPTY_REQUIRED_STRING";

    /// <summary>The spreads are not pages 1 to 8, each once, in order.</summary>
    public const string SpreadPagesInvalid = "SPREAD_PAGES_INVALID";

    /// <summary>More than three recurring elements: a book description, not a lock.</summary>
    public const string TooManyRecurringElements = "TOO_MANY_RECURRING_ELEMENTS";

    /// <summary>A scene bound for the image model names Beki. This is the fault the whole split exists to prevent.</summary>
    public const string BekiInChildWorldScene = "BEKI_IN_CHILD_WORLD_SCENE";

    /// <summary>The back cover names Beki, who is composited onto it later from approved artwork.</summary>
    public const string BekiInBackEnvironment = "BEKI_IN_BACK_ENVIRONMENT";

    /// <summary>A child/world scene never says whose scene it is.</summary>
    public const string ChildMissingFromScene = "CHILD_MISSING_FROM_SCENE";

    /// <summary>The back cover contains the child, which the contract reserves for the front.</summary>
    public const string ChildInBackEnvironment = "CHILD_IN_BACK_ENVIRONMENT";

    /// <summary>A Beki action does not name Beki, so the pose selector has nothing to match on.</summary>
    public const string BekiMissingFromAction = "BEKI_MISSING_FROM_BEKI_ACTION";

    /// <summary>
    /// Too many Beki actions are phrased in verbs the approved pose table cannot read, so the book
    /// would be composited from the neutral hover on most of its pages.
    ///
    /// Not a rule <see cref="VisualScenarioValidator"/> can apply: it is about the pose registry,
    /// which is a different document with its own revisions, and it is a judgement about the book as
    /// a whole rather than about any one spread — three fallbacks in eight pages is a scenario worth
    /// re-asking for, and the same three sentences in isolation are each perfectly valid. So it is
    /// raised by <see cref="CompositePoseVocabulary"/> after validation passes, and travels in this
    /// vocabulary because the retry it spends is the scenario's one retry.
    /// </summary>
    public const string PoseVocabularyMiss = "POSE_VOCABULARY_MISS";
}

/// <summary>One reason, in a code the retry can be told and a detail a human can read.</summary>
public sealed record VisualScenarioProblem(string Code, string Detail)
{
    public override string ToString() => $"{Code}: {Detail}";
}

/// <summary>
/// What the validator concluded, and — when it parsed — the scenario itself.
/// </summary>
public sealed record VisualScenarioValidationResult
{
    public required bool IsValid { get; init; }

    /// <summary>
    /// Present whenever the JSON parsed, valid or not. An invalid scenario is still worth having
    /// in hand: it is what the retry's error list is written about.
    /// </summary>
    public VisualScenarioV2? Scenario { get; init; }

    public IReadOnlyList<VisualScenarioProblem> Problems { get; init; } = [];

    public bool Has(string code) => Problems.Any(problem => problem.Code == code);

    /// <summary>The short error list the one permitted retry is sent.</summary>
    public string Summary => string.Join("; ", Problems);
}

/// <summary>
/// Two layers, both of which always run.
///
/// The first is <c>visual_scenario_v2.schema.json</c>, evaluated verbatim as the file the
/// illustration supplier ships. It is not re-expressed in C#: it uses <c>$ref</c>,
/// <c>prefixItems</c>, <c>allOf</c> and <c>additionalProperties: false</c>, and a hand-written
/// equivalent would be a second copy of a document somebody else revises — the failure mode being
/// a scenario this code accepts and the printing side rejects.
///
/// The second is the contract MD's own list, which no JSON Schema can express: a scene must
/// mention the child, must not mention Beki, and the back cover must contain neither. These are
/// the rules the pipeline actually turns on. The approved manual test only produced a usable image
/// once Beki was absent from the generated scene, so "no Beki here" is not a style preference —
/// it is the difference between compositing one approved Beki and printing two of him.
///
/// Both layers run even when the first has already failed. A model that returns seven spreads
/// breaks the schema's <c>minItems</c> and the page sequence at the same time, and reporting only
/// the first would send the retry half the story of what it did wrong.
/// </summary>
public static class VisualScenarioValidator
{
    /// <summary>The supplier's file name, loaded from the published assets rather than embedded.</summary>
    public const string SchemaFileName = "visual_scenario_v2.schema.json";

    /// <summary>
    /// Every spelling of the guide's name that has to be caught.
    ///
    /// Latin because the scenario is written in English, Georgian because the story it was written
    /// from is Georgian and a model quoting its source drops „ბეკი“ into an English sentence more
    /// often than one would think. Georgian is caseless, so an ordinal case-insensitive comparison
    /// covers every grammatical form: ბეკიმ, ბეკის, ბეკისთან all begin with the stem.
    /// </summary>
    private static readonly string[] BekiNames = ["beki", "ბეკი"];

    private static readonly Lazy<JsonSchema> Schema = new(LoadSchema);

    /// <summary>
    /// Validates one model response, from the raw text upward.
    /// </summary>
    public static VisualScenarioValidationResult Validate(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Invalid([new VisualScenarioProblem(
                VisualScenarioProblemCodes.MalformedJson, "The model returned no text.")]);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return Invalid([new VisualScenarioProblem(
                VisualScenarioProblemCodes.MalformedJson, ex.Message)]);
        }

        using (document)
        {
            var problems = new List<VisualScenarioProblem>();
            problems.AddRange(SchemaProblems(document.RootElement));

            VisualScenarioV2? scenario = null;
            try
            {
                scenario = document.RootElement.Deserialize<VisualScenarioV2>(CompositeJson.Options);
            }
            catch (JsonException ex)
            {
                // The schema will normally have said this first and better; this catch exists so a
                // shape the schema happens to permit but the models cannot hold (a string where an
                // array belongs, say) is still a validation failure and not a 500.
                problems.Add(new VisualScenarioProblem(
                    VisualScenarioProblemCodes.MalformedJson,
                    $"The response did not fit the Visual Scenario shape: {ex.Message}"));
            }

            if (scenario is not null)
            {
                problems.AddRange(SemanticProblems(scenario));
            }

            return new VisualScenarioValidationResult
            {
                IsValid = problems.Count == 0,
                Scenario = scenario,
                Problems = problems
            };
        }
    }

    /// <summary>
    /// The contract MD's rules, applied to an already-parsed scenario.
    ///
    /// Public because the same checks have to hold for a scenario read back out of storage on a
    /// resumed job, where there is no model response to re-parse.
    /// </summary>
    public static IReadOnlyList<VisualScenarioProblem> SemanticProblems(VisualScenarioV2 scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        var problems = new List<VisualScenarioProblem>();

        CheckVisualLock(scenario.VisualLock, problems);
        CheckCover(scenario.Cover, problems);
        CheckSpreads(scenario.Spreads, problems);

        return problems;
    }

    private static void CheckVisualLock(VisualLock? visualLock, List<VisualScenarioProblem> problems)
    {
        if (visualLock is null)
        {
            problems.Add(new VisualScenarioProblem(
                VisualScenarioProblemCodes.EmptyRequiredString, "visual_lock is missing."));
            return;
        }

        RequireText(visualLock.ChildOutfit, "visual_lock.child_outfit", problems);

        var elements = visualLock.RecurringElements ?? [];
        if (elements.Count > 3)
        {
            problems.Add(new VisualScenarioProblem(
                VisualScenarioProblemCodes.TooManyRecurringElements,
                $"visual_lock.recurring_elements holds {elements.Count} entries; the contract allows at most three."));
        }

        for (var index = 0; index < elements.Count; index++)
        {
            RequireText(elements[index], $"visual_lock.recurring_elements[{index}]", problems);
        }
    }

    private static void CheckCover(VisualScenarioCover? cover, List<VisualScenarioProblem> problems)
    {
        if (cover is null)
        {
            problems.Add(new VisualScenarioProblem(
                VisualScenarioProblemCodes.EmptyRequiredString, "cover is missing."));
            return;
        }

        CheckChildWorldScene(cover.FrontChildWorldScene, "cover.front_child_world_scene", problems);
        CheckBekiAction(cover.BekiAction, "cover.beki_action", problems);

        RequireText(cover.BackEnvironment, "cover.back_environment", problems);

        // The back cover is the world with nobody in it. Beki is composited onto the wrap from
        // approved artwork, and the child belongs to the front panel; either one drawn here would
        // be a second copy of a character that also appears elsewhere on the same printed sheet.
        if (MentionsBeki(cover.BackEnvironment))
        {
            problems.Add(new VisualScenarioProblem(
                VisualScenarioProblemCodes.BekiInBackEnvironment,
                "cover.back_environment names Beki; the back cover carries no characters."));
        }

        if (MentionsChild(cover.BackEnvironment))
        {
            problems.Add(new VisualScenarioProblem(
                VisualScenarioProblemCodes.ChildInBackEnvironment,
                "cover.back_environment names the child; the back cover carries no characters."));
        }
    }

    private static void CheckSpreads(
        IReadOnlyList<VisualScenarioSpread>? spreads,
        List<VisualScenarioProblem> problems)
    {
        var pages = spreads ?? [];

        if (pages.Count != BookFormat.SpreadCount)
        {
            problems.Add(new VisualScenarioProblem(
                VisualScenarioProblemCodes.SpreadPagesInvalid,
                $"spreads holds {pages.Count} entries; the book format needs exactly {BookFormat.SpreadCount}."));
        }

        for (var index = 0; index < pages.Count; index++)
        {
            var spread = pages[index];
            var expected = index + 1;

            // Ordered, not merely present. The page number is what binds a scene to its story text,
            // its text side and its Beki pose, so a scenario whose pages arrive shuffled would put
            // spread 5's picture beside spread 3's words — and every later stage would agree.
            if (spread.Page != expected)
            {
                problems.Add(new VisualScenarioProblem(
                    VisualScenarioProblemCodes.SpreadPagesInvalid,
                    $"spreads[{index}] is page {spread.Page}; pages must run 1..{BookFormat.SpreadCount} in order, each exactly once."));
            }

            CheckChildWorldScene(spread.ChildWorldScene, $"spreads[{index}].child_world_scene", problems);
            CheckBekiAction(spread.BekiAction, $"spreads[{index}].beki_action", problems);
        }
    }

    private static void CheckChildWorldScene(string? scene, string location, List<VisualScenarioProblem> problems)
    {
        if (!RequireText(scene, location, problems))
        {
            return;
        }

        // This string is sent to the image model unchanged. Everything the pipeline promises about
        // Beki — one approved PNG, four rounded digits, never redrawn — holds only while no image
        // model is ever asked to draw him.
        if (MentionsBeki(scene))
        {
            problems.Add(new VisualScenarioProblem(
                VisualScenarioProblemCodes.BekiInChildWorldScene,
                $"{location} names Beki; a child/world scene goes straight to the image model and must not."));
        }

        if (!MentionsTheChild(scene))
        {
            problems.Add(new VisualScenarioProblem(
                VisualScenarioProblemCodes.ChildMissingFromScene,
                $"{location} never says \"the child\"; the personalized protagonist must be named in the scene."));
        }
    }

    private static void CheckBekiAction(string? action, string location, List<VisualScenarioProblem> problems)
    {
        if (!RequireText(action, location, problems))
        {
            return;
        }

        // The opposite rule to the one above, for the opposite reason: this string is never sent to
        // an image model, it is matched against the pose registry's keywords. An action that does
        // not name Beki is an action about somebody else.
        if (!MentionsBeki(action))
        {
            problems.Add(new VisualScenarioProblem(
                VisualScenarioProblemCodes.BekiMissingFromAction,
                $"{location} does not name Beki; the pose selector reads this line and nothing else."));
        }
    }

    private static bool RequireText(string? value, string location, List<VisualScenarioProblem> problems)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        problems.Add(new VisualScenarioProblem(
            VisualScenarioProblemCodes.EmptyRequiredString, $"{location} is missing or empty."));
        return false;
    }

    private static bool MentionsBeki(string? text) =>
        text is not null
        && BekiNames.Any(name => text.Contains(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether a scene names the personalized protagonist the way the contract requires.
    ///
    /// The exact phrase, because the contract asks for the exact phrase. "A girl reaches for the
    /// branch" is a scene about somebody; "the child reaches for the branch" is a scene about the
    /// child whose photograph is attached to the request, and the image model treats the two
    /// differently.
    /// </summary>
    private static bool MentionsTheChild(string? text) =>
        text is not null && text.Contains("the child", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a line mentions a child at all — the looser question, asked only of the back cover,
    /// which must be the world with nobody in it. "Children play beyond the ridge" would be as
    /// wrong there as naming the protagonist outright.
    /// </summary>
    private static bool MentionsChild(string? text) =>
        text is not null && text.Contains("child", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<VisualScenarioProblem> SchemaProblems(JsonElement instance)
    {
        var results = Schema.Value.Evaluate(instance, new EvaluationOptions
        {
            // The list form gives one entry per failing keyword with its instance location, which
            // is what makes a schema failure readable in a log and repeatable in a retry. The flag
            // form would only say "no".
            OutputFormat = OutputFormat.List
        });

        if (results.IsValid)
        {
            return [];
        }

        var problems = new List<VisualScenarioProblem>();

        // Details is null rather than empty when nothing below the root reported, so the fallback
        // at the bottom of this method is what a schema failure with no detailed node produces.
        var details = results.Details ?? [];

        foreach (var detail in details.Where(d => !d.IsValid && d.Errors is { Count: > 0 }))
        {
            foreach (var error in detail.Errors!)
            {
                var location = detail.InstanceLocation.ToString();
                problems.Add(new VisualScenarioProblem(
                    VisualScenarioProblemCodes.SchemaViolation,
                    $"{(location.Length == 0 ? "(root)" : location)} failed '{error.Key}': {error.Value}"));
            }
        }

        // A schema can fail without any node carrying a message — an unevaluated branch, say. The
        // book still stops, and it stops saying so rather than saying nothing.
        if (problems.Count == 0)
        {
            problems.Add(new VisualScenarioProblem(
                VisualScenarioProblemCodes.SchemaViolation,
                $"The response does not satisfy {SchemaFileName}."));
        }

        return problems;
    }

    private static VisualScenarioValidationResult Invalid(IReadOnlyList<VisualScenarioProblem> problems) =>
        new() { IsValid = false, Problems = problems };

    private static JsonSchema LoadSchema()
    {
        var path = CompositeAssets.ContractPath(SchemaFileName);

        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"The Visual Scenario schema '{path}' is not in the published output. The composite "
                + "pipeline validates against the supplied file and never against a copy in code.");
        }

        return JsonSchema.FromText(File.ReadAllText(path));
    }
}
