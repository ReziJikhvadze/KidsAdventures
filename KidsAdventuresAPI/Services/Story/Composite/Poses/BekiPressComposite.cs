namespace AdventurePacks.Api.Services.Story.Composite.Poses;

/// <summary>Never send an exact-Beki composite through an image upscaler.</summary>
public static class BekiPressComposite
{
    public static void ValidateSource(byte[] basePng, byte[] composite, BekiCompositionManifest receipt)
    {
        var layer = receipt.BekiLayer;
        if (receipt.CompositionVersion != BekiCompositionManifest.Version
            || !string.Equals(BekiCompositeEngine.Sha256Hex(basePng), receipt.BaseImage.Sha256,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(BekiCompositeEngine.Sha256Hex(composite), receipt.Output.Sha256,
                StringComparison.OrdinalIgnoreCase)
            || layer.Mirrored || layer.Rotated || layer.Warped || layer.Redrawn || layer.Opacity != 1)
        {
            throw new BekiLayoutException(CompositeFailureCodes.PrintPreflightFailed,
                "EXACT_BEKI: source bytes or approved-layer rules do not match the composition receipt.");
        }
    }

    public static BekiCompositeResult Compose(
        byte[] enlargedBase, BekiCompositionManifest original, string outputPrefix)
    {
        var anchor = original.BekiLayer.NormalizedAnchor;
        var result = BekiCompositeEngine.Create().Composite(
            enlargedBase, outputPrefix + "-base.png", original.BekiLayer.PoseId,
            new BekiCompositeAnchor(anchor.VisibleCenterX, anchor.VisibleCenterY, anchor.VisibleHeight),
            outputPrefix + "-composite.png");
        if (!string.Equals(result.Manifest.BekiLayer.Sha256, original.BekiLayer.Sha256,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new BekiLayoutException(CompositeFailureCodes.PrintPreflightFailed,
                "EXACT_BEKI: print recomposition used a different approved pose asset.");
        }
        return result;
    }
}
