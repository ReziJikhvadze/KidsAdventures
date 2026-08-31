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
/// Georgian was a picture of words rather than type; the wash was a full-height dark panel rather
/// than a cream box around the copy; an overlong paragraph was set at the smallest size on the list
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
    /// The wash is cream, it is at the top of its column, and it stops where the copy stops.
    ///
    /// The supplier's audit of the printed book called it a "dark half-page text panel". So the test
    /// measures the panel: how far down the text side it reaches, and what colour it is.
    /// </summary>
    [Fact]
    public void The_wash_is_a_cream_box_around_the_copy_rather_than_a_full_height_panel()
    {
        var pages = RenderFixtureBook();

        // Spread 1's text side is LEFT (the rhythm's first entry), so the wash is on the left leaf.
        Assert.Equal("left", AdventurePacks.Api.Services.Story.Prompts.BekiSpreadRhythm.TextSideFor(1));

        using var page = Image.Load<Rgba32>(pages[FirstStorySpreadPage]);

        int? firstRow = null;
        int? lastRow = null;
        var column = page.Width / 8;

        for (var y = 0; y < page.Height; y++)
        {
            if (!IsCream(page[column, y])) continue;
            firstRow ??= y;
            lastRow = y;
        }

        Assert.True(firstRow is not null, "No cream wash was found under the story copy.");

        // Upper-left: the box starts inside the top fifth of the sheet.
        Assert.True(firstRow!.Value < page.Height / 5,
            $"The wash starts {firstRow.Value} rows down a {page.Height}-row page; the copy is set upper-left.");

        // And measured to the copy: it stops well short of the bottom of the page. The fixture's
        // spreads are one short sentence each, so a wash that reaches even half way down is a panel.
        Assert.True(lastRow!.Value < page.Height / 2,
            $"The wash runs to row {lastRow.Value} of {page.Height}; it is a panel, not a box around the copy.");

        // The old panel was #0D071D at 45% over the artwork — dark. This one is cream, and the ink
        // on it is dark, which is the way round §6 Step 8 asks for.
        var inside = page[column, firstRow.Value + 2];
        Assert.True(inside.R > 200 && inside.G > 200 && inside.B > 180,
            $"The wash under the copy is #{inside.R:X2}{inside.G:X2}{inside.B:X2}; it should be cream.");
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
        => pixel.R > 200 && pixel.G > 195 && pixel.B > 170 && pixel.B < pixel.R;

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
