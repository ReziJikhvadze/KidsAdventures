using System.Text;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Services.Implementations;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Pdf;
using AdventurePacks.Api.Services.Story;
using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// What the press stage costs, and the four changes made to stop it costing an hour.
///
/// The report this suite answers is the owner's, 2026-09-08: "after the progress bar reaches 91 %
/// the job takes more than an hour on the server". Profiling the stage on the stored book
/// 54cba4b3 found the time in four places, and each of them has a test here — not because a
/// millisecond count belongs in an assertion (this machine is nothing like the App Service), but
/// because each fix is a behaviour that can silently regress: an upload that comes back, a copy
/// that starts travelling through the application again, a renderer that goes back to waiting for
/// its neighbour, a parallelism that stops asking the box how many cores it has.
///
/// The one thing measured rather than asserted is written into the documents the stage already
/// stores, so the next production run says where its own time went.
/// </summary>
public class BekiPressStageSpeedTests
{
    // ---- Beki:PrintPrep:Parallelism -------------------------------------------------------

    [Fact]
    public void Parallelism_defaults_to_the_smaller_of_the_cores_and_three()
    {
        var shipped = new BekiPrintPrepOptions();

        Assert.Equal(0, shipped.Parallelism);
        Assert.Equal(
            Math.Max(1, Math.Min(Environment.ProcessorCount, BekiPrintPrepOptions.DefaultParallelismCeiling)),
            shipped.ResolvedParallelism);

        // The ceiling is what makes this a fix rather than a rename: a large machine still takes
        // three thirteen-megapixel canvases at a time, because the stage runs out of memory long
        // before it runs out of cores.
        Assert.True(shipped.ResolvedParallelism <= 3);
    }

    [Fact]
    public void A_configured_parallelism_is_honoured_and_a_nonsense_one_cannot_be()
    {
        Assert.Equal(1, new BekiPrintPrepOptions { Parallelism = 1 }.ResolvedParallelism);
        Assert.Equal(8, new BekiPrintPrepOptions { Parallelism = 8 }.ResolvedParallelism);

        // A typo in an App Service settings box must not ask for a thousand slots.
        Assert.Equal(
            BekiPrintPrepOptions.MaximumParallelism,
            new BekiPrintPrepOptions { Parallelism = 1000 }.ResolvedParallelism);

        // Negative is "decide from the box", the same as unset — a deployment that clears the key
        // gets the default rather than a stage that never starts.
        Assert.Equal(
            new BekiPrintPrepOptions().ResolvedParallelism,
            new BekiPrintPrepOptions { Parallelism = -4 }.ResolvedParallelism);
    }

    // ---- IBlobStorageService.CopyAsync ----------------------------------------------------

    [Fact]
    public async Task A_store_that_cannot_copy_still_copies_by_downloading_and_uploading()
    {
        // The default member, which is what all eighteen hand-written doubles in this assembly
        // inherit. It has to be a copy, not a stub: the snapshot a re-preparation takes is the only
        // thing standing between a refused candidate and a book that cannot be put back.
        var store = new MinimalStore();
        IBlobStorageService storage = store;
        await storage.UploadAsync("a/book.pdf", [1, 2, 3, 4], "application/pdf", CancellationToken.None);

        await storage.CopyAsync("a/book.pdf", "a/previous/book.pdf", "application/pdf", CancellationToken.None);

        Assert.Equal([1, 2, 3, 4], store.Blobs["a/previous/book.pdf"]);
        Assert.Contains("a/book.pdf", store.Downloads);
    }

