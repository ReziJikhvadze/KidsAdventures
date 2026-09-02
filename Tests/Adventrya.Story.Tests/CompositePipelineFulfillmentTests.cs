using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Entities;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Pdf;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using AdventurePacks.Api.Services.Story.Prompts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The fulfilment job on the composite path, at the seams the audit of 2026-09-01 found loose.
///
/// Six findings, one harness. The preview cover is no longer fetched for a path that never ships
/// it (F1). The cover wrap is started the moment the pipeline announces the anchor and awaited where
/// it used to be drawn, with a failure on either side handled exactly as before (F5). The press
/// tail runs on its own clock, and running out of it withholds the printer's files rather than
/// failing a book the family can already read (F4). Every stage of the tail moves the progress bar
/// and names itself on the row (F7). Adopted spreads hand the pipeline their stored QA records so
/// the version guard has something to guard (F12). And the opening claim is a compare-and-set that
/// does no work when it loses (F14).
///
/// Everything is faked; nothing here calls a model. The composed documents are stub bytes, so print
/// and digital preparation refuse the way the cover-projection harness's do, and the gates read a
/// NOT_RELEASABLE book that still ships to the family — which is the owner's policy, and not the
/// subject of any test below.
/// </summary>
public class CompositePipelineFulfillmentTests
{
    // =======================================================================================
    // F1 — the preview cover is not fetched on the composite path
    // =======================================================================================

    [Fact]
    public async Task The_composite_path_never_downloads_the_previewed_cover()
    {
        var world = new PackWorld();

        await world.Run();

        Assert.DoesNotContain(PackWorld.PreviewCoverUrl, world.Blobs.Downloaded);

        // And the book still has its one cover master, made from the wrap.
        Assert.Contains(
            BekiPackBlobs.CoverWrapCompositeName(world.UserId, world.PackId), world.Blobs.Uploaded.Keys);
        Assert.Equal(AdventurePackStatus.Completed, world.Packs.Status);
    }

    // =======================================================================================
    // F5 — the wrap draws beside the spreads
    // =======================================================================================

    /// <summary>
    /// When the pipeline announces the anchor, the job starts the wrap right there — while the
    /// illustrator is still running — and draws it to the announced identity and anchor.
    /// </summary>
    [Fact]
    public async Task The_wrap_starts_when_the_anchor_is_announced_and_is_awaited_after_the_spreads()
    {
        var world = new PackWorld { AnnounceAnchor = true };

        await world.Run();

        Assert.True(world.Generator.WrapStartedDuringIllustrate, "the wrap was not started from the anchor hook.");
        Assert.Equal(1, world.Generator.WrapCalls);
        Assert.Equal(CompositePipelineTestBase.IdentityFixture, world.Generator.WrapIdentity);
        Assert.Equal(PackWorld.AnnouncedAnchor, world.Generator.WrapAnchor);

        // The same master, the same receipt check, the same reader repoint as the serial path.
        var wrapName = BekiPackBlobs.CoverWrapCompositeName(world.UserId, world.PackId);
        Assert.Contains(wrapName, world.Blobs.Uploaded.Keys);
        Assert.Equal(world.Blobs.Uploaded[wrapName], world.Composer.ReadingWrap);
        Assert.Equal(
            $"https://blob.test/{BekiPackBlobs.CoverFrontName(world.UserId, world.PackId)}",
            world.Packs.CoverImageUrl);
    }

    /// <summary>
    /// A wrap that fails while drawing beside the spreads fails the book with the code it always
    /// had, after the spreads — not as a spread failure, and not silently.
    /// </summary>
    [Fact]
    public async Task A_wrap_that_fails_beside_the_spreads_fails_the_book_with_the_same_code()
    {
        var world = new PackWorld { AnnounceAnchor = true, WrapFails = true };

        await world.Job().ProcessAsync(world.PackId, world.RunId, CancellationToken.None);

        Assert.NotNull(world.Packs.FailureReason);
        Assert.StartsWith(CompositeFailureCodes.LayoutFailed, world.Packs.FailureReason);
        Assert.Contains("cover wrap", world.Packs.FailureReason);

        // The spreads were all stored before the wrap's outcome was read, as they always were.
        for (var spread = 1; spread <= BookFormat.SpreadCount; spread++)
        {
            Assert.Contains(
                BekiPackBlobs.SpreadName(world.UserId, world.PackId, spread), world.Blobs.Uploaded.Keys);
        }

        Assert.Equal(1, world.Notifier.Notifications);
    }

    /// <summary>
    /// When the spreads fail with a wrap still drawing beside them, the wrap is stopped and the
    /// book fails of the spreads — the wrap's own outcome is neither the reason nor left running.
    /// </summary>
    [Fact]
    public async Task A_spread_failure_stops_the_wrap_that_was_started_beside_it()
    {
        var world = new PackWorld
        {
            AnnounceAnchor = true,
            WrapHangs = true,
            SpreadsFail = new CompositePipelineException(
                CompositeFailureCodes.ImageQaFailed, "spread 3 was refused.")
            {
                Page = 3,
            },
        };

        await world.Job().ProcessAsync(world.PackId, world.RunId, CancellationToken.None);

        Assert.NotNull(world.Packs.FailureReason);
        Assert.StartsWith($"{CompositeFailureCodes.ImageQaFailed} (spread 3)", world.Packs.FailureReason);

        Assert.True(world.Generator.WrapStartedDuringIllustrate);
        Assert.True(world.Generator.WrapTokenCancelled, "the wrap was left drawing after the book failed.");
    }

