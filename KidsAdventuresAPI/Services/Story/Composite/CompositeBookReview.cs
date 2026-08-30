using System.Text.Json;

namespace AdventurePacks.Api.Services.Story.Composite;

/// <summary>
/// One page whose rendered shot the reviewer thought contradicted the shot it was asked for.
/// Advisory: no page ever failed for this, and none ever will until there is evidence to price it.
/// </summary>
public sealed record CompositeShotAdvisory(int Page, string ShotInstruction, string ReviewerNote);

/// <summary>
/// Everything a finished composite book is worth telling a human about but that did not fail it.
///
/// Three findings live here and they have one shape in common: each is a book-level quality signal
/// that no single page could be refused for. A neutral hover on spread 4 is an approved pose; a
/// medium shot where a wide one was asked for is a usable picture; ფუნღუროში is a real string of
/// Georgian letters. Only when they are counted across a whole book do they become the things the
/// supplier's audit and the owner's proof-reading actually found — a guide who does the same thing
/// on six pages, eight spreads with the same camera, a misspelling in print.
///
/// It is carried out of the pipeline rather than written by it, exactly as the artifacts are: this
/// class has no storage dependency on purpose, and the fulfilment job owns every decision about
/// where a pack's files live. What the pipeline does do is log it, whole and in one line, because a
/// signal nobody can see is not a signal.
/// </summary>
public sealed record CompositeBookReview
{
    /// <summary>The registry that chose the poses, and which revision of its keyword table.</summary>
    public required string PoseRegistryVersion { get; init; }

    public required string PoseKeywordRevision { get; init; }

    /// <summary>The scenario prompt version in force — the one that carries the verb steering.</summary>
    public required string ScenarioPromptVersion { get; init; }

    /// <summary>
    /// How many of the eight spreads got the neutral hover because no keyword matched.
    ///
    /// The number the whole R13 work exists to drive down: the book that started it selected the
    /// fallback on six of eight.
    /// </summary>
    public required int PoseSelectionFallbacks { get; init; }

    /// <summary>Which spreads they were, so a reader can go and look at the sentences.</summary>
    public IReadOnlyList<int> PoseFallbackPages { get; init; } = [];

    /// <summary>How many distinct poses the finished book actually shows across its spreads.</summary>
    public required int DistinctPoses { get; init; }

    /// <summary>
    /// True when the scenario's one permitted retry was spent because the first plan exceeded the
    /// fallback budget — the R13c corrective retry, which is never taken twice.
    /// </summary>
    public bool PoseVocabularyRetrySpent { get; init; }

    /// <summary>
    /// True when the book was drawn anyway with more fallbacks than the budget allows: the retry had
    /// already been spent, and a repetitive Beki is not a reason to discard a paid plan.
    /// </summary>
    public bool PoseFallbackBudgetExceeded { get; init; }

    /// <summary>The known-bad Georgian patterns found in the copy this book will print.</summary>
    public IReadOnlyList<GeorgianTextFlag> GeorgianFlags { get; init; } = [];

    /// <summary>Which checklist revision read the copy, so a flagless book means something.</summary>
    public required string GeorgianChecklistVersion { get; init; }

    /// <summary>
    /// Rules that did not run, and why — an invalid pattern, a rule missing a field, a checklist
    /// that could not be read at all.
    ///
    /// On the record because otherwise a half-loaded checklist and a clean book look identical: both
    /// report no flags. A book checked by three of four rules is a book somebody may still want to
    /// read, and the operator fixing the asset needs to know which packs went past while it was
    /// broken.
    /// </summary>
    public IReadOnlyList<string> GeorgianChecklistProblems { get; init; } = [];

    /// <summary>Pages the reviewer thought were shot differently from the way they were asked for.</summary>
    public IReadOnlyList<CompositeShotAdvisory> ShotAdvisories { get; init; } = [];

    /// <summary>True when there is anything here a person should actually read.</summary>
    public bool NeedsHumanReading =>
        GeorgianFlags.Count > 0 || PoseFallbackBudgetExceeded || ShotAdvisories.Count > 0
        || GeorgianChecklistProblems.Count > 0;

