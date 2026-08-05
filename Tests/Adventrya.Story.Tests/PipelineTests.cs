using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Validation;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adventrya.Story.Tests;

/// <summary>
/// The pipeline's decisions, proved without a model in the loop.
///
/// Every creative stage is a stub that returns exactly what the test wants, so what is under
/// test is the orchestration itself: how many repairs are attempted, what happens when they do
/// not work, and — the judgement the whole tier system rests on — which failures are allowed to
/// stop a book and which are not.
/// </summary>
public class PipelineTests
{
    [Fact]
    public async Task A_sound_plan_and_sound_prose_ship_with_no_repairs()
    {
        var planner = new StubPlanner(StoryFixtures.Valid());
        var writer = new StubWriter(GoodPages());
        var pipeline = Build(planner, writer);

        var result = await pipeline.GenerateAsync(StoryFixtures.EmptyBookState(), CancellationToken.None);

        Assert.Equal(0, planner.RepairCalls);
        Assert.Equal(0, writer.RewriteCalls);
        Assert.Equal(6, result.State.Pages.Count);
        Assert.Equal(0, result.State.Analytics.PlannerRepairCount);
    }

    [Fact]
    public async Task A_broken_plan_is_repaired_and_then_ships()
    {
        // First answer is broken, the repair is sound: exactly the case the loop exists for.
        var planner = new StubPlanner(BrokenPlan(), repairWith: StoryFixtures.Valid());
        var pipeline = Build(planner, new StubWriter(GoodPages()));

        var result = await pipeline.GenerateAsync(StoryFixtures.EmptyBookState(), CancellationToken.None);

        Assert.Equal(1, planner.RepairCalls);
        Assert.Equal(1, result.State.Analytics.PlannerRepairCount);
        Assert.Contains("R1", result.State.Analytics.RuleFailures);
    }

    [Fact]
    public async Task A_plan_that_stays_broken_fails_loudly_rather_than_shipping()
    {
        // The single most important behaviour in the pipeline: a book that contradicts itself
        // never reaches a child, however many attempts it has cost.
        var planner = new StubPlanner(BrokenPlan(), repairWith: BrokenPlan());
        var pipeline = Build(planner, new StubWriter(GoodPages()));

        var ex = await Assert.ThrowsAsync<StoryGenerationException>(
            () => pipeline.GenerateAsync(StoryFixtures.EmptyBookState(), CancellationToken.None));

        Assert.Contains(ex.Report.Blocking, f => f.RuleId == "R1");
        Assert.Equal(2, planner.RepairCalls);
    }

    [Fact]
    public async Task Craft_failures_never_stop_a_book()
    {
        // A plan that is structurally perfect but emotionally flat: three identical feelings in
        // a row, no comedy, no quiet page. Every one of those is a craft finding.
        var flat = FlatButSoundPlan();
        var pipeline = Build(new StubPlanner(flat, repairWith: flat), new StubWriter(GoodPages()));

        var result = await pipeline.GenerateAsync(StoryFixtures.EmptyBookState(), CancellationToken.None);

        Assert.NotNull(result.State.Blueprint);
        Assert.Equal(6, result.State.Pages.Count);
    }

    [Fact]
    public async Task Prose_that_contradicts_the_plan_is_rewritten_page_by_page()
    {
        var writer = new StubWriter(PagesNamingAnAbsentCharacter(), rewriteWith: GoodPages());
        var pipeline = Build(new StubPlanner(StoryFixtures.Valid()), writer);

        var result = await pipeline.GenerateAsync(StoryFixtures.EmptyBookState(), CancellationToken.None);

        Assert.Equal(1, writer.RewriteCalls);
        // Surgical: only the offending page was sent back, not the whole book.
        Assert.Equal([1], writer.LastRewrittenPages);
        Assert.Equal(1, result.State.Analytics.WriterRepairCount);
    }