    // =======================================================================================
    // F7 — the tail is visible
    // =======================================================================================

    [Fact]
    public async Task Progress_moves_through_every_stage_of_the_tail_and_speaks_georgian()
    {
        var world = new PackWorld();

        await world.Run();

        var percents = world.Packs.Progress.Select(step => step.Percent ?? -1).ToList();

        // Never backwards, and every stage after the spreads has a number of its own.
        Assert.Equal(percents.OrderBy(percent => percent), percents);
        Assert.Equal(
            [85, 86, 88, 91, 94, 97, 100],
            percents.Where(percent => percent >= 85));

        // The parent's screen and the admin read the same line, and it is in the book's language.
        Assert.All(world.Packs.Progress, step =>
        {
            Assert.False(string.IsNullOrWhiteSpace(step.Message));
            Assert.Contains(step.Message!, letter => letter is >= 'Ⴀ' and <= 'ჿ');
        });
    }

    // =======================================================================================
    // F4 — the press tail's own clock
    // =======================================================================================

    /// <summary>
    /// The press tail running out of time withholds the printer's files and completes the book.
    ///
    /// The clock fired here is EVERY clock — the job's thirty minutes as well as the tail's own —
    /// which is the shape of the defect: a slow upscaler after the reading copy was stored used to
    /// land in the catch-all as a budget failure, mark a finished book Failed, page an operator and
    /// write to the parent. Now the tail's expiry is recorded as a withholding, the gates still
    /// judge the book, and nothing after the reading copy runs under the job's clock at all.
    /// </summary>
    [Fact]
    public async Task A_press_tail_that_runs_out_of_time_withholds_the_press_files_and_completes_the_book()
    {
        var world = new PackWorld { PressStalls = true };

        await world.Job().ProcessAsync(world.PackId, world.RunId, CancellationToken.None);

        Assert.Null(world.Packs.FailureReason);
        Assert.Equal(AdventurePackStatus.Completed, world.Packs.Status);

        // The family's copy is published; the printer's slot is written, and written null.
        Assert.NotNull(world.Packs.PdfUrl);
        Assert.True(world.Packs.PrintPdfUrlWritten);
        Assert.Null(world.Packs.PrintPdfUrl);

        // The press-status document says why, in the field the gates read.
        using var status = JsonDocument.Parse(
            world.Blobs.Uploaded[BekiPackBlobs.PressStatusName(world.UserId, world.PackId)]);
        Assert.Equal("withheld", status.RootElement.GetProperty("interior").GetString());
        Assert.Equal("withheld", status.RootElement.GetProperty("cover").GetString());
        Assert.Contains(
            BekiPackFulfillment.PressBudgetExceededCode,
            status.RootElement.GetProperty("reason").GetString());

        // Neither preflight was written by this run, so both carry a refusal rather than nothing —
        // or, on a retry, an earlier attempt's pass.
        foreach (var report in new[]
                 {
                     BekiPackBlobs.InteriorPreflightName(world.UserId, world.PackId),
                     BekiPackBlobs.CoverPreflightName(world.UserId, world.PackId),
                 })
        {
            using var withheld = JsonDocument.Parse(world.Blobs.Uploaded[report]);
            Assert.Equal(BekiWithheldReport.FailVerdict, withheld.RootElement.GetProperty("verdict").GetString());
            Assert.Equal(BekiPackFulfillment.PressBudgetExceededCode, withheld.RootElement.GetProperty("gate").GetString());
        }

        // The gates were still evaluated and their verdict stored.
        Assert.Contains(BekiPackBlobs.ReleaseGatesName(world.UserId, world.PackId), world.Blobs.Uploaded.Keys);

        // A person is told through the alarms, as a blocker; nobody is told the book failed.
        var alarm = Assert.Single(world.Alarms.Raised);
        Assert.Equal(BekiPackFulfillment.PressBudgetAlarmCheck, alarm.CheckId);
        Assert.Equal(BekiReleaseSeverity.Blocker, alarm.Severity);
        Assert.Contains(BekiPackFulfillment.PressBudgetExceededCode, alarm.Detail);
        Assert.Equal(0, world.Notifier.Notifications);
        Assert.Empty(world.Email.Failures);

        // And the screen got to the end.
        Assert.Equal(100, world.Packs.Progress.Last().Percent);
    }

    /// <summary>
    /// The job's own clock firing during the press tail is simply not observed: the tail is not
    /// running under it, and the finish line is not either.
    /// </summary>
    [Fact]
    public async Task The_jobs_clock_expiring_during_the_press_tail_does_not_fail_the_book()
    {
        var world = new PackWorld { JobClockFiresDuringPress = true };

        await world.Job().ProcessAsync(world.PackId, world.RunId, CancellationToken.None);

        Assert.Null(world.Packs.FailureReason);
        Assert.Equal(AdventurePackStatus.Completed, world.Packs.Status);
        Assert.Empty(world.Alarms.Raised);
        Assert.Equal(0, world.Notifier.Notifications);
        Assert.Equal(100, world.Packs.Progress.Last().Percent);
    }