    /// <summary>
    /// This attempt's review, completed with what an earlier attempt recorded about the pages this
    /// one adopted.
    ///
    /// It exists because a resumed run rebuilds the review from what it can see, and what it can see
    /// is not the whole book. Two fields are per-attempt facts rather than per-book ones:
    ///
    /// A shot advisory belongs to a page's review, and an adopted page was reviewed by the attempt
    /// that drew it. A resume that adopted seven pages and redrew one would otherwise write a review
    /// saying the book has no shot trouble, and the fulfilment job would then overwrite the stored
    /// document with that — turning an earlier attempt's observations into silence, on a book nobody
    /// looked at again.
    ///
    /// And the pose-vocabulary retry is a fact about how this book was planned, not about who
    /// planned it: a resumed run adopts the scenario and never re-asks, so it cannot know the retry
    /// was spent unless the earlier attempt tells it. Either attempt having spent it means the book
    /// cost one.
    ///
    /// Everything else is deliberately recomputed and not merged. The pose counts come from
    /// replaying the registry over the whole scenario, and the Georgian flags from reading the whole
    /// plan — both describe the finished book completely, and both are better read fresh, because
    /// the keyword table or the check-list may have moved since.
    /// </summary>
    /// <param name="stored">The earlier attempt's review, or null when there is none to read.</param>
    /// <param name="adoptedPages">
    /// The spreads this attempt adopted rather than drew. Only their advisories are taken from
    /// <paramref name="stored"/>: a page redrawn this run was reviewed this run, and a stale note
    /// about the picture it replaced would be a note about an image nobody will ever see.
    /// </param>
    public CompositeBookReview MergedWith(CompositeBookReview? stored, IReadOnlySet<int> adoptedPages)
    {
        if (stored is null)
        {
            return this;
        }

        var mine = ShotAdvisories.Select(advisory => advisory.Page).ToHashSet();

        var inherited = stored.ShotAdvisories
            .Where(advisory => adoptedPages.Contains(advisory.Page) && !mine.Contains(advisory.Page))
            .ToList();

        return this with
        {
            ShotAdvisories = inherited.Count == 0
                ? ShotAdvisories
                : ShotAdvisories.Concat(inherited).OrderBy(advisory => advisory.Page).ToList(),

            PoseVocabularyRetrySpent = PoseVocabularyRetrySpent || stored.PoseVocabularyRetrySpent,
        };
    }

