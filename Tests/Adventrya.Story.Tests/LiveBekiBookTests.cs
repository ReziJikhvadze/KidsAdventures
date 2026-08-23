using System.Text;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Enums;
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
/// A whole Beki book, generated and written out so it can be looked at.
///
/// This is the local test rig for the format, not a check of it. It asserts only the things a
/// program can be sure of — nine images exist, the plan is bilingual, every spread was drawn —
/// because everything that actually matters about a children's book is a judgement a person makes
/// by opening it. The files it leaves behind are the real output.
///
/// It spends real money: one plan, nine illustrations, nine reviews, and one redraw for each
/// image the reviewer refuses. Skipped unless both ADVENTRYA_OPENAI_KEY and ADVENTRYA_CHILD_PHOTO
/// are set, so it can never run by accident in a suite.
/// </summary>
public class LiveBekiBookTests(ITestOutputHelper output)
{
    private static string? ApiKey => Environment.GetEnvironmentVariable("ADVENTRYA_OPENAI_KEY");
    private static string? ChildPhotoPath => Environment.GetEnvironmentVariable("ADVENTRYA_CHILD_PHOTO");

    [SkippableFact]
    public async Task Generate_a_whole_beki_book_and_write_it_out()
    {
        Skip.If(string.IsNullOrWhiteSpace(ApiKey), "Set ADVENTRYA_OPENAI_KEY to run this.");
        Skip.If(
            string.IsNullOrWhiteSpace(ChildPhotoPath) || !File.Exists(ChildPhotoPath),
            "Set ADVENTRYA_CHILD_PHOTO to a real portrait to run this.");

        var input = new MasterStoryInput
        {
            ChildName = Environment.GetEnvironmentVariable("ADVENTRYA_CHILD_NAME") ?? "ნინი",
            Age = int.TryParse(Environment.GetEnvironmentVariable("ADVENTRYA_CHILD_AGE"), out var age) ? age : 4,
            Gender = Environment.GetEnvironmentVariable("ADVENTRYA_CHILD_GENDER") ?? "girl",
            Theme = ThemeType.Dinosaurs,
            EyeColor = "brown",
            ExtraWishes = "loves flying dinosaurs",
            SpreadCount = BookFormat.SpreadCount,
            Language = "ka",
        };

        var photo = await File.ReadAllBytesAsync(ChildPhotoPath!);
        var contentType = Path.GetExtension(ChildPhotoPath!).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg",
        };

        var generator = BuildGenerator();
        var started = DateTime.UtcNow;

        var book = await generator.GenerateAsync(input, photo, contentType, CancellationToken.None);

        var elapsed = DateTime.UtcNow - started;
        var directory = Path.Combine(Path.GetTempPath(), "beki-book", DateTime.Now.ToString("HHmm"));
        Directory.CreateDirectory(directory);

        await WriteBookAsync(book, input, elapsed, directory);

        output.WriteLine($"{book.Plan.Concept.Title} — {elapsed.TotalMinutes:0.0} min");
        output.WriteLine($"accepted: {book.Spreads.Count(s => s.Accepted)}/{book.Spreads.Count} spreads, cover {(book.Cover.Accepted ? "yes" : "no")}");
        foreach (var warning in book.Warnings) output.WriteLine($"warning: {warning}");
        output.WriteLine($"\nWritten to {directory}");

