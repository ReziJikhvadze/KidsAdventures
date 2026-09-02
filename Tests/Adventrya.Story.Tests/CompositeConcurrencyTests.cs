using System.Collections.Concurrent;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using AdventurePacks.Api.Services.Story.Prompts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The parallel spread campaign: what it is allowed to overlap, and what it must not.
///
/// The measurement that started it is one completed book at 651 seconds, drawn strictly one page at
/// a time, of which most was waiting. The risk it introduces is subtler than "too many calls at
/// once": the fulfilment callback on the other side of this pipeline mutates a dictionary, advances
/// a counter and rewrites the one manifest blob a resumed run reads, so a page delivered
/// concurrently or out of order costs a book its record of itself.
///
/// So every test here is about a boundary rather than about speed. The limit holds. Delivery stays
/// single-file and in the book's order however the pages actually finish. The first terminal
/// failure ends the run rather than letting six more pictures be paid for. A run cut short still
/// resumes into the same book. And the caller's own cancellation is still the caller's.
///
/// The stubs here genuinely yield, which the rest of the composite tests do not need to: a stub
/// that returns a completed task makes every "concurrently" in this file a lie the tests would
/// still pass.
/// </summary>
public class CompositeConcurrencyTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "nina_dinosaurs", name);

    private static string ScenarioFixture() =>
        File.ReadAllText(FixturePath("visual_scenario_output_v2.json"));

    private static readonly IReadOnlyDictionary<int, string> ScenesByPage = ReadScenes();

    private static IReadOnlyDictionary<int, string> ReadScenes()
    {
        var scenario = VisualScenarioValidator.Validate(ScenarioFixture()).Scenario!;
        return scenario.Spreads!.ToDictionary(spread => spread.Page, spread => spread.ChildWorldScene!);
    }

    // ---------------------------------------------------------------------------------------
    // The limit
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Spread one is drawn alone, then the rest run together up to the configured limit and never
    /// past it.
    ///
    /// Alone is not a scheduling detail: spread one's accepted base is the child appearance anchor
    /// every later spread is drawn and reviewed against, so a page that started beside it would be
    /// a page drawn without one.
    /// </summary>
    [Fact]
    public async Task Spread_one_draws_alone_and_the_rest_run_up_to_the_configured_limit()
    {
        var images = new GatedImageService { DelayMs = 30 };

        var result = await Pipeline(images, spreadConcurrency: 3)
            .RunAsync(Request(), CancellationToken.None);

        Assert.Equal(BookFormat.SpreadCount, result.Spreads.Count);
        Assert.Equal(BookFormat.SpreadCount, images.ImagePages.Count);

        // Never more at once than the configuration allows…
        Assert.True(images.MaxInFlight <= 3, $"{images.MaxInFlight} spreads were drawing at once.");

        // …and genuinely more than one, or this test would pass on the sequential pipeline it
        // exists to replace.
        Assert.True(images.MaxInFlight > 1, "no two spreads ever overlapped.");

        // The anchor page ran on its own: nothing else was in flight while it was.
        Assert.Equal(1, images.InFlightDuring[1]);
        Assert.Equal(1, images.ImagePages.First());
    }

    /// <summary>
    /// A limit of one is the sequential pipeline this campaign started from, restored by
    /// configuration alone — which is the rollback if the pro-tier image model turns out to rate
    /// limit at four.
    /// </summary>
    [Fact]
    public async Task A_limit_of_one_restores_strictly_sequential_drawing()
    {
        var images = new GatedImageService { DelayMs = 5 };

        await Pipeline(images, spreadConcurrency: 1).RunAsync(Request(), CancellationToken.None);

        Assert.Equal(1, images.MaxInFlight);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], images.ImagePages);
    }

    // ---------------------------------------------------------------------------------------
    // Delivery
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// However the pages finish, the callback sees them one at a time and in the book's order.
    ///
    /// The pages here finish backwards — eight first, two last — which is what the ordering claim
    /// has to survive. On the other side of this callback a job stores a picture, writes a receipt,
    /// advances a progress bar and rewrites the manifest that decides what a resumed run may adopt;
    /// two of those at once loses whichever page lost the race, and out of order reports a book as
    /// further along than it is.
    /// </summary>
    [Fact]
    public async Task Pages_are_delivered_one_at_a_time_in_spread_order_however_they_finish()
    {
        var images = new GatedImageService { Gated = [2, 3, 4, 5, 6, 7, 8] };
        var delivery = new RecordingDelivery();

        var run = Pipeline(images, spreadConcurrency: 8).RunAsync(
            Request(onSpread: delivery.DeliverAsync), CancellationToken.None);

        // All seven are in flight before any of them is allowed to finish.
        await images.WaitUntilStarted(pages: 7, cancellationToken: TestTimeout);

        // Nothing has been delivered but spread one, which was drawn and delivered on its own.
        Assert.Equal([1], delivery.Pages);

        // Finish them backwards.
        foreach (var page in (int[])[8, 7, 6, 5, 4, 3])
        {
            images.Release(page);

            // Page two is still drawing, so nothing after it may be handed over yet.
            await Task.Delay(10);
            Assert.Equal([1], delivery.Pages);
        }

        images.Release(2);
        await run;

        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], delivery.Pages);
        Assert.False(delivery.Overlapped, "two deliveries ran at the same time.");
    }

    // ---------------------------------------------------------------------------------------
    // Fail-fast
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The first page to give up ends the run: no further picture is bought, no further page is
    /// reviewed, and no further page is handed to the fulfilment job.
    ///
    /// A book fails as a book. Left running, the six spreads still in flight would each finish a
    /// picture and a review for a pack that is already going to be marked failed — the most
    /// expensive way there is to produce nothing.
    /// </summary>
    [Fact]
    public async Task The_first_terminal_failure_stops_every_other_spread()
    {
        var images = new GatedImageService
        {
            // Page two never finishes drawing; page three fails its review while it waits.
            Gated = [2],
            FailAtReview = [3],
            DelayMs = 5,
        };

        var delivery = new RecordingDelivery();

        var failure = await Assert.ThrowsAsync<CompositePipelineException>(() =>
            Pipeline(images, spreadConcurrency: 2).RunAsync(
                Request(onSpread: delivery.DeliverAsync), CancellationToken.None));

        // The run failed of the page that actually failed, not of the cancellation that followed.
        Assert.Equal(CompositeFailureCodes.ImageQaFailed, failure.FailureCode);
        Assert.Equal(3, failure.Page);

        // Only the pages that had already started were ever drawn: spread one alone, then two and
        // three under a limit of two. Nothing was started for pages four to eight.
        Assert.Equal([1, 2, 3], images.ImagePages.OrderBy(page => page));
        Assert.DoesNotContain(images.ImagePages, page => page >= 4);

        // And no page was reviewed after the failure — page two never got that far, because it was
        // cancelled inside its generation call.
        Assert.Equal([1, 3], images.ReviewPages.OrderBy(page => page));

        // The fulfilment job was told about spread one and nothing else. A delivery after a
        // terminal failure would write a manifest for a book that does not exist.
        Assert.Equal([1], delivery.Pages);
    }

    /// <summary>
    /// A failure inside the delivery callback is terminal in the same way — it is the fulfilment
    /// job's storage failing, which is not something the remaining spreads should keep drawing
    /// through.
    /// </summary>
    [Fact]
    public async Task A_failing_delivery_callback_stops_the_remaining_spreads()
    {
        var images = new GatedImageService { DelayMs = 5 };
        var delivered = new ConcurrentQueue<int>();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Pipeline(images, spreadConcurrency: 2).RunAsync(
                Request(onSpread: spread =>
                {
                    delivered.Enqueue(spread.Page);

                    return spread.Page == 2
                        ? throw new InvalidOperationException("the blob store is unreachable.")
                        : Task.CompletedTask;
                }),
                CancellationToken.None));

        Assert.Contains("blob store", failure.Message);
        Assert.Equal([1, 2], delivered);
        Assert.True(images.ImagePages.Count < BookFormat.SpreadCount);
    }

    // ---------------------------------------------------------------------------------------
    // Cancellation
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The caller's own cancellation stays the caller's: it is not reinterpreted as a book failure,
    /// because a job cancelled by a deployment has to be requeued and resumed rather than marked
    /// failed and refunded.
    /// </summary>
    [Fact]
    public async Task The_callers_cancellation_propagates_rather_than_becoming_a_book_failure()
    {
        var images = new GatedImageService { Gated = [2, 3, 4, 5, 6, 7, 8] };
        using var cancellation = new CancellationTokenSource();

        var run = Pipeline(images, spreadConcurrency: 4).RunAsync(Request(), cancellation.Token);

        await images.WaitUntilStarted(pages: 4, cancellationToken: TestTimeout);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        // Not a CompositePipelineException: nothing about this book was wrong.
        Assert.True(run.IsCanceled || run.Exception?.InnerException is OperationCanceledException);
    }

    // ---------------------------------------------------------------------------------------
    // The anchor announcement — what lets the cover wrap draw beside the spreads
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The moment spread one is accepted, the run says so — with the scenario, the identity lock
    /// and the anchor — while spreads two to eight are still being drawn.
    ///
    /// That is the whole point of the hook. The cover wrap needs exactly those three things and
    /// nothing the other seven pages produce, so a caller that waited for the run to finish before
    /// drawing it was spending one more image call's worth of wall clock in series for no reason.
    /// Every page but the first is held open here, so an announcement that arrived at all arrived
    /// while they were in flight.
    /// </summary>
    [Fact]
    public async Task The_anchor_is_announced_while_the_remaining_spreads_are_still_drawing()
    {
        var images = new GatedImageService { Gated = [2, 3, 4, 5, 6, 7, 8] };
        var announcements = 0;
        var announced = new TaskCompletionSource<CompositeAnchorAccepted>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var run = Pipeline(images, spreadConcurrency: 4).RunAsync(
            Request(onAnchorAccepted: accepted =>
            {
                Interlocked.Increment(ref announcements);
                announced.TrySetResult(accepted);
                return Task.CompletedTask;
            }),
            CancellationToken.None);

        var accepted = await announced.Task.WaitAsync(TestTimeout);

        // Announced with the book still open: not one of the seven gated pages can have finished.
        Assert.False(run.IsCompleted);
        Assert.Equal(IdentitySpec, accepted.Identity);
        Assert.NotEmpty(accepted.AnchorBasePng);

        foreach (var page in (int[])[2, 3, 4, 5, 6, 7, 8])
        {
            images.Release(page);
        }

        var result = await run;

        // Once, and with the very bytes the run then handed every later spread as its anchor.
        Assert.Equal(1, announcements);
        Assert.Equal(result.Anchor, accepted.AnchorBasePng);
        Assert.Equal(result.Spreads[0].BasePng, accepted.AnchorBasePng);
        Assert.All(images.Anchors.Where(pair => pair.Key != 1),
            pair => Assert.Equal(accepted.AnchorBasePng, pair.Value));
    }

    /// <summary>
    /// A resumed run whose anchor was adopted announces it before drawing anything at all — the
    /// stored spread one is settled before this run's first image call, so the cover may start
    /// beside the first page this run draws rather than after the last.
    /// </summary>
    [Fact]
    public async Task An_adopted_anchor_is_announced_before_the_first_image_call()
    {
        var images = new GatedImageService { DelayMs = 5 };
        var stored = new Dictionary<int, byte[]>
        {
            [1] = Png(1536, 717, red: 1),
            [2] = Png(1536, 717, red: 2),
        };
        var anchor = Png(1536, 717, red: 77);

        var imageCallsAtAnnouncement = -1;
        byte[]? announcedAnchor = null;

        await Pipeline(images, spreadConcurrency: 3).RunAsync(
            Request(
                resume: new CompositeResumeState(ScenarioFixture(), stored, stored)
                {
                    IdentitySpecJson = CompositeChildIdentity.ToStoredJson(IdentitySpec),
                    AnchorBasePng = anchor,
                },
                onAnchorAccepted: accepted =>
                {
                    imageCallsAtAnnouncement = images.ImagePages.Count;
                    announcedAnchor = accepted.AnchorBasePng;
                    return Task.CompletedTask;
                }),
            CancellationToken.None);

        Assert.Equal(0, imageCallsAtAnnouncement);
        Assert.Equal(anchor, announcedAnchor);
    }

    /// <summary>
    /// A fully adopted resume still announces — the caller drawing a cover for a rebuilt book needs
    /// the stored anchor exactly as much — and a run with nothing to say says nothing.
    /// </summary>
    [Fact]
    public async Task A_fully_adopted_resume_announces_the_stored_anchor_and_draws_nothing()
    {
        var images = new GatedImageService();
        var stored = Enumerable.Range(1, BookFormat.SpreadCount)
            .ToDictionary(page => page, page => Png(1536, 717, red: (byte)page));

        byte[]? announcedAnchor = null;

        await Pipeline(images, spreadConcurrency: 3).RunAsync(
            Request(
                resume: new CompositeResumeState(ScenarioFixture(), stored, stored)
                {
                    IdentitySpecJson = CompositeChildIdentity.ToStoredJson(IdentitySpec),
                    AnchorBasePng = stored[1],
                },
                onAnchorAccepted: accepted =>
                {
                    announcedAnchor = accepted.AnchorBasePng;
                    return Task.CompletedTask;
                }),
            CancellationToken.None);

        Assert.Empty(images.ImagePages);
        Assert.Equal(stored[1], announcedAnchor);
    }

    // ---------------------------------------------------------------------------------------
    // Resume
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A concurrent run cut short by a failure leaves exactly the pages it delivered, and a resumed
    /// run adopts those and redraws the rest — anchored to the same spread one, so the second half
    /// of the book is drawn to the same child as the first.
    ///
    /// This is the property serialized delivery exists for. The pages the fulfilment job was told
    /// about are a prefix of the book, in order, so what an interrupted run leaves behind is a
    /// coherent front half rather than pages three, five and eight.
    /// </summary>
    [Fact]
    public async Task A_run_cut_short_by_a_failure_resumes_into_the_same_book()
    {
        var first = new GatedImageService { Gated = [5], FailAtReview = [5], DelayMs = 5 };
        var delivery = new RecordingDelivery();

        // Page five is held until pages two to four have been handed over, so what the interrupted
        // run leaves behind is deterministic rather than a race.
        delivery.OnDelivered = page =>
        {
            if (page == 4) first.Release(5);
        };

        var failure = await Assert.ThrowsAsync<CompositePipelineException>(() =>
            Pipeline(first, spreadConcurrency: 3).RunAsync(
                Request(onSpread: delivery.DeliverAsync), CancellationToken.None));

        Assert.Equal(CompositeFailureCodes.ImageQaFailed, failure.FailureCode);
        Assert.Equal([1, 2, 3, 4], delivery.Pages);

        // What the fulfilment job would have stored: the four delivered pages and their bases.
        var storedSpreads = delivery.Delivered.ToDictionary(
            spread => spread.Page, spread => spread.CompositePng);
        var storedBases = delivery.Delivered.ToDictionary(
            spread => spread.Page, spread => spread.BasePng);

        var anchor = storedBases[1];

        var second = new GatedImageService { DelayMs = 5 };

        var resumed = await Pipeline(second, spreadConcurrency: 3).RunAsync(
            Request(resume: new CompositeResumeState(ScenarioFixture(), storedSpreads, storedBases)
            {
                IdentitySpecJson = CompositeChildIdentity.ToStoredJson(IdentitySpec),
                AnchorBasePng = anchor,
            }),
            CancellationToken.None);

        // Four adopted, four redrawn, and the identity spec adopted rather than derived again.
        Assert.Equal(BookFormat.SpreadCount, resumed.Spreads.Count);
        Assert.Equal([5, 6, 7, 8], second.ImagePages.OrderBy(page => page));
        Assert.Equal(0, second.IdentityCalls);

        Assert.Equal([1, 2, 3, 4], resumed.Spreads.Where(s => s.Adopted).Select(s => s.Page));
        Assert.Equal([5, 6, 7, 8], resumed.Spreads.Where(s => !s.Adopted).Select(s => s.Page));

        // Every redrawn page was matched against the stored spread one, not against a new one.
        Assert.All(second.Anchors.Values, attached => Assert.Equal(anchor, attached));
    }

    // =======================================================================================
    // Harness
    // =======================================================================================

    /// <summary>Fails a hung test in seconds rather than hanging the suite.</summary>
    private static CancellationToken TestTimeout => new CancellationTokenSource(
        TimeSpan.FromSeconds(30)).Token;

    private static readonly ChildIdentitySpec IdentitySpec = new()
    {
        HairColor = "dark brown",
        HairStyle = "shoulder-length wavy with a soft fringe",
        EyeColor = "brown",
        SkinTone = "light warm",
        Eyebrows = "soft, medium-thick, gently arched",
        Glasses = "none",
        FaceShape = "round with a soft chin",
        DistinctiveFeatures = "light freckles across the nose; a dimple on the left cheek",
    };

    private static CompositeBookPipeline Pipeline(IOpenAiService images, int spreadConcurrency) =>
        new(new ScenarioClient(ScenarioFixture()),
            images,
            new UnusedStoryService(),
            Options.Create(new BekiOptions
            {
                CompositePipelineEnabled = true,
                SpreadConcurrency = spreadConcurrency,
            }),
            Options.Create(new BekiPrintLayoutOptions()),
            NullLogger<CompositeBookPipeline>.Instance);

    private static CompositeBookRequest Request(
        CompositeResumeState? resume = null,
        Func<CompositeSpreadResult, Task>? onSpread = null,
        Func<CompositeAnchorAccepted, Task>? onAnchorAccepted = null) => new()
    {
        OnAnchorAccepted = onAnchorAccepted,
        Context = new CompositeBookContext
        {
            JobId = Guid.NewGuid(),
            Input = new BookGenerationInput
            {
                ChildName = "ნინა",
                ChildAge = 1,
                ChildGender = "girl",
                ThemeId = "Dinosaurs",
                ChildPhotoRef = "books/nina/photo.jpg",
            }
        },
        ExistingPlan = Plan(),
        ChildPhoto = Png(512, 512),
        ChildPhotoContentType = "image/png",
        Resume = resume ?? CompositeResumeState.Empty,
        OnSpread = onSpread,
    };

    private static MasterStory Plan()
    {
        using var input = JsonDocument.Parse(
            File.ReadAllText(FixturePath("visual_scenario_input_v2.json")));

        var spreads = input.RootElement
            .GetProperty("story_pages")
            .EnumerateArray()
            .Select(page => new StorySpread
            {
                Number = page.GetProperty("page").GetInt32(),
                Title = string.Empty,
                Caption = string.Empty,
                Text = page.GetProperty("story_text").GetString()!,
                Characters = ["child", "beki"],
                Objects = [],
                Illustration = new IllustrationBrief { Scene = "The child in the valley." },
            })
            .ToList();

        return new MasterStory
        {
            Concept = new StoryConcept
            {
                Title = "ბაფუს დაკარგული ბილიკი",
                Outline = spreads.Select(spread => spread.Text).ToList(),
            },
            Spreads = spreads,
            CharacterLock = "A child.",
            Cover = new IllustrationBrief { Scene = "The child at the edge of the valley." },
            WorldLock = "A warm golden valley.",
            Cast = [],
            Objects = [],
        };
    }

    private static byte[] Png(int width, int height, byte red = 0) =>
        SyntheticImages.SolidPng(width, height, red);

    /// <summary>
    /// The pages the fulfilment job was handed, in the order it was handed them, and whether two
    /// hand-overs were ever in progress at once.
    /// </summary>
    private sealed class RecordingDelivery
    {
        private readonly ConcurrentQueue<int> _pages = new();
        private readonly ConcurrentQueue<CompositeSpreadResult> _delivered = new();
        private int _inside;

        public IReadOnlyList<int> Pages => _pages.ToList();

        public IReadOnlyList<CompositeSpreadResult> Delivered => _delivered.ToList();

        public bool Overlapped { get; private set; }

        /// <summary>Runs inside the callback, so a test can act on the book's own progress.</summary>
        public Action<int>? OnDelivered { get; set; }

        public async Task DeliverAsync(CompositeSpreadResult spread)
        {
            if (Interlocked.Increment(ref _inside) != 1)
            {
                Overlapped = true;
            }

            try
            {
                // A real callback uploads a picture and rewrites a manifest; the await is what makes
                // "one at a time" a claim about more than a synchronous method.
                await Task.Delay(5);

                _pages.Enqueue(spread.Page);
                _delivered.Enqueue(spread);
                OnDelivered?.Invoke(spread.Page);
            }
            finally
            {
                Interlocked.Decrement(ref _inside);
            }
        }
    }

    /// <summary>
    /// An image door that can be held open one page at a time, so a test can decide the order the
    /// pages finish in — which is the only way to say anything true about out-of-order completion.
    /// </summary>
    private sealed class GatedImageService : IOpenAiService
    {
        private readonly ConcurrentDictionary<int, TaskCompletionSource> _gates = new();
        private readonly SemaphoreSlim _started = new(0);
        private int _inFlight;
        private int _maxInFlight;

        public ConcurrentQueue<int> ImagePages { get; } = new();

        public ConcurrentQueue<int> ReviewPages { get; } = new();

        /// <summary>The anchor image each page was drawn against, or null where there was none.</summary>
        public ConcurrentDictionary<int, byte[]?> Anchors { get; } = new();

        /// <summary>How many spreads were drawing at the moment each page started.</summary>
        public ConcurrentDictionary<int, int> InFlightDuring { get; } = new();

        public int MaxInFlight => Volatile.Read(ref _maxInFlight);

        public int IdentityCalls { get; private set; }

        /// <summary>Pages whose generation blocks until <see cref="Release"/>.</summary>
        public HashSet<int> Gated { get; init; } = [];

        /// <summary>Pages whose review comes back as a terminal human_review verdict.</summary>
        public HashSet<int> FailAtReview { get; init; } = [];

        /// <summary>How long an ungated generation takes. Non-zero, so the tasks really interleave.</summary>
        public int DelayMs { get; init; } = 10;

        public void Release(int page) =>
            Gate(page).TrySetResult();

        /// <summary>Waits until this many pages have entered generation.</summary>
        public async Task WaitUntilStarted(int pages, CancellationToken cancellationToken)
        {
            for (var i = 0; i < pages; i++)
            {
                await _started.WaitAsync(cancellationToken);
            }
        }

        private TaskCompletionSource Gate(int page) => _gates.GetOrAdd(
            page, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        public async Task<byte[]> GenerateStoryImageAsync(
            string imagePrompt, StoryImageReference? reference,
            CancellationToken cancellationToken, string? imageSize = null,
            bool requireReferences = false)
        {
            var page = PageOf(imagePrompt);

            ImagePages.Enqueue(page);

            // From v1.2 the appearance anchor is the FIRST attached reference on every spread but
            // the first, so it arrives in the lead slot rather than among the labelled cast. The
            // prompt's own first line is what says which shape this call has.
            Anchors[page] = imagePrompt.Contains(
                "Image 1 - child appearance anchor", StringComparison.Ordinal)
                ? reference?.CharacterAnchorBytes
                : null;

            var inFlight = Interlocked.Increment(ref _inFlight);
            InFlightDuring[page] = inFlight;

            int max;
            while (inFlight > (max = Volatile.Read(ref _maxInFlight)))
            {
                Interlocked.CompareExchange(ref _maxInFlight, inFlight, max);
            }

            _started.Release();

            try
            {
                if (Gated.Contains(page))
                {
                    await Gate(page).Task.WaitAsync(cancellationToken);
                }
                else
                {
                    await Task.Delay(DelayMs, cancellationToken);
                }

                return Png(1536, 1024, (byte)(10 + page));
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        public Task<string> ReviewIllustrationAsync(
            byte[] imageBytes, string reviewPrompt,
            IReadOnlyList<(byte[] Bytes, string ContentType, string Label)> references,
            CancellationToken cancellationToken)
        {
            if (reviewPrompt.StartsWith("You are the identity reader", StringComparison.Ordinal))
            {
                IdentityCalls++;

                return Task.FromResult(
                    $$"""
                    {"hair_color":"{{IdentitySpec.HairColor}}",
                     "hair_style":"{{IdentitySpec.HairStyle}}",
                     "eye_color":"{{IdentitySpec.EyeColor}}",
                     "skin_tone":"{{IdentitySpec.SkinTone}}",
                     "eyebrows":"{{IdentitySpec.Eyebrows}}",
                     "glasses":"{{IdentitySpec.Glasses}}",
                     "face_shape":"{{IdentitySpec.FaceShape}}",
                     "distinctive_features":"{{IdentitySpec.DistinctiveFeatures}}"}
                    """);
            }

            var page = PageOfReview(reviewPrompt);
            ReviewPages.Enqueue(page);

            return Task.FromResult(FailAtReview.Contains(page)
                ? """
                  {"status":"FAIL","failed_checks":["MAIN_SCENE_BEAT"],
                   "recommended_action":"human_review","notes":["a note"]}
                  """
                : """{"status":"PASS","failed_checks":[],"recommended_action":"pass","notes":[]}""");
        }

        /// <summary>
        /// Which page a prompt belongs to, read from the deterministic shot the rhythm gave it —
        /// the one string in an image prompt that is different on all eight pages and comes from
        /// code rather than from a model.
        /// </summary>
        private static int PageOf(string imagePrompt) =>
            CompositeSpreadRhythm.Pages.First(
                page => imagePrompt.Contains(CompositeSpreadRhythm.ShotFor(page), StringComparison.Ordinal));

        /// <summary>The review prompt has no shot in it, so its page is read from its scene.</summary>
        private static int PageOfReview(string reviewPrompt) =>
            ScenesByPage.First(
                scene => reviewPrompt.Contains(scene.Value, StringComparison.Ordinal)).Key;

        public Task<AdventureContentDto> GenerateAdventureContentAsync(
            AdventureGenerationInput input, Guid adventureId, CancellationToken cancellationToken) =>
            Task.FromResult(new AdventureContentDto());

        public Task<string> DescribeCharacterFromPhotoAsync(
            byte[] imageBytes, string contentType, string promptText,
            CancellationToken cancellationToken) => Task.FromResult("a child");

        public Task<string> CompleteTextAsync(string promptText, CancellationToken cancellationToken) =>
            Task.FromResult(string.Empty);
    }

    private sealed class ScenarioClient(string scenario) : IStoryModelClient
    {
        public Task<ModelResult<T>> CompleteAsync<T>(
            string model, string systemPrompt, string userPrompt, string schemaName,
            JsonElement schema, CancellationToken cancellationToken) =>
            Task.FromResult(new ModelResult<T>(
                JsonSerializer.Deserialize<T>(scenario, StoryJson.Options)!, 1, 1));
    }

    private sealed class UnusedStoryService : IMasterStoryService
    {
        public string ModelName => "stub-story-model";

        public string PromptVersion => "v6";

        public (string System, string User) BuildPrompts(MasterStoryInput input) =>
            (string.Empty, string.Empty);

        public Task<MasterStoryResult> WriteAsync(
            MasterStoryInput input, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MasterStoryResult> RetryPlanWithCorrectionsAsync(
            MasterStoryInput input, IReadOnlyList<string> problems,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<MasterStoryResult> WriteCompositePlanAsync(
            CompositeStoryInput input, IReadOnlyList<string> problems,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
