using System.Text;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services;
using AdventurePacks.Api.Services.Implementations;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Prompts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Adventrya.Story.Tests;

/// <summary>
/// The Beki vertical slice, end to end, once: PLAN BOOK → spread → QA → the same character again
/// on the strength of the first accepted image → QA.
///
/// A live test rather than a wiring-up, because the question this phase asks is not "does the
/// pipeline hold together" but "what does the QA model actually say, and what does a spread cost".
/// Nothing here is reachable from the running product: no controller, no job, no registration.
/// Deleting this file removes the prototype entirely.
///
/// Skipped unless ADVENTRYA_OPENAI_KEY is set, like the other live tests, because it spends money.
/// ADVENTRYA_CHILD_PHOTO may point at a real portrait; without one the slice still runs and still
/// measures character continuity and QA, but says nothing about likeness.
/// </summary>
public class LiveBekiSliceTests(ITestOutputHelper output)
{
    private static string? ApiKey => Environment.GetEnvironmentVariable("ADVENTRYA_OPENAI_KEY");
    private static string? ChildPhotoPath => Environment.GetEnvironmentVariable("ADVENTRYA_CHILD_PHOTO");

    /// <summary>Landscape for this phase. The 2.2:1 spread is a later decision.</summary>
    private const string PrototypeImageSize = "1536x1024";

    /// <summary>v0 measures one regeneration, not two. Cost first, then a retry budget.</summary>
    private const int MaxSemanticRegenerations = 1;

