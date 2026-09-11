using System.Collections.Concurrent;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AdventurePacks.Api.Services.Story.Composite.Poses;

/// <summary>
/// Provenance for a reference-guided generated spread. This is not an exact PNG composite:
/// the layer identifies the input reference; zero layer geometry means no separate layer exists.
/// </summary>
public static class BekiGeneratedArtwork
{
    public const string Version = "beki-reference-generated-v4";
    private const string LegacyVersion = "beki-reference-generated-v1";
    public const string PoseId = "canonical-reference-generated";

    // Crop coordinates belong to this exact approved 1024x1536 master. Never apply them
    // to a custom reference with different framing, even if its dimensions happen to match.
    private const string MasterSha256 = "535e4d9ca0e17a68275028cd21d415701f3b02b59edcab12e54806855f9f21ed";
    private static readonly ConcurrentDictionary<string, byte[]> HandDetailsCache = new();

    public static byte[] Reference(string? path = null) => File.ReadAllBytes(
        Path.IsPathRooted(path ?? BekiIdentity.ReferenceAssetPath)
            ? path! : Path.Combine(AppContext.BaseDirectory, path ?? BekiIdentity.ReferenceAssetPath));

    /// <summary>Magnifies the approved hands without asking an image model to redraw them.</summary>
    public static byte[]? HandDetails(byte[] master)
    {
        var hash = BekiCompositeEngine.Sha256Hex(master);
        if (hash != MasterSha256) return null;

        return HandDetailsCache.GetOrAdd(hash, _ =>
        {
            using var source = Image.Load<Rgba32>(master);
            using var left = source.Clone(x => x.Crop(new Rectangle(210, 810, 180, 180)).Resize(384, 384));
            using var right = source.Clone(x => x.Crop(new Rectangle(600, 790, 180, 180)).Resize(384, 384));
            using var details = new Image<Rgba32>(792, 384, new Rgba32(25, 14, 39));
            details.Mutate(x => x.DrawImage(left, new Point(0, 0), 1f)
                .DrawImage(right, new Point(408, 0), 1f));
            using var stream = new MemoryStream();
            details.SaveAsPng(stream);
            return stream.ToArray();
        });
    }

    // Old books remain readable and printable; only new generation uses the stronger lock.
    public static bool IsGenerated(BekiCompositionManifest receipt) =>
        receipt.CompositionVersion is Version or LegacyVersion or "beki-reference-generated-v2" or "beki-reference-generated-v3";

    public static BekiCompositionManifest Receipt(byte[] png, byte[] reference, string file)
    {
        var size = SixLabors.ImageSharp.Image.Identify(png);
        return new BekiCompositionManifest
        {
            CompositionVersion = Version,
            Canvas = new() { WidthPx = size.Width, HeightPx = size.Height },
            BaseImage = new() { File = file, Sha256 = BekiCompositeEngine.Sha256Hex(png) },
            Output = new() { File = file, Sha256 = BekiCompositeEngine.Sha256Hex(png) },
            BekiLayer = new()
            {
                PoseId = PoseId, File = BekiIdentity.ReferenceAssetPath,
                Sha256 = BekiCompositeEngine.Sha256Hex(reference), Redrawn = true,
                SourceAlphaBbox = new() { XPx = 0, YPx = 0, WidthPx = 0, HeightPx = 0 },
                RenderedSizePx = new() { WidthPx = 0, HeightPx = 0 },
                PlacementPx = new() { XPx = 0, YPx = 0 },
                NormalizedAnchor = new() { VisibleCenterX = 0, VisibleCenterY = 0, VisibleHeight = 0 },
            },
        };
    }
}
