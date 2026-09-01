using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Entities;
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
/// The previous (non-composite) pipeline's press interior, and the one gate it was not being asked.
///
/// **Review finding 1.** This branch called the two-value <c>BekiPrintPrep.Prepare</c> with the
/// composed interior and nothing else — no resolution receipt — and then wrote whatever came back
/// into the pack's print slot. That was safe for exactly as long as <c>PRESS_RESOLUTION</c> refused
/// by throwing. It stopped refusing (owner ruling 2026-09-01, rule 4), and what was left was a hole
/// with a very precise shape: layout enlarges an undersized sheet onto the stated trim, the embedded
/// raster then measures a nominal 300 PPI because it genuinely has that many pixels, no receipt
/// arrives to say where those pixels came from, and the gate has nothing to fail on. A low-detail
/// press file went to the printer with a PASSING preflight report stored beside it — a document
/// asserting something nobody had checked, which is the species of claim amendment A1 exists to make
/// impossible.
///
/// The composite path had already been corrected: it concatenates the upscaler's receipt with the
/// composer's and hands both to <c>PrepareWithGates</c>. This path has no upscaler, so the
/// composer's own list IS the whole receipt — and because a legacy book has no release gates, no
/// verdict and no policy to weigh a failure, a failed gate withholds the print slot here rather than
/// publishing and recording.
///
/// These tests drive the real print-preparation stage over the real Ghostscript binary, on the small
/// fixture page <see cref="BekiPressPrepFixtures"/> builds for exactly that: everything except the
/// receipt is genuinely clean, so the only thing that can move the verdict is the receipt.
/// </summary>
public class LegacyFulfillmentPressTests
{
    /// <summary>
    /// The defect itself. A book whose artwork was interpolated up to press size is withheld from
    /// the print slot — and the file and its report are still stored, so the evidence names the gate
    /// rather than disappearing with the URL.
    /// </summary>
    [Fact]
    public async Task An_interpolated_interior_is_withheld_from_the_print_slot()
    {
        var world = new LegacyWorld { Interpolated = true };

        await world.Run();

        Assert.True(world.Packs.PrintPdfUrlWritten);
        Assert.Null(world.Packs.PrintPdfUrl);

        // Stored, not vanished: the printer's file and the report that refuses it are both on the
        // record, and the report names the gate.
        var reportName = BekiPackBlobs.InteriorPreflightName(world.UserId, world.PackId);
        Assert.Contains(BekiPackBlobs.InteriorPdfName(world.UserId, world.PackId), world.Blobs.Uploaded.Keys);
        Assert.Contains(reportName, world.Blobs.Uploaded.Keys);

        using var report = JsonDocument.Parse(
            System.Text.Encoding.UTF8.GetString(world.Blobs.Uploaded[reportName]));

        Assert.Contains(
            report.RootElement.GetProperty("failed_gates").EnumerateArray(),
            gate => gate.GetString() == BekiPrintPrep.PressResolutionGate);

        // The receipt reached the stage at all, which is the half of the fix that is not about
        // withholding: the report can only say "interpolation alone" because it was handed the
        // composer's provenance.
        Assert.Contains(
            "interpolation alone",
            report.RootElement.GetProperty("resolution").GetProperty("problems")[0].GetString()!,
            StringComparison.Ordinal);

        // And the parent is untouched by any of it — the reading copy is published as always.
        Assert.NotNull(world.Packs.PdfUrl);
    }

    /// <summary>
    /// The same book with honest artwork publishes exactly as it always did. The fix is a gate being
    /// asked, not a path being closed.
    /// </summary>
    [Fact]
    public async Task An_interior_that_was_never_enlarged_still_reaches_the_print_slot()
    {
        var world = new LegacyWorld { Interpolated = false };

        await world.Run();

        Assert.True(world.Packs.PrintPdfUrlWritten);
        Assert.Equal(
            $"https://blob.test/{BekiPackBlobs.InteriorPdfName(world.UserId, world.PackId)}",
            world.Packs.PrintPdfUrl);

        var reportName = BekiPackBlobs.InteriorPreflightName(world.UserId, world.PackId);

        using var report = JsonDocument.Parse(
            System.Text.Encoding.UTF8.GetString(world.Blobs.Uploaded[reportName]));

        Assert.Empty(report.RootElement.GetProperty("failed_gates").EnumerateArray());
    }

    // ==============================================================================================
    // Harness
    // ==============================================================================================

    /// <summary>
    /// Everything the fulfilment job touches, with the composite flag OFF — which is the only thing
    /// that selects the branch under test.
    /// </summary>
    private sealed class LegacyWorld
    {
        public Guid PackId { get; } = Guid.NewGuid();

        public Guid RunId { get; } = Guid.NewGuid();

        public Guid UserId { get; } = Guid.NewGuid();

        public FakePacks Packs { get; }

        public FakeBlobs Blobs { get; } = new();

        /// <summary>Whether the composer's receipt admits an interpolation-only enlargement.</summary>
        public bool Interpolated { get; init; }