    [SkippableFact]
    public async Task Plan_a_book_then_draw_two_spreads_sharing_a_character()
    {
        Skip.If(string.IsNullOrWhiteSpace(ApiKey), "Set ADVENTRYA_OPENAI_KEY to run this.");

        var report = new StringBuilder();
        var outputDirectory = Path.Combine(Path.GetTempPath(), "beki-slice");
        Directory.CreateDirectory(outputDirectory);

        var openAi = BuildOpenAiService();
        var childPhoto = await LoadChildPhotoAsync();

        /*
          The child's appearance is read from the photograph, not typed in here.

          Production does exactly this before writing a book, and doing it any other way makes the
          identity measurement meaningless: a hand-written description would be my reading of the
          face rather than the pipeline's, and the whole question this run asks is whether the
          pipeline's reading survives being drawn.
        */
        string? appearance = null;
        if (childPhoto is { } photo)
        {
            appearance = await openAi.DescribeCharacterFromPhotoAsync(
                photo.Bytes,
                photo.ContentType,
                MasterStoryPrompt.PhotoDescribe,
                CancellationToken.None);

            report.AppendLine("## Appearance, read from the photograph");
            report.AppendLine(appearance);
            report.AppendLine();
        }

        var input = new MasterStoryInput
        {
            ChildName = "ნინი",
            Age = 4,
            Gender = "girl",
            Theme = ThemeType.Dinosaurs,
            EyeColor = "brown",
            ExtraWishes = "loves flying dinosaurs",
            AppearanceDescription = appearance,
            SpreadCount = BookFormat.SpreadCount,
            Language = "ka",
        };

        // ---- A. PLAN BOOK -------------------------------------------------
        var storyClient = BuildStoryClient();
        var planStarted = DateTime.UtcNow;

        var planResult = await storyClient.CompleteAsync<MasterStory>(
            "gpt-5.6-sol",
            MasterStoryPromptV5.System(input),
            MasterStoryPromptV5.User(input),
            BekiBookPlanSchema.Name,
            BekiBookPlanSchema.Build(input.SpreadCount),
            CancellationToken.None);

        var plan = planResult.Value;
        var planSeconds = (DateTime.UtcNow - planStarted).TotalSeconds;

        var planJson = JsonSerializer.Serialize(plan, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        });
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "book-plan.json"), planJson);

        report.AppendLine("## PLAN BOOK");
        report.AppendLine($"{planSeconds:0}s · {planResult.PromptTokens} + {planResult.CompletionTokens} tokens");
        report.AppendLine($"title_ka: {plan.Concept.Title}");
        report.AppendLine($"title_en: {plan.TitleEn}");
        report.AppendLine($"cast: {plan.Cast?.Count ?? 0}");
        foreach (var member in plan.Cast ?? [])
        {
            report.AppendLine($"  {member.Id} · {member.Name} — {member.VisualDescription}");
        }

        Assert.Equal(input.SpreadCount, plan.Spreads.Count);
        Assert.False(string.IsNullOrWhiteSpace(plan.TitleEn));
        Assert.All(plan.Spreads, spread => Assert.False(string.IsNullOrWhiteSpace(spread.TextEn)));

        // ---- Pick the two spreads the slice is about ----------------------
        // The first spread carrying a cast member, and the next one carrying the same member. If
        // the story invented nobody, there is no continuity to measure and the run says so rather
        // than quietly drawing two unrelated pictures.
        var ordered = plan.Spreads.OrderBy(s => s.Number).ToList();
        var (first, second, characterId) = FindSharedCharacterSpreads(ordered);

        Skip.If(first is null, "The plan created no recurring character appearing in two spreads.");

        var cast = plan.Cast!.Single(c => c.Id == characterId);
        report.AppendLine();
        report.AppendLine($"shared character: {characterId} ({cast.Name})");
        report.AppendLine($"spreads: {first!.Number} then {second!.Number}");

        // ---- B + C. Two spreads, the second anchored on the first ---------
        var anchors = new Dictionary<string, byte[]>();

        var firstResult = await DrawAndReviewAsync(
            openAi, plan, first, cast, anchors, childPhoto, outputDirectory, report);

        // The handoff's continuity rule: the first accepted image becomes the anchor.
        if (firstResult.Accepted)
        {
            anchors[characterId] = firstResult.Image;
        }

        var secondResult = await DrawAndReviewAsync(
            openAi, plan, second, cast, anchors, childPhoto, outputDirectory, report);

        report.AppendLine();
        report.AppendLine($"images: {outputDirectory}");

        var text = report.ToString();
        output.WriteLine(text);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "report.md"), text);

        Assert.True(firstResult.Image.Length > 0);
        Assert.True(secondResult.Image.Length > 0);
    }

    /// <summary>
    /// One illustration: compose, draw, review, and on a failed review redraw once with the QA
    /// note appended. The handoff's rule is that a retry keeps the original prompt and adds only
    /// the correction, so the prompt is rebuilt rather than rewritten.
    /// </summary>
    private async Task<(byte[] Image, bool Accepted, string Verdict)> DrawAndReviewAsync(
        OpenAiService openAi,
        MasterStory plan,
        StorySpread spread,
        StoryCastMember cast,
        Dictionary<string, byte[]> anchors,
        (byte[] Bytes, string ContentType)? childPhoto,
        string outputDirectory,
        StringBuilder report)
    {
        var textSide = BekiSpreadRhythm.TextSideFor(spread.Number);
        var shot = BekiSpreadRhythm.ShotFor(spread.Number);
        var hasAnchor = anchors.TryGetValue(cast.Id, out var anchorBytes);

        var continuity = hasAnchor
            ? "Keep the supporting character identical to its appearance in the provided continuity reference."
            : $"Include {cast.VisualDescription}";

        var prompt = IllustrationPrompt.ComposeBeki(
            plan.CharacterLock,
            spread.Illustration.Scene,
            continuity,
            textSide,
            shot,
            spread.Illustration.Avoid);

        var reference = BuildReference(childPhoto, hasAnchor ? anchorBytes : null);

        report.AppendLine();
        report.AppendLine($"### Spread {spread.Number} — text {textSide}, anchor: {(hasAnchor ? "yes" : "no")}");
        report.AppendLine("```");
        report.AppendLine(prompt);
        report.AppendLine("```");

        var image = await openAi.GenerateStoryImageAsync(
            prompt, reference, CancellationToken.None, PrototypeImageSize);

        var verdict = await ReviewAsync(openAi, image, spread, textSide, childPhoto, hasAnchor ? anchorBytes : null);
        var accepted = IsPass(verdict);
        report.AppendLine($"QA: {verdict}");

        await File.WriteAllBytesAsync(
            Path.Combine(outputDirectory, $"spread-{spread.Number:00}-attempt-1.png"), image);

        for (var regeneration = 1; !accepted && regeneration <= MaxSemanticRegenerations; regeneration++)
        {
            report.AppendLine($"regenerating (attempt {regeneration + 1}) with the QA note appended");

            var corrected = $"{prompt}\n\nCorrect these problems from the previous attempt: {verdict}";
            image = await openAi.GenerateStoryImageAsync(
                corrected, reference, CancellationToken.None, PrototypeImageSize);

            verdict = await ReviewAsync(openAi, image, spread, textSide, childPhoto, hasAnchor ? anchorBytes : null);
            accepted = IsPass(verdict);
            report.AppendLine($"QA: {verdict}");

            await File.WriteAllBytesAsync(
                Path.Combine(outputDirectory, $"spread-{spread.Number:00}-attempt-{regeneration + 1}.png"), image);
        }

        if (!accepted)
        {
            report.AppendLine("NEEDS_REVIEW — shipped unaccepted for measurement.");
        }

        return (image, accepted, verdict);
    }

    private static Task<string> ReviewAsync(
        OpenAiService openAi,
        byte[] image,
        StorySpread spread,
        string textSide,
        (byte[] Bytes, string ContentType)? childPhoto,
        byte[]? anchor)
    {
        var references = new List<(byte[], string, string)>();
        if (childPhoto is { } photo)
        {
            references.Add((photo.Bytes, photo.ContentType, "Child reference photograph"));
        }

        if (anchor is not null)
        {
            references.Add((anchor, "image/png", "Continuity reference for the supporting character"));
        }

        return openAi.ReviewIllustrationAsync(
            image,
            BekiImageQaPrompt.For(spread.Illustration.Scene, textSide),
            references,
            CancellationToken.None);
    }

    /// <summary>
    /// PASS or FAIL, read from the model's JSON. Deliberately forgiving about the wrapper — a
    /// verdict arriving inside a code fence is still a verdict — and deliberately strict about
    /// the answer: anything that is not recognisably a pass counts as a failure.
    /// </summary>
    private static bool IsPass(string verdict) =>
        verdict.Contains("\"PASS\"", StringComparison.OrdinalIgnoreCase);

    private static (StorySpread? First, StorySpread? Second, string CharacterId) FindSharedCharacterSpreads(
        IReadOnlyList<StorySpread> spreads)
    {
        foreach (var spread in spreads)
        {
            foreach (var id in spread.Characters ?? [])
            {
                if (id.Equals("child", StringComparison.OrdinalIgnoreCase)) continue;

                var later = spreads.FirstOrDefault(candidate =>
                    candidate.Number > spread.Number
                    && (candidate.Characters ?? []).Contains(id, StringComparer.OrdinalIgnoreCase));

                if (later is not null) return (spread, later, id);
            }
        }

        return (null, null, string.Empty);
    }

    private static StoryImageReference? BuildReference(
        (byte[] Bytes, string ContentType)? childPhoto,
        byte[]? anchor)
    {
        var photos = childPhoto is { } photo
            ? new List<CastPhotoReference>
            {
                new()
                {
                    Name = "child",
                    Relationship = "hero",
                    Bytes = photo.Bytes,
                    ContentType = photo.ContentType,
                },
            }
            : [];

        if (photos.Count == 0 && anchor is null) return null;

        return new StoryImageReference { CharacterAnchorBytes = anchor, CastPhotos = photos };
    }

    private static async Task<(byte[] Bytes, string ContentType)?> LoadChildPhotoAsync()
    {
        if (string.IsNullOrWhiteSpace(ChildPhotoPath) || !File.Exists(ChildPhotoPath)) return null;

        var bytes = await File.ReadAllBytesAsync(ChildPhotoPath);
        var contentType = Path.GetExtension(ChildPhotoPath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg",
        };

        return (bytes, contentType);
    }

    private static StoryModelClient BuildStoryClient()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        var provider = services.BuildServiceProvider();

        return new StoryModelClient(
            provider.GetRequiredService<IHttpClientFactory>(),
            Options.Create(new OpenAiOptions { ApiKey = ApiKey!, BaseUrl = "https://api.openai.com/v1" }),
            NullLogger<StoryModelClient>.Instance);
    }

    private static OpenAiService BuildOpenAiService()
    {
        var services = new ServiceCollection();

        // The named client, with its base address, exactly as the API registers it. OpenAiService
        // asks the factory for "OpenAI" and posts to relative paths; an unnamed client has no base
        // address and every call fails on the URI rather than on anything to do with the model.
        services.AddHttpClient("OpenAI", client =>
        {
            client.BaseAddress = new Uri("https://api.openai.com/v1/");
            client.Timeout = TimeSpan.FromMinutes(6);
        });

        var provider = services.BuildServiceProvider();

        // The prototype's own options object, never the deployed configuration: the size is passed
        // per call, and nothing here can move a production default.
        var options = new OpenAiOptions
        {
            ApiKey = ApiKey!,
            BaseUrl = "https://api.openai.com/v1",
            ImageGenerationProvider = "responses",
            EnableStoryImages = true,
            LogPrompts = false,
        };

        return new OpenAiService(
            provider.GetRequiredService<IHttpClientFactory>(),
            new ReferenceImageNormalizer(NullLogger<ReferenceImageNormalizer>.Instance),
            Options.Create(options),
            NullLogger<OpenAiService>.Instance);
    }
}
