namespace AdventurePacks.Api.Services.Story.Composite.Poses;

/// <summary>
/// Provenance for a reference-guided generated spread. This is not an exact PNG composite:
/// the layer identifies the input reference; zero layer geometry means no separate layer exists.
/// </summary>
public static class BekiGeneratedArtwork
{
    public const string Version = "beki-reference-generated-v2";
    private const string LegacyVersion = "beki-reference-generated-v1";
    public const string PoseId = "canonical-reference-generated";

    public static byte[] Reference(string? path = null) => File.ReadAllBytes(
        Path.IsPathRooted(path ?? BekiIdentity.ReferenceAssetPath)
            ? path! : Path.Combine(AppContext.BaseDirectory, path ?? BekiIdentity.ReferenceAssetPath));

    // Old books remain readable and printable; only new generation uses the stronger lock.
    public static bool IsGenerated(BekiCompositionManifest receipt) =>
        receipt.CompositionVersion is Version or LegacyVersion;

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