    [Fact]
    public void The_press_budget_has_a_default_and_falls_back_to_it()
    {
        Assert.Equal(TimeSpan.FromMinutes(15), BekiPackFulfillment.PressBudgetFor(new BekiOptions()));
        Assert.Equal(TimeSpan.FromMinutes(15), BekiPackFulfillment.PressBudgetFor(new BekiOptions { PressBudgetMinutes = 0 }));
        Assert.Equal(TimeSpan.FromMinutes(15), BekiPackFulfillment.PressBudgetFor(new BekiOptions { PressBudgetMinutes = -3 }));
        Assert.Equal(TimeSpan.FromMinutes(25), BekiPackFulfillment.PressBudgetFor(new BekiOptions { PressBudgetMinutes = 25 }));

        // The image phase's default is the deployed value rather than the two it lagged at.
        Assert.Equal(4, new BekiOptions().SpreadConcurrency);
    }

    // =======================================================================================
    // F12 — adopted spreads carry their QA records to the pipeline
    // =======================================================================================

    [Fact]
    public async Task Adopted_spreads_hand_the_pipeline_their_stored_QA_records()
    {
        var world = new PackWorld();
        world.SeedStoredBook(qaFor: [1, 2, 3]);

        await world.Run();

        var resume = world.Generator.Resume!;

        // Every stored page was adopted…
        Assert.Equal(BookFormat.SpreadCount, resume.Spreads.Count);

        // …and exactly the pages with a stored verdict carry one, byte for byte.
        Assert.Equal([1, 2, 3], resume.SpreadQaJson.Keys.OrderBy(page => page));
        Assert.Equal(PackWorld.StoredQa(2), resume.SpreadQaJson[2]);
    }

    /// <summary>
    /// A book stored before the records existed hands over an EMPTY map — which the pipeline reads
    /// as "this caller keeps no QA", the branch a pre-campaign book is meant to take — rather than a
    /// map that would redraw every page.
    /// </summary>
    [Fact]
    public async Task A_stored_book_with_no_QA_records_hands_over_none()
    {
        var world = new PackWorld();
        world.SeedStoredBook(qaFor: []);

        await world.Run();

        Assert.Equal(BookFormat.SpreadCount, world.Generator.Resume!.Spreads.Count);
        Assert.Empty(world.Generator.Resume.SpreadQaJson);
    }

    // =======================================================================================
    // F14 — the claim is a compare-and-set
    // =======================================================================================

    [Fact]
    public async Task A_claim_that_loses_to_another_writer_does_no_work_and_overwrites_nothing()
    {
        var world = new PackWorld();

        // Between the job's read and its claim, the sweep buries the book.
        world.Packs.BeforeClaim = () => world.Packs.Force(
            AdventurePackStatus.Failed, "GENERATION_STALLED: swept", "https://blob.test/earlier.pdf");

        await world.Job().ProcessAsync(world.PackId, world.RunId, CancellationToken.None);

        // The sweep's verdict — status, reason and the columns beside them — is untouched.
        Assert.Equal(AdventurePackStatus.Failed, world.Packs.Status);
        Assert.Equal("GENERATION_STALLED: swept", world.Packs.ErrorMessage);
        Assert.Equal("https://blob.test/earlier.pdf", world.Packs.PdfUrl);

        // Nothing was drawn, stored, failed or paged.
        Assert.Equal(0, world.Generator.IllustrateCalls);
        Assert.Empty(world.Blobs.Uploaded);
        Assert.Null(world.Packs.FailureReason);
        Assert.Equal(0, world.Notifier.Notifications);
    }

    // =======================================================================================
    // F19 — bytes this run already holds are not fetched back
    // =======================================================================================

