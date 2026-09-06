using AdventurePacks.Api.Services.Implementations;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// What a child's photograph costs to keep.
///
/// A portrait was stored as a lossless PNG, on the reasoning that the image model reads it and
/// should read the best copy available. The reasoning does not survive looking at where the photo
/// comes from: the browser fits it to 1024px and re-encodes it as JPEG at 0.82 before it is ever
/// uploaded, so the "best copy available" is already a lossy one, and encoding it losslessly
/// preserved its artefacts at several times the size of the file that carried them.
///
/// These pin the two halves of the fix — that what is stored is a compressed portrait, and that
/// what is handed to the model is still a PNG — because either one silently reverting is a bill
/// nobody would notice until the storage account did.
/// </summary>
public class PortraitStorageSizeTests
{
    [Fact]
    public void A_stored_portrait_is_far_smaller_than_the_lossless_copy_it_replaces()
    {
        var normalizer = new ReferenceImageNormalizer(NullLogger<ReferenceImageNormalizer>.Instance);
        var upload = PhotographAsTheBrowserSendsIt();

        var stored = normalizer.NormalizeForPortraitStorage(upload, "image/jpeg");
        var lossless = normalizer.NormalizeForOpenAi(upload, "image/jpeg");

        Assert.Equal("image/webp", stored.ContentType);
        // The measured ratio on a real portrait was closer to seven; a third is the loosest bound
        // that still fails if the encoder is ever put back to lossless.
        Assert.True(
            stored.Bytes.Length * 3 < lossless.Bytes.Length,
            $"stored {stored.Bytes.Length} bytes against {lossless.Bytes.Length} lossless — "
            + "the portrait is no longer being compressed.");
    }

    [Fact]
    public void What_the_image_model_is_given_is_still_a_png()
    {
        var normalizer = new ReferenceImageNormalizer(NullLogger<ReferenceImageNormalizer>.Instance);

        var forModel = normalizer.NormalizeForOpenAi(PhotographAsTheBrowserSendsIt(), "image/jpeg");

        Assert.Equal("image/png", forModel.ContentType);
        Assert.Equal("reference.png", forModel.FileName);
    }

    /// <summary>
    /// A stored portrait is read back and re-encoded for the model, so the compression has to
    /// survive that round trip rather than only the moment it is written.
    /// </summary>
    [Fact]
    public void A_stored_portrait_can_still_be_prepared_for_the_model()
    {
        var normalizer = new ReferenceImageNormalizer(NullLogger<ReferenceImageNormalizer>.Instance);
        var stored = normalizer.NormalizeForPortraitStorage(PhotographAsTheBrowserSendsIt(), "image/jpeg");

        var forModel = normalizer.NormalizeForOpenAi(stored.Bytes, stored.ContentType);

        Assert.Equal("image/png", forModel.ContentType);
        using var decoded = Image.Load(forModel.Bytes);
        Assert.Equal(1024, decoded.Width);
    }

    /// <summary>
    /// Deliberately not a flat fill: a single colour compresses to nothing under either encoder
    /// and would let a lossless copy pass the size assertion above. Noise stands in for the
    /// detail a face carries, and the JPEG pass is what <c>preparePortrait.ts</c> already did to
    /// it before the request left the phone.
    /// </summary>
    private static byte[] PhotographAsTheBrowserSendsIt()
    {
        using var image = new Image<Rgba32>(1024, 768);
        var random = new Random(20260906);
        image.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = new Rgba32(
                        (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
                }
            }
        });

        using var buffer = new MemoryStream();
        image.Save(buffer, new JpegEncoder { Quality = 82 });
        return buffer.ToArray();
    }

    /// <summary>The display copy stays what the list draws: small, and webp.</summary>
    [Fact]
    public void The_display_copy_is_smaller_again_than_the_stored_one()
    {
        var normalizer = new ReferenceImageNormalizer(NullLogger<ReferenceImageNormalizer>.Instance);
        var upload = PhotographAsTheBrowserSendsIt();

        var stored = normalizer.NormalizeForPortraitStorage(upload, "image/jpeg");
        var display = normalizer.NormalizeForDisplayWebp(upload, "image/jpeg");

        Assert.Equal("image/webp", display.ContentType);
        Assert.True(
            display.Bytes.Length < stored.Bytes.Length,
            $"display copy {display.Bytes.Length} bytes is not smaller than the stored "
            + $"{stored.Bytes.Length}.");

        using var decoded = Image.Load(display.Bytes);
        Assert.Equal(512, Math.Max(decoded.Width, decoded.Height));
    }
}