        Assert.Equal(BookFormat.SpreadCount, book.Spreads.Count);
        Assert.All(book.Spreads, spread => Assert.NotEmpty(spread.Image));
        Assert.NotEmpty(book.Cover.Image);
        Assert.False(string.IsNullOrWhiteSpace(book.Plan.TitleEn));
        Assert.All(book.Plan.Spreads, spread => Assert.False(string.IsNullOrWhiteSpace(spread.TextEn)));
    }

    private static async Task WriteBookAsync(
        BekiBookResult book, MasterStoryInput input, TimeSpan elapsed, string directory)
    {
        await File.WriteAllBytesAsync(Path.Combine(directory, "00-cover.png"), book.Cover.Image);
        foreach (var spread in book.Spreads)
        {
            await File.WriteAllBytesAsync(
                Path.Combine(directory, $"{spread.SpreadNumber:00}-spread.png"), spread.Image);
        }

        await File.WriteAllTextAsync(
            Path.Combine(directory, "book-plan.json"),
            JsonSerializer.Serialize(book.Plan, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true,
            }));

        var report = new StringBuilder();
        report.AppendLine($"# {book.Plan.Concept.Title}");
        report.AppendLine($"*{book.Plan.TitleEn}*");
        report.AppendLine();
        report.AppendLine($"{input.ChildName}, {input.Age}, {input.Gender} · {elapsed.TotalMinutes:0.0} min");
        report.AppendLine($"accepted: {book.Spreads.Count(s => s.Accepted)}/{book.Spreads.Count} spreads; "
            + $"cover {(book.Cover.Accepted ? "PASS" : "NEEDS_REVIEW")}");
        report.AppendLine();

        report.AppendLine("## Appearance, read from the photograph");
        report.AppendLine(book.AppearanceDescription);
        report.AppendLine();

        if (book.Plan.Cast is { Count: > 0 })
        {
            report.AppendLine("## Cast");
            foreach (var member in book.Plan.Cast)
            {
                report.AppendLine($"- **{member.Id} · {member.Name}** — {member.VisualDescription}");
            }

            report.AppendLine();
        }

        if (book.Warnings.Count > 0)
        {
            report.AppendLine("## Warnings");
            foreach (var warning in book.Warnings) report.AppendLine($"- {warning}");
            report.AppendLine();
        }

        AppendImage(report, "Cover", book.Cover);
        foreach (var (spread, plan) in book.Spreads.Zip(book.Plan.Spreads.OrderBy(s => s.Number)))
        {
            AppendImage(report, $"Spread {spread.SpreadNumber}", spread, plan);
        }

        await File.WriteAllTextAsync(Path.Combine(directory, "report.md"), report.ToString());
    }

    private static void AppendImage(
        StringBuilder report, string heading, BekiImageResult image, StorySpread? spread = null)
    {
        report.AppendLine($"## {heading}");
        report.AppendLine($"{(image.Accepted ? "PASS" : "NEEDS_REVIEW")} after {image.Attempts} attempt(s)"
            + (image.AnchoredCharacters.Count > 0
                ? $" · anchored: {string.Join(", ", image.AnchoredCharacters)}"
                : string.Empty));
        report.AppendLine();

        if (spread is not null)
        {
            report.AppendLine($"**ქართული:** {spread.Text}");
            report.AppendLine();
            report.AppendLine($"**English:** {spread.TextEn}");
            report.AppendLine();
        }

        report.AppendLine("<details><summary>prompt</summary>");
        report.AppendLine();
        report.AppendLine("```");
        report.AppendLine(image.Prompt);
        report.AppendLine("```");
        report.AppendLine("</details>");
        report.AppendLine();
        report.AppendLine($"QA: `{image.Verdict}`");
        report.AppendLine();
    }

    private static BekiBookGenerator BuildGenerator()
    {
        var services = new ServiceCollection();
        services.AddHttpClient("OpenAI", client =>
        {
            client.BaseAddress = new Uri("https://api.openai.com/v1/");
            client.Timeout = TimeSpan.FromMinutes(6);
        });
        services.AddHttpClient();

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        var options = Options.Create(new OpenAiOptions
        {
            ApiKey = ApiKey!,
            BaseUrl = "https://api.openai.com/v1",
            EnableStoryImages = true,
            LogPrompts = false,
        });

        var openAi = new OpenAiService(
            factory,
            new ReferenceImageNormalizer(NullLogger<ReferenceImageNormalizer>.Instance),
            options,
            NullLogger<OpenAiService>.Instance);

        var storyClient = new StoryModelClient(factory, options, NullLogger<StoryModelClient>.Instance);

        return new BekiBookGenerator(
            storyClient, openAi, Options.Create(new BekiPrintLayoutOptions()), Options.Create(new BekiOptions()), NullLogger<BekiBookGenerator>.Instance);
    }
}
