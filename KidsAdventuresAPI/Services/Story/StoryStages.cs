using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story.Validation;

namespace AdventurePacks.Api.Services.Story;

/// <summary>
/// The creative stages, behind interfaces.
///
/// Everything the model touches lives behind one of these, and nothing else in the engine may
/// call a model at all. That boundary is what makes the pipeline testable without a network:
/// the control flow, the repair loops and the tier handling can all be proved against stubs,
/// so when a real generation misbehaves the orchestration is not among the suspects.
/// </summary>
public interface IStoryPlanner
{
    Task<StoryBlueprint> PlanAsync(BookState state, CancellationToken cancellationToken);

    /// <summary>
    /// Repairs a plan against specific findings. Given the failures rather than asked to try
    /// again, because "that was wrong, do better" is how a second attempt becomes a different
    /// set of mistakes instead of a fix.
    /// </summary>
    Task<StoryBlueprint> RepairAsync(
        BookState state,
        StoryBlueprint blueprint,
        ValidationReport report,
        CancellationToken cancellationToken);
}

public interface IStoryWriter
{
    Task<IReadOnlyList<WrittenPage>> WriteAsync(
        BookState state,
        IReadOnlyList<StoryState> pageStates,
        CancellationToken cancellationToken);

    /// <summary>
    /// Rewrites only the named pages. Regenerating the book to fix page seven would throw away
    /// six pages that were already good, and risk breaking them.
    /// </summary>
    Task<IReadOnlyList<WrittenPage>> RewriteAsync(
        BookState state,
        IReadOnlyList<StoryState> pageStates,
        IReadOnlyList<int> pages,
        string brief,
        CancellationToken cancellationToken);
}

/// <summary>
/// Judges taste, and nothing else. Continuity is not asked about here, because a model's
/// opinion on whether the key is still in the pocket is worth less than a line of C#.
/// </summary>
public interface ICraftReviewer
{
    Task<CraftVerdict> ReviewAsync(BookState state, CancellationToken cancellationToken);
}

/// <summary>
/// What the reviewer thought, page by page.
///
/// Scores rank pages so the weakest can be rewritten; they are never a gate. A threshold would
/// let a model that keeps answering "six" loop until it gave up, and shipping a slightly flat
/// page beats shipping nothing.
/// </summary>
public sealed record CraftVerdict
{
    /// <summary>Would a child ask for this page again, 0-10, keyed by page.</summary>
    public required IReadOnlyDictionary<int, double> PageDelight { get; init; }

    /// <summary>Notes worth acting on, keyed by page.</summary>
    public IReadOnlyDictionary<int, string> Notes { get; init; } = new Dictionary<int, string>();

    public string? Summary { get; init; }

    public double AverageDelight =>
        PageDelight.Count == 0 ? 0 : PageDelight.Values.Average();

    /// <summary>The weakest pages, worst first, for a surgical rewrite.</summary>
    public IReadOnlyList<int> WeakestPages(int count) =>
        [.. PageDelight.OrderBy(p => p.Value).ThenBy(p => p.Key).Take(count).Select(p => p.Key)];

    /// <summary>The weakest pages that are actually weak. Empty when the book is already good.</summary>
    public IReadOnlyList<int> PagesWorthRewriting(int count, double threshold) =>
        [.. PageDelight.Where(p => p.Value < threshold)
            .OrderBy(p => p.Value).ThenBy(p => p.Key)
            .Take(count).Select(p => p.Key)];
}

/// <summary>How hard the pipeline tries before it gives up or gives in.</summary>
public sealed record StoryPipelineOptions
{
    /// <summary>
    /// Attempts to fix a structurally broken plan. Two is deliberate: a planner that cannot
    /// satisfy the blocking rules twice is not going to on the third go, and every attempt
    /// costs a parent time they are watching pass.
    /// </summary>
    public int MaxPlannerRepairs { get; init; } = 2;

    /// <summary>Attempts to fix prose that contradicts the plan.</summary>
    public int MaxWriterRepairs { get; init; } = 2;

    /// <summary>
    /// Craft rewrites. Exactly one by design — craft is a matter of degree, and a loop chasing
    /// a better score has no natural end.
    /// </summary>
    public int CraftRewritePasses { get; init; } = 1;

    /// <summary>How many weak pages a craft pass rewrites.</summary>
    public int CraftRewritePageCount { get; init; } = 2;

    /// <summary>
    /// Delight below which a page is worth rewriting, out of ten.
    ///
    /// Not a gate — a page below it is never rejected, only offered a second attempt. Without
    /// this the pass would rewrite the two weakest pages of an excellent book, spending tokens
    /// to put good writing at risk. Fixing what is not broken is its own kind of bug.
    /// </summary>
    public double CraftRewriteThreshold { get; init; } = 7.0;

    /// <summary>
    /// Whether craft failures on the plan are worth one repair before writing. Cheap, because
    /// the plan is small, and it is the last moment a structural nudge costs nothing.
    /// </summary>
    public bool RepairCraftBeforeWriting { get; init; } = true;
}

/// <summary>Raised when a book cannot be made correctly. Never raised for craft.</summary>
public sealed class StoryGenerationException(string message, ValidationReport report)
    : InvalidOperationException(message)
{
    public ValidationReport Report { get; } = report;
}

public sealed record StoryGenerationResult
{
    public required BookState State { get; init; }

    /// <summary>Craft findings the book shipped with. Empty is ideal, non-empty is acceptable.</summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    public bool ShippedClean => Warnings.Count == 0;
}