        public LegacyWorld() =>
            Packs = new FakePacks(new AdventurePack
            {
                Id = PackId,
                UserId = UserId,
                Theme = ThemeType.Dinosaurs,
                Status = AdventurePackStatus.StoryReady,
                CreatedAt = DateTime.UtcNow,
            });

        /// <summary>
        /// Runs the job and refuses to let it fail quietly: the fulfilment catch swallows every
        /// exception into a Failed status, and a harness wired wrong would otherwise look exactly
        /// like a gate deciding to withhold.
        /// </summary>
        public async Task Run()
        {
            await Job().ProcessAsync(PackId, RunId, CancellationToken.None);

            Assert.Null(Packs.FailureReason);
        }

        private BekiPackFulfillment Job() =>
            new(Packs,
                new FakeRuns(RunId),
                Blobs,
                new StubGenerator(),
                new StubComposer(Interpolated),
                new SilentNotifier(),
                new RecordingEmailService(),
                new SingleUserRepository(),
                Options.Create(new BekiOptions { CompositePipelineEnabled = false }),
                NullLogger<BekiPackFulfillment>.Instance,
                TimeProvider.System);
    }

    /// <summary>
    /// A composer that hands back a REAL press document for the interior, so print preparation has
    /// something Ghostscript can convert and the resolution gate has real placements to measure.
    ///
    /// The reading copy's bytes are never opened by this path — they are uploaded and that is all —
    /// so they stay a marker rather than a second slow fixture.
    /// </summary>
    private sealed class StubComposer(bool interpolated) : IBekiPdfComposer
    {
        public BekiComposedBook ComposeWithReceipts(
            MasterStory plan, byte[] coverImage, IReadOnlyList<BekiSpreadArtwork> spreads,
            BekiBookPersonalization? personalization = null) =>
            new([0x25, 0x50, 0x44, 0x46], Receipts("reading", interpolated: false));

        public BekiComposedBook ComposeInteriorWithReceipts(
            MasterStory plan, IReadOnlyList<BekiSpreadArtwork> spreads,
            BekiBookPersonalization? personalization = null) =>
            new(BekiPressPrepFixtures.LightTextOnInk(), Receipts("press", interpolated));

        public IReadOnlyList<byte[]> RenderPages(
            MasterStory plan, byte[] coverImage, IReadOnlyList<BekiSpreadArtwork> spreads,
            BekiBookPersonalization? personalization = null) => throw new NotSupportedException();

        /// <summary>
        /// One page, one raster, and the one field this test turns: where the pixels came from.
        /// <c>interpolated: true</c> is what <see cref="BekiPdfComposer.NormalizeForPrint"/> writes
        /// when it has stretched a short sheet onto the stated trim.
        /// </summary>
        private static BekiLayoutReceipts Receipts(string mode, bool interpolated) => new(
            mode,
            [
                new BekiLayoutPageReceipt(
                    1, "story-spread-01", 100, 70, 0,
                    ImageSha256: [new string('e', 64)],
                    Wash: null,
                    Typography: [new BekiTypographyRecord("body", "Noto Sans Georgian", 12, 1.3, "#FFF8EB")],
                    TextLines: ["ერთი სტრიქონი"],
                    TextProbe: null,
                    SourceSha256: [new string('d', 64)],
                    Rasters:
                    [
                        new BekiRasterProvenance(
                            "story-spread-01",
                            SourceWidthPx: interpolated ? 1536 : 1417,
                            SourceHeightPx: interpolated ? 717 : 283,
                            DeliveredWidthPx: 1417,
                            DeliveredHeightPx: 283,
                            Factor: interpolated ? 2.1d : 1d,
                            Resampler: interpolated ? "lanczos3" : "none",
                            Interpolated: interpolated),
                    ]),
            ]);
    }

