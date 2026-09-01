using System.Security.Cryptography;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Entities;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using AdventurePacks.Api.Services.Story.Prompts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// Which cover this book has — and, since audit-2, that it has exactly one.
///
/// It had two. The printer's cover was the composited 512 × 245 wrap; the parent's cover was an AI
/// redraw made after the first spread was accepted, and the reader's own image was re-pointed at
/// that redraw. Two producers, two designs, one book. P0-01 is the supplier rejecting the package
/// for it, and no amount of reviewing either picture would have caught it: each was individually
/// fine.
///
/// So the flow was reordered (plan D1). The wrap is generated first, its bytes are stored and
/// checked against their own composition receipt (P0-10), and every cover anybody sees is cut from
/// them: the press cover, the customer PDF's front and back pages, and the reader's image. The
/// redraw is not called at all, and a wrap that cannot be produced now fails the book rather than
/// quietly falling back to a second design.
/// </summary>
public class CompositeCoverProjectionTests
{
    [Fact]
    public async Task The_stored_wrap_becomes_the_only_cover_and_the_reader_points_at_its_front_board()
    {
        var world = new PackWorld();

        await world.Run();

        // The master is written down — audit P0-10's missing file.
        var wrapName = BekiPackBlobs.CoverWrapCompositeName(world.UserId, world.PackId);
        Assert.Contains(wrapName, world.Blobs.Uploaded.Keys);
        Assert.Contains(BekiPackBlobs.CoverWrapBaseName(world.UserId, world.PackId), world.Blobs.Uploaded.Keys);
        Assert.Contains(BekiPackBlobs.CoverCompositionName(world.UserId, world.PackId), world.Blobs.Uploaded.Keys);

        // The reader's cover is the wrap's own front board, not a second picture.
        var frontName = BekiPackBlobs.CoverFrontName(world.UserId, world.PackId);
        Assert.Contains(frontName, world.Blobs.Uploaded.Keys);
        Assert.Equal($"https://blob.test/{frontName}", world.Packs.CoverImageUrl);
        Assert.NotEqual(PackWorld.PreviewCoverUrl, world.Packs.CoverImageUrl);

        // And the customer's book was laid out from the same bytes the printer's cover is made of.
        Assert.Equal(world.Blobs.Uploaded[wrapName], world.Composer.ReadingWrap);

        // The manifest names the master rather than a redraw version.
        var manifest = world.StoredManifest();
        Assert.Equal(BekiCoverRecord.WrapMaster, manifest.Cover!.PromptVersion);
        Assert.True(manifest.Cover.IsWrapMaster);
        Assert.False(manifest.Cover.IsRedraw);
        Assert.Equal("pose_01_neutral_hover", manifest.Cover.PoseId);
        Assert.Equal(64, manifest.Cover.CompositeSha256!.Length);
    }

    /// <summary>
    /// The AI redraw ships nowhere. `cover.png` is a historical blob from before the correction, and
    /// this path must not write one — a second cover in storage is a second cover somebody points at.
    /// </summary>
    [Fact]
    public async Task The_composite_path_never_writes_the_redrawn_cover()
    {
        var world = new PackWorld();

        await world.Run();

        Assert.DoesNotContain(
            BekiPackBlobs.CoverName(world.UserId, world.PackId), world.Blobs.Uploaded.Keys);
    }

    /// <summary>
    /// Audit P0-10, as a check rather than as a claim: every cover derivation is cut from these
    /// bytes, so a master whose hash disagrees with its own receipt is not a master.
    /// </summary>
    [Fact]
    public async Task A_wrap_that_does_not_match_its_receipt_stops_the_book()
    {
        var world = new PackWorld { WrapReceiptLies = true };

        await world.Job().ProcessAsync(world.PackId, world.RunId, CancellationToken.None);

        Assert.NotNull(world.Packs.FailureReason);
        Assert.StartsWith("IMAGE_GENERATION_FAILED", world.Packs.FailureReason);
        Assert.Contains("composition receipt", world.Packs.FailureReason);
    }

