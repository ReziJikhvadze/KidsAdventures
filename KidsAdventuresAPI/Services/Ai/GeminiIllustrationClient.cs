using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.Services.Interfaces;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;

namespace AdventurePacks.Api.Services.Ai;

/// <summary>
/// The three calls that make or read a picture, answered by Gemini.
///
/// Deliberately not the whole of <see cref="IOpenAiService"/>: the rest of that interface is the
/// legacy A5 text path, which has no business changing vendor because somebody wanted a different
/// illustrator. <see cref="AiServiceRouter"/> is what puts these three in front of the OpenAI
/// implementation when the configuration asks for it.
///
/// References are normalized through the same component the OpenAI path uses, so a photograph is
/// oriented, bounded and encoded identically whichever vendor ends up looking at it — a picture
/// that differs before it is sent makes the two vendors' output incomparable for reasons that
/// have nothing to do with the models.
/// </summary>
public interface IIllustrationClient
{
    Task<byte[]> GenerateStoryImageAsync(
        string imagePrompt,
        StoryImageReference? reference,
        CancellationToken cancellationToken,
        string? imageSize = null);

    Task<string> ReviewIllustrationAsync(
        byte[] imageBytes,
        string reviewPrompt,
        IReadOnlyList<(byte[] Bytes, string ContentType, string Label)> references,
        CancellationToken cancellationToken);

    Task<string> DescribeCharacterFromPhotoAsync(
        byte[] imageBytes,
        string contentType,
        string promptText,
        CancellationToken cancellationToken);
}

