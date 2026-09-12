using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story.Composite;
using AdventurePacks.Api.Services.Story.Composite.Poses;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace AdventurePacks.Api.Services.Story;

public interface IFastPreviewService
{
    Task GenerateAsync(MasterStoryRun run, CancellationToken cancellationToken);
    Task<FastPreviewRevision> ReadAsync(Guid runId, CancellationToken cancellationToken);
    Task ValidatePurchaseAsync(Guid userId, Guid runId, Guid? revisionId, string theme, string name, CancellationToken cancellationToken, int? age = null, string? gender = null);
}

/// <summary>Owns immutable preview assets. No story, identity, polish or review model is reachable here.</summary>
public sealed class FastPreviewService(
    ICompositeBookPipeline pipeline, IBekiPdfComposer composer, IBlobStorageService blobs,
    IMasterStoryRunRepository runs, IReferenceImageNormalizer normalizer,
    IOptions<BekiOptions> options, ILogger<FastPreviewService> logger) : IFastPreviewService
{
    public async Task GenerateAsync(MasterStoryRun run, CancellationToken ct)
    {
        if (run.Status == MasterStoryRunStatus.Ready && run.CoverImageUrl is not null) return;
        await runs.SetProgressAsync(run.Id, MasterStoryRunStatus.Illustrating, "ვქმნით შენი წიგნის ყდას…", ct);
        // A finished revision is the commit marker: restart only republishes its pointer.
        if (await blobs.ExistsAsync(FastPreviewPlan.ReceiptName(run.Id), ct))
        {
            var ready = await ReadAsync(run.Id, ct);
            await PublishAsync(run, ready, ct);
            return;
        }
        var photo = await blobs.DownloadBytesFromStoredUrlAsync(run.PhotoBlobUrl!, ct);
        var normalized = InputNormalization.Normalize(BekiCompositeInputs.For(run, run.Theme), photo);
        if (!normalized.IsValid) throw new InvalidOperationException(string.Join(" ", normalized.Problems));
        var theme = normalized.Story!.ThemeId;
        var title = FastPreviewPlan.Title(run.ChildName, theme, run.Id);
        var scenario = FastPreviewPlan.Plan(theme);
        var watch = Stopwatch.StartNew();
        CompositeCoverWrap wrap;
        FastPreviewRevision? reusedRevision = null;
        var newImageCalls = 0;
        // A name-only revision copies the prior unlettered artwork. The source is verified before use.
        var sourceId = Guid.TryParse(run.UserPrompt, out var source) ? source : (Guid?)null;
        if (sourceId is { } previousId)
        {
            var previous = await runs.GetByIdAsync(previousId, ct) ?? throw new InvalidOperationException("Previous preview expired.");
            var receipt = await ReadAsync(previousId, ct);
            reusedRevision = receipt;
            title = FastPreviewPlan.RenameTitle(receipt.Title, previous.ChildName, run.ChildName);
            if (previous.Age != run.Age || previous.Gender != run.Gender || receipt.ThemeId != theme
                || receipt.PhotoSha256 != FastPreviewPlan.Sha(photo))
                throw new InvalidOperationException("The source preview does not match these inputs.");
            wrap = await ReadWrapAsync(previousId, receipt, ct);
        }
        else if (await blobs.ExistsAsync(BekiRunBlobs.CoverCompositionName(run.Id), ct))
        {
            wrap = await ReadWrapAsync(run.Id, null, ct);
        }
        else
        {
            // Persist an attempt marker before HTTP. An uncertain timeout must not silently buy a second image.
            var attempt = $"master-runs/{run.Id:N}/cover-attempt.json";
            if (await blobs.ExistsAsync(attempt, ct))
                throw new InvalidOperationException("The previous cover attempt was interrupted. Start a new preview to retry.");
            await Put(attempt, Encoding.UTF8.GetBytes("{\"attempts\":1}"), "application/json", ct);
            newImageCalls = 1;
            wrap = await pipeline.DrawFastPreviewCoverAsync(new CompositeBookContext
            {
                JobId = run.Id, Input = BekiCompositeInputs.For(run, run.Theme)
            }, photo, ct);
        }
        // The composition manifest is written last so restart never mistakes a partial upload for a saved base.
        await Put(BekiRunBlobs.CoverWrapBaseName(run.Id), wrap.BasePng, "image/png", ct);
        await Put(BekiRunBlobs.CoverWrapCompositeName(run.Id), wrap.CompositePng, "image/png", ct);
        await Put(BekiRunBlobs.CoverWrapGenerationName(run.Id), Encoding.UTF8.GetBytes(wrap.GenerationReceiptJson!), "application/json", ct);
        await Put(BekiRunBlobs.CoverCompositionName(run.Id), Encoding.UTF8.GetBytes(wrap.ManifestJson), "application/json", ct);
        await Put(BekiRunBlobs.ScenarioName(run.Id), Encoding.UTF8.GetBytes(scenario.Json), "application/json", ct);

        // Compose only two preview visuals; no full PDF, print preparation, QR or credits.
        // The single-page cover PDF preserves the exact vector title and logo the paid composer uses.
        // It exists to be rastered and to be hashed, and is not written down: no PDF is stored.
        var coverPdf = composer.ComposeCoverPressWithReceipts(title, wrap.CompositePng).Pdf;
        var layout = composer.CapturePreviewLayout(title, wrap.CompositePng);
        var rendered = await RasterCoverAsync(coverPdf, ct);
        var front = normalizer.NormalizeForStorageWebp(composer.CropFrontBoard(rendered));
        var intro = normalizer.NormalizeForStorageWebp(composer.RenderPreviewIntro(title,
            new BekiBookPersonalization(run.ChildName, run.Age, DateTime.UtcNow, theme, theme) { PrepareForPrint = false }));
        var frontUrl = await blobs.UploadAsync(BekiRunBlobs.CoverName(run.Id), front.Bytes, front.ContentType, ct);
        await Put(FastPreviewPlan.IntroName(run.Id), intro.Bytes, intro.ContentType, ct);
        var revision = new FastPreviewRevision(run.Id, Guid.NewGuid(), theme, title, FastPreviewPlan.Sha(photo),
            FastPreviewPlan.Sha(wrap.CompositePng), FastPreviewPlan.Sha(wrap.BasePng), FastPreviewPlan.Version,
            reusedRevision?.PromptSha256 ?? FastPreviewPlan.Sha(Encoding.UTF8.GetBytes(FastPreviewPlan.Prompt(run.Age, normalized.Story.ChildGender, theme, run.Id))),
            FastPreviewPlan.Model, scenario.Json, layout, FastPreviewPlan.Sha(coverPdf),
            FastPreviewPlan.Sha(front.Bytes), FastPreviewPlan.Sha(intro.Bytes), frontUrl);
        await Put(FastPreviewPlan.ReceiptName(run.Id), JsonSerializer.SerializeToUtf8Bytes(revision), "application/json", ct);
        await PublishAsync(run, revision, ct);
        logger.LogInformation("Fast preview {PreviewId} revision {RevisionId} ready in {ElapsedMs}ms; newImageCalls={Calls}, storyCalls=0, identityCalls=0, plannerCalls=0",
            run.Id, revision.CoverRevisionId, watch.ElapsedMilliseconds, newImageCalls);
    }

    private async Task PublishAsync(MasterStoryRun run, FastPreviewRevision revision, CancellationToken ct)
    {
        if (revision.FrontUrl is null) throw new InvalidOperationException("Missing preview derivative URL.");
        await runs.SaveCoverAsync(run.Id, revision.FrontUrl, ct);
        await runs.MarkReadyAsync(run.Id, JsonSerializer.Serialize(revision), ct);
    }

    public async Task<FastPreviewRevision> ReadAsync(Guid runId, CancellationToken ct)
    {
        var revision = JsonSerializer.Deserialize<FastPreviewRevision>(await ReadBlob(FastPreviewPlan.ReceiptName(runId), ct))
            ?? throw new InvalidOperationException("Missing preview revision.");
        if (revision.PreviewId != runId || revision.CoverRevisionId == Guid.Empty)
            throw new InvalidOperationException("The stored preview revision belongs to a different run.");
        return revision;
    }

    public async Task ValidatePurchaseAsync(Guid userId, Guid runId, Guid? revisionId, string theme, string name, CancellationToken ct, int? age = null, string? gender = null)
    {
        var run = await runs.GetByIdAsync(runId, ct) ?? throw new InvalidOperationException("Preview expired.");
        if (!FastPreviewPlan.IsFast(run)) return;
        if (run.PackId is not null || run.Status != MasterStoryRunStatus.Ready || run.UserId is { } owner && owner != userId
            || run.ExpiresAt is { } expiry && expiry <= DateTime.UtcNow)
            throw new InvalidOperationException("Preview is not available for checkout.");
        var revision = await ReadAsync(runId, ct);
        var portrait = await blobs.DownloadBytesFromStoredUrlAsync(run.PhotoBlobUrl!, ct);
        var input = InputNormalization.Normalize(BekiCompositeInputs.For(run, theme), portrait);
        if (FastPreviewPlan.Sha(portrait) != revision.PhotoSha256 || revisionId != revision.CoverRevisionId || !input.IsValid || input.Story!.ThemeId != revision.ThemeId
            || name.Trim() != run.ChildName || age is { } givenAge && givenAge != run.Age
            || gender is not null && !string.Equals(gender, run.Gender, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Please open the updated preview before checkout.");
        // The cover being bought is the cover that was shown: ReadWrapAsync hashes the stored
        // wrap against the revision, which is the same guarantee the cover PDF used to carry a
        // second time. The PDF itself is not stored - nothing here keeps one.
        await ReadWrapAsync(runId, revision, ct);
        // Retain it before redirecting to payment; guest expiry must not race a payment callback.
        await runs.ClaimAsync(runId, userId, null, ct);
    }

    private async Task<CompositeCoverWrap> ReadWrapAsync(Guid id, FastPreviewRevision? revision, CancellationToken ct)
    {
        var basePng = await ReadBlob(BekiRunBlobs.CoverWrapBaseName(id), ct);
        var composite = await ReadBlob(BekiRunBlobs.CoverWrapCompositeName(id), ct);
        var json = Encoding.UTF8.GetString(await ReadBlob(BekiRunBlobs.CoverCompositionName(id), ct));
        var manifest = JsonSerializer.Deserialize<BekiCompositionManifest>(json) ?? throw new InvalidOperationException("Missing cover composition.");
        BekiPressComposite.ValidateSource(basePng, composite, manifest);
        if (revision is not null && (FastPreviewPlan.Sha(composite) != revision.MasterSha256 || FastPreviewPlan.Sha(basePng) != revision.BaseSha256))
            throw new InvalidOperationException("Preview cover checksum mismatch.");
        return new CompositeCoverWrap(basePng, composite, json, manifest.BekiLayer.PoseId, "")
        { GenerationReceiptJson = Encoding.UTF8.GetString(await ReadBlob(BekiRunBlobs.CoverWrapGenerationName(id), ct)) };
    }

    private Task Put(string name, byte[] data, string type, CancellationToken ct) => blobs.UploadAsync(name, data, type, ct);
    private async Task<byte[]> ReadBlob(string name, CancellationToken ct)
    {
        await using var stream = await blobs.DownloadAsync(name, ct);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, ct);
        return memory.ToArray();
    }

    private async Task<byte[]> RasterCoverAsync(byte[] pdf, CancellationToken ct)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"beki-preview-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = Path.Combine(dir, "cover.pdf");
            var output = Path.Combine(dir, "cover");
            await File.WriteAllBytesAsync(input, pdf, ct);
            var configured = options.Value.PrintPrep.PopplerPdftoppmPath;
            using var process = new Process { StartInfo = new ProcessStartInfo
            {
                FileName = string.IsNullOrWhiteSpace(configured) ? "pdftoppm" : configured,
                UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true
            }};
            foreach (var arg in new[] { "-singlefile", "-scale-to", "1200", "-png", input, output }) process.StartInfo.ArgumentList.Add(arg);
            process.Start();
            var errors = process.StandardError.ReadToEndAsync(ct);
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(limit.Token); }
            catch { if (!process.HasExited) process.Kill(true); throw; }
            await Task.WhenAll(errors, stdout);
            if (process.ExitCode != 0) throw new InvalidOperationException("Could not render the preview cover.");
            return await File.ReadAllBytesAsync(output + ".png", ct);
        }
        finally { Directory.Delete(dir, true); }
    }
}