    /// <summary>
    /// Risk R1, accepted deliberately: a composite book with no wrap has no cover master, and the
    /// old behaviour — degrade to the previewed picture — is the second producer the audit rejected.
    /// </summary>
    [Fact]
    public async Task A_wrap_that_cannot_be_drawn_fails_the_book_rather_than_falling_back()
    {
        var world = new PackWorld { WrapFails = true };

        await world.Job().ProcessAsync(world.PackId, world.RunId, CancellationToken.None);

        Assert.NotNull(world.Packs.FailureReason);
        Assert.Contains("cover wrap", world.Packs.FailureReason);

        // Nothing shipped, and in particular the previewed cover was not promoted behind the scenes.
        Assert.Equal(PackWorld.PreviewCoverUrl, world.Packs.CoverImageUrl);
    }

    /// <summary>
    /// P0-09: the accepted verdicts were held in memory and dropped, so the rejected package listed
    /// all eight QA files as missing beside two finished PDFs. They are written now, on the success
    /// path, for every page.
    /// </summary>
    [Fact]
    public async Task Every_accepted_spread_leaves_its_QA_record_behind()
    {
        var world = new PackWorld();

        await world.Run();

        for (var spread = 1; spread <= BookFormat.SpreadCount; spread++)
        {
            Assert.Contains(
                BekiPackBlobs.SpreadQaName(world.UserId, world.PackId, spread),
                world.Blobs.Uploaded.Keys);
        }
    }

    /// <summary>
    /// Amendment A4's invariant, from the pipeline side: an adopted artifact carries no receipt of
    /// its own, and storing one anyway would put an empty composition entry where the earlier
    /// attempt's real one belongs — satisfying the exact-Beki gate with nothing.
    /// </summary>
    [Fact]
    public async Task An_adopted_spread_does_not_overwrite_its_earlier_receipt()
    {
        var world = new PackWorld { AdoptSpread = 3 };

        var receiptName = BekiPackBlobs.CompositionManifestName(world.UserId, world.PackId, 3);
        world.Blobs.Seed(receiptName, "{\"the earlier attempt\"}"u8.ToArray());

        // The earlier attempt's own record of that page, which is what this run adopts and must not
        // replace with the blank an adopted artifact carries.
        world.SeedManifest(compositions:
        [
            new BekiCompositionManifestEntry(
                3, $"https://blob.test/{receiptName}", "pose_04_point_forward",
                new string('a', 64), null),
        ]);

        await world.Run();

        Assert.Equal(
            "{\"the earlier attempt\"}"u8.ToArray(), world.Blobs.Uploaded[receiptName]);

        var manifest = world.StoredManifest();
        var adopted = manifest.Compositions!.Single(entry => entry.SpreadNumber == 3);

        Assert.Equal("pose_04_point_forward", adopted.PoseId);
        Assert.Equal(new string('a', 64), adopted.OutputSha256);
    }

    /// <summary>
    /// The asset lock runs before any model call (plan D9): a book built from unapproved bytes must
    /// cost nothing to refuse.
    /// </summary>
    [Fact]
    public async Task The_asset_lock_is_proved_and_stored_before_the_book_is_drawn()
    {
        var world = new PackWorld();

        await world.Run();

        Assert.Contains(
            BekiPackBlobs.AssetLockName(world.UserId, world.PackId), world.Blobs.Uploaded.Keys);
    }

