using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;

namespace AdventurePacks.Api.Services.Pdf;

/// <summary>Final-size preparation is downsampling only, never an upscaler substitute.</summary>
public static class BekiPressRaster
{
    public static byte[] FinalSize(byte[] detailPreparedBase, int width, int height)
    {
        using var image = Image.Load(detailPreparedBase);
        if (image.Width < width || image.Height < height)
            throw new BekiLayoutException(CompositeFailureCodes.PrintPreflightFailed,
                $"PRESS_RESOLUTION: {image.Width}x{image.Height} cannot be interpolated up to {width}x{height}.");
        if (image.Width == width && image.Height == height) return detailPreparedBase;
        // Canvas ratios differ only by integer rounding. Refuse a different composition.
        if (Math.Abs((double)image.Width / image.Height / ((double)width / height) - 1) > 0.005)
            throw new BekiLayoutException(CompositeFailureCodes.PrintPreflightFailed,
                "PRESS_GEOMETRY: prepared base has a different aspect ratio.");
        image.Mutate(context => context.Resize(width, height, KnownResamplers.Lanczos3));
        using var output = new MemoryStream();
        image.SaveAsPng(output);
        return output.ToArray();
    }
}
