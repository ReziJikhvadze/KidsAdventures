using System.IO.Compression;
using System.Text.Json;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Story;

/// <summary>
/// One book's handback package, as a single zip — the artifacts the handoff's "first-run
/// handback" and the supplier audit's §6 ask to be returned together, pulled from the pack's own
/// blob prefix where fulfilment already stored them.
///
/// Assembled on demand rather than at fulfilment time, because the package is an operator's
/// download and not a book artifact: what belongs in it has changed with every audit round, and
/// a zip written at generation time would fossilise whichever round was current.
///
/// What is deliberately NOT in it: the child's identity spec and the child's photograph. The
/// package's whole purpose is to travel — to the supplier, into review threads — and those two
/// describe a real child. The handback lists ask for prompts, manifests, hashes and PDFs, none
/// of which needs the child's face or feature list; a reviewer who needs the pictures has the
/// pictures, where the child appears as the book shows them.
/// </summary>
public sealed class BekiPackageExport(IBlobStorageService blobStorage)
{
    /// <summary>Builds the zip, including whatever exists and listing whatever does not.</summary>
    public async Task<byte[]> BuildAsync(
        Guid userId, Guid packId, string? title, CancellationToken cancellationToken)
    {
        var entries = new List<(string BlobName, string ZipPath)>
        {
            ($"{userId}/{packId}-interior.pdf", "press/interior.pdf"),
            ($"{userId}/{packId}-interior-preflight.json", "press/interior-preflight.json"),
            ($"{userId}/{packId}-cover.pdf", "press/cover.pdf"),
            ($"{userId}/{packId}-cover-preflight.json", "press/cover-preflight.json"),
            ($"{userId}/{packId}.pdf", "reading-copy.pdf"),
            (BekiPackBlobs.ScenarioName(userId, packId), "plan/visual-scenario.json"),
            (BekiPackBlobs.CompositeReviewName(userId, packId), "plan/composite-review.json"),
            (BekiPackBlobs.ManifestName(userId, packId), "plan/fulfilment-manifest.json"),
            (BekiPackBlobs.CoverName(userId, packId), "cover/cover.png"),
            ($"{userId}/{packId}-cover-wrap-base.png", "cover/cover-wrap-base.png"),
            ($"{userId}/{packId}-cover-composition.json", "cover/cover-composition.json"),
        };

        for (var spread = 1; spread <= BookFormat.SpreadCount; spread++)
        {
            entries.Add((BekiPackBlobs.SpreadName(userId, packId, spread),
                $"spreads/spread-{spread:00}.png"));
            entries.Add((BekiPackBlobs.SpreadBaseName(userId, packId, spread),
                $"bases/spread-{spread:00}-base.png"));
            entries.Add((BekiPackBlobs.CompositionManifestName(userId, packId, spread),
                $"receipts/spread-{spread:00}-composition.json"));
            entries.Add((BekiPackBlobs.SpreadQaName(userId, packId, spread),
                $"qa/spread-{spread:00}-qa.json"));
            entries.Add((BekiPackBlobs.FailedSpreadName(userId, packId, spread),
                $"qa/spread-{spread:00}-failed.png"));
        }

        var included = new List<string>();
        var missing = new List<string>();

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (blobName, zipPath) in entries)
            {
                if (!await blobStorage.ExistsAsync(blobName, cancellationToken))
                {
                    missing.Add(zipPath);
                    continue;
                }

                var entry = archive.CreateEntry(zipPath, CompressionLevel.Fastest);
                await using var target = entry.Open();
                await using var source = await blobStorage.DownloadAsync(blobName, cancellationToken);
                await source.CopyToAsync(target, cancellationToken);

                included.Add(zipPath);
            }

            // The listing itself, so the recipient never has to guess whether an absent file was
            // withheld, never produced, or lost. Missing press files are the loudest example:
            // print prep refuses rather than degrades, and this is where the refusal shows up to
            // whoever opens the zip.
            var contents = archive.CreateEntry("PACKAGE_CONTENTS.json", CompressionLevel.Fastest);
            await using var writer = contents.Open();
            await JsonSerializer.SerializeAsync(
                writer,
                new
                {
                    pack_id = packId,
                    title,
                    assembled_at_utc = DateTime.UtcNow,
                    included = included.OrderBy(path => path, StringComparer.Ordinal).ToList(),
                    missing = missing.OrderBy(path => path, StringComparer.Ordinal).ToList(),
                    excluded_by_design = new[]
                    {
                        "child-identity.json — describes a real child; the handback needs no feature list",
                        "the child's photograph — same reason",
                        "the normalized Story JSON — lives on the master-story run record, not the pack's blobs",
                    },
                },
                new JsonSerializerOptions { WriteIndented = true },
                cancellationToken);
        }

        return buffer.ToArray();
    }
}
