using AdventurePacks.Api.Services.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace AdventurePacks.Api.Services.Implementations;

public sealed class ReferenceImageNormalizer(ILogger<ReferenceImageNormalizer> logger) : IReferenceImageNormalizer
{
    private const int OpenAiMaxEdgePixels = 2048;
    private const int OpenAiMinEdgePixels = 256;
    private const int StorageWebpQuality = 88;

    public NormalizedReferenceImage NormalizeForOpenAi(byte[] bytes, string? hintContentType = null)
    {
        using var image = LoadAndPrepare(bytes, hintContentType, OpenAiMaxEdgePixels, OpenAiMinEdgePixels);

        using var output = new MemoryStream();
        image.Save(output, new PngEncoder
        {
            CompressionLevel = PngCompressionLevel.Level6
        });

        return new NormalizedReferenceImage(output.ToArray(), "image/png", "reference.png");
    }

    public NormalizedReferenceImage NormalizeForStorageWebp(byte[] bytes, string? hintContentType = null)
    {
        using var image = LoadAndPrepare(bytes, hintContentType, OpenAiMaxEdgePixels, OpenAiMinEdgePixels);

        using var output = new MemoryStream();
        image.Save(output, new WebpEncoder
        {
            Quality = StorageWebpQuality,
            FileFormat = WebpFileFormatType.Lossy
        });

        return new NormalizedReferenceImage(output.ToArray(), "image/webp", "illustration.webp");
    }

    private Image LoadAndPrepare(
        byte[] bytes,
        string? hintContentType,
        int maxEdgePixels,
        int minEdgePixels)
    {
        if (bytes is not { Length: > 0 })
        {
            throw new InvalidOperationException("Reference image is empty.");
        }

        Image image;
        try
        {
            image = Image.Load(bytes);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Could not decode reference image ({Length} bytes, hint {HintContentType})",
                bytes.Length,
                hintContentType ?? "unknown");
            throw new InvalidOperationException(
                "Photo could not be read. Upload a clear JPG or PNG (front-facing, under 5 MB).",
                ex);
        }

        image.Mutate(ctx => ctx.AutoOrient());

        var width = image.Width;
        var height = image.Height;

        if (width < minEdgePixels || height < minEdgePixels)
        {
            var upscale = Math.Max(
                (double)minEdgePixels / width,
                (double)minEdgePixels / height);
            var targetWidth = Math.Max(minEdgePixels, (int)Math.Round(width * upscale));
            var targetHeight = Math.Max(minEdgePixels, (int)Math.Round(height * upscale));
            image.Mutate(ctx => ctx.Resize(targetWidth, targetHeight));
        }
        else if (width > maxEdgePixels || height > maxEdgePixels)
        {
            image.Mutate(ctx => ctx.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(maxEdgePixels, maxEdgePixels)
            }));
        }

        return image;
    }
}