    /// <summary>
    /// The verdict is written down and read — and, since the release policy, the two audiences it is
    /// read by are told different things about the same book.
    ///
    /// Under this harness the composed documents are stubs, so print and digital preparation both
    /// refuse. The supplier's verdict says so: NOT_RELEASABLE, with the gates that failed named. The
    /// printer's file is withheld, because a press gate is a blocker by the owner's own carve-out.
    /// The family's download is published, because the owner's ruling is that a book with artwork in
    /// hand reaches the family and the problem becomes an alarm — and the waiver saying exactly that
    /// is in the stored document rather than inferable from its absence.
    /// </summary>
    [Fact]
    public async Task The_release_verdict_is_stored_and_withholds_the_files_the_policy_still_blocks()
    {
        var world = new PackWorld();

        await world.Run();

        var gatesName = BekiPackBlobs.ReleaseGatesName(world.UserId, world.PackId);
        Assert.Contains(gatesName, world.Blobs.Uploaded.Keys);

        var verdict = BekiReleaseGateReport.TryParse(
            System.Text.Encoding.UTF8.GetString(world.Blobs.Uploaded[gatesName]))!;

        Assert.Equal(BekiReleaseGates.NotReleasable, verdict.Verdict);
        Assert.NotEmpty(verdict.FailingGates);

        // Withheld on purpose: the slot was written, and written null. Press gates keep their
        // blockers — a bad press PDF is a reprint and an invoice, not a disappointment.
        Assert.True(world.Packs.PrintPdfUrlWritten);
        Assert.Null(world.Packs.PrintPdfUrl);
        Assert.False(verdict.PressFilesMayPublish);
        Assert.False(verdict.SupplierPressReleasable);

        // The parent's download is published under the shipped policy, and the divergence from the
        // supplier's answer is written down rather than left to be worked out.
        Assert.NotNull(world.Packs.PdfUrl);
        Assert.True(verdict.CustomerPdfMayPublish);
        Assert.False(verdict.SupplierCustomerPdfReleasable);
        Assert.NotEmpty(verdict.PolicyWaivers);

        Assert.Equal(AdventurePackStatus.Completed, world.Packs.Status);
        Assert.Contains(
            BekiPackBlobs.SpreadName(world.UserId, world.PackId, 1), world.Blobs.Uploaded.Keys);
    }

    /// <summary>
    /// The supplier's audit found a shipped book with seven composited spreads and one AI-drawn
    /// Beki — a page from somewhere other than the compositor, indistinguishable in the PDF. The
    /// receipt is what tells them apart, so a composite book with a page that has none stops
    /// before layout instead of printing it.
    /// </summary>
    [Fact]
    public async Task A_spread_without_an_exact_Beki_receipt_stops_the_book()
    {
        var world = new PackWorld { DropReceiptForSpread = 8 };

        await world.Job().ProcessAsync(world.PackId, world.RunId, CancellationToken.None);

        Assert.NotNull(world.Packs.FailureReason);
        Assert.StartsWith("IMAGE_GENERATION_FAILED", world.Packs.FailureReason);
        Assert.Contains("spread(s) 8", world.Packs.FailureReason);
    }

    /// <summary>
    /// A composite-enabled run whose theme maps to no approved world used to fall through with a
    /// legacy-shaped resume contract — the shape under which an AI-draws-Beki manifest matches
    /// and its pages could be adopted. Now it refuses at the door, before any adoption decision
    /// exists to get wrong.
    /// </summary>
    [Fact]
    public async Task A_theme_with_no_composite_world_is_refused_before_anything_is_adopted()
    {
        var world = new PackWorld(theme: (ThemeType)99);

        await world.Job().ProcessAsync(world.PackId, world.RunId, CancellationToken.None);

        Assert.NotNull(world.Packs.FailureReason);
        Assert.StartsWith("INVALID_BOOK_INPUT", world.Packs.FailureReason);

        // This refusal, not a later stage's: the point is that no legacy-shaped contract was
        // built and no manifest was consulted on the way to it.
        Assert.Contains("approved composite theme", world.Packs.FailureReason);
    }

    // =======================================================================================
    // Harness
    // =======================================================================================

    /// <summary>
    /// The fulfilment job with everything around it faked: a pack, a run, a blob store that
    /// remembers, a generator that hands back a composite book, and a composer that records which
    /// cover it was given.
    /// </summary>
    private sealed class PackWorld
    {
        public const string PreviewCoverUrl = "https://blob.test/master-runs/preview/cover";

        public Guid PackId { get; } = Guid.NewGuid();

        public Guid RunId { get; } = Guid.NewGuid();

        public Guid UserId { get; } = Guid.NewGuid();

        public FakePacks Packs { get; }

        public FakeBlobs Blobs { get; } = new();

        public RecordingComposer Composer { get; } = new();

        /// <summary>A spread whose exact-Beki receipt the stub withholds, for the gate test.</summary>
        public int? DropReceiptForSpread { get; init; }

        /// <summary>A spread the stub hands back flagged as adopted, carrying no receipt of its own.</summary>
        public int? AdoptSpread { get; init; }