    /// <summary>
    /// The photograph is downloaded once and the finals not at all.
    ///
    /// Two round trips the job used to make against arrays it was already holding: the manifest's
    /// private reference re-downloaded the portrait purely to hash it, and render validation
    /// re-downloaded each final immediately after uploading it. Neither can return anything
    /// different from what is in hand — the uploader was handed that exact array — so both were
    /// latency spent to learn nothing, on the tail of a job a parent is watching.
    ///
    /// A final this run did NOT produce is still fetched from storage; that path is not exercised
    /// here because print preparation refuses on the harness's stub bytes, which is the same
    /// reason the two press finals never reach validation at all.
    /// </summary>
    [Fact]
    public async Task Nothing_this_run_is_already_holding_is_downloaded_again()
    {
        var world = new PackWorld();

        await world.Run();

        var downloads = world.Blobs.Downloaded.ToList();

        // Once, for the illustrator. The manifest's private reference hashes the same array.
        Assert.Equal(1, downloads.Count(url => url == PackWorld.PhotoUrl));

        // And the reading copy, which this run composed and stored, is rendered back from the
        // bytes it stored rather than from a fetch of the blob it just wrote.
        var readingPdf = BekiPackBlobs.ReadingPdfName(world.UserId, world.PackId);
        Assert.Contains(readingPdf, world.Blobs.Uploaded.Keys);
        Assert.DoesNotContain(readingPdf, downloads);

        // The manifest still carries the photograph's identity, which is what the re-download was
        // for: dropping the fetch must not drop the hash.
        var manifest = JsonSerializer.Deserialize<BekiFulfillmentManifest>(
            world.Blobs.Uploaded[BekiPackBlobs.ManifestName(world.UserId, world.PackId)],
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal(PackWorld.PhotoUrl, manifest.ChildPhotograph!.Reference);
        Assert.Equal(64, manifest.ChildPhotograph.Sha256.Length);
    }

    // =======================================================================================
    // Harness
    // =======================================================================================

    private sealed class PackWorld
    {
        public const string PreviewCoverUrl = "https://blob.test/master-runs/preview/cover";

        /// <summary>Where the child's photograph is stored, as the preview run recorded it.</summary>
        public const string PhotoUrl = "https://blob.test/portrait.png";

        /// <summary>The anchor the stubbed pipeline announces, distinct from anything else in storage.</summary>
        public static readonly byte[] AnnouncedAnchor = [0x89, (byte)'P', (byte)'N', (byte)'G', 42, 42, 42];

        public Guid PackId { get; } = Guid.NewGuid();

        public Guid RunId { get; } = Guid.NewGuid();

        public Guid UserId { get; } = Guid.NewGuid();

        public FakePacks Packs { get; }

        public FakeBlobs Blobs { get; } = new();

        public RecordingComposer Composer { get; } = new();

        public CountingNotifier Notifier { get; } = new();

        public RecordingEmailService Email { get; } = new();

        public RecordingAlarms Alarms { get; } = new();

        public ManualTimeProvider Clock { get; } = new();

        /// <summary>Whether the stubbed illustrator announces an anchor the way the real pipeline does.</summary>
        public bool AnnounceAnchor { get; init; }

        public bool WrapFails { get; init; }

        /// <summary>The wrap waits on its token until somebody cancels it.</summary>
        public bool WrapHangs { get; init; }

        /// <summary>Thrown by the illustrator after it has announced the anchor.</summary>
        public Exception? SpreadsFail { get; init; }

        /// <summary>The upscaler fires every clock and then waits on its token — the press tail stalls.</summary>
        public bool PressStalls { get; init; }

        /// <summary>The upscaler fires the JOB's clock alone and answers normally.</summary>
        public bool JobClockFiresDuringPress { get; init; }

        public PackWorld() =>
            Packs = new FakePacks(new AdventurePack
            {
                Id = PackId,
                UserId = UserId,
                Theme = ThemeType.Dinosaurs,
                Status = AdventurePackStatus.StoryReady,
                CoverImageUrl = PreviewCoverUrl,
                CreatedAt = DateTime.UtcNow,
            });

        public async Task Run()
        {
            await Job().ProcessAsync(PackId, RunId, CancellationToken.None);

            Assert.Null(Packs.FailureReason);
        }

        private StubGenerator? _generator;

        public StubGenerator Generator => _generator ??= new StubGenerator(this);

        public BekiPackFulfillment Job() =>
            new(Packs,
                new FakeRuns(RunId),
                Blobs,
                Generator,
                Composer,
                Notifier,
                Email,
                new SingleUserRepository(),
                Options.Create(new BekiOptions { CompositePipelineEnabled = true }),
                NullLogger<BekiPackFulfillment>.Instance,
                Clock,
                pressUpscaler: new ScriptedUpscaler(this),
                alarms: Alarms);

        public static string StoredQa(int page) => $$"""
            {"page": {{page}}, "qa_prompt_version": "{{CompositeMinimalQa.Version}}",
             "status": "PASS", "recommended_action": "ship"}
            """;

        /// <summary>
        /// A whole earlier attempt in storage: eight spreads with their bases and receipts, the
        /// scenario, the identity spec, and a QA record for the pages asked for.
        /// </summary>
        public void SeedStoredBook(IReadOnlyList<int> qaFor)
        {
            var entries = new List<BekiFulfillmentManifestEntry>();
            var compositions = new List<BekiCompositionManifestEntry>();

            for (var page = 1; page <= BookFormat.SpreadCount; page++)
            {
                var spreadName = BekiPackBlobs.SpreadName(UserId, PackId, page);
                var baseName = BekiPackBlobs.SpreadBaseName(UserId, PackId, page);
                var receiptName = BekiPackBlobs.CompositionManifestName(UserId, PackId, page);

                Blobs.Seed(spreadName, [(byte)page]);
                Blobs.Seed(baseName, [(byte)page, 0]);
                Blobs.Seed(receiptName, "{}"u8.ToArray());

                entries.Add(new BekiFulfillmentManifestEntry(page, $"https://blob.test/{spreadName}"));
                compositions.Add(new BekiCompositionManifestEntry(
                    page, $"https://blob.test/{receiptName}", "pose_01_neutral_hover",
                    new string('a', 64), $"https://blob.test/{baseName}"));

                if (qaFor.Contains(page))
                {
                    Blobs.Seed(BekiPackBlobs.SpreadQaName(UserId, PackId, page), Encoding.UTF8.GetBytes(StoredQa(page)));
                }
            }

            var scenarioName = BekiPackBlobs.ScenarioName(UserId, PackId);
            var identityName = BekiPackBlobs.IdentitySpecName(UserId, PackId);

            Blobs.Seed(scenarioName, Encoding.UTF8.GetBytes(StubGenerator.ScenarioJson));
            Blobs.Seed(identityName, Encoding.UTF8.GetBytes(
                CompositeChildIdentity.ToStoredJson(CompositePipelineTestBase.IdentityFixture)));

            var manifest = new BekiFulfillmentManifest
            {
                IllustrationContract = BekiFulfillmentManifest.CurrentContract(
                    BookFormat.SpreadCount, BekiCompositeContractTerms.Current("dinosaurs")),
                Entries = entries,
                Compositions = compositions,
                ScenarioUrl = $"https://blob.test/{scenarioName}",
                IdentitySpecUrl = $"https://blob.test/{identityName}",
            };

            Blobs.Seed(
                BekiPackBlobs.ManifestName(UserId, PackId),
                JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }
    }

    /// <summary>
    /// The illustrator, stubbed at the two seams under test: what it announces, and what the job
    /// handed it to resume from.
    /// </summary>
    private sealed class StubGenerator(PackWorld world) : IBekiBookGenerator
    {
        public static readonly byte[] WrapComposite = [0x89, (byte)'P', (byte)'N', (byte)'G', 7, 7];

        public static string ScenarioJson => File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "nina_dinosaurs", "visual_scenario_output_v2.json"));

        private readonly TaskCompletionSource _wrapEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int IllustrateCalls { get; private set; }

        public int WrapCalls { get; private set; }

        public bool WrapStartedDuringIllustrate { get; private set; }

        public bool WrapTokenCancelled { get; private set; }

        public ChildIdentitySpec? WrapIdentity { get; private set; }

        public byte[]? WrapAnchor { get; private set; }

        /// <summary>What the job handed this run to resume from.</summary>
        public CompositeResumeState? Resume { get; private set; }

        public Task<BekiBookResult> GenerateAsync(
            MasterStoryInput input, byte[] childPhoto, string childPhotoContentType,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<BekiImageResult> DrawCoverAsync(
            MasterStory plan, byte[] childPhoto, string childPhotoContentType,
            CancellationToken cancellationToken, CompositeBookContext? composite = null) =>
            throw new NotSupportedException("the composite path must not ask for a reader-facing cover.");

        public async Task<CompositeCoverWrap> DrawCoverWrapAsync(
            VisualScenarioV2 scenario, byte[] childPhoto, string childPhotoContentType,
            CompositeBookContext composite, ChildIdentitySpec identity, byte[]? childAnchor,
            CancellationToken cancellationToken)
        {
            WrapCalls++;
            WrapIdentity = identity;
            WrapAnchor = childAnchor;
            _wrapEntered.TrySetResult();

            if (world.WrapHangs)
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    WrapTokenCancelled = true;
                    throw;
                }
            }

            if (world.WrapFails)
            {
                throw new BekiLayoutException(
                    CompositeFailureCodes.LayoutFailed,
                    "the cover wrap could not be drawn in this deployment.");
            }

            var receipt = JsonSerializer.Serialize(new
            {
                composition_version = "beki-exact-composite-v1",
                beki_layer = new
                {
                    pose_id = "pose_01_neutral_hover",
                    normalized_anchor = new { visible_center_x = 0.87, visible_center_y = 0.64, visible_height = 0.30 },
                },
                output = new
                {
                    file = "cover-wrap-composite.png",
                    sha256 = Convert.ToHexString(SHA256.HashData(WrapComposite)).ToLowerInvariant(),
                },
            });

            return new CompositeCoverWrap(
                [0x89, (byte)'P', (byte)'N', (byte)'G', 1], WrapComposite, receipt,
                "pose_01_neutral_hover", "wrap prompt");
        }

