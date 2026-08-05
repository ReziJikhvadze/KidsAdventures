using System.Security.Cryptography;
using System.Text;

namespace AdventurePacks.Api.Domain.Story;

/// <summary>
/// The single source of truth for one book, append-only and versioned.
///
/// Two rules govern the whole engine. First: no stage may read anything except this object, and
/// no stage may derive a fact from prose — facts come from the blueprint and the deltas, and
/// prose is only a human-readable rendering of them. That is what stops a key existing in the
/// text but not in the picture.
///
/// Second: nothing here is ever mutated. Each stage returns a new state carrying one more
/// revision, so the history of how a book was made survives alongside the book. When a story
/// reads badly six months from now, the question "what did the planner actually produce, before
/// two repairs rewrote it" has an answer.
///
/// Page state is deliberately absent. Deltas are the only stored truth; snapshots are projected
/// on demand by <c>StateProjector</c>. Persisting them would leave them silently stale behind a
/// repair, which is the worst kind of wrong.
/// </summary>
public sealed record BookState
{
    public required BookMeta Meta { get; init; }
    public required CastingBible Casting { get; init; }
    public required Inspiration Inspiration { get; init; }

    /// <summary>Carried in from earlier books in this child's series. Empty for a first book.</summary>
    public StoryMemory Memory { get; init; } = StoryMemory.Empty;

    public StoryBlueprint? Blueprint { get; init; }
    public IReadOnlyList<WrittenPage> Pages { get; init; } = [];
    public IReadOnlyList<ScenePlan> Scenes { get; init; } = [];

    /// <summary>Every review this book has been through, oldest first. Never replaced, only added to.</summary>
    public IReadOnlyList<StoryReview> Reviews { get; init; } = [];

    /// <summary>What each stage did, in order. The audit trail of how this book came to exist.</summary>
    public IReadOnlyList<BookRevision> Revisions { get; init; } = [];

    public StoryAnalytics Analytics { get; init; } = new();

    /// <summary>Which revision this is. Zero is the state before any stage has run.</summary>
    public int Version => Revisions.Count;

    public BookState WithBlueprint(StoryBlueprint blueprint, string stage, string? note = null) =>
        this with
        {
            Blueprint = blueprint,
            Revisions = [.. Revisions, BookRevision.Create(stage, note)]
        };

    public BookState WithPages(IReadOnlyList<WrittenPage> pages, string stage, string? note = null) =>
        this with
        {
            Pages = pages,
            Revisions = [.. Revisions, BookRevision.Create(stage, note)]
        };

    public BookState WithScenes(IReadOnlyList<ScenePlan> scenes, string stage, string? note = null) =>
        this with
        {
            Scenes = scenes,
            Revisions = [.. Revisions, BookRevision.Create(stage, note)]
        };

    /// <summary>Reviews accumulate. An earlier verdict is evidence, not something to overwrite.</summary>
    public BookState WithReview(StoryReview review) =>
        this with
        {
            Reviews = [.. Reviews, review],
            Revisions = [.. Revisions, BookRevision.Create(review.Stage, $"{review.Findings.Count} findings")]
        };

    public BookState WithAnalytics(StoryAnalytics analytics) =>
        this with { Analytics = analytics };

    public BookState WithMemory(StoryMemory memory) =>
        this with { Memory = memory };
}

/// <summary>One stage's contribution, recorded so the path to a finished book stays readable.</summary>
public sealed record BookRevision(int Ordinal, string Stage, string? Note, DateTime AtUtc)
{
    private static int _counter;

    public static BookRevision Create(string stage, string? note) =>
        new(Interlocked.Increment(ref _counter), stage, note, DateTime.UtcNow);
}

/// <summary>
/// A verdict on the book at one moment. Kept forever: the point of analytics is to compare what
/// the reviewer said before and after a change, which is impossible if the earlier answer was
/// thrown away.
/// </summary>
public sealed record StoryReview
{
    public required string Stage { get; init; }
    public required DateTime AtUtc { get; init; }

    /// <summary>Rendered validation findings, so a review is readable without the validator.</summary>
    public required IReadOnlyList<string> Findings { get; init; }

    /// <summary>Per-page delight, keyed by page. Absent for purely structural reviews.</summary>
    public IReadOnlyDictionary<int, double> PageScores { get; init; } =
        new Dictionary<int, double>();

    public string? Summary { get; init; }
}

public sealed record BookMeta
{
    public required Guid BookId { get; init; }
    public required Guid ChildId { get; init; }
    public required string Language { get; init; }
    public required string WorldId { get; init; }

    /// <summary>
    /// How long this book is. Nothing in the engine assumes a particular value: rules scale from
    /// it through <see cref="StoryScale"/>, so eight, twelve or twenty pages all work without a
    /// domain change.
    /// </summary>
    public required int PageCount { get; init; }