    /// <summary>
    /// Reads a stored review document back, or null when there is not one to read.
    ///
    /// Forgiving by construction, and it has to be: this is called on a resumed job with a document
    /// an earlier deployment wrote, and the only thing worse than resuming without an earlier
    /// attempt's shot notes is failing a paid book because a JSON file from last week has a field
    /// this build does not recognise. Anything unreadable is simply nothing to merge.
    /// </summary>
    public static CompositeBookReview? TryRead(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return new CompositeBookReview
            {
                PoseRegistryVersion = Text(root, "pose_registry_version"),
                PoseKeywordRevision = Text(root, "pose_keyword_revision"),
                ScenarioPromptVersion = Text(root, "scenario_prompt_version"),
                PoseSelectionFallbacks = Number(root, "pose_selection_fallback"),
                PoseFallbackPages = Numbers(root, "pose_fallback_pages"),
                DistinctPoses = Number(root, "distinct_poses"),
                PoseVocabularyRetrySpent = Flag(root, "pose_vocabulary_retry_spent"),
                PoseFallbackBudgetExceeded = Flag(root, "pose_fallback_budget_exceeded"),
                GeorgianChecklistVersion = Text(root, "georgian_checklist_version"),
                GeorgianFlags = ReadFlags(root),
                ShotAdvisories = ReadAdvisories(root),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<GeorgianTextFlag> ReadFlags(JsonElement root) =>
        Items(root, "georgian_flags")
            .Select(item => new GeorgianTextFlag(
                Text(item, "rule_id"), Text(item, "location"), Text(item, "found"),
                Text(item, "expected"), Text(item, "excerpt")))
            .ToList();

    private static IReadOnlyList<CompositeShotAdvisory> ReadAdvisories(JsonElement root) =>
        Items(root, "shot_advisories")
            .Select(item => new CompositeShotAdvisory(
                Number(item, "page"), Text(item, "shot_instruction"), Text(item, "reviewer_note")))
            .Where(advisory => advisory.Page > 0 && advisory.ReviewerNote.Length > 0)
            .ToList();

    private static IReadOnlyList<JsonElement> Items(JsonElement root, string property) =>
        root.TryGetProperty(property, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).ToList()
            : [];

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int Number(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number)
            ? number
            : 0;

    private static bool Flag(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static IReadOnlyList<int> Numbers(JsonElement element, string property) =>
        element.TryGetProperty(property, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Number)
                .Select(item => item.GetInt32())
                .ToList()
            : [];

    /// <summary>
    /// The one-line summary a log is read for, in the same <c>key=value</c> idiom as the pipeline's
    /// other observability lines.
    /// </summary>
    public string Summary =>
        $"pose_selection_fallback={PoseSelectionFallbacks} distinct_poses={DistinctPoses} "
        + $"pose_vocabulary_retry={PoseVocabularyRetrySpent.ToString().ToLowerInvariant()} "
        + $"georgian_flags={GeorgianFlags.Count} shot_advisories={ShotAdvisories.Count} "
        + $"georgian_checklist_problems={GeorgianChecklistProblems.Count}";

    /// <summary>
    /// The countable half of this record, for a measurement document that is read in aggregate.
    ///
    /// What it deliberately leaves behind is every piece of the book's own prose: a Georgian flag's
    /// <see cref="GeorgianTextFlag.Found"/> and <see cref="GeorgianTextFlag.Excerpt"/> are windows
    /// into the story text, and the story text is where the child's name lives — the
    /// hyphenated-suffix rule exists precisely because the defect it finds *is* the child's name
    /// with a suffix stuck on it. Telemetry is the file an operator opens to compare books, and it
    /// has no business carrying one child's name into that comparison. The rule id and the page are
    /// what a person needs to know which book to look at; the words are in the review artifact, in
    /// the pack's own private folder, beside the story they came from.
    ///
    /// Pose counts, shot pages and versions are safe by construction — none of them is derived from
    /// the child, the photograph or the copy.
    /// </summary>
    /// <param name="reviewUrl">Where the full document was stored, so the numbers lead to the prose.</param>
    public object ToTelemetry(string? reviewUrl) => new
    {
        reviewUrl,
        poseRegistryVersion = PoseRegistryVersion,
        poseKeywordRevision = PoseKeywordRevision,
        scenarioPromptVersion = ScenarioPromptVersion,
        poseSelectionFallback = PoseSelectionFallbacks,
        poseFallbackPages = PoseFallbackPages,
        distinctPoses = DistinctPoses,
        poseVocabularyRetrySpent = PoseVocabularyRetrySpent,
        poseFallbackBudgetExceeded = PoseFallbackBudgetExceeded,
        georgianChecklistVersion = GeorgianChecklistVersion,
        georgianChecklistProblems = GeorgianChecklistProblems,
        georgianFlagCount = GeorgianFlags.Count,

        // Rule and location only. Never Found, never Excerpt — see above.
        georgianFlags = GeorgianFlags
            .Select(flag => new { flag.RuleId, flag.Location })
            .ToList(),

        shotAdvisoryCount = ShotAdvisories.Count,
        shotAdvisoryPages = ShotAdvisories.Select(advisory => advisory.Page).ToList(),
        needsHumanReading = NeedsHumanReading,
    };

    /// <summary>
    /// The document, for storage beside the book's other receipts.
    ///
    /// snake_case and indented, like the failure evidence and the composition manifests, because it
    /// is written for a person opening a file in a blob browser rather than for a parser.
    /// </summary>
    public string ToJson() => JsonSerializer.Serialize(
        new
        {
            pose_registry_version = PoseRegistryVersion,
            pose_keyword_revision = PoseKeywordRevision,
            scenario_prompt_version = ScenarioPromptVersion,
            pose_selection_fallback = PoseSelectionFallbacks,
            pose_fallback_pages = PoseFallbackPages,
            distinct_poses = DistinctPoses,
            pose_vocabulary_retry_spent = PoseVocabularyRetrySpent,
            pose_fallback_budget_exceeded = PoseFallbackBudgetExceeded,
            georgian_checklist_version = GeorgianChecklistVersion,
            georgian_checklist_problems = GeorgianChecklistProblems,
            georgian_flags = GeorgianFlags.Select(flag => new
            {
                rule_id = flag.RuleId,
                location = flag.Location,
                found = flag.Found,
                expected = flag.Expected,
                excerpt = flag.Excerpt,
            }).ToList(),
            shot_advisories = ShotAdvisories.Select(advisory => new
            {
                page = advisory.Page,
                shot_instruction = advisory.ShotInstruction,
                reviewer_note = advisory.ReviewerNote,
            }).ToList(),
            needs_human_reading = NeedsHumanReading,
        },
        CompositeJson.Readable);
}
