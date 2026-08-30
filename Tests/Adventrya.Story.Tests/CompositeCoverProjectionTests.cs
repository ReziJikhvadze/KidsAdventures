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
/// Which cover the reader is served, and which one the PDF is laid out from — and that the two are
/// the same picture.
///
/// They were not. From v1.2 the fulfilment job redraws the cover once the book's first spread is
/// accepted, with the child's identity lock in the prompt and that spread as the appearance anchor,
/// and stores it under the pack's own prefix; the PDF has been laid out from it since. The reader
/// went on serving the pack's cover column, which has held the preview run's picture since
/// purchase — the one drawn before the child had an identity spec at all, and the one the owner
/// watched lose the eye colour. The owner's first check is the on-screen book, so a book whose
/// cover was fixed only in the PDF was a book whose fix nobody could see.
///
/// Three cases, and the third is the one worth writing a harness for: a resumed run that adopted
/// every page draws no cover of its own, and must neither overwrite the stored redraw with the
/// previewed picture nor stop pointing at it.
/// </summary>
public class CompositeCoverProjectionTests
{
    [Fact]
    public async Task An_accepted_redraw_points_the_reader_at_the_packs_own_cover()
    {
        var world = new PackWorld();

        await world.Run();

        // The cover was stored under the pack's prefix…
        var coverName = BekiPackBlobs.CoverName(world.UserId, world.PackId);
        Assert.Contains(coverName, world.Blobs.Uploaded.Keys);

        // …and the reader's cover column now points at it rather than at the preview run's.
        Assert.Equal($"https://blob.test/{coverName}", world.Packs.CoverImageUrl);
        Assert.NotEqual(PackWorld.PreviewCoverUrl, world.Packs.CoverImageUrl);

        // The manifest says which of the two provenances shipped, with the verdict that passed.
        var manifest = world.StoredManifest();
        Assert.Equal(
            CompositeIllustrationPrompt.CoverRedrawVersion, manifest.Cover!.PromptVersion);
        Assert.True(manifest.Cover.IsRedraw);
        Assert.Contains("PASS", manifest.Cover.Verdict);

        // And the PDF was laid out from the same bytes the reader is now served.
        Assert.Equal(world.Blobs.Uploaded[coverName], world.Composer.CoverLaidOut);
    }

    [Fact]
    public async Task An_adopted_preview_cover_leaves_the_reader_pointing_where_it_did()
    {
        var world = new PackWorld { RedrawTheCover = false };

        await world.Run();

        // Unchanged: an adopted cover IS the preview run's cover, so re-pointing the column at a
        // copy of it would change nothing except which blob a reader has to fetch.
        Assert.Equal(PackWorld.PreviewCoverUrl, world.Packs.CoverImageUrl);

        var manifest = world.StoredManifest();
        Assert.Equal(BekiCoverRecord.AdoptedPreviewCover, manifest.Cover!.PromptVersion);
        Assert.False(manifest.Cover.IsRedraw);

        // Nobody reviewed it, and a blank verdict in that field would read as a pass.
        Assert.Null(manifest.Cover.Verdict);
    }

    /// <summary>
    /// A resumed run that adopted every page draws no cover, and must leave the redrawn one alone —
    /// the blob, the record and the reader's pointer.
    ///
    /// The hazard is quiet and total: the run hands back the previewed cover because that is all it
    /// has, and an unguarded job would upload it over the reviewed one, rewrite the record to say
    /// "adopted", and leave a pack whose reader points at a picture that had just been replaced by
    /// a worse one.
    /// </summary>
    [Fact]
    public async Task A_resumed_run_keeps_the_cover_an_earlier_attempt_redrew()
    {
        var world = new PackWorld { RedrawTheCover = false };

        var coverName = BekiPackBlobs.CoverName(world.UserId, world.PackId);
        var redrawnCover = new byte[] { 9, 9, 9, 9 };

        // What the earlier attempt left: a redrawn cover blob and a manifest that says so.
        world.Blobs.Seed(coverName, redrawnCover);
        world.SeedManifest(new BekiCoverRecord(
            $"https://blob.test/{coverName}",
            CompositeIllustrationPrompt.CoverRedrawVersion,
            "PASS (pass)"));

        await world.Run();

        // The stored cover is untouched: this run never uploaded over it.
        Assert.Equal(redrawnCover, world.Blobs.Uploaded[coverName]);

        // The record still says a redraw shipped, with the verdict it shipped under.
        var manifest = world.StoredManifest();
        Assert.True(manifest.Cover!.IsRedraw);
        Assert.Equal("PASS (pass)", manifest.Cover.Verdict);

        // The reader still points at it…
        Assert.Equal($"https://blob.test/{coverName}", world.Packs.CoverImageUrl);

        // …and this attempt's PDF was laid out from the stored redraw rather than from the
        // previewed cover it was handed, so the printed book and the screen agree.
        Assert.Equal(redrawnCover, world.Composer.CoverLaidOut);
    }

