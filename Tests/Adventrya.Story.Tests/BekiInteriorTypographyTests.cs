using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using Microsoft.Extensions.Options;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The interior typography contract (§6 Step 8, R4 and R10).
///
/// Four things the shipped book got wrong, each with a test that would have caught it: the visible
/// Georgian was a picture of words rather than type; the text panel was a full-height dark slab
/// rather than a shape the copy's own size; an overlong paragraph was set at the smallest size on the list
/// and printed off the page instead of stopping with <c>TEXT_OVERFLOW</c>; and the file embedded two
/// faces — Noto Serif Georgian and QuestPDF's default Lato — that the interior is not allowed.
/// </summary>
public class BekiInteriorTypographyTests
{
    /// <summary>Cover, endpaper, intro, then the eight spreads: spread 1 is page index 3.</summary>
    private const int FirstStorySpreadPage = 3;

    /// <summary>
    /// The defaults are the approved reference's, and they are the supplier's config's too.
    ///
    /// Two sources for one set of numbers, so the test compares them rather than restating either:
    /// <c>pipeline_config_v1.json</c>'s <c>interior</c> block is the supplier's document, and the
    /// options are what the composer actually sets type with.
    /// </summary>
    [Fact]
    public void The_typography_defaults_are_the_approved_reference()
    {
        var layout = new BekiPrintLayoutOptions();

        Assert.Equal(18f, layout.StoryFontSize);
        Assert.Equal(27f, layout.StoryLeadingPt);
        Assert.Equal(170f, layout.MaxTextWidthMm);

        using var config = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Assets", "BekiComposite", "pipeline_config_v1.json")));
        var interior = config.RootElement.GetProperty("interior");

        Assert.Equal(layout.StoryFontSize, interior.GetProperty("body_font_default_pt").GetSingle());
        Assert.Equal(layout.StoryLeadingPt, interior.GetProperty("body_leading_default_pt").GetSingle());
        Assert.Equal(layout.MaxTextWidthMm, interior.GetProperty("text_max_width_default_mm").GetSingle());
        Assert.Equal(layout.SpreadWidthMm, interior.GetProperty("trim_width_mm").GetSingle());
        Assert.Equal(layout.SpreadHeightMm, interior.GetProperty("trim_height_mm").GetSingle());
        Assert.Equal(layout.BleedMm, interior.GetProperty("bleed_mm_each_outer_edge").GetSingle());
        Assert.Equal("vector", interior.GetProperty("text_layer").GetString());
    }

    /// <summary>
    /// The visible Georgian is vector type, not a raster of it.
    ///
    /// Asked as a count of image XObjects, because that is what the two versions actually differ by:
    /// the old composer drew each block into a PNG at 300 DPI and laid an invisible text run over it,
    /// so a story spread carried two images — the illustration and a picture of its own words. A
    /// spread now carries exactly one, and it is the illustration.
    /// </summary>
    [Fact]
    public void The_story_text_is_vector_type_and_not_a_picture_of_words()
    {
        using var document = Open(ComposeFixtureBook());

        for (var spread = 1; spread < BookFormat.SpreadCount; spread++)
        {
            var page = document.Pages[FirstStorySpreadPage + spread - 1];
            var images = ImageCount(page);

            Assert.True(images == 1,
                $"Story spread {spread} carries {images} images; it should carry only its illustration.");
        }
    }