        /// <summary>Whether the cover wrap refuses, which is now fatal for a composite book.</summary>
        public bool WrapFails { get; init; }

        /// <summary>Whether the wrap's receipt declares a hash the composited bytes do not have.</summary>
        public bool WrapReceiptLies { get; init; }

        public PackWorld(ThemeType theme = ThemeType.Dinosaurs) =>
            Packs = new FakePacks(new AdventurePack
            {
                Id = PackId,
                UserId = UserId,
                Theme = theme,
                Status = AdventurePackStatus.StoryReady,
                CoverImageUrl = PreviewCoverUrl,
                CreatedAt = DateTime.UtcNow,
            });

        /// <summary>
        /// Runs the job and refuses to let it fail quietly: the fulfilment catch swallows every
        /// exception into a Failed status, so a harness that is wired wrong would otherwise look
        /// like a pipeline that decided not to store a cover.
        /// </summary>
        public async Task Run()
        {
            await Job().ProcessAsync(PackId, RunId, CancellationToken.None);

            Assert.Null(Packs.FailureReason);
        }

        public BekiPackFulfillment Job() =>
            new(Packs,
                new FakeRuns(RunId),
                Blobs,
                new StubGenerator(DropReceiptForSpread, AdoptSpread, WrapFails, WrapReceiptLies),
                Composer,
                new SilentNotifier(),
                new RecordingEmailService(),
                new SingleUserRepository(),
                Options.Create(new BekiOptions { CompositePipelineEnabled = true }),
                NullLogger<BekiPackFulfillment>.Instance,
                TimeProvider.System);

