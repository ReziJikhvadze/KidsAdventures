using System.Security.Cryptography;
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

    /// <summary>
    /// Results already computed by this instance, keyed by what decides them: the variant and
    /// the bytes that went in.
    ///
    /// Normalizing is pure — the same bytes always produce the same PNG — and a Beki book asks
    /// for the same two or three references over and over: every render and every review sends
    /// the child's photograph and the 2.6 MB Beki master, so one book decoded, resized and
    /// re-encoded them sixty times over to hand the API sixty identical files. The service is
    /// scoped, so this cache lives exactly as long as the request or the Hangfire job that owns
    /// it, and one child's photograph can never be handed to another child's book.
    ///
    /// Bounded because a job could, in principle, keep feeding it new images; past the limit it
    /// simply stops remembering rather than growing.
    /// </summary>
    private readonly Dictionary<string, NormalizedReferenceImage> _cache = [];

    private const int MaxCacheEntries = 16;

    public NormalizedReferenceImage NormalizeForOpenAi(byte[] bytes, string? hintContentType = null) =>
        Cached("openai", bytes, () =>
        {
            using var image = LoadAndPrepare(bytes, hintContentType, OpenAiMaxEdgePixels, OpenAiMinEdgePixels);

            using var output = new MemoryStream();
            image.Save(output, new PngEncoder
            {
                CompressionLevel = PngCompressionLevel.Level6
            });

            return new NormalizedReferenceImage(output.ToArray(), "image/png", "reference.png");
        });

    public NormalizedReferenceImage NormalizeForStorageWebp(byte[] bytes, string? hintContentType = null) =>
        Cached("webp", bytes, () =>
        {
            using var image = LoadAndPrepare(bytes, hintContentType, OpenAiMaxEdgePixels, OpenAiMinEdgePixels);

            using var output = new MemoryStream();
            image.Save(output, new WebpEncoder
            {
                Quality = StorageWebpQuality,
                FileFormat = WebpFileFormatType.Lossy
            });

            return new NormalizedReferenceImage(output.ToArray(), "image/webp", "illustration.webp");
        });

    /// <summary>
    /// Hashing the input is far cheaper than normalizing it again: SHA-256 over a few megabytes
    /// is milliseconds, where the decode, resize and re-encode it replaces is seconds. A failure
    /// is never cached — an unreadable photo must raise its own message every time it is tried.
    /// </summary>
    private NormalizedReferenceImage Cached(
        string variant, byte[] bytes, Func<NormalizedReferenceImage> normalize)
    {
        if (bytes is not { Length: > 0 })
        {
            return normalize();
        }

        var key = $"{variant}:{Convert.ToHexString(SHA256.HashData(bytes))}";
        if (_cache.TryGetValue(key, out var hit))
        {
            return hit;
        }

        var normalized = normalize();
        if (_cache.Count < MaxCacheEntries)
        {
            _cache[key] = normalized;
        }

        return normalized;
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