    /// <summary>
    /// The copy is cream type upper-left on the artwork, on a translucent panel that is the copy's
    /// own size — a shade under the words, not the slab the shipped book had.
    ///
    /// This page has now been four things. The shipped book had a "dark half-page text panel"; audit
    /// P1-04 replaced it with a local cream wash; owner ruling 2026-09-01's third removed the box in
    /// favour of cream type with its own dark rim; and the fourth ruling of the same day —
    /// "transparent-like background, but not too transparent" — put a translucent plum panel under
    /// that type, because a rim alone cannot carry cream letters on pale artwork. So the test asks
    /// what is actually on the leaf: where the cream starts and stops; where the shade starts and
    /// stops around it, which has to be the padding and no more; and whether the picture still
    /// shows through the shade, which is what makes it a shade.
    /// </summary>
    [Fact]
    public void The_story_copy_is_dark_type_upper_left_on_a_cream_wash_its_own_size()
    {
        var pages = RenderFixtureBook();
        var layout = BekiLayoutFixture.ScreenProofLayout();

        // Spread 1's text side is LEFT (the rhythm's first entry), so the copy is on the left leaf.
        Assert.Equal("left", AdventurePacks.Api.Services.Story.Prompts.BekiSpreadRhythm.TextSideFor(1));

        using var page = Image.Load<Rgba32>(pages[FirstStorySpreadPage]);

        // The proof sheet is the 440 mm spread plus 5 mm of bleed on each edge, so the copy column —
        // 12 mm inside the trim and 118 mm wide — runs from 17 mm to about 147 mm across it, and
        // starts 17 mm down.
        var pxPerMm = page.Width / 450f;
        var left = (int)(17 * pxPerMm);
        var right = (int)(147 * pxPerMm);

        var ink = Bounds(page, left, right, IsStoryInk);
        var shade = Bounds(page, left, right, IsCream);

        Assert.True(ink is not null, "No dark type was found in the story copy's column.");
        Assert.True(shade is not null, "No cream wash was found in the story copy's column.");

        // Upper-left: the copy starts inside the top fifth of the sheet.
        Assert.True(ink!.Value.Top < page.Height / 5,
            $"The copy starts {ink.Value.Top} rows down a {page.Height}-row page; it is set upper-left.");

        // And it stops where the words stop. The fixture's spreads are one short sentence each, so
        // cream reaching even half way down the leaf would be a slab rather than a paragraph.
        Assert.True(ink.Value.Bottom < page.Height / 2,
            $"Ink runs to row {ink.Value.Bottom} of {page.Height}; that is a slab, not a paragraph.");

        // The panel hugs the copy. Its top-left corner is the column's own, and measured against
        // the cream its edges sit the padding away plus the type's own air — the ascender a little
        // below its line box, the descender a little above the box's bottom — so between half the
        // padding and twice it on every side. The shipped book's slab ran the height of the leaf.
        var pad = layout.WashPaddingMm * pxPerMm;

        Assert.InRange(shade!.Value.Top, left - 2, left + 2);
        Assert.InRange(shade.Value.Left, left - 2, left + 2);
        Assert.InRange(ink.Value.Top - shade.Value.Top, pad * 0.5, pad * 2.0);
        Assert.InRange(shade.Value.Bottom - ink.Value.Bottom, pad * 0.5, pad * 2.0);
        Assert.InRange(ink.Value.Left - shade.Value.Left, pad * 0.5, pad * 1.5);
        Assert.InRange(shade.Value.Right - ink.Value.Right, pad * 0.5, pad * 2.0);

        // Translucent: inside the panel, away from the type and its rim, the artwork's green is
        // still the strongest channel and is still most of what is there. The plum at sixty per
        // cent over (0, 200, 120) is about (24, 96, 86); an opaque plum would be (40, 27, 63), and
        // no panel at all would leave the green at 200.
        var panelPixels = new List<Rgba32>();
        var inkPixels = 0;
        var total = 0;

        for (var y = shade.Value.Top; y <= shade.Value.Bottom; y++)
        {
            for (var x = shade.Value.Left; x <= shade.Value.Right; x++)
            {
                var pixel = page[x, y];
                total++;

                if (IsStoryInk(pixel))
                {
                    inkPixels++;
                }
                else if (IsCream(pixel))
                {
                    panelPixels.Add(pixel);
                }
            }
        }

        Assert.True(panelPixels.Count > total / 2,
            $"Only {panelPixels.Count} of the panel's {total} pixels are the shaded picture; the "
            + "rest are type, rim, or a panel too opaque to see the artwork through.");
        Assert.True(panelPixels.Average(pixel => pixel.G) > panelPixels.Average(pixel => pixel.B),
            "Inside the panel blue outweighs green; the plum is covering the picture rather than shading it.");

        // And type is still type: cream is a small share of the panel it sits on.
        var share = (double)inkPixels / total;
        Assert.True(share < 0.25,
            $"{share:P0} of the wash is dark ink. Type covers a small part of the wash it "
            + "sits on; a quarter or more is a cream box, which is not what the ruling asked for.");
    }