        public async Task<BekiBookResult> IllustrateAsync(
            MasterStory plan, byte[] childPhoto, string childPhotoContentType, byte[]? existingCover,
            Func<BekiImageResult, Task>? onImage, CancellationToken cancellationToken,
            IReadOnlyDictionary<int, byte[]>? existingSpreads = null,
            CompositeBookContext? composite = null)
        {
            IllustrateCalls++;
            Resume = composite?.Resume;

            if (world.AnnounceAnchor && composite?.OnAnchorAccepted is { } announce)
            {
                var scenario = VisualScenarioValidator.Validate(ScenarioJson).Scenario!;

                await announce(new CompositeAnchorAccepted(
                    scenario, CompositePipelineTestBase.IdentityFixture, PackWorld.AnnouncedAnchor));

                // The job is expected to have started the wrap inside the hook: this waits for the
                // wrap to be ENTERED while the illustrator is still running, which is the claim.
                try
                {
                    await _wrapEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                    WrapStartedDuringIllustrate = true;
                }
                catch (TimeoutException)
                {
                    WrapStartedDuringIllustrate = false;
                }
            }

            if (world.SpreadsFail is { } failure)
            {
                throw failure;
            }

            var spreads = Enumerable.Range(1, BookFormat.SpreadCount)
                .Select(number => new BekiImageResult
                {
                    SpreadNumber = number,
                    Image = [(byte)number],
                    Accepted = true,
                    Verdict = "PASS (pass)",
                    Attempts = 1,
                    Prompt = "spread prompt",
                })
                .ToList();

            return new BekiBookResult
            {
                Plan = plan,
                AppearanceDescription = string.Empty,
                Cover = new BekiImageResult
                {
                    Image = existingCover ?? [],
                    Accepted = true,
                    Verdict = "not drawn here",
                    Attempts = 0,
                    Prompt = string.Empty,
                },
                Spreads = spreads,
                Warnings = [],
                Composite = new CompositeBookArtifacts
                {
                    ScenarioJson = ScenarioJson,
                    ReviewJson = """{"needs_human_reading": false}""",
                    Identity = CompositePipelineTestBase.IdentityFixture,
                    Anchor = [1, 2, 3, 4],
                    Spreads = spreads
                        .Select(spread => new CompositeSpreadArtifact(
                            spread.SpreadNumber!.Value, "pose_01_neutral_hover", "{}",
                            new string('0', 64), BasePng: [])
                        {
                            QaJson = PackWorld.StoredQa(spread.SpreadNumber!.Value),
                        })
                        .ToList(),
                },
            };
        }
    }

    /// <summary>
    /// The super-resolver, scripted to fire whichever clock a test names. Unconfigured otherwise,
    /// which is the shipped state and makes print preparation refuse on stub bytes as it should.
    /// </summary>
    private sealed class ScriptedUpscaler(PackWorld world) : IPressUpscaler
    {
        public bool IsConfigured => false;

        public async Task<PressUpscaleResult> UpscaleAsync(
            byte[] png, int targetWidth, int targetHeight, CancellationToken cancellationToken)
        {
            if (world.PressStalls)
            {
                world.Clock.FireAll();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (world.JobClockFiresDuringPress)
            {
                world.Clock.FireFirst();
            }

            return PressUpscaleResult.NotConfigured(1, 1);
        }
    }

    /// <summary>A timer nobody has to wait for — see GenerationBudgetTests for the original.</summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];

        public override ITimer CreateTimer(
            TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state);
            lock (_timers)
            {
                _timers.Add(timer);
            }

            return timer;
        }

        /// <summary>Every deadline passes, now.</summary>
        public void FireAll()
        {
            ManualTimer[] snapshot;
            lock (_timers)
            {
                snapshot = _timers.ToArray();
            }

            foreach (var timer in snapshot)
            {
                timer.Fire();
            }
        }

        /// <summary>The earliest-created deadline — the job's own — passes, and no other.</summary>
        public void FireFirst()
        {
            ManualTimer? first;
            lock (_timers)
            {
                first = _timers.FirstOrDefault();
            }

            first?.Fire();
        }

        private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
        {
            public void Fire() => callback(state);
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingAlarms : IBekiAlarmService
    {
        public List<BekiAlarmRaise> Raised { get; } = [];

        public Task RaiseAsync(BekiAlarmRaise raise, CancellationToken ct)
        {
            Raised.Add(raise);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<BekiAlarm>> ListOpenAsync(int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<BekiAlarm>>([]);

        public Task<IReadOnlyList<BekiAlarm>> ListForPackAsync(Guid packId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<BekiAlarm>>([]);

        public Task<bool> ReviewAsync(Guid alarmId, string reviewedBy, string resolution, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<int> CountOpenAsync(CancellationToken ct) => Task.FromResult(Raised.Count);
    }

    private sealed class RecordingComposer : IBekiPdfComposer
    {
        public byte[]? ReadingWrap { get; private set; }

        public BekiComposedBook ComposeWithReceipts(
            MasterStory plan, byte[] coverImage, IReadOnlyList<BekiSpreadArtwork> spreads,
            BekiBookPersonalization? personalization = null) =>
            new([0x25, 0x50, 0x44, 0x46], Receipts("press"));

        public BekiComposedBook ComposeReading(
            MasterStory plan, byte[] wrapComposite, IReadOnlyList<BekiSpreadArtwork> spreads,
            BekiBookPersonalization? personalization = null)
        {
            ReadingWrap = wrapComposite;
            return new BekiComposedBook([0x25, 0x50, 0x44, 0x46], Receipts("reading"));
        }

        public BekiComposedBook ComposeInteriorWithReceipts(
            MasterStory plan, IReadOnlyList<BekiSpreadArtwork> spreads,
            BekiBookPersonalization? personalization = null) =>
            new([0x25, 0x50, 0x44, 0x46, 0x2D], Receipts("interior"));

        public BekiComposedBook ComposeCoverPressWithReceipts(string title, byte[] wrapComposite) =>
            new([0x25, 0x50, 0x44, 0x46, 0x2D], Receipts("cover"));

        public byte[] CropFrontBoard(byte[] wrapPng) => [.. wrapPng, 0xF1];

        public byte[] CropBackBoard(byte[] wrapPng) => [.. wrapPng, 0xB1];

        public IReadOnlyList<byte[]> RenderPages(
            MasterStory plan, byte[] coverImage, IReadOnlyList<BekiSpreadArtwork> spreads,
            BekiBookPersonalization? personalization = null) => throw new NotSupportedException();

        private static BekiLayoutReceipts Receipts(string mode) => new(
            mode,
            new[] { "cover-front", "endpaper-front", "intro", "credits", "endpaper-rear", "cover-back" }
                .Select((role, index) => new BekiLayoutPageReceipt(
                    index + 1, role, 220, 200, 0,
                    ImageSha256: [new string('e', 64)],
                    Wash: null,
                    Typography: [new BekiTypographyRecord(role, "Noto Sans Georgian", 12, 1.3, "#241A33")],
                    TextLines: ["ერთი სტრიქონი"],
                    TextProbe: null))
                .ToList());
    }

    /// <summary>A blob store that remembers what it was given, and what it was asked for.</summary>
    private sealed class FakeBlobs : IBlobStorageService
    {
        public ConcurrentDictionary<string, byte[]> Uploaded { get; } = new(StringComparer.Ordinal);

        public ConcurrentBag<string> Downloaded { get; } = [];

        public void Seed(string blobName, byte[] bytes) => Uploaded[blobName] = bytes;

        public Task<string> UploadAsync(
            string blobName, byte[] bytes, string contentType, CancellationToken cancellationToken)
        {
            Uploaded[blobName] = bytes;
            return Task.FromResult($"https://blob.test/{blobName}");
        }

        public Task<Stream> DownloadAsync(string blobName, CancellationToken cancellationToken)
        {
            Downloaded.Add(blobName);
            return Task.FromResult<Stream>(new MemoryStream(
                Uploaded.TryGetValue(blobName, out var bytes) ? bytes : []));
        }

        public Task<bool> ExistsAsync(string blobName, CancellationToken cancellationToken) =>
            Task.FromResult(Uploaded.ContainsKey(blobName));

        public Task<byte[]> DownloadBytesFromStoredUrlAsync(
            string storedUrl, CancellationToken cancellationToken)
        {
            Downloaded.Add(storedUrl);

            var name = storedUrl.Replace("https://blob.test/", string.Empty, StringComparison.Ordinal);

            return Task.FromResult(Uploaded.TryGetValue(name, out var bytes) ? bytes : [1, 1, 1, 1]);
        }

        public Task<bool> DeleteByStoredUrlAsync(string storedUrl, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    /// <summary>The pack row: compare-and-set the way the real one is, remembering its progress line.</summary>
    private sealed class FakePacks(AdventurePack seed) : IAdventurePackRepository
    {
        private readonly AdventurePack _pack = seed;
        private readonly object _gate = new();
        private readonly List<(string? Message, int? Percent)> _progress = [];

        public string? CoverImageUrl => _pack.CoverImageUrl;

        public string? PdfUrl => _pack.PdfUrl;

        public string? ErrorMessage => _pack.ErrorMessage;

        public AdventurePackStatus Status => _pack.Status;

        public string? PrintPdfUrl { get; private set; }

        public bool PrintPdfUrlWritten { get; private set; }

        public string? FailureReason { get; private set; }

        /// <summary>Runs inside the claim, after the job has decided what it expects to find.</summary>
        public Action? BeforeClaim { get; set; }

        public IReadOnlyList<(string? Message, int? Percent)> Progress
        {
            get
            {
                lock (_gate)
                {
                    return _progress.ToList();
                }
            }
        }

        public void Force(AdventurePackStatus status, string? error, string? pdfUrl)
        {
            _pack.Status = status;
            _pack.ErrorMessage = error;
            _pack.PdfUrl = pdfUrl;
        }

        public Task<AdventurePack?> GetByIdNoOwnershipAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<AdventurePack?>(_pack);

        /// <summary>The unconditional write, which the job must not use to claim a pack any more.</summary>
        public Task<bool> UpdateStatusAsync(
            Guid id, AdventurePackStatus status, string? generatedJson, string? pdfUrl,
            string? errorMessage, CancellationToken cancellationToken) =>
            throw new NotSupportedException("the claim must be a compare-and-set.");

        public Task<bool> TryUpdateStatusAsync(
            Guid id, AdventurePackStatus expected, AdventurePackStatus status, string? generatedJson,
            string? pdfUrl, string? errorMessage, CancellationToken cancellationToken)
        {
            if (status == AdventurePackStatus.GeneratingStory)
            {
                BeforeClaim?.Invoke();
            }

            if (_pack.Status != expected)
            {
                return Task.FromResult(false);
            }

            _pack.Status = status;
            _pack.PdfUrl = pdfUrl;
            _pack.ErrorMessage = errorMessage;
            return Task.FromResult(true);
        }

        public Task<bool> TryFailAsync(
            Guid id, AdventurePackStatus expectedStatus, string errorMessage,
            CancellationToken cancellationToken)
        {
            if (_pack.Status != expectedStatus)
            {
                return Task.FromResult(false);
            }

            FailureReason = errorMessage;
            _pack.Status = AdventurePackStatus.Failed;
            _pack.ErrorMessage = errorMessage;
            return Task.FromResult(true);
        }

        public Task UpdateProgressAsync(
            Guid id, string? progressMessage, int? progressPercent, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _progress.Add((progressMessage, progressPercent));
            }

            return Task.CompletedTask;
        }

        public Task UpdatePrintPdfUrlAsync(Guid id, string? printPdfUrl, CancellationToken cancellationToken)
        {
            PrintPdfUrl = printPdfUrl;
            PrintPdfUrlWritten = true;
            return Task.CompletedTask;
        }

        public Task UpdateBookPresentationAsync(
            Guid id, string? title, string? coverImageUrl, CancellationToken cancellationToken)
        {
            _pack.Title = title ?? _pack.Title;
            _pack.CoverImageUrl = coverImageUrl ?? _pack.CoverImageUrl;
            return Task.CompletedTask;
        }

        public Task SetGenerationPipelineAsync(Guid id, string pipeline, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<AdventurePack>> ListWithheldBekiPacksAsync(
            int limit, BekiWithheldCursor? after, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AdventurePack>>([]);

        // Everything else the interface declares and this job never calls.
        public Task<Guid> CreatePendingAsync(AdventurePack pack, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AdventurePack?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdventurePack>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdventurePack>> GetByCharacterIdAsync(Guid characterId, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> GetNextSequenceNumberAsync(Guid seriesId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> SetAccessLevelAsync(Guid id, BookAccessLevel accessLevel, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> MarkReadAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> SetPrintEntitlementAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CountForMonthAsync(Guid userId, DateTime utcMonthStart, DateTime utcMonthEnd, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<StaleGenerationPack>> ListStaleGenerationAsync(DateTime cutoffUtc, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryFailStaleGenerationAsync(Guid id, AdventurePackStatus expected, DateTime cutoffUtc, string errorMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateProgressMessageAsync(Guid id, string? progressMessage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetPdfCreditChargedAsync(Guid id, bool charged, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdatePreviewIllustrationAsync(Guid id, PreviewIllustrationStatus status, string? illustrationUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryClaimPreviewIllustrationGenerationAsync(Guid id, int staleAfterMinutes, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task TouchPreviewIllustrationHeartbeatAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> UpdateGeneratedJsonAsync(Guid id, string generatedJson, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeRuns(Guid runId) : IMasterStoryRunRepository
    {
        private readonly MasterStoryRun _run = new()
        {
            Id = runId,
            ChildName = "ნინა",
            Age = 5,
            Gender = "girl",
            Theme = nameof(ThemeType.Dinosaurs),
            SpreadCount = BookFormat.SpreadCount,
            StoryLanguage = "ka",
            PhotoBlobUrl = PackWorld.PhotoUrl,
            CoverImageUrl = PackWorld.PreviewCoverUrl,
            StoryJson = JsonSerializer.Serialize(Plan(), StoryJson.Options),
        };

        public Task<MasterStoryRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<MasterStoryRun?>(_run);

        public Task CreateAsync(MasterStoryRun run, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<MasterStoryRunProgress?> GetProgressAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<MasterStoryRunProgress?>(null);
        public Task SetProgressAsync(Guid id, string status, string? progressMessage, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SavePromptsAsync(Guid id, string model, string promptVersion, string systemPrompt, string userPrompt, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveStoryAsync(Guid id, string storyJson, string contentJson, int promptTokens, int completionTokens, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveCoverAsync(Guid id, string coverImageUrl, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task MarkReadyAsync(Guid id, string contentJson, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ClaimAsync(Guid id, Guid userId, Guid? packId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<ExpiredMasterStoryRun>> ListExpiredAsync(int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ExpiredMasterStoryRun>>([]);
        public Task<int> DeleteAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private sealed class CountingNotifier : IAdminNotifier
    {
        public int Notifications { get; private set; }

        public Task BookFailedAsync(Guid packId, string reason, CancellationToken cancellationToken)
        {
            Notifications++;
            return Task.CompletedTask;
        }

        public Task OrderPaidAsync(Order order, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PrintOrderPlacedAsync(PrintOrder printOrder, string? bookTitle, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private static MasterStory Plan() => new()
    {
        Concept = new StoryConcept
        {
            Title = "ბაფუს ბილიკი",
            Outline = Enumerable.Range(1, BookFormat.SpreadCount).Select(n => $"beat {n}").ToList(),
        },
        Spreads = Enumerable.Range(1, BookFormat.SpreadCount).Select(number => new StorySpread
        {
            Number = number,
            Title = string.Empty,
            Caption = string.Empty,
            Text = $"ნინა და ბეკი — გვერდი {number}.",
            Characters = ["child", "beki"],
            Objects = [],
            Illustration = new IllustrationBrief { Scene = "The child in the valley." },
        }).ToList(),
        CharacterLock = string.Empty,
        Cover = new IllustrationBrief { Scene = "The child at the valley's edge." },
        WorldLock = "A warm golden valley.",
        Cast = [],
        Objects = [],
    };
}
