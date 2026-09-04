using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story;
using Microsoft.Extensions.Options;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The handback zip: what an operator downloads from the admin console and forwards to the supplier.
///
/// Its contract after audit-2 has five parts. Everything stored is included under a stable path;
/// everything absent is listed as missing rather than silently dropped; every entry carries its own
/// SHA-256, because a package with no checksums cannot be shown to be the package that was reviewed
/// (P1-10). The three deliverables use the audit's own file names, and — amendment A5 — a file whose
/// gates did not pass travels under <c>diagnostic/</c> instead of at the root, so nobody hands a
/// refused press PDF to a printer. And the two artifacts that describe a real child never travel at
/// all, while the normalized story now does (audit §9).
/// </summary>
public class BekiPackageExportTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid PackId = Guid.NewGuid();

    private static BekiPackageExport Export(IBlobStorageService blobs) =>
        new(blobs, Options.Create(new BekiOptions()));

    [Fact]
    public async Task The_package_carries_what_exists_and_names_what_does_not()
    {
        var blobs = new FakeBlobs();
        blobs.Seed(BekiPackBlobs.CanonicalPreflightName(UserId, PackId), "{}"u8.ToArray());
        blobs.Seed(BekiPackBlobs.ReadingPdfName(UserId, PackId), [4, 5, 6]);
        blobs.Seed(BekiPackBlobs.StoryName(UserId, PackId), """{"title":"x"}"""u8.ToArray());
        blobs.Seed(BekiPackBlobs.ScenarioName(UserId, PackId), "{}"u8.ToArray());
        blobs.Seed(BekiPackBlobs.AssetLockName(UserId, PackId), "{}"u8.ToArray());
        blobs.Seed(BekiPackBlobs.SpreadName(UserId, PackId, 1), [7]);
        blobs.Seed(BekiPackBlobs.SpreadBaseName(UserId, PackId, 1), [8]);
        blobs.Seed(BekiPackBlobs.CompositionManifestName(UserId, PackId, 1), "{}"u8.ToArray());
        blobs.Seed(BekiPackBlobs.LayoutReceiptName(UserId, PackId, "canonical"), "{}"u8.ToArray());
        blobs.Seed(BekiPackBlobs.FixedPageQaName(UserId, PackId, "credits"), "{}"u8.ToArray());

        // Stored, and deliberately not packaged: the child's own artifacts.
        blobs.Seed(BekiPackBlobs.IdentitySpecName(UserId, PackId), "{secret}"u8.ToArray());

        var zip = await Export(blobs).BuildAsync(
            UserId, PackId, "სინათლის პატარა ქალაქი", CancellationToken.None);

        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        var paths = archive.Entries.Select(entry => entry.FullName).ToList();

        Assert.Contains("press/canonical-preflight.json", paths);
        Assert.Contains("plan/story.json", paths);
        Assert.Contains("plan/visual-scenario.json", paths);
        Assert.Contains("lock/asset-lock-manifest.json", paths);
        Assert.Contains("spreads/spread-01.png", paths);
        Assert.Contains("bases/spread-01-base.png", paths);
        Assert.Contains("receipts/spread-01-composition.json", paths);
        Assert.Contains("receipts/canonical-layout.json", paths);
        Assert.Contains("qa/fixed-credits-qa.json", paths);
        Assert.Contains("PACKAGE_CONTENTS.json", paths);
        Assert.Contains("RELEASE_STATUS.json", paths);
        Assert.Contains("provenance.json", paths);
        Assert.Contains("assets/fonts/font-hashes.json", paths);

        // Nothing that describes the child, under any name.
        Assert.DoesNotContain(paths, path => path.Contains("identity", StringComparison.OrdinalIgnoreCase));
        foreach (var entry in archive.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            Assert.DoesNotContain("{secret}", await reader.ReadToEndAsync());
        }

        var contents = await ContentsOf(archive);

        // The listing tells the recipient what was never produced instead of leaving an absence
        // to be misread as an oversight.
        var missing = contents.RootElement.GetProperty("missing")
            .EnumerateArray().Select(element => element.GetString()).ToList();
        Assert.Contains("press/press-status.json", missing);
        Assert.Contains("cover/cover-wrap-composite.png", missing);

        Assert.Equal("beki-package-contents-v2", contents.RootElement.GetProperty("schema").GetString());

        // The normalized story is no longer excluded by design — audit §9 asked for it back.
        var excluded = contents.RootElement.GetProperty("excluded_by_design")
            .EnumerateArray().Select(element => element.GetString()!).ToList();
        Assert.Contains(excluded, note => note.Contains("child-identity"));
        Assert.Contains(excluded, note => note.Contains("photograph"));
        Assert.DoesNotContain(excluded, note => note.Contains("Story JSON", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Every entry carries the hash of the bytes that are actually in the zip — P1-10's complaint,
    /// which was that the shipped package had no checksums at all and so could not be shown to be
    /// the package anybody had reviewed.
    /// </summary>
    [Fact]
    public async Task Every_entry_is_listed_with_its_own_checksum_and_status()
    {
        var blobs = new FakeBlobs();
        blobs.Seed(BekiPackBlobs.SpreadName(UserId, PackId, 1), [9, 9, 9, 9]);

        var zip = await Export(blobs).BuildAsync(UserId, PackId, "t", CancellationToken.None);

        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        var contents = await ContentsOf(archive);

        var entry = contents.RootElement.GetProperty("entries")
            .EnumerateArray()
            .Single(item => item.GetProperty("path").GetString() == "spreads/spread-01.png");

        Assert.Equal(4, entry.GetProperty("bytes").GetInt32());
        Assert.Equal("image/png", entry.GetProperty("mime").GetString());
        Assert.Equal("canonical", entry.GetProperty("status").GetString());
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(new byte[] { 9, 9, 9, 9 })).ToLowerInvariant(),
            entry.GetProperty("sha256").GetString());
    }

    /// <summary>
    /// A book whose gates passed gets the audit's own file names at the root. A book whose gates did
    /// not gets the same bytes under <c>diagnostic/</c> — amendment A5's rule, and the difference
    /// between "here is the deliverable" and "here is what we have".
    /// </summary>
    [Fact]
    public async Task Released_files_take_the_audit_names_and_refused_files_go_to_diagnostic()
    {
        var released = new FakeBlobs();
        SeedThreeDeliverables(released);
        released.Seed(
            BekiPackBlobs.ReleaseGatesName(UserId, PackId), Gates(BekiReleaseGates.Pass));

        using var releasedZip = new ZipArchive(
            new MemoryStream(await Export(released).BuildAsync(UserId, PackId, "t", CancellationToken.None)),
            ZipArchiveMode.Read);

        var releasedPaths = releasedZip.Entries.Select(entry => entry.FullName).ToList();
        Assert.Contains(BekiPackageExport.CanonicalBookFileName(PackId), releasedPaths);
        Assert.DoesNotContain(BekiPackageExport.PressCoverFileName(PackId), releasedPaths);
        Assert.DoesNotContain(BekiPackageExport.PressInteriorFileName(PackId), releasedPaths);
        Assert.DoesNotContain(BekiPackageExport.DigitalReadingFileName(PackId), releasedPaths);

        var refused = new FakeBlobs();
        SeedThreeDeliverables(refused);
        refused.Seed(BekiPackBlobs.ReleaseGatesName(UserId, PackId), Gates(BekiReleaseGates.Fail));

        using var refusedZip = new ZipArchive(
            new MemoryStream(await Export(refused).BuildAsync(UserId, PackId, "t", CancellationToken.None)),
            ZipArchiveMode.Read);

        var refusedPaths = refusedZip.Entries.Select(entry => entry.FullName).ToList();
        Assert.Contains($"diagnostic/{BekiPackageExport.CanonicalBookFileName(PackId)}", refusedPaths);
        Assert.DoesNotContain(BekiPackageExport.CanonicalBookFileName(PackId), refusedPaths);

        var contents = await ContentsOf(refusedZip);
        var entry = contents.RootElement.GetProperty("entries")
            .EnumerateArray()
            .Single(item => item.GetProperty("path").GetString()
                            == $"diagnostic/{BekiPackageExport.CanonicalBookFileName(PackId)}");

        Assert.Equal("diagnostic", entry.GetProperty("status").GetString());
    }

    /// <summary>
    /// Amendment B1's required test, and the reason the truth split exists at all.
    ///
    /// A book whose gates failed is published to the family who bought it — that is the owner's
    /// ruling and the whole point of the release policy. What must NOT follow is that the same file
    /// is handed to a printer as a deliverable. The package classifies by the RAW family
    /// (<c>SupplierPressReleasable</c>, <c>SupplierCustomerPdfReleasable</c>), so the file the parent
    /// is reading right now still travels under <c>diagnostic/</c> and the handback still says
    /// NOT_RELEASABLE. One file, two audiences, two true statements.
    /// </summary>
    [Fact]
    public async Task A_file_the_policy_published_to_the_parent_is_still_a_diagnostic_to_the_supplier()
    {
        var blobs = new FakeBlobs();
        SeedThreeDeliverables(blobs);
        blobs.Seed(BekiPackBlobs.ReleaseGatesName(UserId, PackId), WaivedGates());

        using var archive = new ZipArchive(
            new MemoryStream(await Export(blobs).BuildAsync(UserId, PackId, "t", CancellationToken.None)),
            ZipArchiveMode.Read);

        var paths = archive.Entries.Select(entry => entry.FullName).ToList();

        // The parent has this file. The supplier is not being handed it as a deliverable.
        Assert.Contains($"diagnostic/{BekiPackageExport.CanonicalBookFileName(PackId)}", paths);
        Assert.DoesNotContain(BekiPackageExport.CanonicalBookFileName(PackId), paths);

        var contents = await ContentsOf(archive);
        var entry = contents.RootElement.GetProperty("entries")
            .EnumerateArray()
            .Single(item => item.GetProperty("path").GetString()
                            == $"diagnostic/{BekiPackageExport.CanonicalBookFileName(PackId)}");

        Assert.Equal("diagnostic", entry.GetProperty("status").GetString());

        await using var status = archive.GetEntry("RELEASE_STATUS.json")!.Open();
        using var release = await JsonDocument.ParseAsync(status);
        var root = release.RootElement;

        Assert.Equal(BekiReleaseGates.NotReleasable, root.GetProperty("verdict").GetString());

        // And the document says the divergence out loud rather than leaving a reader to wonder how a
        // NOT_RELEASABLE book ended up in somebody's library.
        Assert.False(root.GetProperty("supplier_release").GetProperty("customer_pdf").GetBoolean());
        Assert.True(root.GetProperty("parent_publication").GetProperty("customer_pdf").GetBoolean());
        Assert.Equal(
            "DIGITAL_GEOMETRY",
            root.GetProperty("parent_publication").GetProperty("waivers")
                .EnumerateArray().First().GetProperty("check_id").GetString());
    }

    /// <summary>
    /// The waiver evidence travels — review finding 6, and the reason the console's evidence button
    /// is a button at all.
    ///
    /// Every policy waiver raises an alarm whose <c>EvidenceBlob</c> names one of these files, and
    /// the only way the console offers to look at one is a download of THIS zip for the alarm's
    /// order. The package carried none of them. So the single most common alarm in the system —
    /// "we shipped a page the reviewer refused" — had an evidence button that produced a package
    /// with no such file in it, while the file sat in storage the whole time.
    ///
    /// Diagnostic, and only ever diagnostic: these are the record of a decision, not part of the
    /// book, and the canonical classification stays computed from the RAW supplier family
    /// (amendment B1) exactly as it was.
    /// </summary>
    [Fact]
    public async Task The_waiver_evidence_a_policy_shipped_past_travels_under_diagnostic()
    {
        var blobs = new FakeBlobs();

        // Two checks on one spread — the pair that used to overwrite each other — and the cover.
        blobs.Seed(
            BekiPackBlobs.PolicyWaiverName(UserId, PackId, BekiReleaseChecks.CentreFold, 3),
            """{"check":"centre_fold"}"""u8.ToArray());
        blobs.Seed(
            BekiPackBlobs.WaivedEvidenceName(UserId, PackId, BekiReleaseChecks.CentreFold, 3),
            [1, 1]);
        blobs.Seed(
            BekiPackBlobs.PolicyWaiverName(UserId, PackId, BekiReleaseChecks.ImageQa, 3),
            """{"check":"image_qa"}"""u8.ToArray());
        blobs.Seed(
            BekiPackBlobs.WaivedEvidenceName(UserId, PackId, BekiReleaseChecks.ImageQa, 3), [2, 2]);
        blobs.Seed(
            BekiPackBlobs.PolicyWaiverName(UserId, PackId, BekiReleaseChecks.CoverBands, 0),
            """{"check":"cover_bands"}"""u8.ToArray());

        using var archive = new ZipArchive(
            new MemoryStream(await Export(blobs).BuildAsync(UserId, PackId, "t", CancellationToken.None)),
            ZipArchiveMode.Read);

        var paths = archive.Entries.Select(entry => entry.FullName).ToList();

        Assert.Contains("diagnostic/waivers/spread-03-centre_fold.json", paths);
        Assert.Contains("diagnostic/waivers/spread-03-centre_fold.png", paths);
        Assert.Contains("diagnostic/waivers/spread-03-image_qa.json", paths);
        Assert.Contains("diagnostic/waivers/spread-03-image_qa.png", paths);
        Assert.Contains("diagnostic/waivers/cover-cover_bands.json", paths);

        // Each picture is its own picture, which is the other half of the same fault.
        await using var fold = archive.GetEntry("diagnostic/waivers/spread-03-centre_fold.png")!.Open();
        using var foldBytes = new MemoryStream();
        await fold.CopyToAsync(foldBytes);
        Assert.Equal(new byte[] { 1, 1 }, foldBytes.ToArray());

        var contents = await ContentsOf(archive);

        var entry = contents.RootElement.GetProperty("entries")
            .EnumerateArray()
            .Single(item => item.GetProperty("path").GetString()
                            == "diagnostic/waivers/spread-03-image_qa.json");

        Assert.Equal("diagnostic", entry.GetProperty("status").GetString());

        /*
          And a healthy book's listing is not buried under them.

          There are fifty-four possible waiver names per book and a good book has none. Reporting
          each absence as "missing" would put a hundred and eight lines into the one document whose
          whole job is to tell a recipient which of the files that SHOULD be here are not.
        */
        var missing = contents.RootElement.GetProperty("missing")
            .EnumerateArray().Select(element => element.GetString()!).ToList();

        Assert.DoesNotContain(missing, path => path.StartsWith("diagnostic/waivers/"));
    }

    /// <summary>
    /// The AI redraw is still worth having and is no longer anything's cover, so it travels where it
    /// cannot be mistaken for the master (audit P0-01).
    /// </summary>
    [Fact]
    public async Task The_old_cover_redraw_travels_as_a_diagnostic_only()
    {
        var blobs = new FakeBlobs();
        blobs.Seed(BekiPackBlobs.CoverName(UserId, PackId), [1]);
        blobs.Seed(BekiPackBlobs.CoverWrapCompositeName(UserId, PackId), [2]);

        using var archive = new ZipArchive(
            new MemoryStream(await Export(blobs).BuildAsync(UserId, PackId, "t", CancellationToken.None)),
            ZipArchiveMode.Read);

        var paths = archive.Entries.Select(entry => entry.FullName).ToList();

        Assert.Contains("diagnostic/cover-redraw.png", paths);
        Assert.Contains("cover/cover-wrap-composite.png", paths);
        Assert.DoesNotContain("cover/cover.png", paths);
    }

    /// <summary>
    /// The root verdict, so that nobody has to derive the rejection themselves the way the supplier
    /// did. A book with no stored evaluation says so and releases nothing.
    /// </summary>
    [Fact]
    public async Task The_release_status_states_the_verdict_or_admits_there_is_none()
    {
        var blobs = new FakeBlobs();

        using var archive = new ZipArchive(
            new MemoryStream(await Export(blobs).BuildAsync(UserId, PackId, "t", CancellationToken.None)),
            ZipArchiveMode.Read);

        await using var status = archive.GetEntry("RELEASE_STATUS.json")!.Open();
        using var document = await JsonDocument.ParseAsync(status);

        Assert.Equal("UNKNOWN", document.RootElement.GetProperty("verdict").GetString());
        Assert.True(document.RootElement.GetProperty("awaiting_human_review").GetBoolean());
    }

    /// <summary>
    /// Provenance answers "which tree built this book". The csproj stamps the commit onto the
    /// assembly's informational version; a build without one says "unknown" rather than inventing.
    /// </summary>
    [Fact]
    public async Task Provenance_records_the_build_and_the_contract_versions()
    {
        var blobs = new FakeBlobs();

        using var archive = new ZipArchive(
            new MemoryStream(await Export(blobs).BuildAsync(UserId, PackId, "t", CancellationToken.None)),
            ZipArchiveMode.Read);

        await using var stream = archive.GetEntry("provenance.json")!.Open();
        using var document = await JsonDocument.ParseAsync(stream);

        var build = document.RootElement.GetProperty("build");
        Assert.False(string.IsNullOrWhiteSpace(build.GetProperty("commit").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(build.GetProperty("runtime").GetString()));

        var versions = document.RootElement.GetProperty("contract_versions");
        Assert.Equal("PDF/X-4", versions.GetProperty("pdfx").GetString());
        Assert.Equal(BekiReleaseGates.Schema, versions.GetProperty("release_gates").GetString());
        Assert.Equal(BekiFixedPageQa.Version, versions.GetProperty("fixed_page_qa").GetString());
    }

    // ==============================================================================================
    // Harness
    // ==============================================================================================

    private static void SeedThreeDeliverables(FakeBlobs blobs)
    {
        blobs.Seed(BekiPackBlobs.ReadingPdfName(UserId, PackId), [3]);
    }

    /// <summary>A stored verdict where every gate has the same answer — enough for the file rules.</summary>
    private static byte[] Gates(string status)
    {
        var ids = BekiReleaseGates.ReadGateIds(AppContext.BaseDirectory);

        var report = new BekiReleaseGateReport
        {
            Verdict = status == BekiReleaseGates.Pass
                ? BekiReleaseGates.Releasable
                : BekiReleaseGates.NotReleasable,
            EvaluatedAtUtc = DateTimeOffset.UtcNow,
            AwaitingHumanReview = false,
            FailingGates = status == BekiReleaseGates.Pass ? [] : ids,
            Gates = ids
                .Select(id => new BekiGateResult(id, status, GateClassOf(id), "test", []))
                .ToList(),
        };

        return System.Text.Encoding.UTF8.GetBytes(report.ToJson());
    }

    /// <summary>
    /// A stored verdict where the reading copy's own gate failed and the policy waived it: the exact
    /// shape a book has when the family is reading it and the supplier must not be handed it.
    /// </summary>
    private static byte[] WaivedGates()
    {
        var ids = BekiReleaseGates.ReadGateIds(AppContext.BaseDirectory);

        var report = new BekiReleaseGateReport
        {
            Verdict = BekiReleaseGates.NotReleasable,
            EvaluatedAtUtc = DateTimeOffset.UtcNow,
            AwaitingHumanReview = false,
            FailingGates = ["DIGITAL_GEOMETRY"],
            Gates = ids
                .Select(id => id == "DIGITAL_GEOMETRY"
                    ? new BekiGateResult(
                        id, BekiReleaseGates.Fail, BekiReleaseGates.DigitalClass,
                        "the stored digital preflight report records a refusal", [])
                    {
                        Disposition = BekiReleaseGates.WaivedByPolicy,
                    }
                    : new BekiGateResult(id, BekiReleaseGates.Pass, GateClassOf(id), "test", []))
                .ToList(),
            PolicyWaivers =
            [
                new BekiGateWaiver(
                    "DIGITAL_GEOMETRY", BekiReleaseGates.DigitalClass, BekiReleaseGates.Fail,
                    "the stored digital preflight report records a refusal"),
            ],
        };

        return System.Text.Encoding.UTF8.GetBytes(report.ToJson());
    }

    private static string GateClassOf(string id) => id switch
    {
        "DIGITAL_GEOMETRY" => BekiReleaseGates.DigitalClass,
        "HANDBACK_COMPLETENESS" => BekiReleaseGates.PackageClass,
        "PRESS_GEOMETRY" or "PRESS_COLOR" or "PRESS_RESOLUTION" or "TEXT_COLOR_INTEGRITY"
            or "RENDER_VALIDATION" or "QR" => BekiReleaseGates.PressClass,
        _ => BekiReleaseGates.SharedClass,
    };

    private static async Task<JsonDocument> ContentsOf(ZipArchive archive)
    {
        await using var stream = archive.GetEntry("PACKAGE_CONTENTS.json")!.Open();
        return await JsonDocument.ParseAsync(stream);
    }

    /// <summary>A blob store that remembers what it was given and hands it back by name.</summary>
    private sealed class FakeBlobs : IBlobStorageService
    {
        private readonly Dictionary<string, byte[]> _blobs = new(StringComparer.Ordinal);

        public void Seed(string blobName, byte[] bytes) => _blobs[blobName] = bytes;

        public Task<string> UploadAsync(
            string blobName, byte[] bytes, string contentType, CancellationToken cancellationToken)
        {
            _blobs[blobName] = bytes;
            return Task.FromResult($"https://blob.test/{blobName}");
        }

        public Task<Stream> DownloadAsync(string blobName, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream(
                _blobs.TryGetValue(blobName, out var bytes) ? bytes : []));

        public Task<bool> ExistsAsync(string blobName, CancellationToken cancellationToken) =>
            Task.FromResult(_blobs.ContainsKey(blobName));

        public Task<byte[]> DownloadBytesFromStoredUrlAsync(
            string storedUrl, CancellationToken cancellationToken) =>
            Task.FromResult(_blobs.TryGetValue(
                storedUrl.Replace("https://blob.test/", string.Empty), out var bytes) ? bytes : []);

        public Task<bool> DeleteAsync(string blobName, CancellationToken cancellationToken) =>
            Task.FromResult(_blobs.Remove(blobName));

        public Task<bool> DeleteByStoredUrlAsync(string storedUrl, CancellationToken cancellationToken) =>
            Task.FromResult(_blobs.Remove(storedUrl.Replace("https://blob.test/", string.Empty)));
    }
}