public sealed class GeminiIllustrationClient(
    IGeminiInteractionsClient gemini,
    IReferenceImageNormalizer referenceImageNormalizer,
    IOptions<GeminiOptions> geminiOptions,
    IOptions<OpenAiOptions> openAiOptions,
    ILogger<GeminiIllustrationClient> logger) : IIllustrationClient
{
    private readonly GeminiOptions _gemini = geminiOptions.Value;
    private readonly OpenAiOptions _openAi = openAiOptions.Value;

    public async Task<byte[]> GenerateStoryImageAsync(
        string imagePrompt,
        StoryImageReference? reference,
        CancellationToken cancellationToken,
        string? imageSize = null)
    {
        if (!_openAi.EnableStoryImages)
        {
            throw new InvalidOperationException("Story images are disabled in configuration.");
        }

        var size = string.IsNullOrWhiteSpace(imageSize) ? _openAi.ImageSize : imageSize!;
        var aspectRatio = AspectRatioFor(size);

        var input = new List<GeminiInputItem> { GeminiInputItem.Text(imagePrompt) };
        input.AddRange(CollectReferences(reference));

        if (_openAi.LogPrompts)
        {
            logger.LogInformation(
                "Gemini image request → model={Model} aspect={Aspect} size={Size} references={Count}\n" +
                "--- prompt ---\n{Prompt}",
                _gemini.ImageModel, aspectRatio, _gemini.ImageSize, input.Count - 1, imagePrompt);
        }

        var responseFormat = new
        {
            type = "image",
            // JPEG because it is the only thing the API will return: asking for image/png is a
            // 400 saying so in as many words. The transcode below is what keeps that a detail of
            // this file rather than a fact the whole pipeline has to learn.
            mime_type = "image/jpeg",
            aspect_ratio = aspectRatio,
            image_size = _gemini.ImageSize
        };

        var jpeg = await gemini.GenerateImageAsync(
            _gemini.ImageModel, input, responseFormat, cancellationToken);

        return ToPng(jpeg);
    }

    public async Task<string> ReviewIllustrationAsync(
        byte[] imageBytes,
        string reviewPrompt,
        IReadOnlyList<(byte[] Bytes, string ContentType, string Label)> references,
        CancellationToken cancellationToken)
    {
        // Same order the OpenAI reviewer sees: the instruction, then the picture under review,
        // then each reference behind its own label. A reviewer handed three pictures and no
        // labels has no way to know which one it is being asked to judge.
        var generated = referenceImageNormalizer.NormalizeForOpenAi(imageBytes, "image/png");

        var input = new List<GeminiInputItem>
        {
            GeminiInputItem.Text(reviewPrompt),
            GeminiInputItem.Text("[The generated illustration under review]"),
            GeminiInputItem.Image(generated.Bytes, generated.ContentType),
        };

        foreach (var (bytes, contentType, label) in references)
        {
            var normalized = referenceImageNormalizer.NormalizeForOpenAi(bytes, contentType);
            input.Add(GeminiInputItem.Text($"[{label}]"));
            input.Add(GeminiInputItem.Image(normalized.Bytes, normalized.ContentType));
        }

        // No schema. The verdict is read by a sanitizer that already copes with a code fence or a
        // sentence around the JSON, and the QA prompt spells the shape out; adding a schema here
        // would be a second contract to keep in step with the prompt for no gain.
        var result = await gemini.CompleteTextAsync(
            _gemini.VisionModel, input, null, cancellationToken);

        return result.Text;
    }

    public async Task<string> DescribeCharacterFromPhotoAsync(
        byte[] imageBytes,
        string contentType,
        string promptText,
        CancellationToken cancellationToken)
    {
        var normalized = referenceImageNormalizer.NormalizeForOpenAi(imageBytes, contentType);

        var result = await gemini.CompleteTextAsync(
            _gemini.VisionModel,
            [
                GeminiInputItem.Text(promptText),
                GeminiInputItem.Image(normalized.Bytes, normalized.ContentType),
            ],
            null,
            cancellationToken);

        return result.Text;
    }

    private IEnumerable<GeminiInputItem> CollectReferences(StoryImageReference? reference)
    {
        if (reference is null)
        {
            yield break;
        }

        if (reference.CharacterAnchorBytes is { Length: > 0 } anchor)
        {
            var normalized = referenceImageNormalizer.NormalizeForOpenAi(anchor, "image/webp");
            yield return GeminiInputItem.Image(normalized.Bytes, normalized.ContentType);
        }

        foreach (var cast in reference.CastPhotos)
        {
            if (cast.Bytes is not { Length: > 0 })
            {
                continue;
            }

            var normalized = referenceImageNormalizer.NormalizeForOpenAi(cast.Bytes, cast.ContentType);
            yield return GeminiInputItem.Image(normalized.Bytes, normalized.ContentType);
        }
    }

    /// <summary>
    /// The pipeline asks for a pixel size because that is what the OpenAI image API takes; Gemini
    /// takes a ratio and a resolution class instead. Translating here rather than configuring a
    /// ratio separately keeps one answer to "what shape is a spread": the caller's, which is the
    /// only one that knows whether it is drawing a spread, a cover or an A5 page.
    ///
    /// An unrecognisable size falls back to the API's default rather than failing — a book is not
    /// worth losing over a shape, and a square is a visible symptom that leads straight here.
    /// </summary>
    internal static string AspectRatioFor(string imageSize)
    {
        var parts = imageSize?.Split('x', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts is not { Length: 2 }
            || !int.TryParse(parts[0], out var width)
            || !int.TryParse(parts[1], out var height)
            || width <= 0 || height <= 0)
        {
            return "1:1";
        }

        var divisor = Gcd(width, height);
        return $"{width / divisor}:{height / divisor}";
    }

    /// <summary>
    /// Gemini hands back JPEG; everything downstream of the generator is written for PNG — the
    /// blob is named .png, served as image/png, and composed from PNG bytes. One re-encode here
    /// keeps that one invariant true rather than teaching the manifest, the making-of endpoint,
    /// the composer and the reader that a picture's format now depends on which vendor drew it.
    ///
    /// Fastest compression, deliberately: the bytes are re-encoded again on the way into print,
    /// so paying CPU for a smaller intermediate buys nothing.
    /// </summary>
    private static byte[] ToPng(byte[] jpeg)
    {
        using var image = Image.Load(jpeg);
        using var buffer = new MemoryStream();
        image.Save(buffer, new PngEncoder { CompressionLevel = PngCompressionLevel.BestSpeed });
        return buffer.ToArray();
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a;
    }
}
