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

        var pdf = Compose().Compose(plan, PixelPng(), spreads);

        // Cover, front endpaper, the P1 invitation, one page per spread, the P18 closing leaf,
        // back endpaper and back cover — six fixed pages around the spreads. The spread is one
        // page, not two: the fold is the printer's to impose, and splitting it here would put a
        // seam through every picture.
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
        var layout = new BekiPrintLayoutOptions();
        var plan = SyntheticPlan();
        var spreads = plan.Spreads
            .Select(spread => new BekiSpreadArtwork(spread.Number, PixelPng()))
            .ToList();

        var pdf = Compose(layout).Compose(plan, PixelPng(), spreads);

        var sizes = MediaBoxSizes(pdf);
        var leafPt = MmToPt(layout.PageWidthMm + (layout.BleedMm * 2));
        var spreadPt = MmToPt(layout.SpreadWidthMm + (layout.BleedMm * 2));
        var heightPt = MmToPt(layout.SpreadHeightMm + (layout.BleedMm * 2));

        Assert.Contains(sizes, size => Math.Abs(size.Width - leafPt) < 1.5);
        Assert.Contains(sizes, size => Math.Abs(size.Width - spreadPt) < 1.5);
        Assert.All(sizes, size => Assert.True(Math.Abs(size.Height - heightPt) < 1.5));
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
        Assert.Equal(spreads.Count + 6, CountPages(pdf));
    }

    /// <summary>
    /// The outline used to cost the document eight extra copies of every paragraph: the faux
    /// stroke was eight offset text runs under the fill, and all nine were real text, so every
    /// sentence of every book came back nine times from <c>pdftotext</c> — and from anything else
    /// that reads a PDF for words, a screen reader included. The outline is now rastered once and
    /// exactly one invisible text run is laid over it.
    ///
    /// Which makes this checkable without a PDF library: turn the outline off and the composer
    /// draws one plain text run per block by construction, so that book's text-operator count is
    /// the arithmetic floor. Turn it back on and the count must not move. Before the fix it was
    /// roughly nine times higher.
    /// </summary>
    [Fact]
    public void The_outline_no_longer_multiplies_the_text_in_the_document()
    {
        var plan = SyntheticPlan();
        var spreads = plan.Spreads
            .Select(spread => new BekiSpreadArtwork(spread.Number, PixelPng()))
            .ToList();

        var withoutOutline = Compose(new BekiPrintLayoutOptions { TextOutlineWidth = 0f })
            .Compose(plan, PixelPng(), spreads);
        var withOutline = Compose(new BekiPrintLayoutOptions())
            .Compose(plan, PixelPng(), spreads);

        var floor = TextShowOperators(withoutOutline);

        // Guards the guard: if QuestPDF ever stops deflating its content streams there is nothing
        // to count, and a test that silently compares zero to zero is worse than no test.
        Assert.True(floor > 0, "No readable text operators were found; the counter needs revisiting.");
        Assert.Equal(floor, TextShowOperators(withOutline));
    }

    /// <summary>
    /// The six reusable pages are placeholders until the partner delivers real art, and this is
    /// the delivery door: configure a path, and that page prints the file instead of the drawing.
    /// Both halves matter — the file being used when it is there, and the drawing coming back when
    /// it is not — because a mistyped path in configuration must cost a page its art, never the
    /// order its book.
    ///
    /// The endpaper goes through the themed template rather than a plain path, since that is the
    /// one reusable page whose art is allowed to differ per book.
    /// </summary>
    [Fact]
    public void A_supplied_asset_replaces_a_reusable_page_and_a_missing_one_does_not()
    {
        var plan = SyntheticPlan();
        var spreads = plan.Spreads
            .Select(spread => new BekiSpreadArtwork(spread.Number, PixelPng()))
            .ToList();

        var folder = Directory.CreateTempSubdirectory("beki-assets-").FullName;

        try
        {
            var layout = new BekiPrintLayoutOptions
            {
                InvitationAssetPath = Path.Combine(folder, "invitation.png"),
                EndpaperAssetPathTemplate = Path.Combine(folder, "endpaper-{theme}.png"),
            };

            // Nothing on disk yet: both pages fall back to what the composer draws itself.
            var drawn = Compose(layout).RenderPages(plan, PixelPng(), spreads, "Space");

            Assert.Equal(BookFormat.SpreadCount + 6, drawn.Count);
            Assert.NotEqual(Magenta, CentrePixel(drawn[InvitationPage]));
            Assert.NotEqual(Teal, CentrePixel(drawn[FrontEndpaperPage]));

            File.WriteAllBytes(layout.InvitationAssetPath!, SolidPng(Magenta));

            // Named for the theme the book is passed, lowercased — the composer resolves the
            // placeholder, so a file named for a different theme would not be found at all.
            File.WriteAllBytes(Path.Combine(folder, "endpaper-space.png"), SolidPng(Teal));

            var supplied = Compose(layout).RenderPages(plan, PixelPng(), spreads, "Space");

            Assert.Equal(drawn.Count, supplied.Count);
            Assert.Equal(Magenta, CentrePixel(supplied[InvitationPage]));
            Assert.Equal(Teal, CentrePixel(supplied[FrontEndpaperPage]));
            Assert.Equal(Teal, CentrePixel(supplied[BackEndpaperPage]));

            // Full bleed, not a picture on a page: the corner is the asset too, right out to where
            // the trim will fall.
            Assert.Equal(Magenta, CornerPixel(supplied[InvitationPage]));
            Assert.Equal(Teal, CornerPixel(supplied[BackEndpaperPage]));

            // A page with no asset configured is untouched by any of this.
            Assert.Equal(CentrePixel(drawn[BackCoverPage]), CentrePixel(supplied[BackCoverPage]));

            // And a book with a different theme finds no endpaper of its own, so it keeps the
            // drawn one — a partial set of themed papers is a perfectly good state to ship in.
            var otherTheme = Compose(layout).RenderPages(plan, PixelPng(), spreads, "Ocean");
            Assert.NotEqual(Teal, CentrePixel(otherTheme[FrontEndpaperPage]));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
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

    /// <summary>Page indices in the fourteen-page book, counted the way Build composes it.</summary>
    private const int FrontEndpaperPage = 1;
    private const int InvitationPage = 2;
    private const int BackEndpaperPage = BookFormat.SpreadCount + 4;
    private const int BackCoverPage = BookFormat.SpreadCount + 5;

    private static readonly (byte R, byte G, byte B) Magenta = (0xE0, 0x1E, 0x9B);
    private static readonly (byte R, byte G, byte B) Teal = (0x14, 0x8F, 0x8A);

    private static BekiPdfComposer Compose(BekiPrintLayoutOptions? layout = null) =>
        new(Options.Create(layout ?? new BekiPrintLayoutOptions()));

    /// <summary>
    /// A flat sheet of one colour, at the leaf's own proportions so the composer's centre-crop
    /// passes it through untouched and the whole page ends up that colour.
    /// </summary>
    private static byte[] SolidPng((byte R, byte G, byte B) colour)
    {
        var layout = new BekiPrintLayoutOptions();
        const int width = 440;
        var height = (int)MathF.Round(width
            * (layout.SpreadHeightMm + (layout.BleedMm * 2))
            / (layout.PageWidthMm + (layout.BleedMm * 2)));

        using var image = new Image<Rgba32>(
            width, height, new Rgba32(colour.R, colour.G, colour.B, 255));
        using var buffer = new MemoryStream();
        image.Save(buffer, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        return buffer.ToArray();
    }

    private static (byte R, byte G, byte B) CentrePixel(byte[] png)
    {
        using var image = Image.Load<Rgba32>(png);
        var pixel = image[image.Width / 2, image.Height / 2];
        return (pixel.R, pixel.G, pixel.B);
    }

    /// <summary>One pixel in from the top-left corner — a page that bleeds is coloured here.</summary>
    private static (byte R, byte G, byte B) CornerPixel(byte[] png)
    {
        using var image = Image.Load<Rgba32>(png);
        var pixel = image[1, 1];
        return (pixel.R, pixel.G, pixel.B);
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
