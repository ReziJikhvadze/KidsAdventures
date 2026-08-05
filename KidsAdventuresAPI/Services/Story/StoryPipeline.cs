using System.Diagnostics;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story.Validation;

namespace AdventurePacks.Api.Services.Story;

public interface IStoryPipeline
{
    Task<StoryGenerationResult> GenerateAsync(BookState state, CancellationToken cancellationToken);
}

/// <summary>
/// Runs the stages in order and decides what happens when one of them is wrong.
///
/// The pipeline holds one judgement that shapes everything else: a blocking failure is a broken
/// book and may stop the line, while a craft failure is a book that could have been better and
/// may not. Continuity errors are what make a story feel broken to a child; a flat emotional
/// curve is a disappointment, not a defect, and refusing to deliver over one would be a worse
/// outcome than shipping it with a note.
///
/// Nothing here calls a model. Every creative step is behind an interface, so this whole class
/// can be proved against stubs — and when a real book comes out wrong, the orchestration is
/// already ruled out.
/// </summary>
public sealed class StoryPipeline(
    IStoryPlanner planner,
    IStoryWriter writer,
    ICraftReviewer reviewer,
    IStoryValidator validator,
    StoryPipelineOptions options,
    ILogger<StoryPipeline> logger) : IStoryPipeline
{
    public async Task<StoryGenerationResult> GenerateAsync(BookState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        var stopwatch = Stopwatch.StartNew();
        var ruleFailures = new List<string>();

        var (planned, plannerRepairs) = await PlanAsync(state, ruleFailures, cancellationToken);
        var pageStates = StateProjector.Project(planned.Blueprint!, planned.Casting);

        var (written, writerRepairs) = await WriteAsync(planned, pageStates, ruleFailures, cancellationToken);

        var (polished, verdict) = await PolishAsync(written, pageStates, cancellationToken);

        // The last word on what shipped. Craft findings that survived every pass are recorded
        // rather than hidden, because a warning nobody can see is the same as no warning.
        var finalReport = validator.ValidateProse(ProseContextFor(polished, pageStates));
        var warnings = finalReport.Craft.Select(f => f.ToString()).ToList();

        stopwatch.Stop();

        var complete = polished
            .WithAnalytics(BuildAnalytics(
                polished, verdict, ruleFailures, warnings,
                plannerRepairs, writerRepairs, stopwatch.ElapsedMilliseconds))
            .WithReview(new StoryReview
            {
                Stage = "final",
                AtUtc = DateTime.UtcNow,
                Findings = warnings,
                PageScores = verdict?.PageDelight ?? new Dictionary<int, double>(),
                Summary = verdict?.Summary
            });

        logger.LogInformation(
            "Book {BookId} generated in {Elapsed}ms: {Pages} pages, {PlannerRepairs} plan repairs, "
            + "{WriterRepairs} prose repairs, {Warnings} craft warnings, delight {Delight:0.0}.",
            state.Meta.BookId, stopwatch.ElapsedMilliseconds, complete.Pages.Count,
            plannerRepairs, writerRepairs, warnings.Count, verdict?.AverageDelight ?? 0);

        return new StoryGenerationResult { State = complete, Warnings = warnings };
    }

    /// <summary>
    /// Plans, then argues with the plan until it is structurally sound or provably will not be.
    /// This is the cheapest place in the engine to be wrong, which is exactly why the arguing
    /// happens here rather than after twelve pages of prose have been paid for.
    /// </summary>
    private async Task<(BookState State, int Repairs)> PlanAsync(
        BookState state,
        List<string> ruleFailures,
        CancellationToken cancellationToken)
    {
        var blueprint = await planner.PlanAsync(state, cancellationToken);
        var current = state.WithBlueprint(blueprint, "planner");

        var repairs = 0;
        for (var attempt = 0; attempt <= options.MaxPlannerRepairs; attempt++)
        {
            var report = validator.ValidateBlueprint(BlueprintContextFor(current));
            ruleFailures.AddRange(report.Findings.Select(f => f.RuleId));

            current = current.WithReview(new StoryReview
            {
                Stage = attempt == 0 ? "blueprint" : $"blueprint-repair-{attempt}",
                AtUtc = DateTime.UtcNow,
                Findings = [.. report.Findings.Select(f => f.ToString())]
            });

            if (report.CanShip)
            {
                // Structurally sound. One optional nudge for craft, then on to the prose —
                // and if the nudge does not take, that is not a reason to stop.
                if (options.RepairCraftBeforeWriting && report.Craft.Any() && attempt <= options.MaxPlannerRepairs)
                {
                    var improved = await TryRepairCraftAsync(current, report, cancellationToken);
                    if (improved is not null)
                    {
                        repairs++;
                        return (improved, repairs);
                    }
                }

                return (current, repairs);
            }

            if (attempt == options.MaxPlannerRepairs)
            {
                // A plan that still contradicts itself must never reach a reader. Failing loudly
                // here is the whole reason the blueprint exists as a separate, cheap artefact.
                logger.LogError(
                    "Book {BookId}: the plan is still broken after {Attempts} repairs: {Findings}",
                    state.Meta.BookId, attempt, report.ToRepairBrief(RuleTier.Blocking));

                throw new StoryGenerationException(
                    $"The story plan could not be made consistent after {attempt} repairs.", report);
            }

            logger.LogWarning("Book {BookId}: repairing the plan, attempt {Attempt}. {Findings}",
                state.Meta.BookId, attempt + 1, report.ToRepairBrief(RuleTier.Blocking));

            var repaired = await planner.RepairAsync(current, current.Blueprint!, report, cancellationToken);
            current = current.WithBlueprint(repaired, $"planner-repair-{attempt + 1}",
                report.ToRepairBrief(RuleTier.Blocking));
            repairs++;
        }

        return (current, repairs);
    }

    /// <summary>Craft repair is best effort: if it makes things worse structurally, it is discarded.</summary>
    private async Task<BookState?> TryRepairCraftAsync(
        BookState state,
        ValidationReport report,
        CancellationToken cancellationToken)
    {
        try
        {
            var repaired = await planner.RepairAsync(state, state.Blueprint!, report, cancellationToken);
            var candidate = state.WithBlueprint(repaired, "planner-craft", report.ToRepairBrief(RuleTier.Craft));

            var recheck = validator.ValidateBlueprint(BlueprintContextFor(candidate));
            if (recheck.CanShip)
            {
                return candidate;
            }

            logger.LogInformation(
                "Book {BookId}: the craft repair broke the plan, keeping the sound one.", state.Meta.BookId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Book {BookId}: craft repair of the plan failed; continuing.", state.Meta.BookId);
        }

        return null;
    }

    /// <summary>
    /// Writes the prose, then checks it says only what the plan allows. Failures here are
    /// blocking because they mean the text and the pictures are about to disagree.
    /// </summary>
    private async Task<(BookState State, int Repairs)> WriteAsync(
        BookState state,
        IReadOnlyList<StoryState> pageStates,
        List<string> ruleFailures,
        CancellationToken cancellationToken)
    {
        var pages = await writer.WriteAsync(state, pageStates, cancellationToken);
        var current = state.WithPages(pages, "writer");

        var repairs = 0;
        for (var attempt = 0; attempt <= options.MaxWriterRepairs; attempt++)
        {
            var report = validator.ValidateProse(ProseContextFor(current, pageStates));
            ruleFailures.AddRange(report.Findings.Select(f => f.RuleId));

            current = current.WithReview(new StoryReview
            {
                Stage = attempt == 0 ? "prose" : $"prose-repair-{attempt}",
                AtUtc = DateTime.UtcNow,
                Findings = [.. report.Findings.Select(f => f.ToString())]
            });

            if (report.CanShip)
            {
                return (current, repairs);
            }

            if (attempt == options.MaxWriterRepairs)
            {
                logger.LogError("Book {BookId}: the prose still contradicts the plan: {Findings}",
                    state.Meta.BookId, report.ToRepairBrief(RuleTier.Blocking));

                throw new StoryGenerationException(
                    $"The prose could not be reconciled with the plan after {attempt} rewrites.", report);
            }

            var affected = report.AffectedPages(RuleTier.Blocking);
            logger.LogWarning("Book {BookId}: rewriting pages {Pages}. {Findings}",
                state.Meta.BookId, string.Join(", ", affected), report.ToRepairBrief(RuleTier.Blocking));

            var rewritten = await writer.RewriteAsync(
                current, pageStates, affected, report.ToRepairBrief(RuleTier.Blocking), cancellationToken);

            current = current.WithPages(Merge(current.Pages, rewritten), $"writer-repair-{attempt + 1}");
            repairs++;
        }

        return (current, repairs);
    }

    /// <summary>
    /// One pass at making the weakest pages better. Ranked, never gated: a threshold would let a
    /// reviewer that keeps answering "six" hold a finished book hostage.
    /// </summary>
    private async Task<(BookState State, CraftVerdict? Verdict)> PolishAsync(
        BookState state,
        IReadOnlyList<StoryState> pageStates,
        CancellationToken cancellationToken)
    {
        CraftVerdict? verdict = null;
        var current = state;

        for (var pass = 0; pass < options.CraftRewritePasses; pass++)
        {
            try
            {
                verdict = await reviewer.ReviewAsync(current, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A reviewer that falls over must not cost a parent their book.
                logger.LogWarning(ex, "Book {BookId}: craft review failed; shipping unreviewed.",
                    state.Meta.BookId);
                return (current, verdict);
            }

            var weakest = verdict.PagesWorthRewriting(
                options.CraftRewritePageCount, options.CraftRewriteThreshold);

            if (weakest.Count == 0)
            {
                logger.LogInformation(
                    "Book {BookId}: every page scored at least {Threshold}, so nothing is rewritten.",
                    state.Meta.BookId, options.CraftRewriteThreshold);
                break;
            }

            current = current.WithReview(new StoryReview
            {
                Stage = $"craft-{pass + 1}",
                AtUtc = DateTime.UtcNow,
                Findings = [.. weakest.Select(p => $"page {p}: {Note(verdict, p)}")],
                PageScores = verdict.PageDelight,
                Summary = verdict.Summary
            });

            try
            {
                var brief = string.Join(Environment.NewLine, weakest.Select(p => $"page {p}: {Note(verdict, p)}"));
                var rewritten = await writer.RewriteAsync(current, pageStates, weakest, brief, cancellationToken);
                var candidate = current.WithPages(Merge(current.Pages, rewritten), $"craft-rewrite-{pass + 1}");

                // A craft rewrite that breaks continuity is discarded outright. Better prose is
                // never worth a book that contradicts itself.
                var recheck = validator.ValidateProse(ProseContextFor(candidate, pageStates));
                if (recheck.CanShip)
                {
                    current = candidate;
                }
                else
                {
                    logger.LogInformation(
                        "Book {BookId}: the craft rewrite broke continuity, keeping the sound pages.",
                        state.Meta.BookId);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Book {BookId}: craft rewrite failed; keeping the pages as written.",
                    state.Meta.BookId);
            }
        }

        return (current, verdict);
    }

    private static string Note(CraftVerdict verdict, int page) =>
        verdict.Notes.TryGetValue(page, out var note) ? note : "weakest page in the book";

    /// <summary>Rewritten pages replace their originals; everything else is left exactly as it was.</summary>
    private static IReadOnlyList<WrittenPage> Merge(
        IReadOnlyList<WrittenPage> existing,
        IReadOnlyList<WrittenPage> rewritten)
    {
        var replacements = rewritten.ToDictionary(p => p.Page);
        return [.. existing
            .Select(p => replacements.TryGetValue(p.Page, out var updated) ? updated : p)
            .OrderBy(p => p.Page)];
    }

    private static BlueprintContext BlueprintContextFor(BookState state) => new()
    {
        Blueprint = state.Blueprint!,
        Casting = state.Casting,
        States = StateProjector.Project(state.Blueprint!, state.Casting),
        Meta = state.Meta
    };

    private static ProseContext ProseContextFor(BookState state, IReadOnlyList<StoryState> pageStates) => new()
    {
        Pages = state.Pages,
        Blueprint = state.Blueprint!,
        Casting = state.Casting,
        States = pageStates,
        Meta = state.Meta
    };

    private static StoryAnalytics BuildAnalytics(
        BookState state,
        CraftVerdict? verdict,
        IReadOnlyList<string> ruleFailures,
        IReadOnlyList<string> warnings,
        int plannerRepairs,
        int writerRepairs,
        long elapsedMs)
    {
        var beats = state.Blueprint?.Beats ?? [];
        var pages = state.Pages;

        return new StoryAnalytics
        {
            PlannerRepairCount = plannerRepairs,
            WriterRepairCount = writerRepairs,
            RuleFailures = [.. ruleFailures],
            WeakestPages = verdict?.WeakestPages(3) ?? [],
            DelightScore = verdict?.AverageDelight,
            DialogueRatio = pages.Count == 0
                ? 0
                : (double)pages.Count(DialogueCoverageRule.HasDialogue) / pages.Count,
            HumorDensity = beats.Count == 0
                ? 0
                : (double)beats.Count(b => b.Purpose == NarrativePurpose.Comedy
                                           || b.Energy == NarrativeEnergy.Humor) / beats.Count,
            EmotionDistribution = Distribution(beats.Select(b => b.Emotion.ToString())),
            PurposeDistribution = Distribution(beats.Select(b => b.Purpose.ToString())),
            EnergyDistribution = Distribution(beats.Select(b => b.Energy.ToString())),
            TotalMilliseconds = elapsedMs,
            ShippedWithWarnings = [.. warnings]
        };
    }

    private static IReadOnlyDictionary<string, int> Distribution(IEnumerable<string> values) =>
        values.GroupBy(v => v, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
}