    public required int ChildAge { get; init; }

    /// <summary>Which book this is in the child's series. 1 for a first adventure.</summary>
    public int ChapterNumber { get; init; } = 1;

    /// <summary>Engine version that produced this book, so old books stay readable after changes.</summary>
    public required string EngineVersion { get; init; }
}

/// <summary>One page of finished prose. The writer owns these and nothing else.</summary>
public sealed record WrittenPage
{
    public required int Page { get; init; }
    public required string Title { get; init; }

    /// <summary>The short line printed on the illustration.</summary>
    public required string Caption { get; init; }

    /// <summary>The read-aloud text.</summary>
    public required string Content { get; init; }
}

/// <summary>
/// An illustration, planned as structure rather than as a sentence.
///
/// Stored as fields and rendered to a prompt at the very end. A string would be unreviewable and
/// undiffable; this can be inspected field by field, and the vision reviewer can be handed
/// exactly the checklist it needs.
/// </summary>
public sealed record ScenePlan
{
    public required int Page { get; init; }
    public required string SceneDescription { get; init; }
    public required string Camera { get; init; }
    public required string Composition { get; init; }
    public required string Lighting { get; init; }
    public required string Mood { get; init; }

    public required IReadOnlyList<string> Foreground { get; init; }
    public required IReadOnlyList<string> Midground { get; init; }
    public required IReadOnlyList<string> Background { get; init; }

    /// <summary>Character ids that must be visible, taken from state rather than from the prose.</summary>
    public required IReadOnlyList<string> Characters { get; init; }

    /// <summary>Object ids that must be visible. This is why the key stays in the picture.</summary>
    public required IReadOnlyList<string> Props { get; init; }

    public required StoryEmotion Emotion { get; init; }
    public required string Action { get; init; }

    /// <summary>Which edge is left calm for the caption. Alternates so text never covers a face twice running.</summary>
    public required string TextSafeBand { get; init; }

    /// <summary>
    /// Fingerprint of everything that must not change about the hero's look on this page. The
    /// vision reviewer checks the image against these components, and a change between adjacent
    /// pages without a wardrobe beat is a blueprint bug caught before anything is drawn.
    /// </summary>
    public required VisualHash Hero { get; init; }
}

/// <summary>
/// A hash of the visual facts that must hold, plus the components that produced it so a failure
/// can say what actually differs rather than only that something did.
/// </summary>
public sealed record VisualHash(string Value, IReadOnlyList<string> Components)
{
    public static VisualHash For(StoryCharacter character, Outfit outfit)
    {
        var components = new List<string>
        {
            $"hair:{character.Appearance.HairColor.Trim().ToLowerInvariant()}",
            $"hairstyle:{character.Appearance.HairStyle.Trim().ToLowerInvariant()}",
            $"eyes:{character.Appearance.EyeColor.Trim().ToLowerInvariant()}",
            $"skin:{character.Appearance.SkinTone.Trim().ToLowerInvariant()}",
            outfit.ToCanonicalString()
        };

        var joined = string.Join("|", components);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return new VisualHash(Convert.ToHexString(bytes)[..12], components);
    }
}

/// <summary>
/// What the engine learned making this book.
///
/// Persisted for every generation so that prompt and rule changes can be judged against
/// thousands of real books instead of an impression. Without it, "the stories feel weak" stays
/// an opinion; with it, it becomes a number that moved.
/// </summary>
public sealed record StoryAnalytics
{
    public int PlannerRepairCount { get; init; }
    public int WriterRepairCount { get; init; }
    public int SceneRepairCount { get; init; }

    /// <summary>Every rule that fired, including ones that were repaired away.</summary>
    public IReadOnlyList<string> RuleFailures { get; init; } = [];

    /// <summary>Pages the craft reviewer ranked lowest, worst first.</summary>
    public IReadOnlyList<int> WeakestPages { get; init; } = [];

    /// <summary>Would a child ask for this page again, 0-10, averaged across pages.</summary>
    public double? DelightScore { get; init; }

    /// <summary>Share of pages containing spoken dialogue.</summary>
    public double DialogueRatio { get; init; }

    /// <summary>Share of pages carrying a joke or a piece of physical comedy.</summary>
    public double HumorDensity { get; init; }

    public IReadOnlyDictionary<string, int> EmotionDistribution { get; init; } =
        new Dictionary<string, int>();

    public IReadOnlyDictionary<string, int> PurposeDistribution { get; init; } =
        new Dictionary<string, int>();

    public IReadOnlyDictionary<string, int> EnergyDistribution { get; init; } =
        new Dictionary<string, int>();

    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public long TotalMilliseconds { get; init; }

    /// <summary>Craft rules that were still failing when the book shipped. Tier two never blocks.</summary>
    public IReadOnlyList<string> ShippedWithWarnings { get; init; } = [];
}