    [Fact]
    public async Task The_local_folder_copies_the_file_rather_than_reading_it_into_memory()
    {
        var root = Path.Combine(Path.GetTempPath(), "adventrya-press-copy", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var store = new LocalFileBlobStorageService(
                Options.Create(new LocalBlobOptions { Enabled = true, RootPath = root }),
                Options.Create(new AzureBlobOptions { ContainerName = "books" }),
                new PressTestHostEnvironment(root),
                NullLogger<LocalFileBlobStorageService>.Instance);

            await store.UploadAsync("user/pack.pdf", [9, 8, 7], "application/pdf", CancellationToken.None);

            // Into a folder that does not exist yet, which is every snapshot's first write.
            await store.CopyAsync(
                "user/pack.pdf", "user/previous/20260908T000000Z/pack.pdf", "application/pdf",
                CancellationToken.None);

            Assert.True(await store.ExistsAsync(
                "user/previous/20260908T000000Z/pack.pdf", CancellationToken.None));
            Assert.Equal(
                [9, 8, 7],
                await store.DownloadBytesFromStoredUrlAsync(
                    "books/user/previous/20260908T000000Z/pack.pdf", CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task The_azure_store_asks_the_account_to_copy_rather_than_moving_the_bytes_itself()
    {
        var account = new PressFakeAccount();
        var service = account.Service("books");

        await service.CopyAsync("user/pack.pdf", "user/previous/pack.pdf", "application/pdf",
            CancellationToken.None);

        var container = account.Containers["books"];
        Assert.Equal(
            [("user/previous/pack.pdf", "https://account.blob.core.windows.net/books/user/pack.pdf")],
            container.Copies);

        // The whole point: no download, no upload. A fifty-megabyte PDF being put beside itself
        // used to make two round trips through this process.
        Assert.Equal(0, container.Downloads);
        Assert.Equal(0, container.Uploads);
    }

    [Fact]
    public async Task A_copy_the_account_did_not_finish_is_reported_rather_than_assumed()
    {
        var account = new PressFakeAccount { CopyStatus = CopyStatus.Failed };
        var service = account.Service("books");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CopyAsync("user/pack.pdf", "user/previous/pack.pdf", "application/pdf",
                CancellationToken.None));

        Assert.Contains("user/pack.pdf", error.Message, StringComparison.Ordinal);
        Assert.Contains("Failed", error.Message, StringComparison.Ordinal);
    }

    // ---- render validation ----------------------------------------------------------------

    [Fact]
    public void The_three_renderers_run_together_and_say_exactly_what_they_said_apart()
    {
        var pdf = BekiRenderFixtures.WithQrCodes("https://beki.ge");
        var request = new BekiRenderValidationRequest(
            QrPage: 1, ExpectedQrDestination: "https://beki.ge", ExpectedPages: 1);

        var sequential = BekiRenderValidation.Validate(
            pdf, "canonical-book", new BekiPrintPrepOptions { Parallelism = 1 }, request);
        var concurrent = BekiRenderValidation.Validate(
            pdf, "canonical-book", new BekiPrintPrepOptions { Parallelism = 3 }, request);

        // The evidence, not the clock. Two renderers and a font lister that share nothing must
        // produce the same verdict whether they are started one after another or at once — and if
        // they ever do not, that is a real disagreement and this assertion is the place it surfaces.
        Assert.Equal(sequential.Verdict, concurrent.Verdict);
        Assert.Equal(sequential.FailedGates, concurrent.FailedGates);
        Assert.Equal(sequential.Ghostscript.Status, concurrent.Ghostscript.Status);
        Assert.Equal(sequential.PopplerPdftoppm.Status, concurrent.PopplerPdftoppm.Status);
        Assert.Equal(sequential.PopplerPdffonts.Status, concurrent.PopplerPdffonts.Status);
        Assert.Equal(sequential.Fonts.Status, concurrent.Fonts.Status);
        Assert.Equal(sequential.Qr.Payloads, concurrent.Qr.Payloads);
        Assert.Equal(sequential.Pages.Count, concurrent.Pages.Count);

        // The contact sheet is what the human approval signs against a hash, so "the same evidence"
        // has to mean the same pixels rather than the same page count.
        Assert.Equal(sequential.ContactSheetSha256, concurrent.ContactSheetSha256);

        using var report = JsonDocument.Parse(concurrent.ReportJson);
        Assert.True(report.RootElement.GetProperty("concurrent_renderers").GetBoolean());

        using var sequentialReport = JsonDocument.Parse(sequential.ReportJson);
        Assert.False(sequentialReport.RootElement.GetProperty("concurrent_renderers").GetBoolean());
    }

    [Fact]
    public void The_render_report_says_what_each_renderer_cost()
    {
        var result = BekiRenderValidation.Validate(
            BekiRenderFixtures.Pages(2), "canonical-book", new BekiPrintPrepOptions());

        using var report = JsonDocument.Parse(result.ReportJson);
        var timings = report.RootElement.GetProperty("timings_ms");

        foreach (var step in new[]
        {
            "ghostscript", "pdftoppm", "pdffonts", "renderers", "read_pages", "qr_scan",
            "contact_sheet", "total",
        })
        {
            Assert.True(timings.TryGetProperty(step, out var reading), $"timings_ms.{step} is missing");
            Assert.True(reading.GetInt64() >= 0);
        }

        // The renderer block cannot cost less than its slowest member or more than the whole stage.
        var renderers = timings.GetProperty("renderers").GetInt64();
        Assert.True(renderers >= timings.GetProperty("ghostscript").GetInt64());
        Assert.True(renderers <= timings.GetProperty("total").GetInt64());
    }

    // ---- the stage's own record -------------------------------------------------------------

    [Fact]
    public async Task The_press_status_document_says_how_long_every_step_took()
    {
        var world = new CompositePipelineFulfillmentTests.PackWorld();
        world.Composer.CanonicalPdf = BekiCanonicalBookFixtures.CanonicalScreenBook();

        await world.Job().ProcessAsync(world.PackId, world.RunId, CancellationToken.None);

        using var status = JsonDocument.Parse(Encoding.UTF8.GetString(
            world.Blobs.Uploaded[BekiPackBlobs.PressStatusName(world.UserId, world.PackId)]));

        var timings = status.RootElement.GetProperty("timings_ms");

        // Every step the press stage owns. The render-back timings are in the render report, which
        // is written after this document and is that stage's own evidence.
        foreach (var step in new[]
        {
            "read_bases", "normalize", "composite", "compose_pdf", "preflight", "publish", "total",
        })
        {
            Assert.True(timings.TryGetProperty(step, out var reading), $"timings_ms.{step} is missing");
            Assert.True(reading.GetInt64() >= 0);
        }

        // The total is the stage, so nothing inside it can be longer.
        var total = timings.GetProperty("total").GetInt64();
        Assert.True(timings.GetProperty("compose_pdf").GetInt64() <= total);

        Assert.Equal(
            new BekiPrintPrepOptions().ResolvedParallelism,
            status.RootElement.GetProperty("parallelism").GetInt32());
    }

    [Fact]
    public async Task The_press_stage_stores_a_receipt_for_each_raster_and_not_the_rasters()
    {
        var world = new PressEvidenceWorld();

        await world.RunAsync();

        var prefix = $"{world.UserId}/{world.PackId}/print/";
        var written = world.Blobs.Uploaded.Keys
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(written);

        // 208 MB of PNGs on the measured book, uploaded on every press run and fetched back by
        // nobody. The receipt beside them stays.
        Assert.DoesNotContain(written, name => name.EndsWith(".png", StringComparison.Ordinal));
        Assert.Single(written, name => name.EndsWith("-composition.json", StringComparison.Ordinal));

        using var receipt = JsonDocument.Parse(
            world.Blobs.Uploaded[prefix + "spread-01-composition.json"]);
        var root = receipt.RootElement;

        Assert.Equal("beki-press-composite-receipt-v1", root.GetProperty("stage").GetString());
        Assert.Equal("spread-01", root.GetProperty("role").GetString());

        // The claim EXACT_BEKI makes is about hashes, and both are still here — with the byte
        // lengths that used to be implicit in a file somebody could stat.
        foreach (var image in new[] { "base_png", "composite_png" })
        {
            var stated = root.GetProperty(image);
            Assert.Equal(64, stated.GetProperty("sha256").GetString()!.Length);
            Assert.True(stated.GetProperty("byte_length").GetInt64() > 0);
            Assert.EndsWith(".png", stated.GetProperty("file").GetString()!, StringComparison.Ordinal);
        }

        // The partner manifest travels inside, verbatim and still closed-schema, so a printer can
        // diff it against the reference implementation's.
        var composition = root.GetProperty("composition");
        Assert.Equal("beki-exact-composite-v1", composition.GetProperty("composition_version").GetString());
        Assert.Equal(
            root.GetProperty("base_png").GetProperty("sha256").GetString(),
            composition.GetProperty("base_image").GetProperty("sha256").GetString());
        Assert.Equal(
            root.GetProperty("composite_png").GetProperty("sha256").GetString(),
            composition.GetProperty("output").GetProperty("sha256").GetString());

        // Storing the receipt is timed on its own, because storage latency is the thing that made
        // this stage slow and the next slow run has to be readable rather than guessed at.
        using var status = JsonDocument.Parse(Encoding.UTF8.GetString(
            world.Blobs.Uploaded[BekiPackBlobs.PressStatusName(world.UserId, world.PackId)]));
        Assert.True(status.RootElement.GetProperty("timings_ms")
            .TryGetProperty("upload_receipts", out _));
    }

    // ---- harness -----------------------------------------------------------------------------

    /// <summary>
    /// A book whose first spread really does reach the press composite, so the receipt this stage
    /// writes is a receipt for pixels rather than for a stub.
    ///
    /// One raster and not nine: the composite is a decode, a paste and an encode of a thirteen-
    /// megapixel canvas, and nine of them would put twenty seconds into a suite that runs in two
    /// and a half minutes. The other eight are refused by the preparer, which is a case the stage
    /// already handles by keeping the verified original and recording a preparation problem — so
    /// this world exercises both halves at once.
    /// </summary>
    private sealed class PressEvidenceWorld
    {
        private readonly CompositePipelineFulfillmentTests.PackWorld _world = new();

        public Guid PackId => _world.PackId;

        public Guid UserId => _world.UserId;

        public CompositePipelineFulfillmentTests.FakeBlobs Blobs => _world.Blobs;

        public async Task RunAsync()
        {
            _world.Composer.CanonicalPdf = BekiCanonicalBookFixtures.CanonicalScreenBook();

            var job = new BekiPackFulfillment(
                _world.Packs,
                new CompositePipelineFulfillmentTests.FakeRuns(_world.RunId, _world.UserId),
                _world.Blobs,
                _world.Generator,
                _world.Composer,
                _world.Notifier,
                _world.Email,
                new SingleUserRepository(),
                Options.Create(new BekiOptions { CompositePipelineEnabled = true, InsertBekiInGeneration = false }),
                NullLogger<BekiPackFulfillment>.Instance,
                _world.Clock,
                pressUpscaler: new OneRealRasterUpscaler(),
                alarms: _world.Alarms,
                orders: _world.Orders);
            await job.ProcessAsync(_world.PackId, _world.RunId, CancellationToken.None);
            _world.Composer.CanonicalPdf = BekiCanonicalBookFixtures.CanonicalPressBook();
            await job.RepreparePrintAsync(_world.PackId, CancellationToken.None);
        }
    }

    /// <summary>
    /// Prepares the first interior sheet for real and refuses the rest — see
    /// <see cref="PressEvidenceWorld"/> for why.
    /// </summary>
    private sealed class OneRealRasterUpscaler : IPressUpscaler
    {
        private int _prepared;

        public bool IsConfigured => true;

        public Task<PressUpscaleResult> UpscaleAsync(
            byte[] png, int targetWidth, int targetHeight, CancellationToken cancellationToken)
        {
            if (targetWidth != BekiPressRaster.InteriorWidthPx
                || Interlocked.Increment(ref _prepared) != 1)
            {
                return Task.FromResult(new PressUpscaleResult(
                    false, null, "none", 1d, 0, 0, 0, 0,
                    "this test prepares one raster; the rest keep their verified originals."));
            }

            return Task.FromResult(new PressUpscaleResult(
                true,
                SyntheticImages.SolidPng(targetWidth, targetHeight, (40, 90, 60)),
                BekiPressRaster.DeterministicTool,
                1d,
                targetWidth,
                targetHeight,
                targetWidth,
                targetHeight,
                null));
        }
    }

    /// <summary>A store with nothing but the required members — the shape every double here has.</summary>
    private sealed class MinimalStore : IBlobStorageService
    {
        public Dictionary<string, byte[]> Blobs { get; } = new(StringComparer.Ordinal);

        public List<string> Downloads { get; } = [];

        public Task<string> UploadAsync(
            string blobName, byte[] bytes, string contentType, CancellationToken cancellationToken)
        {
            Blobs[blobName] = bytes;
            return Task.FromResult($"https://blob.test/{blobName}");
        }

        public Task<Stream> DownloadAsync(string blobName, CancellationToken cancellationToken)
        {
            Downloads.Add(blobName);
            return Task.FromResult<Stream>(new MemoryStream(Blobs[blobName]));
        }

        public Task<bool> ExistsAsync(string blobName, CancellationToken cancellationToken) =>
            Task.FromResult(Blobs.ContainsKey(blobName));

        public Task<byte[]> DownloadBytesFromStoredUrlAsync(
            string storedUrl, CancellationToken cancellationToken) =>
            Task.FromResult(Blobs[storedUrl]);

        public Task<byte[]?> TryDownloadBesideAsync(
            string storedUrl, string suffix, CancellationToken cancellationToken) =>
            Task.FromResult<byte[]?>(null);

        public Task UploadBesideAsync(
            string storedUrl, string suffix, byte[] bytes, string contentType,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> DeleteByStoredUrlAsync(string storedUrl, CancellationToken cancellationToken) =>
            Task.FromResult(Blobs.Remove(storedUrl));
    }

    /// <summary>The account seam, counting what the copy did and did not ask for.</summary>
    private sealed class PressFakeAccount
    {
        public Dictionary<string, PressFakeContainer> Containers { get; } = [];

        public CopyStatus CopyStatus { get; init; } = CopyStatus.Success;

        public AzureBlobStorageService Service(string containerName) =>
            new(Options.Create(new AzureBlobOptions { ContainerName = containerName }), name =>
            {
                var container = new PressFakeContainer(name, CopyStatus);
                Containers[name] = container;
                return container;
            });
    }

    private sealed class PressFakeContainer(string name, CopyStatus status) : BlobContainerClient
    {
        public List<(string Destination, string Source)> Copies { get; } = [];

        public int Downloads { get; set; }

        public int Uploads { get; set; }

        public override Task<Response<BlobContainerInfo>> CreateIfNotExistsAsync(
            PublicAccessType publicAccessType = PublicAccessType.None,
            IDictionary<string, string>? metadata = null,
            BlobContainerEncryptionScopeOptions? encryptionScopeOptions = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Response<BlobContainerInfo>>(new PressValueOnly<BlobContainerInfo>(null!));

        public override BlobClient GetBlobClient(string blobName) =>
            new PressFakeBlob(this, name, blobName, status);
    }

    private sealed class PressFakeBlob(
        PressFakeContainer container, string containerName, string blobName, CopyStatus status)
        : BlobClient
    {
        public override Uri Uri { get; } =
            new($"https://account.blob.core.windows.net/{containerName}/{blobName}");

        public override Task<CopyFromUriOperation> StartCopyFromUriAsync(
            Uri source, BlobCopyFromUriOptions options, CancellationToken cancellationToken = default)
        {
            container.Copies.Add((blobName, source.ToString()));
            return Task.FromResult<CopyFromUriOperation>(new PressFakeCopy(status));
        }

        public override Task<Response<BlobDownloadStreamingResult>> DownloadStreamingAsync(
            HttpRange range = default, BlobRequestConditions? conditions = null,
            bool rangeGetContentHash = false, CancellationToken cancellationToken = default)
        {
            container.Downloads++;
            throw new NotSupportedException("the copy must not download.");
        }

        public override Task<Response<BlobContentInfo>> UploadAsync(
            Stream content, BlobUploadOptions options, CancellationToken cancellationToken = default)
        {
            container.Uploads++;
            throw new NotSupportedException("the copy must not upload.");
        }
    }

    /// <summary>
    /// A copy that has already finished, which is what an intra-account copy normally is — or one
    /// the service ended as failed, which the SDK surfaces by throwing out of the wait.
    /// </summary>
    private sealed class PressFakeCopy(CopyStatus status) : CopyFromUriOperation
    {
        public override string Id => "press-test-copy";

        public override long Value => 0;

        public override bool HasValue => true;

        public override bool HasCompleted => true;

        public override Response GetRawResponse() => new PressEmptyResponse();

        public override Response UpdateStatus(CancellationToken cancellationToken = default) =>
            GetRawResponse();

        public override ValueTask<Response> UpdateStatusAsync(CancellationToken cancellationToken = default) =>
            new(GetRawResponse());

        public override ValueTask<Response<long>> WaitForCompletionAsync(
            CancellationToken cancellationToken = default) =>
            status == CopyStatus.Success
                ? new(Response.FromValue(0L, GetRawResponse()))
                : throw new RequestFailedException($"the copy ended as {status}");
    }

    private sealed class PressEmptyResponse : Response
    {
        public override int Status => 202;

        public override string ReasonPhrase => "Accepted";

        public override Stream? ContentStream { get; set; }

        public override string ClientRequestId { get; set; } = string.Empty;

        public override void Dispose()
        {
        }

        protected override bool ContainsHeader(string name) => false;

        protected override IEnumerable<HttpHeader> EnumerateHeaders() => [];

        protected override bool TryGetHeader(
            string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value)
        {
            value = null;
            return false;
        }

        protected override bool TryGetHeaderValues(
            string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IEnumerable<string>? values)
        {
            values = null;
            return false;
        }
    }

    private sealed class PressValueOnly<T>(T value) : Response<T>
    {
        public override T Value => value;

        public override Response GetRawResponse() => new PressEmptyResponse();
    }

    private sealed class PressTestHostEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";

        public string ApplicationName { get; set; } = "Adventrya.Story.Tests";

        public string ContentRootPath { get; set; } = contentRoot;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
