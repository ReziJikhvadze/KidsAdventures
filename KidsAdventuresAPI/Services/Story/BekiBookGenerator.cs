using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Infrastructure;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story.Prompts;

namespace AdventurePacks.Api.Services.Story;

public sealed record BekiImageAttempt(long GenerationMs, long ReviewMs, string Verdict, bool Accepted);

/// <summary>What one Beki illustration came out as, and what it cost to get there.</summary>
public sealed record BekiImageResult
{
    /// <summary>null for the cover; 1-based for a spread.</summary>
    public int? SpreadNumber { get; init; }

    public required byte[] Image { get; init; }
    public required bool Accepted { get; init; }

    /// <summary>The model's own words, unparsed. Kept because it is the point of running this.</summary>
    public required string Verdict { get; init; }

    /// <summary>1 means it passed first time.</summary>
    public required int Attempts { get; init; }

    public IReadOnlyList<BekiImageAttempt> AttemptDetails { get; init; } = [];

    public required string Prompt { get; init; }

    /// <summary>Which characters this image was drawn from an anchor for, rather than a description.</summary>
    public IReadOnlyList<string> AnchoredCharacters { get; init; } = [];
}

public sealed record BekiBookResult
{
    public long PlanMs { get; init; }
    public required MasterStory Plan { get; init; }
    public required string AppearanceDescription { get; init; }
    public required BekiImageResult Cover { get; init; }
    public required IReadOnlyList<BekiImageResult> Spreads { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public interface IBekiBookGenerator
{
    Task<BekiBookResult> GenerateAsync(
        MasterStoryInput input,
        byte[] childPhoto,
        string childPhotoContentType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Draws a plan that already exists — the fulfilment path, where the story was written at
    /// preview time and the parent has already read it. Writing another plan here would replace
    /// the story they chose to buy, which is the exact fault preview adoption exists to prevent.
    /// When <paramref name="existingCover"/> is given it is adopted instead of drawn, for the
    /// same reason: the cover the parent decided on is the cover they get.
    /// </summary>
    /// <param name="onImage">
    /// Called once per finished illustration, in drawing order, before the next one starts.
    /// The book takes minutes and a parent is watching a spinner; this is how the pictures
    /// reach them while the rest are still being drawn. Null when nobody is watching. Not called
    /// for a spread adopted from <paramref name="existingSpreads"/> — it was already delivered
    /// the run that drew it.
    /// </param>
    /// <param name="existingSpreads">
    /// Accepted artwork a previous, interrupted attempt at this same book already produced,
    /// keyed by spread number. Resuming a fulfilment job redraws only the spreads missing from
    /// here — the rest are adopted outright, with no second review and no second bill. Null (the
    /// default) means nothing survives from an earlier attempt, which is every caller but the
    /// resumable fulfilment job.
    /// </param>
    Task<BekiBookResult> IllustrateAsync(
        MasterStory plan,
        byte[] childPhoto,
        string childPhotoContentType,
        byte[]? existingCover,
        Func<BekiImageResult, Task>? onImage,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<int, byte[]>? existingSpreads = null);

    /// <summary>
    /// The Beki cover alone: the child and the Beki master reference, QA-reviewed exactly as
    /// fulfilment draws it. Exposed so a preview can carry the same cover the parent will get if
    /// they buy the book, instead of the legacy single-reference cover the A5 flow has always
    /// drawn. A refused review surfaces through the result's own
    /// <see cref="BekiImageResult.Verdict"/> rather than as a failure.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The Beki master reference is missing from the deployment. This cover is the child and Beki
    /// together, and there is no honest way to draw it without the one picture that says what
    /// Beki looks like — see <see cref="BekiBookGenerator.RequireBekiReference"/>. Callers that
    /// can fall back to a cover with no companion at all should catch it and do that.
    /// </exception>
    Task<BekiImageResult> DrawCoverAsync(
        MasterStory plan, byte[] childPhoto, string childPhotoContentType, CancellationToken cancellationToken);
}

/// <summary>
/// One Beki-format book, start to finish: plan, cover, eight spreads, each reviewed.
///
/// The handoff's whole architecture is three model tasks and a rule about what each image is
/// allowed to know, and this class is mostly that rule. An image call receives the child's
/// photograph, the child's fixed appearance, this spread's scene, the continuity it needs and
/// nothing else — not the story, not the other spreads, not the extra wish, not a dimension or a
/// typography rule. Everything a picture must not be told is simply never assembled here.
///
/// Spreads draw concurrently, capped by <see cref="BekiOptions.SpreadConcurrency"/>, under one
/// dependency rule: a spread that reuses a recurring character or object waits for the
/// immediately previous spread listing the same id, so an anchor — the first accepted image a
/// recurring id appeared in — is always settled before its next use. That chain reproduces the
/// old sequential semantics exactly; setting the concurrency to 1 reproduces the old order too,
/// because the scheduler always launches the lowest-numbered ready spread.
///
/// It generates and reviews. It does not lay out, store, bill or persist anything — the layout
/// and the printed format are a separate decision that has not been taken yet, and a generator
/// that quietly wrote files would be harder to run twice while that decision is pending.
/// </summary>
public sealed class BekiBookGenerator(
    IStoryModelClient storyClient,
    IOpenAiService openAi,
    IOptions<BekiPrintLayoutOptions> printLayoutOptions,
    IOptions<BekiOptions> bekiOptions,
    ILogger<BekiBookGenerator> logger) : IBekiBookGenerator
{
    /// <summary>
    /// Landscape, until the 2.2:1 spread is decided. gpt-image offers three shapes and none of
    /// them is 440×200, so this is the closest that is not a distortion; the final framing is a
    /// layout question rather than a generation one.
    /// </summary>
    public const string SpreadImageSize = "1536x1024";

    /// <summary>
    /// The handoff allows the original plus two retries. One, for now: the first measured run
    /// showed a correction adding a second copy of a character that the reviewer then failed to
    /// notice, so a second automatic retry buys another chance to make it worse.
    /// </summary>
    public const int MaxRegenerations = 1;

    private const string BekiReferencePath = BekiIdentity.ReferenceAssetPath;

    private readonly BekiPrintLayoutOptions _layout = printLayoutOptions.Value;
    private readonly BekiOptions _bekiOptions = bekiOptions.Value;

    /// <summary>
    /// Read once per generator instance and kept: the file never changes mid-run, and a book
    /// draws it up to nine times — the cover and every spread that carries Beki.
    /// </summary>
    private byte[]? _cachedBekiReference;
    private bool _bekiReferenceLoadAttempted;
    private readonly object _bekiReferenceLock = new();

    internal static IReadOnlyDictionary<int, IReadOnlyList<int>> SpreadDependencies(MasterStory plan, IReadOnlySet<int> adopted)
    {
        var result = new Dictionary<int, IReadOnlyList<int>>();
        var castIds = (plan.Cast ?? []).Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var objIds = (plan.Objects ?? []).Select(o => o.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var previousOccurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var globallyAnchoredByAdoption = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var spread in plan.Spreads)
        {
            if (adopted.Contains(spread.Number))
            {
                var ids = (spread.Characters ?? []).Concat(spread.Objects ?? []);
                foreach (var id in ids)
                {
                    if (castIds.Contains(id) || objIds.Contains(id))
                    {
                        globallyAnchoredByAdoption.Add(id);
                    }
                }
            }
        }

        foreach (var spread in plan.Spreads.OrderBy(s => s.Number))
        {
            var deps = new HashSet<int>();
            
            if (adopted.Contains(spread.Number))
            {
                result[spread.Number] = [];
                continue;
            }

            // Distinct, because nothing upstream forbids a spread listing the same id twice —
            // and a duplicated id would make the second occurrence see the first as its
            // "previous" spread, a self-dependency the scheduler can never satisfy.
            var ids = (spread.Characters ?? []).Concat(spread.Objects ?? [])
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var id in ids)
            {
                if (id.Equals("child", StringComparison.OrdinalIgnoreCase) || id.Equals("beki", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!castIds.Contains(id) && !objIds.Contains(id))
                    continue;

                if (globallyAnchoredByAdoption.Contains(id))
                    continue;

                if (previousOccurrences.TryGetValue(id, out var prev) && prev != spread.Number)
                {
                    deps.Add(prev);
                }

                previousOccurrences[id] = spread.Number;
            }

            result[spread.Number] = deps.ToList();
        }

        return result;
    }

    /// <summary>
    /// QA must judge what the reader will see, not pixels the print crop discards.
    /// <see cref="BekiPdfComposer"/> centre-crops every render to its sheet at layout time, so
    /// the reviewer is shown that same crop rather than the full 3:2 provider frame — a face the
    /// print will trim away should not be able to fail a review, and a face the print keeps
    /// should not be able to hide from one behind a background band that never ships.
    /// </summary>
    private float SpreadCropRatio =>
        (_layout.SpreadWidthMm + (_layout.BleedMm * 2)) / (_layout.SpreadHeightMm + (_layout.BleedMm * 2));

    /// <summary>The cover prints as a single leaf, half the spread — see <see cref="SpreadCropRatio"/>.</summary>
    private float CoverCropRatio =>
        (_layout.PageWidthMm + (_layout.BleedMm * 2)) / (_layout.SpreadHeightMm + (_layout.BleedMm * 2));

    public async Task<BekiBookResult> GenerateAsync(
        MasterStoryInput input,
        byte[] childPhoto,
        string childPhotoContentType,
        CancellationToken cancellationToken)
    {
        // The child's appearance is read from the photograph, exactly as the A5 flow reads it.
        // The planner needs it for characterLock; it never sees the photograph itself.
        var appearance = await openAi.DescribeCharacterFromPhotoAsync(
            childPhoto, childPhotoContentType, MasterStoryPrompt.PhotoDescribe, cancellationToken);

        var planSw = System.Diagnostics.Stopwatch.StartNew();
        var plan = await PlanAsync(input with { AppearanceDescription = appearance }, cancellationToken);
        planSw.Stop();

        logger.LogInformation(
            "Beki plan \"{Title}\": {Spreads} spreads, {Cast} recurring character(s).",
            plan.Concept.Title, plan.Spreads.Count, plan.Cast?.Count ?? 0);

        var book = await IllustrateAsync(plan, childPhoto, childPhotoContentType, null, null, cancellationToken);
        return book with { AppearanceDescription = appearance, PlanMs = planSw.ElapsedMilliseconds };
    }

    public async Task<BekiBookResult> IllustrateAsync(
        MasterStory plan,
        byte[] childPhoto,
        string childPhotoContentType,
        byte[]? existingCover,
        Func<BekiImageResult, Task>? onImage,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<int, byte[]>? existingSpreads = null)
    {
        var warnings = new List<string>();
        var adopted = existingSpreads ?? new Dictionary<int, byte[]>();

        var castById = (plan.Cast ?? []).ToDictionary(member => member.Id, StringComparer.OrdinalIgnoreCase);
        var objById = (plan.Objects ?? []).ToDictionary(o => o.Id, StringComparer.OrdinalIgnoreCase);
        var anchors = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        /*
          Adopted spreads anchor first, before a single new picture is drawn.

          A resumed run redraws only the holes a previous attempt left; every adopted spread is
          already part of the book the parent will read, so a redrawn hole has to be drawn against
          it, not against nothing. Anchoring here — ahead of the loop below — means a redrawn
          spread 3 sees char_01's face from an adopted spread 2 exactly as it would if spread 2
          had just been drawn in this same run.
        */
        foreach (var spread in plan.Spreads.OrderBy(s => s.Number))
        {
            if (!adopted.TryGetValue(spread.Number, out var adoptedImage))
            {
                continue;
            }

            var ids = (spread.Characters ?? []).Concat(spread.Objects ?? []);
            foreach (var id in ids)
            {
                if ((castById.ContainsKey(id) || objById.ContainsKey(id)) && !anchors.ContainsKey(id))
                {
                    anchors[id] = adoptedImage;
                }
            }
        }

        var cover = existingCover is null
            ? await DrawCoverAsync(plan, childPhoto, childPhotoContentType, cancellationToken)
            : new BekiImageResult
            {
                Image = existingCover,
                Accepted = true,
                Verdict = "Adopted from the preview the parent chose; not drawn here.",
                Attempts = 0,
                Prompt = string.Empty,
            };

        var spreads = new BekiImageResult[plan.Spreads.Count];
        var dependencies = SpreadDependencies(plan, adopted.Keys.ToHashSet());
        
        using var anchorsGate = new SemaphoreSlim(1, 1);
        using var deliveryGate = new SemaphoreSlim(1, 1);

        var deliveredSpreads = adopted.Keys.ToHashSet();
        var completedResults = new Dictionary<int, BekiImageResult>();

        // One failure cancels the whole fleet: a spread that dies mid-book must not leave its
        // siblings generating and reviewing paid images for minutes while the job waits to fail.
        using var renderCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        async Task<BekiImageResult> ProcessSpreadAsync(StorySpread spread)
        {
            renderCts.Token.ThrowIfCancellationRequested();

            IReadOnlyDictionary<string, byte[]> snapshot;
            await anchorsGate.WaitAsync(renderCts.Token).ConfigureAwait(false);
            try
            {
                snapshot = new Dictionary<string, byte[]>(anchors, StringComparer.OrdinalIgnoreCase);
            }
            finally
            {
                anchorsGate.Release();
            }

            var result = await DrawSpreadAsync(
                plan, spread, castById, objById, snapshot, childPhoto, childPhotoContentType,
                renderCts.Token).ConfigureAwait(false);

            await anchorsGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (result.Accepted)
                {
                    var ids = (spread.Characters ?? []).Concat(spread.Objects ?? []);
                    foreach (var id in ids)
                    {
                        if ((castById.ContainsKey(id) || objById.ContainsKey(id)) && !anchors.ContainsKey(id))
                        {
                            anchors[id] = result.Image;
                            logger.LogInformation("Beki: spread {Spread} is now the anchor for {Character}.", spread.Number, id);
                        }
                    }
                }
                else
                {
                    lock (warnings)
                    {
                        warnings.Add($"Spread {spread.Number} shipped as NEEDS_REVIEW after {result.Attempts} attempt(s).");
                    }
                }
            }
            finally
            {
                anchorsGate.Release();
            }

            if (onImage is not null)
            {
                await deliveryGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    completedResults[spread.Number] = result;
                    for (int n = 1; n <= plan.Spreads.Count; n++)
                    {
                        if (!deliveredSpreads.Contains(n))
                        {
                            if (completedResults.TryGetValue(n, out var res))
                            {
                                await onImage(res).ConfigureAwait(false);
                                deliveredSpreads.Add(n);
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                }
                finally
                {
                    deliveryGate.Release();
                }
            }
            
            return result;
        }

        var completed = adopted.Keys.ToHashSet();
        var pending = plan.Spreads.Where(s => !adopted.ContainsKey(s.Number)).OrderBy(s => s.Number).ToList();
        var running = new Dictionary<int, Task<BekiImageResult>>();
        var spreadsList = plan.Spreads.OrderBy(s => s.Number).ToList();
        
        foreach (var spread in spreadsList)
        {
            if (adopted.TryGetValue(spread.Number, out var adoptedImage))
            {
                var result = new BekiImageResult
                {
                    SpreadNumber = spread.Number,
                    Image = adoptedImage,
                    Accepted = true,
                    Verdict = "Adopted from a previous run's accepted artwork.",
                    Attempts = 0,
                    Prompt = string.Empty,
                };
                spreads[spreadsList.IndexOf(spread)] = result;
            }
        }

        try
        {
            while (pending.Count > 0 || running.Count > 0)
            {
                // Clamped to one: a zero or negative setting would leave the loop with ready
                // work, nothing running, and nothing to await — a silent spin, not a slower book.
                var concurrency = Math.Max(1, _bekiOptions.SpreadConcurrency);

                var ready = pending.Where(s => dependencies[s.Number].All(d => completed.Contains(d))).ToList();

                // Nothing ready, nothing running, work left: a dependency that can never be
                // satisfied. SpreadDependencies is built so this cannot happen — every
                // dependency points at an earlier, distinct spread — but a loop that would
                // otherwise spin forever fails loudly instead.
                if (ready.Count == 0 && running.Count == 0)
                {
                    throw new InvalidOperationException(
                        "Beki spread scheduling stalled; unsatisfiable dependencies for "
                        + $"spread(s) {string.Join(", ", pending.Select(s => s.Number))}.");
                }

                foreach (var s in ready)
                {
                    if (running.Count >= concurrency)
                        break;

                    cancellationToken.ThrowIfCancellationRequested();
                    running.Add(s.Number, ProcessSpreadAsync(s));
                    pending.Remove(s);
                }

                if (running.Count > 0)
                {
                    var finishedTask = await Task.WhenAny(running.Values).ConfigureAwait(false);
                    var finishedNumber = running.First(x => x.Value == finishedTask).Key;
                    var result = await finishedTask.ConfigureAwait(false);
                    
                    running.Remove(finishedNumber);
                    completed.Add(finishedNumber);
                    
                    var index = spreadsList.FindIndex(s => s.Number == finishedNumber);
                    spreads[index] = result;
                }
            }
        }
        catch
        {
            // Cancel first, then drain: the siblings observe the linked token inside their own
            // provider calls, so the drain is bounded by a request timeout rather than by
            // however many minutes of image generation were still queued up.
            renderCts.Cancel();
            if (running.Count > 0)
            {
                await Task.WhenAll(running.Values.Select(t => t.ContinueWith(_ => {}))).ConfigureAwait(false);
            }
            throw;
        }

        var unanchored = castById.Keys.Concat(objById.Keys).Where(id => !anchors.ContainsKey(id)).ToList();
        if (unanchored.Count > 0)
        {
            warnings.Add(
                $"No accepted image ever established an anchor for: {string.Join(", ", unanchored)}. "
                + "Those characters/objects were drawn from their description every time.");
        }

        return new BekiBookResult
        {
            Plan = plan,
            AppearanceDescription = string.Empty,
            Cover = cover,
            Spreads = spreads.ToList(),
            Warnings = warnings,
        };
    }

    private async Task<MasterStory> PlanAsync(MasterStoryInput input, CancellationToken cancellationToken)
    {
        var result = await storyClient.CompleteAsync<MasterStory>(
            "gpt-5.6-sol",
            MasterStoryPromptV5.System(input),
            MasterStoryPromptV5.User(input),
            BekiBookPlanSchema.Name,
            BekiBookPlanSchema.Build(input.SpreadCount),
            cancellationToken);

        var plan = result.Value;
        if (plan.Spreads.Count != input.SpreadCount)
        {
            throw new InvalidOperationException(
                $"The Beki planner returned {plan.Spreads.Count} spreads, expected {input.SpreadCount}.");
        }

        return plan;
    }

    /// <summary>
    /// <inheritdoc cref="IBekiBookGenerator.DrawCoverAsync" />
    ///
    /// Its subject is the relationship rather than a scene from the story — the handoff asks for
    /// the child as the hero with Beki beside them, warm and lovable — so the shot instruction is
    /// written here rather than taken from the spread rhythm, and no text side is reserved: a
    /// title is typeset over the cover later and is never drawn.
    ///
    /// The cast is closed to two. One shipped cover put a plane beside the child and another put
    /// half the book's supporting characters there; a cover is the one picture a parent judges
    /// the whole book by, and it says who the book is about, which is the child and Beki. So the
    /// shot instruction says the setting stays simple and <see cref="CoverAvoidClause"/> names
    /// the third character the model would otherwise invent for it.
    /// </summary>
    public async Task<BekiImageResult> DrawCoverAsync(
        MasterStory plan, byte[] childPhoto, string childPhotoContentType, CancellationToken cancellationToken)
    {
        // No null branch: this cover is the child and Beki together, so it is one of the images
        // that may not be drawn without the master reference.
        var beki = RequireBekiReference("the cover");

        var prompt = IllustrationPrompt.ComposeBeki(
            plan.CharacterLock,
            plan.Cover.Scene,
            BekiIdentity.CoverContinuity,
            // The cover has no story text over it, so no side is reserved; "either" reads as a
            // free composition to the model rather than as a constraint it must satisfy.
            "either",
            "A warm hero portrait of the child with Beki beside them, inviting the reader in. "
            + "These two are the only characters on the cover; keep the setting simple and "
            + "iconic, one clear suggestion of the world behind them.",
            CoverAvoid(plan.Cover.Avoid));

        var references = new List<(byte[] Bytes, string ContentType, string Label)>
        {
            (childPhoto, childPhotoContentType, "Child reference photograph"),
            (beki, "image/png", BekiIdentity.ReferenceLabel),
        };

        return await DrawReviewedAsync(
            null, plan.Cover.Scene, "either", plan.CharacterLock, prompt, references, [], cancellationToken);
    }

    /// <summary>
    /// What must not be on a cover, whatever the plan's own avoid list says. Appended to it
    /// rather than replacing it — the plan knows this book's particular hazards, this knows the
    /// format's.
    /// </summary>
    private const string CoverAvoidClause =
        "any character other than the child and Beki, a second companion, additional creatures or "
        + "animals, other people, crowds, vehicles or machines drawn as characters, cluttered or "
        + "busy backgrounds";

    private static string CoverAvoid(string? planAvoid) =>
        string.IsNullOrWhiteSpace(planAvoid)
            ? CoverAvoidClause
            : $"{planAvoid.Trim()}, {CoverAvoidClause}";

    private async Task<BekiImageResult> DrawSpreadAsync(
        MasterStory plan,
        StorySpread spread,
        IReadOnlyDictionary<string, StoryCastMember> castById,
        IReadOnlyDictionary<string, StoryObjectItem> objById,
        IReadOnlyDictionary<string, byte[]> anchors,
        byte[] childPhoto,
        string childPhotoContentType,
        CancellationToken cancellationToken)
    {
        var textSide = BekiSpreadRhythm.TextSideFor(spread.Number);
        var shot = BekiSpreadRhythm.ShotFor(spread.Number);

        var presentCast = (spread.Characters ?? [])
            .Where(castById.ContainsKey)
            .ToList();

        var presentObjects = (spread.Objects ?? [])
            .Where(objById.ContainsKey)
            .ToList();

        var references = new List<(byte[] Bytes, string ContentType, string Label)>
        {
            (childPhoto, childPhotoContentType, "Child reference photograph"),
        };

        var continuity = new List<string>();
        var anchored = new List<string>();

        foreach (var id in presentCast)
        {
            var member = castById[id];
            if (anchors.TryGetValue(id, out var anchor))
            {
                /*
                  An anchored character keeps its description in the prompt, and the closing
                  clause is not decoration: a spread carrying two anchors once came back with the
                  same creature drawn twice, because nothing in the request told the two attached
                  references apart. The label is the character's name alone so the filename
                  carries it, the description stays in the sentence so the model knows which
                  picture is being talked about, and the "no other character" clause is what
                  stops one design being reused for both.
                */
                anchored.Add(id);
                references.Add((anchor, "image/png", member.Name));
                continuity.Add(
                    $"{member.Name} — {member.VisualDescription} — appears again here: keep "
                    + "it identical to its own continuity reference, and do not give any other "
                    + "character its design.");
            }
            else
            {
                continuity.Add($"Include {member.Name}: {member.VisualDescription}");
            }
        }

        foreach (var id in presentObjects)
        {
            var obj = objById[id];
            if (anchors.TryGetValue(id, out var anchor))
            {
                anchored.Add(id);
                references.Add((anchor, "image/png", obj.Name));
                continuity.Add(
                    $"{obj.Name} — {obj.VisualDescription} — appears again here: "
                    + "keep it identical to its own continuity reference, and do not give any other object its design.");
            }
            else
            {
                continuity.Add($"Include {obj.Name}: {obj.VisualDescription}");
            }
        }

        if (SpreadNeedsBeki(spread))
        {
            var beki = RequireBekiReference($"spread {spread.Number}");
            references.Add((beki, "image/png", BekiIdentity.ReferenceLabel));
            continuity.Add(BekiIdentity.SpreadContinuity);
        }

        var ctaSafe = spread.Number == BookFormat.SpreadCount;
        var prompt = IllustrationPrompt.ComposeBeki(
            plan.CharacterLock,
            spread.Illustration.Scene,
            string.Join("\n", continuity),
            textSide,
            shot,
            spread.Illustration.Avoid,
            ctaSafe);

        return await DrawReviewedAsync(
            spread.Number,
            spread.Illustration.Scene,
            textSide,
            plan.CharacterLock,
            prompt,
            references,
            anchored,
            cancellationToken,
            ctaSafe);
    }

    /// <summary>
    /// Whether this spread's illustration must be drawn with Beki's master reference attached.
    ///
    /// The characters list is the plan's own answer and is trusted outright. The scene text is a
    /// backstop for the case that shipped a book: a spread whose scene had Beki pointing at
    /// something while its characters list did not mention Beki. The reference was not attached,
    /// the prompt still described Beki doing things, and the model drew whatever the words
    /// suggested — an invented Beki, which is the one thing this format promises never to
    /// produce. <see cref="BekiPlanValidator"/> now reports such a plan as broken and the planner
    /// gets a corrective retry, so this should not be reached for a freshly written plan; it is
    /// reached for a plan written before that rule existed and drawn after it, which the
    /// fulfilment job does every time it picks up an older purchase.
    ///
    /// The spread's Avoid field wins only when it explicitly forbids Beki — "do not show Beki",
    /// the one spread of a book where the child is alone. A mere mention of Beki in Avoid is the
    /// opposite: "Beki with wings" forbids the wings, and suppressing the reference over it would
    /// draw an invented Beki on exactly the spread that named the real one.
    /// </summary>
    private static bool SpreadNeedsBeki(StorySpread spread)
    {
        var listed = (spread.Characters ?? [])
            .Any(id => id.Equals(BekiPlanValidator.BekiId, StringComparison.OrdinalIgnoreCase));

        if (listed) return true;

        return BekiPlanValidator.NamesBeki(spread.Illustration.Scene)
            && !BekiPlanValidator.ForbidsBeki(spread.Illustration.Avoid);
    }

    /// <summary>
    /// Draw, review, and on a refusal redraw once with the reviewer's own words appended.
    ///
    /// The original prompt is kept whole and the correction added to it, as the handoff requires:
    /// a rewritten prompt is a different picture, and the point of a retry is the same picture
    /// without the fault. An image that never passes is returned anyway, marked — a book with a
    /// flawed spread can be looked at and judged; a book with a hole cannot.
    /// </summary>
    /// <param name="characterLock">
    /// The child's fixed appearance, passed on to the reviewer as well as the illustrator. A
    /// reviewer asked whether the child's clothes match, without being told what they are
    /// supposed to be, can only answer from the photograph — and the photograph shows the child's
    /// real clothes, not the ones the book dressed them in.
    /// </param>
    private async Task<BekiImageResult> DrawReviewedAsync(
        int? spreadNumber,
        string scene,
        string textSide,
        string characterLock,
        string prompt,
        IReadOnlyList<(byte[] Bytes, string ContentType, string Label)> references,
        IReadOnlyList<string> anchored,
        CancellationToken cancellationToken,
        bool ctaSafe = false)
    {
        var label = spreadNumber is null ? "cover" : $"spread {spreadNumber}";
        var reference = BekiImageReferences.ToStoryImageReference(references);

        var reviewRatio = spreadNumber is null ? CoverCropRatio : SpreadCropRatio;
        var attemptDetails = new List<BekiImageAttempt>();

        var genSw = System.Diagnostics.Stopwatch.StartNew();
        var image = await openAi.GenerateStoryImageAsync(
            prompt, reference, cancellationToken, SpreadImageSize);
        genSw.Stop();

        var reviewCopy = DownscaleForReview(SpreadArtCrop.CropToRatio(image, reviewRatio));
        var revSw = System.Diagnostics.Stopwatch.StartNew();
        var verdict = await ReviewAsync(
            reviewCopy,
            scene,
            textSide,
            characterLock,
            references,
            cancellationToken,
            ctaSafe);
        revSw.Stop();

        attemptDetails.Add(new BekiImageAttempt(genSw.ElapsedMilliseconds, revSw.ElapsedMilliseconds, verdict, IsPass(verdict)));
        var attempts = 1;

        while (!IsPass(verdict) && attempts <= MaxRegenerations)
        {
            logger.LogInformation("Beki {Label} refused by QA; redrawing. {Verdict}", label, verdict);

            var corrected = $"{prompt}\n\n{Corrections(verdict)}";
            genSw.Restart();
            image = await openAi.GenerateStoryImageAsync(
                corrected, reference, cancellationToken, SpreadImageSize);
            genSw.Stop();

            reviewCopy = DownscaleForReview(SpreadArtCrop.CropToRatio(image, reviewRatio));
            revSw.Restart();
            verdict = await ReviewAsync(
                reviewCopy,
                scene,
                textSide,
                characterLock,
                references,
                cancellationToken,
                ctaSafe);
            revSw.Stop();

            attemptDetails.Add(new BekiImageAttempt(genSw.ElapsedMilliseconds, revSw.ElapsedMilliseconds, verdict, IsPass(verdict)));
            attempts++;
        }

        return new BekiImageResult
        {
            SpreadNumber = spreadNumber,
            Image = image,
            Accepted = IsPass(verdict),
            Verdict = verdict,
            Attempts = attemptDetails.Count,
            AttemptDetails = attemptDetails,
            Prompt = prompt,
            AnchoredCharacters = anchored,
        };
    }

    private static byte[] DownscaleForReview(byte[] imageBytes)
    {
        using var image = Image.Load<Rgba32>(imageBytes);
        if (image.Width <= 1024) return imageBytes;

        var height = (int)Math.Round(image.Height * (1024.0 / image.Width));
        image.Mutate(x => x.Resize(1024, height, KnownResamplers.Lanczos3));

        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    private Task<string> ReviewAsync(
        byte[] image,
        string scene,
        string textSide,
        string characterLock,
        IReadOnlyList<(byte[] Bytes, string ContentType, string Label)> references,
        CancellationToken cancellationToken,
        bool ctaSafe = false) =>
        openAi.ReviewIllustrationAsync(
            image, BekiImageQaPrompt.For(scene, textSide, characterLock, ctaSafe), references, cancellationToken);

    /// <summary>
    /// Forgiving about the wrapper, strict about the answer.
    ///
    /// A verdict inside a code fence is still a verdict — <see cref="ModelJsonSanitizer"/> pulls
    /// the object out the same way <see cref="Corrections"/> does — but once it parses, only an
    /// exact <c>"status":"PASS"</c> counts. A plain substring search used to accept a verdict that
    /// merely mentioned PASS anywhere, including inside an issue explaining why something did NOT
    /// pass; parsing the field is the difference between reading the verdict and reading around
    /// it. The substring check survives only as the fallback for a verdict whose JSON will not
    /// parse at all — and even then, anything unrecognisable stays a failure.
    /// </summary>
    private static bool IsPass(string verdict)
    {
        var json = ModelJsonSanitizer.ExtractJsonObject(verdict);
        if (string.IsNullOrWhiteSpace(json))
        {
            return verdict.Contains("\"PASS\"", StringComparison.OrdinalIgnoreCase);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("status", out var status)
                && status.ValueKind == JsonValueKind.String
                && string.Equals(status.GetString(), "PASS", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return verdict.Contains("\"PASS\"", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The faults out of the verdict, and nothing else.
    ///
    /// The whole JSON blob used to be pasted onto the prompt, and the reviewer does not fill
    /// `issues` with faults alone. A real refusal read: "no unwanted text, lettering, logo, frame
    /// or QR code is present", "no Beki character is present, which is correct", and then, buried
    /// among them, the one thing actually wrong. The redraw was being asked to correct seven
    /// observations that were already right — five retries across two runs fixed nothing, and one
    /// added a duplicate character.
    ///
    /// So the array is parsed out and the items that report an absence of a problem are dropped,
    /// leaving a short list of instructions. When the verdict will not parse the raw text is used
    /// rather than nothing: a noisy correction still beats redrawing with no correction at all.
    /// </summary>
    internal static string Corrections(string verdict)
    {
        var json = ModelJsonSanitizer.ExtractJsonObject(verdict);
        if (string.IsNullOrWhiteSpace(json))
        {
            return $"Correct these problems from the previous attempt: {verdict.Trim()}";
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("issues", out var issues)
                || issues.ValueKind != JsonValueKind.Array)
            {
                return $"Correct these problems from the previous attempt: {verdict.Trim()}";
            }

            var faults = issues
                .EnumerateArray()
                .Select(issue => issue.GetString())
                .Where(issue => !string.IsNullOrWhiteSpace(issue))
                .Select(issue => issue!.Trim())
                .Where(IsFault)
                .ToList();

            if (faults.Count == 0)
            {
                return "The previous attempt was refused. Follow the composition rules above exactly.";
            }

            var numbered = string.Join("\n", faults.Select((fault, index) => $"{index + 1}. {fault}"));
            return "The previous attempt was refused for these reasons. Draw it again, the same "
                + $"scene, with each of them fixed:\n{numbered}";
        }
        catch (JsonException)
        {
            return $"Correct these problems from the previous attempt: {verdict.Trim()}";
        }
    }

    /// <summary>
    /// True for an item that names something wrong. The reviewer writes its clean findings in the
    /// same list as its faults — "there is no unwanted text", "no Beki is present, which is
    /// correct" — and those are the ones a redraw must not be handed.
    /// </summary>
    private static bool IsFault(string issue)
    {
        var text = issue.ToLowerInvariant();

        // "no X is present/visible", "there is no unwanted …" — an absence being reported as fine.
        if (text.StartsWith("no ", StringComparison.Ordinal)
            || text.StartsWith("there is no ", StringComparison.Ordinal)
            || text.StartsWith("there are no ", StringComparison.Ordinal))
        {
            // Unless it goes on to say the absence is itself the problem.
            return text.Contains("but ", StringComparison.Ordinal)
                || text.Contains("as requested", StringComparison.Ordinal)
                || text.Contains("required", StringComparison.Ordinal);
        }

        /*
          Approvals the reviewer files under issues anyway: "…is clear and correct", "…which is
          correct", "not applicable". A fault can also contain the word correct — "the colour is
          not correct" — so a bare search for it would throw away real findings, and the negations
          are checked first.

          This is a heuristic over prose and it will not be exactly right. It errs towards keeping
          an item: a redraw handed one observation too many is a worse prompt, while a redraw
          handed one fault too few is a spread that stays broken.
        */
        var negated = text.Contains("not correct", StringComparison.Ordinal)
            || text.Contains("incorrect", StringComparison.Ordinal)
            || text.Contains("does not", StringComparison.Ordinal)
            || text.Contains("but ", StringComparison.Ordinal);

        if (negated) return true;

        return !text.Contains("is correct", StringComparison.Ordinal)
            && !text.Contains("and correct", StringComparison.Ordinal)
            && !text.Contains("are correct", StringComparison.Ordinal)
            && !text.Contains("matches the request", StringComparison.Ordinal)
            && !text.Contains("not applicable", StringComparison.Ordinal)
            && !text.Contains("no issue", StringComparison.Ordinal);
    }

    /// <summary>
    /// Beki's canonical picture, or an exception. Called only for an illustration that requires
    /// Beki — the cover, and every spread that lists or names Beki.
    ///
    /// A missing file used to be a warning: draw the picture anyway, from the words alone, on the
    /// reasoning that a book with a described Beki beats no book. Two shipped books are the
    /// argument against it. Beki is not this book's character, it is the platform's one character,
    /// and the file at <see cref="BekiIdentity.ReferenceAssetPath"/> is the only thing that says
    /// what it looks like — so an image drawn without it does not contain a slightly-off Beki, it
    /// contains a different character wearing the name. That is a worse outcome than a failed job:
    /// a failure is retried once the asset is deployed, while an invented Beki is printed, posted
    /// and read to a child.
    ///
    /// The file read happens at most once per generator instance — a book asks for it up to nine
    /// times and it never changes mid-run — but the throw is unconditional, so a run that is
    /// missing the asset fails on its first illustration rather than producing eight wrong ones.
    /// </summary>
    /// <exception cref="InvalidOperationException">The asset is missing or unreadable.</exception>
    private byte[] RequireBekiReference(string context)
    {
        // The whole check sits under the lock, not just the load: spreads now draw
        // concurrently, the fields are ordinary (non-volatile), and a lock-free fast path
        // would be trading a once-per-book file read for a memory-model argument. The read
        // happens once; every later call takes an uncontended lock and returns the cache.
        byte[]? cached;
        lock (_bekiReferenceLock)
        {
            if (!_bekiReferenceLoadAttempted)
            {
                try
                {
                    var path = Path.Combine(AppContext.BaseDirectory, BekiReferencePath);
                    if (File.Exists(path))
                    {
                        _cachedBekiReference = File.ReadAllBytes(path);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Could not read the Beki master reference.");
                }

                _bekiReferenceLoadAttempted = true;
            }

            cached = _cachedBekiReference;
        }

        if (cached is null)
        {
            logger.LogError(
                "The Beki master reference is missing from {Path}; {Context} requires it and cannot be drawn.",
                Path.Combine(AppContext.BaseDirectory, BekiReferencePath), context);

            throw new InvalidOperationException(
                $"The Beki master reference ({BekiReferencePath}) is missing or unreadable, and "
                + $"{context} requires it. No illustration containing Beki may be drawn without "
                + "the one image that defines the character.");
        }

        return cached;
    }
}