    [Fact]
    public async Task Prose_that_will_not_come_right_fails_rather_than_shipping_a_contradiction()
    {
        var bad = PagesNamingAnAbsentCharacter();
        var pipeline = Build(new StubPlanner(StoryFixtures.Valid()), new StubWriter(bad, rewriteWith: bad));

        var ex = await Assert.ThrowsAsync<StoryGenerationException>(
            () => pipeline.GenerateAsync(StoryFixtures.EmptyBookState(), CancellationToken.None));

        Assert.Contains(ex.Report.Blocking, f => f.RuleId == "R21");
    }

    [Fact]
    public async Task A_craft_rewrite_that_breaks_continuity_is_discarded()
    {
        // The reviewer asks for a better page 1 and the writer returns one that names a
        // character who is not there. Better prose is never worth a broken book.
        var writer = new StubWriter(GoodPages(), rewriteWith: PagesNamingAnAbsentCharacter());
        var reviewer = new StubReviewer(delight: 3.0);
        var pipeline = Build(new StubPlanner(StoryFixtures.Valid()), writer, reviewer);

        var result = await pipeline.GenerateAsync(StoryFixtures.EmptyBookState(), CancellationToken.None);

        Assert.DoesNotContain("Rust", result.State.Pages.First(p => p.Page == 1).Content);
        Assert.Equal(6, result.State.Pages.Count);
    }

    [Fact]
    public async Task A_reviewer_that_falls_over_does_not_cost_the_parent_their_book()
    {
        var pipeline = Build(
            new StubPlanner(StoryFixtures.Valid()),
            new StubWriter(GoodPages()),
            new ThrowingReviewer());

        var result = await pipeline.GenerateAsync(StoryFixtures.EmptyBookState(), CancellationToken.None);

        Assert.Equal(6, result.State.Pages.Count);
        Assert.Null(result.State.Analytics.DelightScore);
    }

    [Fact]
    public async Task Analytics_record_what_happened()
    {
        var pipeline = Build(
            new StubPlanner(BrokenPlan(), repairWith: StoryFixtures.Valid()),
            new StubWriter(GoodPages()),
            new StubReviewer(delight: 8.5));

        var analytics = (await pipeline.GenerateAsync(StoryFixtures.EmptyBookState(), CancellationToken.None))
            .State.Analytics;

        Assert.Equal(1, analytics.PlannerRepairCount);
        Assert.Equal(8.5, analytics.DelightScore);
        Assert.NotEmpty(analytics.EmotionDistribution);
        Assert.NotEmpty(analytics.PurposeDistribution);
        Assert.True(analytics.DialogueRatio > 0);
        Assert.True(analytics.TotalMilliseconds >= 0);
    }

    [Fact]
    public async Task Every_stage_is_recorded_so_the_path_to_the_book_is_readable()
    {
        var pipeline = Build(
            new StubPlanner(BrokenPlan(), repairWith: StoryFixtures.Valid()),
            new StubWriter(GoodPages()),
            new StubReviewer(delight: 9));

        var state = (await pipeline.GenerateAsync(StoryFixtures.EmptyBookState(), CancellationToken.None)).State;

        Assert.Contains(state.Revisions, r => r.Stage == "planner");
        Assert.Contains(state.Revisions, r => r.Stage.StartsWith("planner-repair"));
        Assert.Contains(state.Revisions, r => r.Stage == "writer");
        Assert.Contains(state.Reviews, r => r.Stage == "final");
    }

    // ---- helpers ---------------------------------------------------------

    private static StoryPipeline Build(
        IStoryPlanner planner,
        IStoryWriter writer,
        ICraftReviewer? reviewer = null) =>
        new(planner, writer, reviewer ?? new StubReviewer(delight: 9),
            new StoryValidator(), new StoryPipelineOptions { RepairCraftBeforeWriting = false },
            NullLogger<StoryPipeline>.Instance);

