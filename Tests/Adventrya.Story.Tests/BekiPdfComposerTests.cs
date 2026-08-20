using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Adventrya.Story.Tests;

/// <summary>
/// The Beki book, set for print.
///
/// The count test runs everywhere and guards the thing a program can be sure of: a book that
/// loses a spread between generation and print loses it silently, and nobody notices until a
/// parent counts. The folder test is for looking — point it at a generated book and it lays one
/// out, which is the only way to find out whether Georgian actually sits over the artwork.
/// </summary>
public class BekiPdfComposerTests(ITestOutputHelper output)
{
    /// <summary>A folder written by LiveBekiBookTests: nine PNGs and a book-plan.json.</summary>
    private static string? BookDirectory => Environment.GetEnvironmentVariable("ADVENTRYA_BEKI_BOOK");

    [Fact]
    public void Every_spread_and_the_cover_reach_the_pdf()
    {
        var plan = SyntheticPlan();
        var spreads = plan.Spreads
            .Select(spread => new BekiSpreadArtwork(spread.Number, PixelPng()))
            .ToList();

        var pdf = Compose().Compose(plan, PixelPng(), spreads);

        // Cover plus one page per spread. The spread is one page, not two: the fold is the
        // printer's to impose, and splitting it here would put a seam through every picture.
        Assert.Equal(BookFormat.SpreadCount + 1, CountPages(pdf));
    }

    /// <summary>
    /// A spread whose words went missing is still printed as artwork rather than dropped. The
    /// alternative is a book that is quietly one page shorter than the story it was written from.
    /// </summary>
    [Fact]
    public void A_spread_with_no_matching_text_is_still_printed()
    {
        var plan = SyntheticPlan();
        var spreads = plan.Spreads
            .Select(spread => new BekiSpreadArtwork(spread.Number, PixelPng()))
            .Append(new BekiSpreadArtwork(99, PixelPng()))
            .ToList();

        var pdf = Compose().Compose(plan, PixelPng(), spreads);

        Assert.Equal(BookFormat.SpreadCount + 2, CountPages(pdf));
    }

    [SkippableFact]
    public async Task Lay_out_a_generated_book_for_inspection()
    {
        Skip.If(
            string.IsNullOrWhiteSpace(BookDirectory) || !Directory.Exists(BookDirectory),
            "Set ADVENTRYA_BEKI_BOOK to a folder written by LiveBekiBookTests.");

        var planJson = await File.ReadAllTextAsync(Path.Combine(BookDirectory!, "book-plan.json"));
        var plan = JsonSerializer.Deserialize<MasterStory>(
            planJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        var cover = await File.ReadAllBytesAsync(Path.Combine(BookDirectory!, "00-cover.png"));

        var spreads = new List<BekiSpreadArtwork>();
        foreach (var spread in plan.Spreads.OrderBy(s => s.Number))
        {
            var path = Path.Combine(BookDirectory!, $"{spread.Number:00}-spread.png");
            if (File.Exists(path))
            {
                spreads.Add(new BekiSpreadArtwork(spread.Number, await File.ReadAllBytesAsync(path)));
            }
        }

        var composer = Compose();

        var pdf = composer.Compose(plan, cover, spreads);
        var outputPath = Path.Combine(BookDirectory!, "beki-book.pdf");
        await File.WriteAllBytesAsync(outputPath, pdf);

        // And the same pages as images, because a PDF cannot be looked at by anything here.
        var pageDirectory = Path.Combine(BookDirectory!, "pdf-pages");
        Directory.CreateDirectory(pageDirectory);

        var pages = composer.RenderPages(plan, cover, spreads);
        for (var index = 0; index < pages.Count; index++)
        {
            await File.WriteAllBytesAsync(
                Path.Combine(pageDirectory, $"page-{index + 1:00}.png"), pages[index]);
        }

        output.WriteLine($"{plan.Concept.Title}: {CountPages(pdf)} pages → {outputPath}");
        output.WriteLine($"page images → {pageDirectory}");
        Assert.Equal(spreads.Count + 1, CountPages(pdf));
    }

    private static BekiPdfComposer Compose(BekiPrintLayoutOptions? layout = null) =>
        new(Options.Create(layout ?? new BekiPrintLayoutOptions()));

    private static MasterStory SyntheticPlan() => new()
    {
        Concept = new StoryConcept { Title = "ტესტი", Outline = ["a", "b"] },
        CharacterLock = "A child.",
        Cover = new IllustrationBrief { Scene = "cover" },
        TitleEn = "Test",
        Spreads = Enumerable.Range(1, BookFormat.SpreadCount)
            .Select(number => new StorySpread
            {
                Number = number,
                Title = string.Empty,
                Caption = string.Empty,
                Text = $"ქართული ტექსტი {number}.",
                TextEn = $"English text {number}.",
                Illustration = new IllustrationBrief { Scene = $"scene {number}" },
                Characters = ["child"],
            })
            .ToList(),
    };

    /// <summary>Smallest valid PNG: the layout is what is being tested, not the artwork.</summary>
    private static byte[] PixelPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static int CountPages(byte[] pdf)
    {
        var text = System.Text.Encoding.Latin1.GetString(pdf);
        return System.Text.RegularExpressions.Regex.Matches(text, @"/Type\s*/Page[^s]").Count;
    }
}