        /// <summary>What an earlier attempt left behind for this one to resume from.</summary>
        public void SeedManifest(
            BekiCoverRecord? cover = null,
            IReadOnlyList<BekiCompositionManifestEntry>? compositions = null)
        {
            var manifest = new BekiFulfillmentManifest
            {
                // The contract this run will compute for itself, so the manifest is adopted rather
                // than discarded — these tests are about the cover, not about invalidation.
                IllustrationContract = BekiFulfillmentManifest.CurrentContract(
                    BookFormat.SpreadCount,
                    BekiCompositeContractTerms.Current("dinosaurs")),
                Entries = [],
                Cover = cover,
                Compositions = compositions,
            };

            Blobs.Seed(
                BekiPackBlobs.ManifestName(UserId, PackId),
                JsonSerializer.SerializeToUtf8Bytes(
                    manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }

        public BekiFulfillmentManifest StoredManifest() =>
            JsonSerializer.Deserialize<BekiFulfillmentManifest>(
                Blobs.Uploaded[BekiPackBlobs.ManifestName(UserId, PackId)],
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    /// <summary>
    /// A generator that returns a finished composite book and, on request, a cover wrap.
    ///
    /// The wrap is the part these tests are about. It hands back a real composition receipt — the
    /// composited bytes' own SHA-256 under <c>output.sha256</c> — because that agreement is exactly
    /// what the fulfilment job now checks before it cuts a single derivation from those bytes.
    /// </summary>
    private sealed class StubGenerator(
        int? dropReceiptForSpread = null,
        int? adoptSpread = null,
        bool wrapFails = false,
        bool wrapReceiptLies = false) : IBekiBookGenerator
    {
        public static readonly byte[] WrapComposite = [0x89, (byte)'P', (byte)'N', (byte)'G', 7, 7];

        public Task<BekiBookResult> GenerateAsync(
            MasterStoryInput input, byte[] childPhoto, string childPhotoContentType,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<BekiImageResult> DrawCoverAsync(
            MasterStory plan, byte[] childPhoto, string childPhotoContentType,
            CancellationToken cancellationToken, CompositeBookContext? composite = null) =>
            throw new NotSupportedException();

        public Task<CompositeCoverWrap> DrawCoverWrapAsync(
            VisualScenarioV2 scenario, byte[] childPhoto, string childPhotoContentType,
            CompositeBookContext composite, CancellationToken cancellationToken)
        {
            if (wrapFails)
            {
                throw new BekiLayoutException(
                    CompositeFailureCodes.LayoutFailed,
                    "the cover wrap could not be drawn in this deployment.");
            }

            var declared = wrapReceiptLies
                ? new string('f', 64)
                : Convert.ToHexString(SHA256.HashData(WrapComposite)).ToLowerInvariant();

            var receipt = JsonSerializer.Serialize(new
            {
                composition_version = "beki-exact-composite-v1",
                beki_layer = new
                {
                    pose_id = "pose_01_neutral_hover",
                    normalized_anchor = new
                    {
                        visible_center_x = 0.87,
                        visible_center_y = 0.64,
                        visible_height = 0.30,
                    },
                },
                output = new { file = "cover-wrap-composite.png", sha256 = declared },
            });

            return Task.FromResult(new CompositeCoverWrap(
                [0x89, (byte)'P', (byte)'N', (byte)'G', 1],
                WrapComposite,
                receipt,
                "pose_01_neutral_hover",
                "wrap prompt"));
        }

        public Task<BekiBookResult> IllustrateAsync(
            MasterStory plan, byte[] childPhoto, string childPhotoContentType, byte[]? existingCover,
            Func<BekiImageResult, Task>? onImage, CancellationToken cancellationToken,
            IReadOnlyDictionary<int, byte[]>? existingSpreads = null,
            CompositeBookContext? composite = null)
        {
            // The previewed cover, adopted. Since audit-2 the composite path draws no cover of its
            // own here at all: the master is the wrap, and it is produced after the spreads.
            var cover = new BekiImageResult
            {
                Image = [7, 7, 7, 7],
                Accepted = true,
                Verdict = "Adopted from the preview the parent chose; not drawn here.",
                Attempts = 0,
                Prompt = string.Empty,
            };

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

            return Task.FromResult(new BekiBookResult
            {
                Plan = plan,
                AppearanceDescription = string.Empty,
                Cover = cover,
                Spreads = spreads,
                Warnings = [],
                // Non-null: everything these tests are about lives inside the composite branch.
                Composite = new CompositeBookArtifacts
                {
                    ScenarioJson = ScenarioJson,
                    ReviewJson = """{"needs_human_reading": false}""",
                    // One receipt per page: the fulfilment job refuses to lay out a composite
                    // spread without its exact-Beki composition receipt, and this stub's book
                    // claims to be a finished composite book.
                    Spreads = spreads
                        .Where(spread => spread.SpreadNumber != dropReceiptForSpread)
                        .Select(spread => spread.SpreadNumber == adoptSpread
                            // The adopted shape the pipeline hands back: flagged, and empty, so a
                            // fulfilment layer that stored it would be storing a blank receipt.
                            ? new CompositeSpreadArtifact(
                                spread.SpreadNumber!.Value, string.Empty, string.Empty,
                                string.Empty, BasePng: [])
                            {
                                Adopted = true,
                            }
                            : new CompositeSpreadArtifact(
                                spread.SpreadNumber!.Value,
                                "pose_01_neutral_hover",
                                "{}",
                                new string('0', 64),
                                BasePng: [])
                            {
                                QaJson = $$"""
                                    {"page": {{spread.SpreadNumber}},
                                     "qa_prompt_version": "{{CompositeMinimalQa.Version}}",
                                     "status": "PASS", "recommended_action": "ship"}
                                    """,
                            })
                        .ToList(),
                },
            });
        }

        /// <summary>
        /// A scenario the validator accepts, so the wrap stage can read one back. The same approved
        /// Nina fixture the pipeline tests plan from, rather than a second one written here.
        /// </summary>
        private static string ScenarioJson => File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "nina_dinosaurs", "visual_scenario_output_v2.json"));
    }

    private sealed class RecordingComposer : IBekiPdfComposer
    {
        /// <summary>The wrap the customer's book was cut from — the single-master check.</summary>
        public byte[]? ReadingWrap { get; private set; }

        /// <summary>Whether the print interior was composed as its own artifact.</summary>
        public bool InteriorComposed { get; private set; }

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
            BekiBookPersonalization? personalization = null)
        {
            InteriorComposed = true;
            return new BekiComposedBook([0x25, 0x50, 0x44, 0x46, 0x2D], Receipts("interior"));
        }

        public BekiComposedBook ComposeCoverPressWithReceipts(string title, byte[] wrapComposite) =>
            new([0x25, 0x50, 0x44, 0x46, 0x2D], Receipts("cover"));

        public byte[] CropFrontBoard(byte[] wrapPng) => [.. wrapPng, 0xF1];

        public byte[] CropBackBoard(byte[] wrapPng) => [.. wrapPng, 0xB1];

        public IReadOnlyList<byte[]> RenderPages(
            MasterStory plan, byte[] coverImage, IReadOnlyList<BekiSpreadArtwork> spreads,
            BekiBookPersonalization? personalization = null) => throw new NotSupportedException();

        /// <summary>
        /// A minimal receipt book: enough pages for the fixed-page QA to have something to describe,
        /// which is what the VISUAL_QA gate reads.
        /// </summary>
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

    /// <summary>A blob store that remembers what it was given and hands it back.</summary>
    private sealed class FakeBlobs : IBlobStorageService
    {
        public Dictionary<string, byte[]> Uploaded { get; } = new(StringComparer.Ordinal);

        public void Seed(string blobName, byte[] bytes) => Uploaded[blobName] = bytes;

        public Task<string> UploadAsync(
            string blobName, byte[] bytes, string contentType, CancellationToken cancellationToken)
        {
            Uploaded[blobName] = bytes;
            return Task.FromResult($"https://blob.test/{blobName}");
        }

        public Task<Stream> DownloadAsync(string blobName, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream(
                Uploaded.TryGetValue(blobName, out var bytes) ? bytes : []));

        public Task<bool> ExistsAsync(string blobName, CancellationToken cancellationToken) =>
            Task.FromResult(Uploaded.ContainsKey(blobName));

        /// <summary>The stored URL is the blob name with this fake's prefix, so it round-trips.</summary>
        public Task<byte[]> DownloadBytesFromStoredUrlAsync(
            string storedUrl, CancellationToken cancellationToken)
        {
            var name = storedUrl.Replace("https://blob.test/", string.Empty, StringComparison.Ordinal);

            return Task.FromResult(Uploaded.TryGetValue(name, out var bytes) ? bytes : [1, 1, 1, 1]);
        }

        public Task<bool> DeleteByStoredUrlAsync(string storedUrl, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    /// <summary>The pack row, remembering the one column these tests are about.</summary>
    private sealed class FakePacks(AdventurePack seed) : IAdventurePackRepository
    {
        private readonly AdventurePack _pack = seed;

        public string? CoverImageUrl => _pack.CoverImageUrl;

        /// <summary>The parent's download column — null while a gate is still withholding it.</summary>
        public string? PdfUrl => _pack.PdfUrl;

        public AdventurePackStatus Status => _pack.Status;

        public Task UpdateBookPresentationAsync(
            Guid id, string? title, string? coverImageUrl, CancellationToken cancellationToken)
        {
            // COALESCE, like the real one: a null leaves the column as it was.
            _pack.Title = title ?? _pack.Title;
            _pack.CoverImageUrl = coverImageUrl ?? _pack.CoverImageUrl;
            return Task.CompletedTask;
        }

        public Task<AdventurePack?> GetByIdNoOwnershipAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<AdventurePack?>(_pack);

        public Task<bool> UpdateStatusAsync(
            Guid id, AdventurePackStatus status, string? generatedJson, string? pdfUrl,
            string? errorMessage, CancellationToken cancellationToken)
        {
            _pack.Status = status;
            return Task.FromResult(true);
        }

        public Task UpdateProgressAsync(
            Guid id, string? progressMessage, int? progressPercent, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public string? PrintPdfUrl { get; private set; }

        /// <summary>Distinguishes "withheld on purpose" from "never written at all".</summary>
        public bool PrintPdfUrlWritten { get; private set; }

        public Task UpdatePrintPdfUrlAsync(Guid id, string? printPdfUrl, CancellationToken cancellationToken)
        {
            PrintPdfUrl = printPdfUrl;
            PrintPdfUrlWritten = true;
            return Task.CompletedTask;
        }

        // Everything else the interface declares and this job never calls.
        public Task<Guid> CreatePendingAsync(AdventurePack pack, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AdventurePack?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AdventurePack>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AdventurePack>> GetByCharacterIdAsync(
            Guid characterId, Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> GetNextSequenceNumberAsync(Guid seriesId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> SetAccessLevelAsync(
            Guid id, BookAccessLevel accessLevel, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> MarkReadAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> SetPrintEntitlementAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> CountForMonthAsync(
            Guid userId, DateTime utcMonthStart, DateTime utcMonthEnd, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> TryUpdateStatusAsync(
            Guid id, AdventurePackStatus expected, AdventurePackStatus status, string? generatedJson,
            string? pdfUrl, string? errorMessage, CancellationToken cancellationToken)
        {
            _pack.Status = status;

            // The real statement writes the column unconditionally, null included — which is how a
            // withheld download is recorded rather than merely not recorded.
            _pack.PdfUrl = pdfUrl;
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<StaleGenerationPack>> ListStaleGenerationAsync(
            DateTime cutoffUtc, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> TryFailStaleGenerationAsync(
            Guid id, AdventurePackStatus expected, DateTime cutoffUtc, string errorMessage,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        /// <summary>Records why the job gave up, so a broken harness says so instead of vanishing.</summary>
        public string? FailureReason { get; private set; }

        public Task<bool> TryFailAsync(
            Guid id, AdventurePackStatus expectedStatus, string errorMessage,
            CancellationToken cancellationToken)
        {
            FailureReason = errorMessage;
            _pack.Status = AdventurePackStatus.Failed;
            return Task.FromResult(true);
        }

        public Task UpdateProgressMessageAsync(
            Guid id, string? progressMessage, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SetPdfCreditChargedAsync(Guid id, bool charged, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpdatePreviewIllustrationAsync(
            Guid id, PreviewIllustrationStatus status, string? illustrationUrl,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> TryClaimPreviewIllustrationGenerationAsync(
            Guid id, int staleAfterMinutes, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task TouchPreviewIllustrationHeartbeatAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> UpdateGeneratedJsonAsync(
            Guid id, string generatedJson, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        // B5's discriminator and B7's withheld sweep. Neither is this double's subject: the pipeline
        // stamp is recorded so a test can read it back, and no test here asks for withheld books.
        public string? StampedPipeline { get; private set; }

        public Task SetGenerationPipelineAsync(Guid id, string pipeline, CancellationToken cancellationToken)
        {
            StampedPipeline = pipeline;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AdventurePack>> ListWithheldBekiPacksAsync(
            int limit, BekiWithheldCursor? after, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AdventurePack>>([]);

        public Task TouchGenerationHeartbeatAsync(Guid id, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    /// <summary>The preview run this pack was bought from: a plan, a portrait and a cover.</summary>
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
            PhotoBlobUrl = "https://blob.test/portrait.png",
            CoverImageUrl = PackWorld.PreviewCoverUrl,
            StoryJson = JsonSerializer.Serialize(Plan(), StoryJson.Options),
        };

        public Task<MasterStoryRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<MasterStoryRun?>(_run);

        public Task CreateAsync(MasterStoryRun run, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<MasterStoryRunProgress?> GetProgressAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<MasterStoryRunProgress?>(null);

        public Task SetProgressAsync(
            Guid id, string status, string? progressMessage, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SavePromptsAsync(
            Guid id, string model, string promptVersion, string systemPrompt, string userPrompt,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveStoryAsync(
            Guid id, string storyJson, string contentJson, int promptTokens, int completionTokens,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveCoverAsync(Guid id, string coverImageUrl, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task MarkReadyAsync(Guid id, string contentJson, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ClaimAsync(Guid id, Guid userId, Guid? packId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<ExpiredMasterStoryRun>> ListExpiredAsync(
            int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExpiredMasterStoryRun>>([]);

        public Task<int> DeleteAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }

    private sealed class SilentNotifier : IAdminNotifier
    {
        public Task OrderPaidAsync(
            AdventurePacks.Api.Domain.Entities.Order order, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task BookFailedAsync(Guid packId, string reason, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task PrintOrderPlacedAsync(
            AdventurePacks.Api.Domain.Entities.PrintOrder printOrder, string? bookTitle,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>A plan the fulfilment job can read: eight Georgian spreads and a cover brief.</summary>
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