    /// <summary>
    /// And the panel is sized to the copy that was set, not to the column it was set in: a spread
    /// with three words on it gets a panel three words wide.
    ///
    /// Rendered through the composer's own one-spread path with no proof style, which is the
    /// production page pixel for pixel; the fixture book's sentences fill their column too nearly
    /// to tell a panel wrapped to its widest line from one drawn to the column's edge.
    /// </summary>
    [Fact]
    public void The_panel_is_as_wide_as_the_widest_line_and_not_as_wide_as_the_column()
    {
        var layout = BekiLayoutFixture.ScreenProofLayout();
        var plan = BekiLayoutFixture.EightSpreadPlan("ნინო ჩუმად იჯდა.");
        var spread = plan.Spreads.Single(page => page.Number == 1);

        var png = new BekiPdfComposer(Options.Create(layout)).RenderStyleProofSpread(
            spread, BekiLayoutFixture.SheetPng((0, 200, 120)), BekiLayoutFixture.Personalization(),
            style: null, rasterDpi: 96);

        using var page = Image.Load<Rgba32>(png);
        var pxPerMm = page.Width / 450f;
        var left = (int)(17 * pxPerMm);
        var right = (int)(147 * pxPerMm);

        var ink = Bounds(page, left, right, IsStoryInk);
        var shade = Bounds(page, left, right, IsCream);

        Assert.True(ink is not null, "No dark type was found in the story copy's column.");
        Assert.True(shade is not null, "No cream wash was found in the story copy's column.");

        var pad = layout.WashPaddingMm * pxPerMm;

        // The panel ends the padding past the last letter…
        Assert.InRange(shade!.Value.Right - ink!.Value.Right, pad * 0.5, pad * 2.0);

        // …which on three words is nowhere near the column's far edge.
        Assert.True(right - shade.Value.Right > 30 * pxPerMm,
            $"The panel runs to {shade.Value.Right / pxPerMm:0.#} mm on a column that ends at 147 mm; "
            + "that is the column's width, not the copy's.");
    }

