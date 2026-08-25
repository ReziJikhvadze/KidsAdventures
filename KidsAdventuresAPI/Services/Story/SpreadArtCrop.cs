using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AdventurePacks.Api.Services.Story;

/// <summary>
/// Centre-crops image bytes to a target aspect ratio, for judging rather than for storing.
///
/// QA must judge what the reader will see, not pixels the print crop discards. Every Beki render
/// arrives at 3:2 and <see cref="BekiPdfComposer"/> centre-crops it to the sheet at layout time —
/// the top and bottom sixths trimmed from a spread, the outer edges trimmed from a cover — so a
/// reviewer shown the full provider frame can pass a picture whose one clear face sits in a band
/// print will remove, or fail one whose only fault already lies outside the sheet. This mirrors
/// the composer's own crop math exactly, so what QA sees is what print keeps.
/// </summary>
public static class SpreadArtCrop
{
    /// <summary>
    /// Crops to <paramref name="targetRatio"/> (width divided by height), keeping the centre.
    /// Never upscales or stretches: only the dimension in excess of the target is trimmed, and a
    /// source already at the target ratio passes through unchanged.
    /// </summary>
    public static byte[] CropToRatio(byte[] png, float targetRatio)
    {
        using var image = Image.Load<Rgba32>(png);

        var width = image.Width;
        var height = image.Height;
        var cropWidth = width;
        var cropHeight = height;

        if ((float)width / height > targetRatio)
        {
            cropWidth = Math.Clamp((int)MathF.Round(height * targetRatio), 1, width);
        }
        else
        {
            cropHeight = Math.Clamp((int)MathF.Round(width / targetRatio), 1, height);
        }

        if (cropWidth == width && cropHeight == height)
        {
            return png;
        }

        image.Mutate(ctx => ctx.Crop(new Rectangle(
            (width - cropWidth) / 2, (height - cropHeight) / 2, cropWidth, cropHeight)));

        using var buffer = new MemoryStream();
        image.Save(buffer, new PngEncoder());
        return buffer.ToArray();
    }

    /// <summary>
    /// Crops to <paramref name="targetRatio"/> and reduces the result to <paramref name="maxWidth"/>
    /// in a single decode, for the reviewer's copy.
    ///
    /// The two steps used to be two calls, and two calls meant two decodes and two lossless
    /// encodes of a 1536×1024 render for every attempt — ten seconds and more of an App Service
    /// core, per picture, between the render arriving and the review starting, in the one gap
    /// neither stopwatch was measuring. Same pixels out; one pass to get them.
    ///
    /// The encoder is deliberately the fastest one rather than the smallest: this PNG lives for
    /// one HTTP request and then dies, so every byte the compressor saves is bought with CPU
    /// nobody gets back.
    /// </summary>
    public static byte[] CropAndReduce(byte[] png, float targetRatio, int maxWidth)
    {
        using var image = Image.Load<Rgba32>(png);

        var width = image.Width;
        var height = image.Height;
        var cropWidth = width;
        var cropHeight = height;

        if ((float)width / height > targetRatio)
        {
            cropWidth = Math.Clamp((int)MathF.Round(height * targetRatio), 1, width);
        }
        else
        {
            cropHeight = Math.Clamp((int)MathF.Round(width / targetRatio), 1, height);
        }

        var cropped = cropWidth != width || cropHeight != height;
        var reduced = maxWidth > 0 && cropWidth > maxWidth;

        if (!cropped && !reduced)
        {
            return png;
        }

        image.Mutate(ctx =>
        {
            if (cropped)
            {
                ctx.Crop(new Rectangle(
                    (width - cropWidth) / 2, (height - cropHeight) / 2, cropWidth, cropHeight));
            }

            if (reduced)
            {
                var targetHeight = Math.Max(1, (int)Math.Round(cropHeight * ((double)maxWidth / cropWidth)));
                ctx.Resize(maxWidth, targetHeight, KnownResamplers.Lanczos3);
            }
        });

        using var buffer = new MemoryStream();
        image.Save(buffer, new PngEncoder { CompressionLevel = PngCompressionLevel.BestSpeed });
        return buffer.ToArray();
    }
}
