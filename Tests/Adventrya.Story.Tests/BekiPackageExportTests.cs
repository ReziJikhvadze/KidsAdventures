using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The handback zip: what an operator downloads from the admin console and forwards to the
/// supplier. Its contract has three parts — everything stored is included under a stable path,
/// everything absent is listed as missing rather than silently dropped, and the two artifacts
/// that describe a real child never travel.
/// </summary>
public class BekiPackageExportTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid PackId = Guid.NewGuid();

    [Fact]
    public async Task The_package_carries_what_exists_and_names_what_does_not()
    {
        var blobs = new FakeBlobs();
        blobs.Seed($"{UserId}/{PackId}-interior.pdf", [1, 2, 3]);
        blobs.Seed($"{UserId}/{PackId}-interior-preflight.json", "{}"u8.ToArray());
        blobs.Seed($"{UserId}/{PackId}.pdf", [4, 5, 6]);
        blobs.Seed(BekiPackBlobs.ScenarioName(UserId, PackId), "{}"u8.ToArray());
        blobs.Seed(BekiPackBlobs.SpreadName(UserId, PackId, 1), [7]);
        blobs.Seed(BekiPackBlobs.SpreadBaseName(UserId, PackId, 1), [8]);
        blobs.Seed(BekiPackBlobs.CompositionManifestName(UserId, PackId, 1), "{}"u8.ToArray());

        // Stored, and deliberately not packaged: the child's own artifacts.
        blobs.Seed(BekiPackBlobs.IdentitySpecName(UserId, PackId), "{secret}"u8.ToArray());

        var zip = await new BekiPackageExport(blobs).BuildAsync(
            UserId, PackId, "სინათლის პატარა ქალაქი", CancellationToken.None);

        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        var paths = archive.Entries.Select(entry => entry.FullName).ToList();

        Assert.Contains("press/interior.pdf", paths);
        Assert.Contains("press/interior-preflight.json", paths);
        Assert.Contains("reading-copy.pdf", paths);
        Assert.Contains("plan/visual-scenario.json", paths);
        Assert.Contains("spreads/spread-01.png", paths);
        Assert.Contains("bases/spread-01-base.png", paths);
        Assert.Contains("receipts/spread-01-composition.json", paths);
        Assert.Contains("PACKAGE_CONTENTS.json", paths);

        // Nothing that describes the child, under any name.
        Assert.DoesNotContain(paths, path => path.Contains("identity", StringComparison.OrdinalIgnoreCase));
        foreach (var entry in archive.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            Assert.DoesNotContain("{secret}", await reader.ReadToEndAsync());
        }

        // The listing tells the recipient what was withheld or never produced — the press cover
        // here — instead of leaving an absence to be misread as an oversight.
        var contentsEntry = archive.GetEntry("PACKAGE_CONTENTS.json")!;
        using var contents = await JsonDocument.ParseAsync(contentsEntry.Open());

        var missing = contents.RootElement.GetProperty("missing")
            .EnumerateArray().Select(element => element.GetString()).ToList();
        Assert.Contains("press/cover.pdf", missing);
        Assert.Contains("press/cover-preflight.json", missing);

        var included = contents.RootElement.GetProperty("included")
            .EnumerateArray().Select(element => element.GetString()).ToList();
        Assert.Contains("press/interior.pdf", included);

        Assert.Contains(
            contents.RootElement.GetProperty("excluded_by_design").EnumerateArray(),
            element => element.GetString()!.Contains("child-identity"));
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