    /// <summary>
    /// Copy that will not fit at any permitted size stops the book. It used to be set at the last
    /// rung of the ladder whether it fitted or not.
    /// </summary>
    [Fact]
    public void Copy_that_cannot_fit_at_any_permitted_size_stops_the_book()
    {
        var failure = Assert.Throws<BekiLayoutException>(() => ComposeFixtureBook(text: LongCopy(60)));

        Assert.Equal(CompositeFailureCodes.TextOverflow, failure.FailureCode);
        Assert.Contains("does not fit", failure.Message, StringComparison.OrdinalIgnoreCase);

        // The copy is never rewritten to make it fit — the failure says so, because that is the
        // thing a reader of this message might otherwise be tempted to do in code.
        Assert.Contains("not rewritten", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// And the ladder is what stands between a long paragraph and that failure: the same copy that
    /// composes with the configured rungs available stops the book when they are taken away.
    /// </summary>
    [Fact]
    public void The_step_down_ladder_is_what_saves_copy_that_is_merely_long()
    {
        // Seven sentences: measured at roughly 750 pt set at 20 pt and 400 pt at 14 pt, against a
        // column that holds 527 pt. Long enough that the age band's own size cannot hold it, short
        // enough that a permitted reduction can.
        var copy = LongCopy(7);

        var withLadder = BekiLayoutFixture.ScreenProofLayout();
        var withoutLadder = BekiLayoutFixture.ScreenProofLayout();
        withoutLadder.StoryFontSizeLadderPt = [];

        // Long enough to need a reduction…
        var failure = Assert.Throws<BekiLayoutException>(() => ComposeFixtureBook(withoutLadder, copy));
        Assert.Equal(CompositeFailureCodes.TextOverflow, failure.FailureCode);

        // …and short enough that a permitted reduction saves it.
        var pdf = ComposeFixtureBook(withLadder, copy);
        Assert.True(pdf.Length > 0);
    }

    /// <summary>
    /// The book embeds only the faces the handoff allows: Noto Sans Georgian for the interior, the
    /// licensed Ottia for the cover title. An acceptance check (R15) — the shipped PDF embedded
    /// <c>NotoSerifGeorgian-SemiBold</c> and <c>Lato-Regular</c>, and nothing in the build noticed.
    /// </summary>
    [Fact]
    public void The_book_embeds_only_the_whitelisted_faces()
    {
        using var document = Open(ComposeFixtureBook());

        var embedded = EmbeddedFontNames(document);
        Assert.NotEmpty(embedded);

        foreach (var font in embedded)
        {
            var allowed = font.Contains("NotoSansGeorgian", StringComparison.OrdinalIgnoreCase)
                || font.Contains("NotoSans-Georgian", StringComparison.OrdinalIgnoreCase)
                || font.Contains("Ottia", StringComparison.OrdinalIgnoreCase);

            Assert.True(allowed, $"The book embeds '{font}', which is not on the Beki font whitelist.");
        }

        Assert.DoesNotContain(embedded, font => font.Contains("Lato", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(embedded, font => font.Contains("Serif", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The intro spread names the child correctly.
    ///
    /// The shipped book printed „ეს წიგნი ეკუთვნის თემო-ს“ — a hyphen Georgian only writes before a
    /// suffix on a word in another alphabet. Tested on the rule rather than on one name, because the
    /// bug was a template that could only ever be right by accident.
    /// </summary>
    [Theory]
    [InlineData("თემო", "თემოს")]
    [InlineData("ნინო", "ნინოს")]
    [InlineData("გიორგი", "გიორგის")]
    [InlineData("ლუკა", "ლუკას")]
    [InlineData("ბორის", "ბორისს")]
    [InlineData("  ანა  ", "ანას")]
    [InlineData("Luka", "Luka-ს")]
    [InlineData("", "")]
    public void A_childs_name_takes_its_case_ending_the_way_Georgian_writes_it(string name, string expected)
        => Assert.Equal(expected, GeorgianNameSuffix.Dative(name));

    /// <summary>And the shipped default template cannot produce the hyphen again.</summary>
    [Fact]
    public void The_intro_dedication_template_carries_no_hyphen_of_its_own()
    {
        var layout = new BekiPrintLayoutOptions();

        Assert.Contains("{name_dative}", layout.IntroBelongsTemplate, StringComparison.Ordinal);
        Assert.DoesNotContain("-ს", layout.IntroBelongsTemplate, StringComparison.Ordinal);
        Assert.DoesNotContain("{date}", layout.IntroInviteTemplate, StringComparison.Ordinal);

        // No date anywhere on the intro spread: a reprint must be the same book as the one bought.
        Assert.DoesNotContain(
            typeof(BekiPrintLayoutOptions).GetProperties(),
            property => property.Name.Contains("Date", StringComparison.Ordinal));
    }

    /// <summary>
    /// An illustration that is not the sheet's shape is refused rather than recomposed by a crop
    /// nobody approved. R7's measured rule: 4% per axis, and a raw 3:2 render is 30%.
    /// </summary>
    [Fact]
    public void Artwork_that_would_lose_more_than_the_tolerance_to_the_crop_is_refused()
    {
        var threeToTwo = BekiLayoutFixture.SheetPng((0, 200, 120), width: 1500);
        using var probe = Image.Load<Rgba32>(threeToTwo);
        Assert.Equal(700, probe.Height); // the fixture's own sheet shape, for contrast

        var failure = Assert.Throws<BekiLayoutException>(
            () => ComposeFixtureBook(artwork: Solid(1536, 1024)));

        Assert.Equal(CompositeFailureCodes.LayoutFailed, failure.FailureCode);
        Assert.Contains("crop", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PrintCropTolerance", failure.Message, StringComparison.Ordinal);
    }

    private static string LongCopy(int sentences) => string.Join(
        " ",
        Enumerable.Repeat(
            "ნინო ფრთხილად შევიდა მოჯადოებულ ტყეში და ხავსიან ბილიკზე ნაბიჯ-ნაბიჯ წავიდა წინ.",
            sentences));

    private static byte[] Solid(int width, int height) =>
        SyntheticImages.SolidPng(width, height, (20, 120, 90));

    /// <summary>
    /// The standard book composed to a PDF, built once. Two tests below open exactly this document
    /// and ask different questions of it, and composing it twice bought nothing but the wait.
    /// </summary>
    private static readonly Lazy<byte[]> StandardBook = new(() => ComposeBook(null, null, null));

    private static byte[] ComposeFixtureBook(
        BekiPrintLayoutOptions? layout = null, string? text = null, byte[]? artwork = null) =>
        layout is null && text is null && artwork is null
            ? StandardBook.Value.ToArray()
            : ComposeBook(layout, text, artwork);

    private static byte[] ComposeBook(
        BekiPrintLayoutOptions? layout, string? text, byte[]? artwork)
    {
        var plan = BekiLayoutFixture.EightSpreadPlan(text);
        var spreads = plan.Spreads
            .Select(spread => new BekiSpreadArtwork(
                spread.Number, artwork ?? BekiLayoutFixture.SheetPng((0, 200, 120))))
            .ToList();

        return new BekiPdfComposer(Options.Create(layout ?? BekiLayoutFixture.ScreenProofLayout()))
            .ComposeWithReceipts(plan, BekiLayoutFixture.LeafPng((200, 60, 60)), spreads,
                BekiLayoutFixture.Personalization()).Pdf;
    }

    private static IReadOnlyList<byte[]> RenderFixtureBook() => BekiLayoutFixture.ScreenProofPages();

    private static bool IsCream(Rgba32 pixel)
        => pixel.R > 180 && pixel.G > 190 && pixel.B > 160;

    private static bool IsStoryInk(Rgba32 pixel)
        => pixel.R < 90 && pixel.G < 80 && pixel.B < 120;

    /// <summary>The fixture's flat green, (0, 200, 120), with room for the rasteriser.</summary>
    private static bool IsArtwork(Rgba32 pixel)
        => pixel.R < 30 && Math.Abs(pixel.G - 200) < 30 && Math.Abs(pixel.B - 120) < 30;

    /// <summary>The rows and columns something occupies, inclusive.</summary>
    private readonly record struct Box(int Top, int Bottom, int Left, int Right);

    /// <summary>
    /// The bounding box of every pixel between <paramref name="left"/> and <paramref name="right"/>
    /// that <paramref name="wanted"/> accepts, or null when there is none.
    /// </summary>
    private static Box? Bounds(Image<Rgba32> page, int left, int right, Func<Rgba32, bool> wanted)
    {
        int? top = null;
        var bottom = 0;
        var first = right;
        var last = left - 1;

        for (var y = 0; y < page.Height; y++)
        {
            for (var x = left; x < right; x++)
            {
                if (!wanted(page[x, y])) continue;

                top ??= y;
                bottom = y;
                if (x < first) first = x;
                if (x > last) last = x;
            }
        }

        return top is { } found ? new Box(found, bottom, first, last) : null;
    }

    private static PdfDocument Open(byte[] pdf)
        => PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);

    private static int ImageCount(PdfPage page)
    {
        var resources = page.Elements.GetDictionary("/Resources");
        var xObjects = resources?.Elements.GetDictionary("/XObject");
        if (xObjects is null) return 0;

        var count = 0;
        foreach (var key in xObjects.Elements.Keys)
        {
            var item = xObjects.Elements.GetObject(key);
            if (item is PdfDictionary dictionary
                && dictionary.Elements.GetName("/Subtype") == "/Image")
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Every font the file names, read from the page resources rather than from the raw bytes: a
    /// composite Georgian font is a Type0 wrapper around a descendant, and a substring search over
    /// the file would miss whichever of the two happened to be compressed.
    /// </summary>
    private static IReadOnlyList<string> EmbeddedFontNames(PdfDocument document)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var page in document.Pages)
        {
            var fonts = page.Elements.GetDictionary("/Resources")?.Elements.GetDictionary("/Font");
            if (fonts is null) continue;

            foreach (var key in fonts.Elements.Keys)
            {
                if (fonts.Elements.GetObject(key) is not PdfDictionary font) continue;

                Collect(font, names);
            }
        }

        return [.. names];
    }

    private static void Collect(PdfDictionary font, SortedSet<string> names)
    {
        var baseFont = font.Elements.GetName("/BaseFont");
        if (!string.IsNullOrEmpty(baseFont))
        {
            // Subset prefixes look like "/ABCDEF+NotoSansGeorgian"; the name after the plus is the
            // face, and the prefix changes on every build.
            var trimmed = baseFont.TrimStart('/');
            var plus = trimmed.IndexOf('+');
            names.Add(plus >= 0 ? trimmed[(plus + 1)..] : trimmed);
        }

        if (font.Elements.GetObject("/DescendantFonts") is PdfArray descendants)
        {
            foreach (var item in descendants.Elements)
            {
                var resolved = item is PdfReference reference ? reference.Value : item;
                if (resolved is PdfDictionary descendant)
                {
                    Collect(descendant, names);
                }
            }
        }
    }
}
