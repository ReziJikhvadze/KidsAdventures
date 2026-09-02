using System.Net.Http.Headers;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Infrastructure;
using AdventurePacks.Api.Services;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Implementations;

/// <summary>
/// Text + images via OpenAI Responses API; reference-photo illustrations via Images Edit (gpt-image-1.5).
/// </summary>
public sealed class OpenAiService(
    IHttpClientFactory httpClientFactory,
    IReferenceImageNormalizer referenceImageNormalizer,
    IOptions<OpenAiOptions> options,
    ILogger<OpenAiService> logger) : IOpenAiService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OpenAiOptions _options = options.Value;

    public async Task<AdventureContentDto> GenerateAdventureContentAsync(
        AdventureGenerationInput input,
        Guid adventureId,
        CancellationToken cancellationToken)
    {
        var prompt = AdventurePromptBuilder.BuildStoryPrompt(input, adventureId);

        var client = CreateClient();
        var payload = new
        {
            model = _options.Model,
            input = prompt,
            text = new
            {
                format = new { type = "json_object" }
            }
        };

        using var response = await client.PostAsJsonAsync("responses", payload, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        var outputText = ModelJsonSanitizer.ExtractJsonObject(ExtractOutputText(responseText));
        if (string.IsNullOrWhiteSpace(outputText))
        {
            throw new InvalidOperationException("OpenAI output was empty.");
        }

        AdventureContentDto content;
        try
        {
            content = JsonSerializer.Deserialize<AdventureContentDto>(outputText, JsonOptions)
                      ?? throw new InvalidOperationException("Failed to parse OpenAI JSON output.");
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "OpenAI returned non-JSON story output (first 200 chars): {Preview}",
                outputText.Length > 200 ? outputText[..200] : outputText);
            throw new InvalidOperationException("Story model returned invalid JSON. Please try again.");
        }

        if (content.StoryPages.Count == 0)
        {
            throw new InvalidOperationException("Generated content is incomplete.");
        }

        var expectedPages = input.StoryPageCount > 0 ? input.StoryPageCount : AdventureStoryConstants.LegacyPageCount;
        if (expectedPages > AdventureStoryConstants.LegacyPageCount)
        {
            expectedPages = AdventureStoryConstants.LegacyPageCount;
        }

        if (content.StoryPages.Count > expectedPages)
        {
            logger.LogWarning(
                "Trimming {Actual} story pages down to {Expected} for adventure {AdventureId}",
                content.StoryPages.Count,
                expectedPages,
                adventureId);
            content.StoryPages = content.StoryPages.Take(expectedPages).ToList();
        }

        if (content.StoryPages.Count != expectedPages)
        {
            logger.LogWarning(
                "Expected {Expected} story pages but model returned {Actual} for adventure {AdventureId}",
                expectedPages,
                content.StoryPages.Count,
                adventureId);
        }

        return content;
    }

    public async Task<string> DescribeCharacterFromPhotoAsync(
        byte[] imageBytes,
        string contentType,
        string promptText,
        CancellationToken cancellationToken)
    {
        var client = CreateClient();
        var normalized = referenceImageNormalizer.NormalizeForOpenAi(imageBytes, contentType);
        var dataUrl = ToDataUrl(normalized.Bytes, normalized.ContentType);

        var payload = new
        {
            model = _options.Model,
            input = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "input_text",
                            text = promptText
                        },
                        new
                        {
                            type = "input_image",
                            image_url = dataUrl
                        }
                    }
                }
            }
        };

        using var response = await client.PostAsJsonAsync("responses", payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI vision failed: {body}");
        }

        var text = ExtractOutputText(body);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("OpenAI vision returned empty description.");
        }

        return text.Trim();
    }

    public async Task<string> CompleteTextAsync(string promptText, CancellationToken cancellationToken)
    {
        var client = CreateClient();

        var payload = new
        {
            model = _options.Model,
            input = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "input_text", text = promptText }
                    }
                }
            }
        };

        using var response = await client.PostAsJsonAsync("responses", payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI text completion failed: {body}");
        }

        return (ExtractOutputText(body) ?? string.Empty).Trim();
    }

    /// <summary>
    /// QA IMAGE — the third of the Beki format's three model tasks.
    ///
    /// Built on the same vision call as <see cref="DescribeCharacterFromPhotoAsync"/> rather than
    /// on a new client, and deliberately unopinionated: it hands back whatever JSON the model
    /// produced instead of parsing it into a verdict type. The prototype is here to find out what
    /// the model actually says and how often, and a parser written before that is known would
    /// quietly discard the cases worth reading.
    ///
    /// The generated image comes first in the payload, then the references, each announced by a
    /// line of text — an image model given three pictures and no labels has no way to know which
    /// one it is being asked to judge.
    /// </summary>
    public async Task<string> ReviewIllustrationAsync(
        byte[] imageBytes,
        string reviewPrompt,
        IReadOnlyList<(byte[] Bytes, string ContentType, string Label)> references,
        CancellationToken cancellationToken)
    {
        var client = CreateClient();

        var generated = referenceImageNormalizer.NormalizeForOpenAi(imageBytes, "image/png");
        var content = new List<object>
        {
            new { type = "input_text", text = reviewPrompt },
            new { type = "input_text", text = "[The generated illustration under review]" },
            new { type = "input_image", image_url = ToDataUrl(generated.Bytes, generated.ContentType) },
        };

        foreach (var (bytes, contentType, label) in references)
        {
            var normalized = referenceImageNormalizer.NormalizeForOpenAi(bytes, contentType);
            content.Add(new { type = "input_text", text = $"[{label}]" });
            content.Add(new { type = "input_image", image_url = ToDataUrl(normalized.Bytes, normalized.ContentType) });
        }

        var payload = new
        {
            model = _options.Model,
            input = new object[] { new { role = "user", content } },
        };

        using var response = await client.PostAsJsonAsync("responses", payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI image QA failed: {body}");
        }

        return ExtractOutputText(body).Trim();
    }

    public async Task<byte[]> GenerateStoryImageAsync(
        string imagePrompt,
        StoryImageReference? reference,
        CancellationToken cancellationToken,
        string? imageSize = null,
        bool requireReferences = false,
        string? imageQuality = null)
    {
        if (!_options.EnableStoryImages)
        {
            throw new InvalidOperationException("Story images are disabled in configuration.");
        }

        // Null means the configured size, which is what every caller but the Beki prototype
        // passes. Resolved once here so the three routes below cannot disagree about it.
        var size = string.IsNullOrWhiteSpace(imageSize) ? _options.ImageSize : imageSize!;

        // Same shape for the quality: null is the deployment's default, anything else is this
        // one picture's own ask — the anchor and the cover are worth more than a page.
        var quality = string.IsNullOrWhiteSpace(imageQuality) ? _options.ImageQuality : imageQuality!.Trim();

        var referenceImages = CollectReferenceImages(reference);

        if (_options.LogPrompts)
        {
            // What we asked for, never what came back. A returned illustration is megabytes of
            // base64 and putting that in a log makes the log useless and expensive at once; the
            // prompt is the part that explains why the picture looks the way it does.
            logger.LogInformation(
                "OpenAI image request → model={Model} size={Size} quality={Quality} references={ReferenceCount} route={Route}\n" +
                "--- prompt ---\n{Prompt}",
                referenceImages.Count > 0 ? _options.ImageEditModel : _options.ImageModel,
                size,
                quality,
                referenceImages.Count,
                referenceImages.Count > 0 ? "images/edits" : "images/generations",
                imagePrompt);
        }

        // A caller that needs the references cannot be handed a picture drawn without them, and
        // "there were none to send" is the same outcome as "sending them failed". Checked before
        // the call rather than after, because the failure is in the request and there is nothing to
        // learn by paying for the answer.
        if (requireReferences && referenceImages.Count == 0)
        {
            throw new InvalidOperationException(
                "This illustration must be drawn from its reference images, and none were "
                + "supplied. Drawing it from the prompt alone would produce a picture of a "
                + "different child in a different world.");
        }

        if (referenceImages.Count > 0)
        {
            try
            {
                return await GenerateStoryImageViaEditApiWithRetryAsync(
                    imagePrompt,
                    referenceImages,
                    size,
                    quality,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                /*
                  The fallback, and the one caller it is wrong for.

                  Drawing from the prompt alone when the edit route dies is the right trade for the
                  A5 flow: the prompt already carries a written appearance description, so what
                  comes back is a slightly-off hero rather than a stranger, and a book with one
                  weaker picture beats a failed job.

                  It is the wrong trade wherever the references ARE the description. On the
                  composite path the child's likeness exists only in the attached photograph, the
                  world only in the approved theme reference, and a recurring creature only in the
                  continuity image — so this fallback would return a picture of somebody else,
                  which is then composited with the approved Beki, reviewed, stored and printed.
                  Such a caller sets requireReferences and gets the exception instead.
                */
                if (requireReferences)
                {
                    logger.LogError(
                        ex,
                        "GPT Image edit failed after retries and this illustration may not be "
                        + "drawn without its references; failing rather than generating an "
                        + "unanchored picture.");
                    throw;
                }

                var usedPhoto = reference?.CastPhotos.Any(static c => c.Bytes is { Length: > 0 }) == true;
                logger.LogWarning(
                    ex,
                    usedPhoto
                        ? "GPT Image edit failed after retries; falling back to text-only generation (uploaded photo was not used — likeness may be lost)."
                        : "GPT Image edit failed after retries; one text-only images/generations fallback.");
            }
        }

        return await GenerateStoryImageViaImagesApiAsync(imagePrompt, size, quality, cancellationToken);
    }

    private async Task<byte[]> GenerateStoryImageViaEditApiWithRetryAsync(
        string imagePrompt,
        IReadOnlyList<(byte[] Bytes, string FileName, string ContentType)> referenceImages,
        string size,
        string qualitySetting,
        CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Clamp(_options.ImageRetryAttempts, 1, 6);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await GenerateStoryImageViaEditApiAsync(
                    imagePrompt, referenceImages, size, qualitySetting, cancellationToken);
            }
            catch (Exception ex) when (attempt < maxAttempts && IsRetryable(ex, cancellationToken))
            {
                var asked = (ex as OpenAiTransientException)?.RetryAfter;
                var delay = RetryDelay(asked, attempt, _options.ImageRetryBackoffSeconds);

                logger.LogWarning(
                    ex,
                    "Image edit attempt {Attempt}/{Total} hit a retryable OpenAI error; waiting {Delay}s{Capped}",
                    attempt,
                    maxAttempts,
                    delay.TotalSeconds,
                    asked is { } wanted && wanted > MaxRetryDelay
                        ? $" (the server asked for {wanted.TotalSeconds:0}s)"
                        : string.Empty);

                // Through the seam, on the caller's token: the sleep is inside the generation
                // budget, and a job whose deadline passes while it waits must stop waiting.
                await Delay(delay, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Whether one more attempt is worth making.
    ///
    /// Never when the caller's own token has fired — that is the job's deadline or the host going
    /// away, and the retry would sleep on the very token that stopped it. Otherwise yes for the
    /// three things that mean "nothing was generated, nothing was billed": the provider saying so
    /// with a 408, 429 or 5xx (<see cref="OpenAiTransientException"/>), a connection that never
    /// reached it (<see cref="HttpRequestException"/>), and the client's own timeout, which .NET
    /// reports as a cancellation that nobody asked for. The message rule from before is kept
    /// underneath for anything that arrives some other way.
    /// </summary>
    private static bool IsRetryable(Exception ex, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        if (ex is OpenAiTransientException or HttpRequestException or OperationCanceledException)
        {
            return true;
        }

        if (ex.Message.Contains("invalid_image_file", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("unsupported mimetype", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("image_generation_user_error", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return ex.Message.Contains("rate_limit", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("429", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("disconnect", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The longest one retry will sleep, however long the provider asks for. The same minute the
    /// Gemini client uses, for the same reason: a <c>Retry-After</c> obeyed without limit parked a
    /// paid book for as long as the header said, and a book that stalls for an hour is worse than
    /// one that fails in minutes.
    /// </summary>
    internal static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long to wait before the next attempt: the server's own <c>Retry-After</c> when it sent
    /// one, otherwise the configured backoff multiplied by the attempt just made — and never more
    /// than <see cref="MaxRetryDelay"/>. Pure, so the rule can be tested without waiting it out.
    /// </summary>
    internal static TimeSpan RetryDelay(TimeSpan? retryAfter, int attempt, int backoffSeconds)
    {
        var requested = retryAfter is { } advice && advice > TimeSpan.Zero
            ? advice
            : TimeSpan.FromSeconds(Math.Max(1, backoffSeconds) * Math.Max(1, attempt));

        return requested > MaxRetryDelay ? MaxRetryDelay : requested;
    }

    /// <summary>
    /// The sleep between attempts. A property rather than a call to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>
    /// so the suite can record what the retry decided to wait instead of waiting it.
    /// </summary>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var delta = response.Headers.RetryAfter?.Delta;
        if (delta is { } d && d > TimeSpan.Zero)
        {
            return d;
        }

        if (response.Headers.RetryAfter?.Date is { } at)
        {
            var wait = at - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                return wait;
            }
        }

        return null;
    }

    /// <summary>
    /// The exception for a failed image call, typed by whether asking again is worth anything: a
    /// 408, 429 or 5xx is the provider asking to be asked again, and carries its advice on when;
    /// any other status is our request being wrong, which it will be just as much next time.
    /// </summary>
    private static InvalidOperationException FailureFor(string route, HttpResponseMessage response, string body)
    {
        var status = (int)response.StatusCode;
        var message = $"{route} failed ({status}): {body}";

        return status is 408 or 429 || status >= 500
            ? new OpenAiTransientException(message, RetryAfter(response))
            : new InvalidOperationException(message);
    }

    private sealed class OpenAiTransientException(string message, TimeSpan? retryAfter)
        : InvalidOperationException(message)
    {
        public TimeSpan? RetryAfter { get; } = retryAfter;
    }

    private async Task<byte[]> GenerateStoryImageViaEditApiAsync(
        string imagePrompt,
        IReadOnlyList<(byte[] Bytes, string FileName, string ContentType)> referenceImages,
        string size,
        string qualitySetting,
        CancellationToken cancellationToken)
    {
        var client = CreateImageClient();
        using var form = new MultipartFormDataContent();
        var model = ResolveGptImageEditModel();
        var quality = MapGptImageQuality(qualitySetting);

        form.Add(new StringContent(model), "model");
        form.Add(new StringContent(imagePrompt), "prompt");
        form.Add(new StringContent(size), "size");
        form.Add(new StringContent(quality), "quality");

        /*
          input_fidelity — the setting that decides whether the child in the photograph is the
          child in the picture.

          This route is the only one in this class that carries reference images, and it was
          sending nothing here. On the gpt-image-1 family the field defaults to LOW, so every
          anchored illustration was drawn from a deliberately coarse reading of the face we
          attached — which is what the composite pipeline's CHILD_IDENTITY QA kept rejecting on
          2026-09-01.

          Sent as a form field because this is multipart, and only for models that accept it:
          gpt-image-2 already treats every input as high fidelity and answers a request carrying
          this field with a 400. ResolveImageInputFidelity holds that judgement; a null means the
          field is omitted, and omitted is the correct request, not a degraded one.
        */
        var inputFidelity = referenceImages.Count > 0
            ? ResolveImageInputFidelity(model, _options.ImageInputFidelity)
            : null;

        if (inputFidelity is not null)
        {
            form.Add(new StringContent(inputFidelity), "input_fidelity");
        }

        logger.LogDebug(
            "Images edit input_fidelity for {Model}: {Value}",
            model,
            inputFidelity ?? "(omitted)");

        for (var index = 0; index < referenceImages.Count; index++)
        {
            var (bytes, fileName, contentType) = referenceImages[index];
            logger.LogDebug(
                "Images edit reference {Index}: {FileName} ({ContentType}, {Bytes} bytes)",
                index + 1,
                fileName,
                contentType,
                bytes.Length);

            var imageContent = new ByteArrayContent(bytes);
            imageContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            form.Add(imageContent, "image[]", fileName);
        }

        using var response = await client.PostAsync("images/edits", form, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw FailureFor("OpenAI Images Edit API", response, responseText);
        }

        return await ExtractImageBytesFromImagesResponseAsync(responseText, cancellationToken);
    }

    private async Task<byte[]> GenerateStoryImageViaResponsesApiAsync(string imagePrompt, CancellationToken cancellationToken)
    {
        var client = CreateClient();

        // The tool was declared with no settings at all, so every illustration came back at
        // the model's own default — a 1024x1024 square — no matter what OpenAI:ImageSize and
        // OpenAI:ImageQuality were set to. This is the default provider, so that silently
        // applied to essentially every picture the product has ever produced, and made those
        // two settings look broken. A storybook page is portrait; state it explicitly.
        //
        // No input_fidelity here, and it is not an omission. This route sends a prompt string and
        // nothing else — there are no reference images for the model to be faithful to, so the
        // tool property has nothing to act on. Note also that nothing calls this method:
        // OpenAi:ImageGenerationProvider is read by configuration and by the live tests, but
        // GenerateStoryImageAsync routes on whether references exist, never on the provider name.
        // Anyone reviving this path for reference-carrying input must add the fidelity property to
        // the tool object itself, subject to the same per-model rule as the edit route.
        var payload = new
        {
            model = _options.Model,
            input = imagePrompt,
            tools = new[]
            {
                new
                {
                    type = "image_generation",
                    model = ResolveImagesApiModel(),
                    size = _options.ImageSize,
                    quality = MapGptImageQuality(_options.ImageQuality)
                }
            }
        };

        using var response = await client.PostAsJsonAsync("responses", payload, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI Responses image generation failed: {responseText}");
        }

        var imageBytes = ExtractImageGenerationResult(responseText);
        if (imageBytes is null || imageBytes.Length == 0)
        {
            throw new InvalidOperationException("OpenAI Responses image generation returned no image.");
        }

        return imageBytes;
    }

    private async Task<byte[]> GenerateStoryImageViaImagesApiAsync(
        string imagePrompt,
        string size,
        string qualitySetting,
        CancellationToken cancellationToken)
    {
        var client = CreateImageClient();
        var imageModel = ResolveImagesApiModel();

        var payload = new Dictionary<string, object>
        {
            ["model"] = imageModel,
            ["prompt"] = imagePrompt,
            ["n"] = 1,
            ["size"] = size
        };

        if (IsGptImageModel(imageModel))
        {
            payload["quality"] = MapGptImageQuality(qualitySetting);
        }
        else if (imageModel.Equals("dall-e-3", StringComparison.OrdinalIgnoreCase))
        {
            payload["quality"] = MapDalleQuality(qualitySetting);
        }

        using var response = await client.PostAsJsonAsync("images/generations", payload, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw FailureFor("OpenAI Images API", response, responseText);
        }

        return await ExtractImageBytesFromImagesResponseAsync(responseText, cancellationToken);
    }

    private List<(byte[] Bytes, string FileName, string ContentType)> CollectReferenceImages(
        StoryImageReference? reference)
    {
        var images = new List<(byte[] Bytes, string FileName, string ContentType)>();
        if (reference is null)
        {
            return images;
        }

        if (reference.CharacterAnchorBytes is { Length: > 0 } anchor)
        {
            var normalizedAnchor = referenceImageNormalizer.NormalizeForOpenAi(anchor, "image/webp");
            images.Add((normalizedAnchor.Bytes, "01-hero-anchor.png", normalizedAnchor.ContentType));
        }

        foreach (var cast in reference.CastPhotos)
        {
            if (cast.Bytes is not { Length: > 0 })
            {
                continue;
            }

            var slot = images.Count + 1;
            var slug = cast.IsHero ? "hero" : SanitizeFileSlug(cast.Name);
            var normalized = referenceImageNormalizer.NormalizeForOpenAi(cast.Bytes, cast.ContentType);
            images.Add((normalized.Bytes, $"{slot:D2}-{slug}-reference.png", normalized.ContentType));
        }

        return images;
    }

    private static string SanitizeFileSlug(string name)
    {
        var chars = name.Where(char.IsLetterOrDigit).Take(24).ToArray();
        return chars.Length > 0 ? new string(chars).ToLowerInvariant() : "cast";
    }

    private string ResolveGptImageEditModel()
    {
        if (IsGptImageModel(_options.ImageEditModel))
        {
            return _options.ImageEditModel;
        }

        if (IsGptImageModel(_options.ImageModel))
        {
            return _options.ImageModel;
        }

        return "gpt-image-1-mini";
    }

    private string ResolveImagesApiModel()
    {
        if (IsGptImageModel(_options.ImageModel) || IsDalleModel(_options.ImageModel))
        {
            return _options.ImageModel;
        }

        return "gpt-image-1-mini";
    }

    private static bool IsGptImageModel(string model) =>
        model.StartsWith("gpt-image", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// What to put in the request's <c>input_fidelity</c>, or null to leave the field out.
    ///
    /// Four separate reasons to send nothing, and they are not the same reason:
    ///
    /// - the operator asked for nothing (empty setting), which is the escape hatch for a model
    ///   this code has not been taught about yet;
    /// - the model is not a GPT Image model at all — DALL·E has never had the concept;
    /// - the model always reads its inputs at high fidelity and refuses to be told so. gpt-image-2
    ///   answers a request carrying this field with 400 invalid_input_fidelity_model, so omitting
    ///   it there is not a lost setting: it is the same behaviour, correctly requested;
    /// - the value is not one the API knows. "hgih" would be a 400 for a typo, and the request is
    ///   better off without it — a slightly worse picture beats a failed page.
    ///
    /// Internal rather than private so the suite can assert the decision itself: the multipart
    /// body proves the live route, and this proves the models that route never reaches.
    /// </summary>
    internal static string? ResolveImageInputFidelity(string model, string configured)
    {
        if (string.IsNullOrWhiteSpace(configured) ||
            !IsGptImageModel(model) ||
            AlwaysReadsInputsAtHighFidelity(model))
        {
            return null;
        }

        var value = configured.Trim().ToLowerInvariant();
        return value is "low" or "high" ? value : null;
    }

    /// <summary>
    /// Models that do the high-fidelity thing on their own and reject the instruction to do it.
    ///
    /// A prefix match on the family, because that is how the refusal is scoped: every gpt-image-2
    /// variant OpenAI has shipped behaves this way, and matching the exact name would let
    /// "gpt-image-2-mini" fail its first real book instead of its first test.
    /// </summary>
    private static bool AlwaysReadsInputsAtHighFidelity(string model) =>
        model.StartsWith("gpt-image-2", StringComparison.OrdinalIgnoreCase);

    private static bool IsDalleModel(string model) =>
        model.StartsWith("dall-e", StringComparison.OrdinalIgnoreCase);

    private static string MapGptImageQuality(string quality)
    {
        if (quality.Equals("hd", StringComparison.OrdinalIgnoreCase) ||
            quality.Equals("high", StringComparison.OrdinalIgnoreCase))
        {
            return "high";
        }

        if (quality.Equals("medium", StringComparison.OrdinalIgnoreCase))
        {
            return "medium";
        }

        return "low";
    }

    private static string MapDalleQuality(string quality) =>
        quality.Equals("hd", StringComparison.OrdinalIgnoreCase) ? "hd" : "standard";

    private static string ToDataUrl(byte[] bytes, string contentType) =>
        $"data:{contentType};base64,{Convert.ToBase64String(bytes)}";

    private static async Task<byte[]> ExtractImageBytesFromImagesResponseAsync(
        string responseJson,
        CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var data = doc.RootElement.GetProperty("data");
        if (data.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("OpenAI Images API response contained no data.");
        }

        var item = data[0];
        if (item.TryGetProperty("b64_json", out var b64Prop))
        {
            var b64 = b64Prop.GetString();
            if (!string.IsNullOrWhiteSpace(b64))
            {
                return Convert.FromBase64String(b64);
            }
        }

        if (item.TryGetProperty("url", out var urlProp))
        {
            var url = urlProp.GetString();
            if (!string.IsNullOrWhiteSpace(url))
            {
                using var http = new HttpClient();
                return await http.GetByteArrayAsync(url, cancellationToken);
            }
        }

        throw new InvalidOperationException("OpenAI Images API response missing image data.");
    }

    /// <summary>
    /// The named client the image routes post through. Its own registration because it has its
    /// own timeout — see <see cref="OpenAiOptions.ImageTimeoutMinutes"/> — while the text calls
    /// keep the longer one a reasoning model needs.
    /// </summary>
    public const string ImageHttpClientName = "OpenAI.Images";

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient("OpenAI");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        return client;
    }

    private HttpClient CreateImageClient()
    {
        var client = httpClientFactory.CreateClient(ImageHttpClientName);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        return client;
    }

    private static byte[]? ExtractImageGenerationResult(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        if (!doc.RootElement.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var typeProp))
            {
                continue;
            }

            var type = typeProp.GetString();
            if (!string.Equals(type, "image_generation_call", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (item.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.String)
            {
                var b64 = result.GetString();
                if (!string.IsNullOrWhiteSpace(b64))
                {
                    return Convert.FromBase64String(b64);
                }
            }
        }

        return null;
    }

    private static string ExtractOutputText(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        if (root.TryGetProperty("output_text", out var outputTextElement) && outputTextElement.ValueKind == JsonValueKind.String)
        {
            return outputTextElement.GetString() ?? string.Empty;
        }

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var segment in content.EnumerateArray())
            {
                if (segment.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    return text.GetString() ?? string.Empty;
                }
            }
        }

        return string.Empty;
    }
}
