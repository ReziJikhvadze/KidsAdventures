using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;

namespace AdventurePacks.Api.Services.Pdf;

/// <summary>
/// The press raster's locked sizes, and the one deterministic way this product reaches them.
///
/// <see cref="NormalizeDeterministic"/> is the 2026-09-06 decision record's pipeline step: an
/// aspect-safe minimal crop, then exactly one Lanczos3 resize onto the locked pixel size, then 300
/// PPI metadata and a lossless PNG for the composer to make its single JPEG encode from.
/// <see cref="FinalSize"/> is the older, narrower helper that only ever reduces — it exists for the
/// external super-resolution mode, whose tool returns whole-factor output that overshoots.
/// </summary>
public static class BekiPressRaster
{
    /// <summary>
    /// The resampler identity written into every deterministic receipt.
    ///
    /// A version and an algorithm rather than the word "resize": the receipt's job is to let a
    /// physical proof be inspected against what actually produced it, and "Lanczos3 as ImageSharp
    /// 3.1.12 implements it" is a reproducible statement where "resize" is not.
    /// </summary>
    public const string DeterministicTool = "imagesharp-3.1.12-lanczos3";

    /// <summary>
    /// How much of an axis a ratio reconciliation may discard before it stops being rounding.
    ///
    /// The locked sheets and the generated frames do not divide into each other exactly, so a
    /// centred crop of a pixel or two is arithmetic. Half a percent of a 5315 px spread is 26 px;
    /// anything past that is a composition the artwork does not have, and stretching it into the
    /// sheet instead would be the defect this whole stage exists to refuse.
    /// </summary>
    public const double MaxSafeCropFraction = 0.005;

    /// <summary>Locked interior spread raster: 450 mm at 300 PPI.</summary>
    public const int InteriorWidthPx = 5315;

    /// <summary>Locked interior spread raster: 210 mm at 300 PPI.</summary>
    public const int InteriorHeightPx = 2480;

    /// <summary>Locked cover wrap raster: 512 mm at 300 PPI.</summary>
    public const int CoverWidthPx = 6047;

    /// <summary>Locked cover wrap raster: 245 mm at 300 PPI.</summary>
    public const int CoverHeightPx = 2894;

    /// <summary>
    /// Brings one raster to exactly <paramref name="targetWidth"/>×<paramref name="targetHeight"/>,
    /// enlarging or reducing, and records what it did.
    /// </summary>
    /// <returns>
    /// The prepared PNG with its provenance, or a refusal with a reason. Never throws for image
    /// content: undecodable bytes and an artwork whose composition is simply wrong for the sheet
    /// are both answers the caller records and the gate then judges on the composed output.
    /// </returns>
    public static PressUpscaleResult NormalizeDeterministic(
        byte[] png, int targetWidth, int targetHeight)
    {
        ArgumentNullException.ThrowIfNull(png);

        Image<Rgba32> image;
        try
        {
            image = Image.Load<Rgba32>(png);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new PressUpscaleResult(
                false, null, "none", 1d, 0, 0, 0, 0,
                $"the source could not be decoded as an image ({ex.GetType().Name}).");
        }

        using (image)
        {
            var width = image.Width;
            var height = image.Height;

            // Already exact: the same bytes come back, by reference. A raster that is the size the
            // sheet needs must not be resampled a second time — a re-encode here would cost the
            // one thing the pipeline is protecting, which is that the press file is one generation
            // away from the render.
            if (width == targetWidth && height == targetHeight)
            {
                return new PressUpscaleResult(
                    true, png, "native-source", 1d, width, height, targetWidth, targetHeight, null);
            }

            // The centred crop that makes the source ratio equal the target's to within one source
            // pixel — the same arithmetic SpreadArtCrop.CropToRatio uses, so the reviewer's copy and
            // the press raster keep the same frame.
            var targetRatio = (double)targetWidth / targetHeight;
            var cropWidth = width;
            var cropHeight = height;

            if ((double)width / height > targetRatio)
            {
                cropWidth = Math.Clamp((int)Math.Round(height * targetRatio), 1, width);
            }
            else
            {
                cropHeight = Math.Clamp((int)Math.Round(width / targetRatio), 1, height);
            }

            var cropFractionX = 1d - ((double)cropWidth / width);
            var cropFractionY = 1d - ((double)cropHeight / height);

            if (cropFractionX > MaxSafeCropFraction || cropFractionY > MaxSafeCropFraction)
            {
                return new PressUpscaleResult(
                    false, null, "none", 1d, width, height, width, height,
                    $"aspect mismatch: normalizing {width}×{height} to {targetWidth}×{targetHeight} "
                    + $"would crop {cropFractionX:P2} of width and {cropFractionY:P2} of height, past "
                    + $"the {MaxSafeCropFraction:P1} rounding allowance; the artwork must be "
                    + "re-cropped upstream, not stretched");
            }

            var x = (width - cropWidth) / 2;
            var y = (height - cropHeight) / 2;

            image.Mutate(context =>
            {
                if (cropWidth != width || cropHeight != height)
                {
                    context.Crop(new Rectangle(x, y, cropWidth, cropHeight));
                }

                // ResizeMode.Stretch is safe here and only here: the ratio was reconciled by the
                // crop above, so the two axes scale by the same factor to within one source pixel
                // and nothing is distorted. Reached for enlargement and reduction alike — one
                // resampling pass either way, which is what "deterministic" buys.
                context.Resize(new ResizeOptions
                {
                    Size = new Size(targetWidth, targetHeight),
                    Mode = ResizeMode.Stretch,
                    Sampler = KnownResamplers.Lanczos3,
                });
            });

            // Stated, not implied. The gate computes PPI from pixels over placed millimetres and
            // never reads this — but a press operator opening the intermediate does read it.
            image.Metadata.ResolutionUnits = PixelResolutionUnit.PixelsPerInch;
            image.Metadata.HorizontalResolution = 300d;
            image.Metadata.VerticalResolution = 300d;

            // PNG, deliberately: this is an intermediate, and the composer performs the single
            // JPEG encode the press file ships with. Two encodes would be two generations of loss
            // where the specification allows one.
            using var buffer = new MemoryStream();
            image.Save(buffer, new PngEncoder());

            return new PressUpscaleResult(
                true,
                buffer.ToArray(),
                DeterministicTool,
                (double)targetWidth / cropWidth,
                width,
                height,
                targetWidth,
                targetHeight,
                null,
                new PressCropGeometry(x, y, cropWidth, cropHeight, cropFractionX, cropFractionY));
        }
    }

    /// <summary>
    /// Reduces an over-sized prepared base to the locked size. External mode only: a super-resolver
    /// scales by whole factors and overshoots, and this is where the overshoot comes off.
    /// </summary>
    public static byte[] FinalSize(byte[] detailPreparedBase, int width, int height)
    {
        using var image = Image.Load(detailPreparedBase);
        if (image.Width < width || image.Height < height)
            throw new BekiLayoutException(CompositeFailureCodes.PrintPreflightFailed,
                $"PRESS_RESOLUTION: {image.Width}x{image.Height} is short of the {width}x{height} "
                + "this placement needs; final sizing only reduces.");
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
