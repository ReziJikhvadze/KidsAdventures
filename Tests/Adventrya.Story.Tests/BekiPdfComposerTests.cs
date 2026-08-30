using System.IO.Compression;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Prompts;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
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
    public void Every_spread_the_cover_and_the_ending_reach_the_pdf()
    {
        var plan = SyntheticPlan();
        var spreads = plan.Spreads
            .Select(spread => new BekiSpreadArtwork(spread.Number, PixelPng()))
            .ToList();

        var pdf = Compose().Compose(plan, PixelPng(), spreads, BekiLayoutFixture.Personalization());

        // Cover, the front-matter spread, the intro spread, one page per story spread, the
        // credits spread, the rear endpaper spread and the back cover — six fixed pages around
        // the spreads. The spread is one page, not two: the fold is the printer's to impose,
        // and splitting it here would put a seam through every picture.
        Assert.Equal(BookFormat.SpreadCount + 6, CountPages(pdf));
    }

    /// <summary>
    /// The cover and the closing leaf are single pages — half a spread wide — and every page
    /// shares one height. This is the shape of the physical book; a composer that quietly put
    /// the cover on a spread-wide sheet would print a book with a cover twice the size of its
    /// pages, and nothing else in the pipeline would notice.
    /// </summary>
    [Fact]
    public void Cover_and_ending_are_half_the_width_of_a_spread()
    {
        var layout = BekiLayoutFixture.ScreenProofLayout();
        var plan = SyntheticPlan();
        var spreads = plan.Spreads
            .Select(spread => new BekiSpreadArtwork(spread.Number, PixelPng()))
            .ToList();

        var pdf = Compose(layout).Compose(plan, PixelPng(), spreads, BekiLayoutFixture.Personalization());

        var sizes = MediaBoxSizes(pdf);
        var leafPt = MmToPt(layout.PageWidthMm + (layout.BleedMm * 2));
        var spreadPt = MmToPt(layout.SpreadWidthMm + (layout.BleedMm * 2));
        var heightPt = MmToPt(layout.SpreadHeightMm + (layout.BleedMm * 2));

        Assert.Contains(sizes, size => Math.Abs(size.Width - leafPt) < 1.5);
        Assert.Contains(sizes, size => Math.Abs(size.Width - spreadPt) < 1.5);
        Assert.All(sizes, size => Assert.True(Math.Abs(size.Height - heightPt) < 1.5));
    }

    /// <summary>
    /// The print interior is the twelve interior spreads and nothing else: no cover face, no
    /// back-cover face, and every page a full spread wide. The supplier's audit rejected the
    /// hybrid — two 230mm leaves bound into a 450mm interior — as the production deliverable,
    /// and this is the artifact that replaces it. The cover is a dieline wrap delivered
    /// separately, or not at all until the dieline exists.
    /// </summary>
    [Fact]
    public void The_print_interior_carries_no_cover_faces_and_only_spread_wide_pages()
    {
        var layout = BekiLayoutFixture.ScreenProofLayout();
        var plan = SyntheticPlan();
        var spreads = plan.Spreads
            .Select(spread => new BekiSpreadArtwork(spread.Number, PixelPng()))
            .ToList();

        var pdf = Compose(layout).ComposeInterior(plan, spreads, BekiLayoutFixture.Personalization());

        // Front endpaper, intro, eight story spreads, credits, rear endpaper — twelve, the
        // supplier config's interior.spread_count.
        Assert.Equal(BookFormat.SpreadCount + 4, CountPages(pdf));

        var sizes = MediaBoxSizes(pdf);
        var leafPt = MmToPt(layout.PageWidthMm + (layout.BleedMm * 2));
        var spreadPt = MmToPt(layout.SpreadWidthMm + (layout.BleedMm * 2));

        Assert.All(sizes, size => Assert.True(
            Math.Abs(size.Width - spreadPt) < 1.5,
            $"an interior page is {size.Width}pt wide, not the spread's {spreadPt}pt."));
        Assert.DoesNotContain(sizes, size => Math.Abs(size.Width - leafPt) < 1.5);
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

        var pdf = Compose().Compose(plan, PixelPng(), spreads, BekiLayoutFixture.Personalization());

        Assert.Equal(BookFormat.SpreadCount + 7, CountPages(pdf));
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

        var pdf = composer.Compose(plan, cover, spreads, BekiLayoutFixture.Personalization());
        var outputPath = Path.Combine(BookDirectory!, "beki-book.pdf");
        await File.WriteAllBytesAsync(outputPath, pdf);

        // And the same pages as images, because a PDF cannot be looked at by anything here.
        var pageDirectory = Path.Combine(BookDirectory!, "pdf-pages");
        Directory.CreateDirectory(pageDirectory);

        var pages = composer.RenderPages(plan, cover, spreads, BekiLayoutFixture.Personalization());
        for (var index = 0; index < pages.Count; index++)
        {
            await File.WriteAllBytesAsync(
                Path.Combine(pageDirectory, $"page-{index + 1:00}.png"), pages[index]);
        }

        output.WriteLine($"{plan.Concept.Title}: {CountPages(pdf)} pages → {outputPath}");
        output.WriteLine($"page images → {pageDirectory}");
        Assert.Equal(spreads.Count + 6, CountPages(pdf));
    }

    /// <summary>
    /// The outlined blocks are vector text again — nine real runs, not a picture of them.
    ///
    /// This asserts the OPPOSITE of what it used to. The nine-copy stack was once rastered to a
    /// PNG with one invisible run over it, so <c>pdftotext</c> said each line once; the
    /// supplier's preflight then rejected exactly that raster ("a raster title-effect image is
    /// placed underneath" the vector title), because a printed glyph should be the RIP's own
    /// edge. So the stack ships as text runs, the operator count rises with the outline on, and
    /// the nine-fold extraction of the two outlined lines is the recorded, accepted cost.
    /// </summary>
    [Fact]
    public void The_outline_is_vector_text_runs_not_a_raster()
    {
        var plan = SyntheticPlan();
        var spreads = plan.Spreads
            .Select(spread => new BekiSpreadArtwork(spread.Number, PixelPng()))
            .ToList();

        var withoutOutline = Compose(NoOutline())
            .Compose(plan, PixelPng(), spreads, BekiLayoutFixture.Personalization());
        var withOutline = Compose()
            .Compose(plan, PixelPng(), spreads, BekiLayoutFixture.Personalization());

        var floor = TextShowOperators(withoutOutline);

        // Guards the guard: if QuestPDF ever stops deflating its content streams there is nothing
        // to count, and a test that silently compares zero to zero is worse than no test.
        Assert.True(floor > 0, "No readable text operators were found; the counter needs revisiting.");

        // Strictly more operators with the outline on: the rim is drawn as text. If this ever
        // equals the floor again, the visible glyphs have gone back to being pixels somewhere.
        Assert.True(
            TextShowOperators(withOutline) > floor,
            "The outlined blocks drew no extra text runs — the outline is a raster again.");
    }

    /// <summary>
    /// The composer no longer takes an asset path from configuration at all.
    ///
    /// It used to: <c>IntroAssetPathTemplate</c> and <c>EndpaperAssetPathTemplate</c> named files,
    /// and a path that pointed at nothing fell back to a drawn placeholder — "a mistyped setting
    /// should cost a page its art, never the order its book". That was the wrong trade, and it is
    /// what shipped: the approved endpaper pattern sat unwired in the asset tree while books printed
    /// a code-drawn dot field, and nothing anywhere could tell the difference. The fixed pages now
    /// come from <see cref="AdventurePacks.Api.Services.Story.BekiLayoutAssets"/>, hash-verified,
    /// with no path to configure and no drawing to fall back to —
    /// <c>BekiLayoutAssetTests</c> and <c>BekiFixedPageLayoutTests</c> own that behaviour now.
    /// </summary>
    [Fact]
    public void The_layout_options_carry_no_asset_paths_to_fall_back_from()
    {
        var settable = typeof(BekiPrintLayoutOptions)
            .GetProperties()
            .Where(property => property.CanWrite)
            .Select(property => property.Name)
            .ToList();

        Assert.DoesNotContain("IntroAssetPathTemplate", settable);
        Assert.DoesNotContain("EndpaperAssetPathTemplate", settable);
        Assert.DoesNotContain("BackCoverAssetPath", settable);
    }

    /// <summary>
    /// A resumed fulfilment job adopts spreads a dead attempt already drew. It may only do that
    /// while the rules those spreads were drawn under are still the rules in force: the text side,
    /// the shot, and which Beki the illustrator was shown. All three live in code, so a deploy
    /// between two attempts can move any of them, and a manifest that survived such a deploy would
    /// hand the finished book a mixture — a close-up where the rhythm now asks for a wide shot, or
    /// the retired lamb design sharing a book with the leaf spirit.
    ///
    /// Nothing here pins the actual wording of a shot or the current Beki version; both are
    /// expected to change, and a test that froze them would fail the moment they did for the right
    /// reasons. What is pinned is that changing either invalidates a manifest.
    /// </summary>
    [Fact]
    public void A_manifest_drawn_under_different_illustration_rules_is_rejected()
    {
        var current = BekiFulfillmentManifest.CurrentContract(BookFormat.SpreadCount);

        Assert.Equal(BookFormat.SpreadCount, current.Count);
        Assert.True(current.SequenceEqual(
            BekiFulfillmentManifest.CurrentContract(BookFormat.SpreadCount)));

        // All three terms are actually in there.
        Assert.Contains(BekiSpreadRhythm.TextSideFor(1), current[0]);
        Assert.Contains(BekiSpreadRhythm.ShotFor(1), current[0]);
        Assert.All(current, line => Assert.Contains(BekiIdentity.Version, line));

        var reshot = current.ToArray();
        reshot[3] = reshot[3].Replace(
            BekiSpreadRhythm.ShotFor(4), "Come in close on the character's face.");
        Assert.False(reshot.SequenceEqual(current));

        var redesigned = current
            .Select(line => line.Replace(BekiIdentity.Version, "beki-canonical-v1"))
            .ToArray();
        Assert.False(redesigned.SequenceEqual(current));

        // And the old snapshot — text sides and nothing else — no longer satisfies the contract,
        // so every manifest written before this change is discarded rather than half-trusted.
        var sidesOnly = Enumerable.Range(1, BookFormat.SpreadCount)
            .Select(BekiSpreadRhythm.TextSideFor)
            .ToArray();
        Assert.False(sidesOnly.SequenceEqual(current));
    }

    private static BekiPdfComposer Compose(BekiPrintLayoutOptions? layout = null) =>
        new(Options.Create(layout ?? BekiLayoutFixture.ScreenProofLayout()));

    /// <summary>The same book with the cover title's faux outline turned off.</summary>
    private static BekiPrintLayoutOptions NoOutline()
    {
        var layout = BekiLayoutFixture.ScreenProofLayout();
        layout.TextOutlineWidth = 0f;
        return layout;
    }

    /// <summary>
    /// How many times the document says "draw these glyphs".
    ///
    /// A PDF keeps its drawing instructions in deflated streams, so counting them means inflating
    /// every stream that will inflate and looking for the two text-showing operators. Streams that
    /// will not inflate are images and fonts and are skipped — nothing is being parsed here beyond
    /// "how many draw-text calls does this file contain", which is the only question being asked,
    /// and asking it this way keeps a PDF-reading library out of the test project.
    /// </summary>
    private static int TextShowOperators(byte[] pdf)
    {
        var text = System.Text.Encoding.Latin1.GetString(pdf);
        var total = 0;

        foreach (System.Text.RegularExpressions.Match match in
                 System.Text.RegularExpressions.Regex.Matches(text, @"stream\r?\n"))
        {
            var start = match.Index + match.Length;
            var end = text.IndexOf("endstream", start, StringComparison.Ordinal);
            if (end < 0) continue;

            var deflated = System.Text.Encoding.Latin1.GetBytes(text[start..end]);

            string inflated;
            try
            {
                using var source = new MemoryStream(deflated);
                using var zlib = new ZLibStream(source, CompressionMode.Decompress);
                using var target = new MemoryStream();
                zlib.CopyTo(target);
                inflated = System.Text.Encoding.Latin1.GetString(target.ToArray());
            }
            catch (InvalidDataException)
            {
                continue;
            }

            total += System.Text.RegularExpressions.Regex.Matches(inflated, @"\b(TJ|Tj)\b").Count;
        }

        return total;
    }

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

    private static float MmToPt(float mm) => mm / 25.4f * 72f;

    /// <summary>Every distinct page size the PDF declares, read straight from its MediaBoxes.</summary>
    private static IReadOnlyList<(float Width, float Height)> MediaBoxSizes(byte[] pdf)
    {
        var text = System.Text.Encoding.Latin1.GetString(pdf);
        return System.Text.RegularExpressions.Regex
            .Matches(text, @"/MediaBox\s*\[\s*0\s+0\s+([\d.]+)\s+([\d.]+)\s*\]")
            .Select(match => (
                float.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                float.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)))
            .Distinct()
            .ToList();
    }
}