    /// <summary>
    /// The print slot never points at the hybrid again. With print prep unconfigured — this
    /// deployment's actual state, the FOGRA39 profile being an owner-side input — the interior
    /// is composed, the stage refuses, and the slot is explicitly cleared: a withheld print
    /// artifact with a named reason, not a layout export wearing a print label. The parent's
    /// reading copy ships regardless.
    /// </summary>
    [Fact]
    public async Task The_print_slot_is_withheld_until_print_prep_can_actually_run()
    {
        var world = new PackWorld();

        await world.Run();

        Assert.True(world.Composer.InteriorComposed);

        // Withheld on purpose: the slot was written, and written null.
        Assert.True(world.Packs.PrintPdfUrlWritten);
        Assert.Null(world.Packs.PrintPdfUrl);

        // No print artifact was stored, and the reading copy was — under its own name.
        Assert.DoesNotContain($"{world.UserId}/{world.PackId}-interior.pdf", world.Blobs.Uploaded.Keys);
        Assert.Contains($"{world.UserId}/{world.PackId}.pdf", world.Blobs.Uploaded.Keys);
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

        /// <summary>
        /// Whether the generator hands back a cover it drew and had reviewed, or the previewed one
        /// adopted unchanged — which is what a refused redraw and a resume both produce.
        /// </summary>
        public bool RedrawTheCover { get; init; } = true;

        /// <summary>A spread whose exact-Beki receipt the stub withholds, for the gate test.</summary>
        public int? DropReceiptForSpread { get; init; }

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
                new StubGenerator(RedrawTheCover, DropReceiptForSpread),
                Composer,
                new SilentNotifier(),
                new RecordingEmailService(),
                new SingleUserRepository(),
                Options.Create(new BekiOptions { CompositePipelineEnabled = true }),
                NullLogger<BekiPackFulfillment>.Instance,
                TimeProvider.System);

        /// <summary>What an earlier attempt left behind for this one to resume from.</summary>
        public void SeedManifest(BekiCoverRecord cover)
        {
            var manifest = new BekiFulfillmentManifest
            {
                // The contract this run will compute for itself, so the manifest is adopted rather
                // than discarded — this test is about the cover, not about invalidation.
                IllustrationContract = BekiFulfillmentManifest.CurrentContract(
                    BookFormat.SpreadCount,
                    BekiCompositeContractTerms.Current("dinosaurs")),
                Entries = [],
                Cover = cover,
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
    /// A generator that returns a finished composite book. The cover is the only part these tests
    /// are about: attempt rows are what tell the job a redraw actually happened, exactly as the
    /// real generator reports it.
    /// </summary>
    private sealed class StubGenerator(bool redrawTheCover, int? dropReceiptForSpread = null)
        : IBekiBookGenerator
    {
        public Task<BekiBookResult> GenerateAsync(
            MasterStoryInput input, byte[] childPhoto, string childPhotoContentType,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<BekiImageResult> DrawCoverAsync(
            MasterStory plan, byte[] childPhoto, string childPhotoContentType,
            CancellationToken cancellationToken, CompositeBookContext? composite = null) =>
            throw new NotSupportedException();

        public Task<BekiBookResult> IllustrateAsync(
            MasterStory plan, byte[] childPhoto, string childPhotoContentType, byte[]? existingCover,
            Func<BekiImageResult, Task>? onImage, CancellationToken cancellationToken,
            IReadOnlyDictionary<int, byte[]>? existingSpreads = null,
            CompositeBookContext? composite = null)
        {
            var cover = redrawTheCover
                ? new BekiImageResult
                {
                    Image = [1, 2, 3, 4],
                    Accepted = true,
                    Verdict = "PASS (pass)",
                    Attempts = 1,
                    AttemptDetails = [new BekiImageAttempt(10, 5, "PASS (pass)", true)],
                    Prompt = "cover prompt",
                }
                : new BekiImageResult
                {
                    // The previewed cover, adopted: no attempt rows, because nothing was drawn or
                    // reviewed. That absence is what the job reads.
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
                // Non-null: the cover block this test is about lives inside the composite branch.
                Composite = new CompositeBookArtifacts
                {
                    ScenarioJson = "{}",
                    // One receipt per page: the fulfilment job refuses to lay out a composite
                    // spread without its exact-Beki composition receipt, and this stub's book
                    // claims to be a finished composite book.
                    Spreads = spreads
                        .Where(spread => spread.SpreadNumber != dropReceiptForSpread)
                        .Select(spread => new CompositeSpreadArtifact(
                            spread.SpreadNumber!.Value,
                            "pose_01_neutral_hover",
                            "{}",
                            new string('0', 64),
                            BasePng: []))
                        .ToList(),
                },
            });
        }
    }

    private sealed class RecordingComposer : IBekiPdfComposer
    {
        /// <summary>The cover the PDF was actually laid out from.</summary>
        public byte[]? CoverLaidOut { get; private set; }

        /// <summary>Whether the print interior was composed as its own artifact.</summary>
        public bool InteriorComposed { get; private set; }

        public byte[] Compose(
            MasterStory plan, byte[] coverImage, IReadOnlyList<BekiSpreadArtwork> spreads,
            BekiBookPersonalization? personalization = null)
        {
            CoverLaidOut = coverImage;
            return [0x25, 0x50, 0x44, 0x46];
        }

        public byte[] ComposeInterior(
            MasterStory plan, IReadOnlyList<BekiSpreadArtwork> spreads,
            BekiBookPersonalization? personalization = null)
        {
            InteriorComposed = true;
            return [0x25, 0x50, 0x44, 0x46, 0x2D];
        }

        public IReadOnlyList<byte[]> RenderPages(
            MasterStory plan, byte[] coverImage, IReadOnlyList<BekiSpreadArtwork> spreads,
            BekiBookPersonalization? personalization = null) => throw new NotSupportedException();
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