    private static StoryBlueprint BrokenPlan() =>
        StoryFixtures.Valid().With(beat =>
            beat.Page == 2 ? beat.Replace(objectsUsed: [StoryFixtures.KeyId]) : beat);

    /// <summary>Structurally sound, emotionally monotonous. Craft findings only.</summary>
    private static StoryBlueprint FlatButSoundPlan()
    {
        var valid = StoryFixtures.Valid();
        return new StoryBlueprint
        {
            Promise = valid.Promise, Answer = valid.Answer,
            EmotionCurve = [.. valid.Beats.Select(_ => StoryEmotion.Wonder)],
            Locations = valid.Locations, Objects = valid.Objects, Cast = valid.Cast,
            Threads = valid.Threads, Surprises = [],
            Beats = [.. valid.Beats.Select(b => b.WithFeel(StoryEmotion.Wonder, NarrativeEnergy.Wonder))]
        };
    }

    private static IReadOnlyList<WrittenPage> GoodPages() =>
        [.. Enumerable.Range(1, 6).Select(page => new WrittenPage
        {
            Page = page,
            Title = $"Page {page}",
            Caption = $"Onward to {page}",
            Content = page == 1
                ? "\"Look at this,\" Tamar whispered, and the paper in her hands began to glow softly."
                : $"\"Keep going,\" said Tamar, and the two of them pressed on past the {page} chiming stones."
        })];

    /// <summary>Page one names Rust, who the plan does not put on page one.</summary>
    private static IReadOnlyList<WrittenPage> PagesNamingAnAbsentCharacter() =>
        [.. GoodPages().Select(p => p.Page == 1
            ? p with { Content = "\"Wait for me!\" called Rust, bounding through the ferns after Tamar." }
            : p)];

    private sealed class StubPlanner(StoryBlueprint first, StoryBlueprint? repairWith = null) : IStoryPlanner
    {
        public int RepairCalls { get; private set; }

        public Task<StoryBlueprint> PlanAsync(BookState state, CancellationToken cancellationToken) =>
            Task.FromResult(first);

        public Task<StoryBlueprint> RepairAsync(
            BookState state, StoryBlueprint blueprint, ValidationReport report, CancellationToken cancellationToken)
        {
            RepairCalls++;
            return Task.FromResult(repairWith ?? blueprint);
        }
    }

    private sealed class StubWriter(
        IReadOnlyList<WrittenPage> first,
        IReadOnlyList<WrittenPage>? rewriteWith = null) : IStoryWriter
    {
        public int RewriteCalls { get; private set; }
        public IReadOnlyList<int> LastRewrittenPages { get; private set; } = [];

        public Task<IReadOnlyList<WrittenPage>> WriteAsync(
            BookState state, IReadOnlyList<StoryState> pageStates, CancellationToken cancellationToken) =>
            Task.FromResult(first);

        public Task<IReadOnlyList<WrittenPage>> RewriteAsync(
            BookState state, IReadOnlyList<StoryState> pageStates,
            IReadOnlyList<int> pages, string brief, CancellationToken cancellationToken)
        {
            RewriteCalls++;
            LastRewrittenPages = pages;
            var source = rewriteWith ?? first;
            return Task.FromResult<IReadOnlyList<WrittenPage>>(
                [.. source.Where(p => pages.Contains(p.Page))]);
        }
    }

    private sealed class StubReviewer(double delight) : ICraftReviewer
    {
        public Task<CraftVerdict> ReviewAsync(BookState state, CancellationToken cancellationToken) =>
            Task.FromResult(new CraftVerdict
            {
                PageDelight = state.Pages.ToDictionary(p => p.Page, _ => delight),
                Summary = "stub"
            });
    }

    private sealed class ThrowingReviewer : ICraftReviewer
    {
        public Task<CraftVerdict> ReviewAsync(BookState state, CancellationToken cancellationToken) =>
            throw new HttpRequestException("the reviewer is down");
    }
}