    /// <summary>A finished legacy book: a cover it drew, eight spreads, and no composite artifacts.</summary>
    private sealed class StubGenerator : IBekiBookGenerator
    {
        public Task<BekiBookResult> GenerateAsync(
            MasterStoryInput input, byte[] childPhoto, string childPhotoContentType,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<BekiImageResult> DrawCoverAsync(
            MasterStory plan, byte[] childPhoto, string childPhotoContentType,
            CancellationToken cancellationToken, CompositeBookContext? composite = null) =>
            throw new NotSupportedException();

        public Task<CompositeCoverWrap> DrawCoverWrapAsync(
            VisualScenarioV2 scenario, byte[] childPhoto, string childPhotoContentType,
            CompositeBookContext composite, ChildIdentitySpec identity, byte[]? childAnchor,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("The previous path draws no cover wrap.");

        public Task<BekiBookResult> IllustrateAsync(
            MasterStory plan, byte[] childPhoto, string childPhotoContentType, byte[]? existingCover,
            Func<BekiImageResult, Task>? onImage, CancellationToken cancellationToken,
            IReadOnlyDictionary<int, byte[]>? existingSpreads = null,
            CompositeBookContext? composite = null) =>
            Task.FromResult(new BekiBookResult
            {
                Plan = plan,
                AppearanceDescription = string.Empty,
                Cover = new BekiImageResult
                {
                    Image = [7, 7, 7, 7],
                    Accepted = true,
                    Verdict = "PASS",
                    Attempts = 1,
                    Prompt = "cover prompt",
                },
                Spreads = Enumerable.Range(1, BookFormat.SpreadCount)
                    .Select(number => new BekiImageResult
                    {
                        SpreadNumber = number,
                        Image = [(byte)number],
                        Accepted = true,
                        Verdict = "PASS (pass)",
                        Attempts = 1,
                        Prompt = "spread prompt",
                    })
                    .ToList(),
                Warnings = [],
                // Null, which is what makes this the previous path rather than a composite book
                // with the flag switched off underneath it.
                Composite = null,
            });
    }

    /// <summary>A blob store that remembers what it was given and hands it back.</summary>
    private sealed class FakeBlobs : IBlobStorageService
    {
        public Dictionary<string, byte[]> Uploaded { get; } = new(StringComparer.Ordinal);

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

        public Task<byte[]> DownloadBytesFromStoredUrlAsync(
            string storedUrl, CancellationToken cancellationToken)
        {
            var name = storedUrl.Replace("https://blob.test/", string.Empty, StringComparison.Ordinal);

            return Task.FromResult(Uploaded.TryGetValue(name, out var bytes) ? bytes : [1, 1, 1, 1]);
        }

        public Task<bool> DeleteByStoredUrlAsync(string storedUrl, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    /// <summary>The pack row, remembering the two columns these tests are about.</summary>
    private sealed class FakePacks(AdventurePack seed) : IAdventurePackRepository
    {
        private readonly AdventurePack _pack = seed;

        /// <summary>The parent's download column.</summary>
        public string? PdfUrl => _pack.PdfUrl;

        public string? PrintPdfUrl { get; private set; }

        /// <summary>Distinguishes "withheld on purpose" from "never written at all".</summary>
        public bool PrintPdfUrlWritten { get; private set; }

        /// <summary>Records why the job gave up, so a broken harness says so instead of vanishing.</summary>
        public string? FailureReason { get; private set; }

        public Task UpdatePrintPdfUrlAsync(Guid id, string? printPdfUrl, CancellationToken cancellationToken)
        {
            PrintPdfUrl = printPdfUrl;
            PrintPdfUrlWritten = true;
            return Task.CompletedTask;
        }

        public Task<AdventurePack?> GetByIdNoOwnershipAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<AdventurePack?>(_pack);

        public Task UpdateBookPresentationAsync(
            Guid id, string? title, string? coverImageUrl, CancellationToken cancellationToken)
        {
            _pack.Title = title ?? _pack.Title;
            _pack.CoverImageUrl = coverImageUrl ?? _pack.CoverImageUrl;
            return Task.CompletedTask;
        }

        public Task<bool> UpdateStatusAsync(
            Guid id, AdventurePackStatus status, string? generatedJson, string? pdfUrl,
            string? errorMessage, CancellationToken cancellationToken)
        {
            _pack.Status = status;
            return Task.FromResult(true);
        }

        public Task<bool> TryUpdateStatusAsync(
            Guid id, AdventurePackStatus expected, AdventurePackStatus status, string? generatedJson,
            string? pdfUrl, string? errorMessage, CancellationToken cancellationToken)
        {
            _pack.Status = status;
            _pack.PdfUrl = pdfUrl;
            return Task.FromResult(true);
        }

        public Task<bool> TryFailAsync(
            Guid id, AdventurePackStatus expectedStatus, string errorMessage,
            CancellationToken cancellationToken)
        {
            FailureReason = errorMessage;
            _pack.Status = AdventurePackStatus.Failed;
            return Task.FromResult(true);
        }

        public Task UpdateProgressAsync(
            Guid id, string? progressMessage, int? progressPercent, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SetGenerationPipelineAsync(Guid id, string pipeline, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task TouchGenerationHeartbeatAsync(Guid id, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<AdventurePack>> ListWithheldBekiPacksAsync(
            int limit, BekiWithheldCursor? after, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AdventurePack>>([]);

        // Everything else the interface declares and this job never calls.
        public Task<Guid> CreatePendingAsync(AdventurePack pack, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AdventurePack?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AdventurePack>> GetByUserIdAsync(
            Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();

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

        public Task<IReadOnlyList<StaleGenerationPack>> ListStaleGenerationAsync(
            DateTime cutoffUtc, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> TryFailStaleGenerationAsync(
            Guid id, AdventurePackStatus expected, DateTime cutoffUtc, string errorMessage,
            CancellationToken cancellationToken) => throw new NotSupportedException();

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
    }

    /// <summary>The preview run this pack was bought from: a plan and a portrait.</summary>
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
        public Task OrderPaidAsync(Order order, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task BookFailedAsync(Guid packId, string reason, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task PrintOrderPlacedAsync(
            PrintOrder printOrder, string? bookTitle, CancellationToken cancellationToken) =>
            Task.CompletedTask;
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
