using System;
using System.IO;
using System.Linq;
using Xunit;
using PdfSharp.Pdf.IO;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Domain.Story;
using Microsoft.Extensions.Options;
using AdventurePacks.Api.Configuration.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Adventrya.Story.Tests;

public class BekiSpecV2PrintTests
{
    /// <summary>
    /// The handoff's interior geometry, restated so the test fails if the defaults drift back.
    ///
    /// These used to be 420 × 210 with 3 mm of bleed — numbers that matched neither the handoff nor
    /// the composer's own defaults, and that therefore proved nothing about the book being printed.
    /// The sheet is 450 × 210 mm: a 440 × 200 trim with 5 mm on every outer edge.
    /// </summary>
    private const float TrimWidthMm = 440f;
    private const float TrimHeightMm = 200f;
    private const float BleedMm = 5f;
    private const float MediaWidthMm = TrimWidthMm + (BleedMm * 2f);
    private const float MediaHeightMm = TrimHeightMm + (BleedMm * 2f);

    private static BekiPrintLayoutOptions DefaultLayout => BekiLayoutFixture.ScreenProofLayout();

    /// <summary>
    /// The book's own defaults are the handoff's numbers.
    ///
    /// A build-time acceptance check (R15): 3 mm of bleed shipped once and the only thing that
    /// caught it was a supplier opening the printed PDF.
    /// </summary>
    [Fact]
    public void The_interior_geometry_defaults_are_the_handoffs()
    {
        var layout = new BekiPrintLayoutOptions();

        Assert.Equal(TrimWidthMm, layout.SpreadWidthMm);
        Assert.Equal(TrimHeightMm, layout.SpreadHeightMm);
        Assert.Equal(BleedMm, layout.BleedMm);
        Assert.Equal(TrimWidthMm / 2f, layout.PageWidthMm);

        // 450 ÷ 210 is exactly 15:7, which is the ratio the illustration stage normalizes to — so
        // artwork that arrived normalized has nothing to crop. That equality is the whole reason
        // the crop tolerance can be as tight as it is.
        Assert.Equal(15f / 7f, MediaWidthMm / MediaHeightMm, 5);
    }

    [Fact]
    public void PdfPrintBoxes_Apply_CreatesCorrectMediaAndTrimBoxes()
    {
        var pdfBytes = ComposeFixtureBook();

        using var stream = new MemoryStream(pdfBytes);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        var page = document.Pages[0];

        Assert.NotNull(page.MediaBox);
        Assert.NotNull(page.TrimBox);

        Assert.True(page.MediaBox.Width > page.TrimBox.Width);
        Assert.True(page.MediaBox.Height > page.TrimBox.Height);
    }

    /// <summary>
    /// One interior layer, at exactly the working raster §6 Step 8 specifies.
    ///
    /// The old version of this test asked only whether the output was a JPEG, which the previous
    /// implementation satisfied while resizing by width alone, skipping anything already wide enough
    /// and writing neither a density nor a colour profile. All four clauses are the test now.
    ///
    /// The source is now larger than the target rather than smaller. It used to be a 1500-pixel
    /// sheet stretched up to 5315 — see the refusal below for why that is no longer a thing this
    /// method will do.
    /// </summary>
    [Fact]
    public void NormalizeForPrint_delivers_the_exact_working_raster()
    {
        var target = new BekiPdfComposer.PrintRasterTarget(5315, 2480, 300, 90);

        // A sheet-shaped source above the target: the resize is a reduction and stays proportional,
        // which is the only kind of resize the interior rules permit and the only kind that cannot
        // claim detail it does not have.
        var normalized = BekiPdfComposer.NormalizeForPrint(SheetPng(6000), target);

        var info = Image.Identify(normalized);
        Assert.Equal(5315, info.Width);
        Assert.Equal(2480, info.Height);
        Assert.Equal(300, info.Metadata.HorizontalResolution, 1);
        Assert.Equal(300, info.Metadata.VerticalResolution, 1);
        Assert.NotNull(info.Metadata.IccProfile);

        Assert.Equal(0xFF, normalized[0]);
        Assert.Equal(0xD8, normalized[1]);
    }

    /// <summary>
    /// **This test asserts the opposite of what it used to.** It pinned the upscale as intended —
    /// "the resize is an upscale and stays proportional, which is the only kind of resize the
    /// interior rules permit" — and audit P1-01 read the printed consequence: the story rasters
    /// arrived at about 143 effective PPI and were Lanczos-stretched to 5315 × 2480, so the press
    /// PDF reported 300 PPI everywhere and carried less than half of it anywhere.
    ///
    /// Interpolation does not create detail. So an enlargement past 1.05× — five per cent, a
    /// rounding difference rather than a claim — is refused (D5b), and real source pixels or an
    /// approved upscaler have to arrive BEFORE layout. Reduction stays legal, which the test above
    /// is now written on.
    /// </summary>
    [Fact]
    public void NormalizeForPrint_refuses_to_invent_detail_by_enlarging()
    {
        var target = new BekiPdfComposer.PrintRasterTarget(5315, 2480, 300, 90);

        var failure = Assert.Throws<BekiLayoutException>(
            () => BekiPdfComposer.NormalizeForPrint(SheetPng(1500), target));

        Assert.Equal("LAYOUT_FAILED", failure.FailureCode);
        Assert.Contains("PRESS_RESOLUTION", failure.Message, StringComparison.Ordinal);
        Assert.Contains("P1-01", failure.Message, StringComparison.Ordinal);
        Assert.Contains("3.54×", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the line is where it is said to be: five per cent up is a rounding difference and passes,
    /// six per cent is a claim about detail and does not.
    /// </summary>
    [Fact]
    public void NormalizeForPrint_allows_the_rounding_difference_and_refuses_the_claim()
    {
        var target = new BekiPdfComposer.PrintRasterTarget(5315, 2480, 300, 90);

        // 5315 ÷ 1.04 — inside the allowance.
        Assert.NotEmpty(BekiPdfComposer.NormalizeForPrint(SheetPng(5111), target));

        // 5315 ÷ 1.20 — outside it.
        Assert.Throws<BekiLayoutException>(
            () => BekiPdfComposer.NormalizeForPrint(SheetPng(4429), target));
    }

    /// <summary>
    /// An image that is not the sheet's shape is refused rather than squashed onto it. §6 Step 8
    /// forbids stretching, and the composer's crop is what makes the ratios agree — so a layer that
    /// still disagrees by the time it reaches here never went through it.
    /// </summary>
    [Fact]
    public void NormalizeForPrint_refuses_to_stretch_a_layer_onto_the_sheet()
    {
        var target = new BekiPdfComposer.PrintRasterTarget(5315, 2480, 300, 90);
        var square = Solid(1200, 1200, (10, 10, 10));

        var failure = Assert.Throws<BekiLayoutException>(
            () => BekiPdfComposer.NormalizeForPrint(square, target));

        Assert.Equal("LAYOUT_FAILED", failure.FailureCode);
        Assert.Contains("stretch", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The whole composed book, reopened: fourteen pages in spec v2's locked sequence, and every
    /// page carrying the print boxes §26 demands — MediaBox and BleedBox the bled sheet, TrimBox
    /// the trim centred inside it, never equal to the bleed. Run against the real Compose output
    /// rather than a synthetic document, so it also proves the QuestPDF file survives PDFsharp's
    /// reopen-mutate-save round trip with its page list intact.
    /// </summary>
    [Fact]
    public void The_composed_book_has_fourteen_pages_with_correct_print_boxes()
    {
        var pdf = ComposeFixtureBook();

        using var stream = new MemoryStream(pdf);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);

        Assert.Equal(14, document.Pages.Count);

        const double mmToPt = 72.0 / 25.4;
        var leafPt = ((TrimWidthMm / 2f) + (BleedMm * 2f)) * mmToPt;
        var spreadPt = MediaWidthMm * mmToPt;
        var heightPt = MediaHeightMm * mmToPt;
        var bleedPt = BleedMm * mmToPt;

        for (var index = 0; index < document.Pages.Count; index++)
        {
            var page = document.Pages[index];
            var media = page.MediaBox;
            var bleed = page.Elements.GetRectangle("/BleedBox");
            var trim = page.TrimBox;

            // Cover and back cover are single leaves; everything between is a spread sheet.
            var expectedWidth = index == 0 || index == 13 ? leafPt : spreadPt;
            Assert.True(Math.Abs(media.Width - expectedWidth) < 1.5,
                $"Page {index + 1}: MediaBox width {media.Width:F1}pt, expected {expectedWidth:F1}pt.");
            Assert.True(Math.Abs(media.Height - heightPt) < 1.5,
                $"Page {index + 1}: MediaBox height {media.Height:F1}pt.");

            Assert.True(Math.Abs(bleed.Width - media.Width) < 0.1
                && Math.Abs(bleed.Height - media.Height) < 0.1,
                $"Page {index + 1}: BleedBox must equal MediaBox.");

            Assert.True(Math.Abs(trim.Width - (media.Width - (2 * bleedPt))) < 1.5,
                $"Page {index + 1}: TrimBox width {trim.Width:F1}pt is not the trim.");
            Assert.True(Math.Abs(trim.X1 - media.X1 - bleedPt) < 1.5,
                $"Page {index + 1}: TrimBox is not centred.");
            Assert.True(trim.Width < bleed.Width,
                $"Page {index + 1}: TrimBox must never equal the bleed size.");
        }

        // And the interior sheets are the handoff's 450 × 210 mm exactly, not merely self-consistent.
        // Within a fifth of a point — 0.07 mm — because a PDF's own boxes are written to one decimal.
        var interior = document.Pages[1];
        AssertMillimetres(MediaWidthMm, interior.MediaBox.Width, "MediaBox width");
        AssertMillimetres(MediaHeightMm, interior.MediaBox.Height, "MediaBox height");
        AssertMillimetres(TrimWidthMm, interior.TrimBox.Width, "TrimBox width");
        AssertMillimetres(TrimHeightMm, interior.TrimBox.Height, "TrimBox height");

        static void AssertMillimetres(double expectedMm, double actualPt, string what)
        {
            var expectedPt = expectedMm * 72.0 / 25.4;
            Assert.True(Math.Abs(actualPt - expectedPt) < 0.2,
                $"{what}: {actualPt:F2}pt, expected {expectedPt:F2}pt ({expectedMm}mm).");
        }
    }

    /// <summary>
    /// The intro spread's personalization is real typeset text: a book that prints the child's own
    /// lines carries strictly more draw-text calls than one whose dedication templates are blank.
    /// Counted rather than extracted — the Georgian lives in the file as font glyph indices a
    /// substring search cannot see — and counted as an inequality, because an operator maps to a
    /// typeset line, not to a block, and line counts follow wrapping.
    ///
    /// The comparison used to be against an anonymous book. There is no such book any more: without
    /// a theme there is no approved intro background, and R11 makes that a stop rather than a
    /// generic page.
    /// </summary>
    [Fact]
    public void The_personalized_intro_adds_its_personal_lines()
    {
        var bare = BekiLayoutFixture.ScreenProofLayout();
        bare.IntroBelongsTemplate = string.Empty;
        bare.IntroAgeTemplate = string.Empty;

        var floor = TextShowOperators(ComposeFixtureBook(bare));
        Assert.True(floor > 0, "No readable text operators were found; the counter needs revisiting.");
        Assert.True(TextShowOperators(ComposeFixtureBook()) > floor,
            "A book that prints the child's own lines must carry more typeset text than one that does not.");
    }

    /// <summary>
    /// Spread 8 carries no QR and no Continue Adventure chip — the opposite of what this test
    /// used to prove. The Locked Print Specification §6 places exactly ONE code in the book, on
    /// the credits spread, and removed the chip from the final story spread. Probed the same way
    /// the old assertion found the tile: the tile's quiet zone was white by construction and
    /// nothing else on a pure-green spread is, so any white patch here is the chip come back.
    /// </summary>
    [Fact]
    public void Spread_8_carries_no_qr_chip_and_the_credits_page_carries_the_only_code()
    {
        var layout = BekiLayoutFixture.ScreenProofLayout();
        var plan = BekiLayoutFixture.EightSpreadPlan();
        var spreads = plan.Spreads
            .Select(s => new BekiSpreadArtwork(s.Number, BekiLayoutFixture.SheetPng((0, 255, 0))))
            .ToList();

        var pages = new BekiPdfComposer(Options.Create(layout)).RenderPages(
            plan, BekiLayoutFixture.LeafPng((255, 0, 0)), spreads, BekiLayoutFixture.Personalization());

        // Cover, front endpapers, intro, then the eight spreads: the final spread is index 10.
        using var page = Image.Load<Rgba32>(pages[10]);

        var white = 0;
        for (var x = 0; x < page.Width; x++)
        {
            for (var y = 0; y < page.Height; y++)
            {
                var pixel = page[x, y];
                // 250, not 235: the story copy is now outlined cream type (#FFF8EB, blue
                // channel 235) straight on the artwork, and this probe is looking for the
                // QR tile's PURE white quiet zone, not for type.
                if (pixel.R >= 250 && pixel.G >= 250 && pixel.B >= 250)
                {
                    white++;
                }
            }
        }

        Assert.True(white < 500, $"Spread 8 shows a white tile again ({white} white pixels) — the chip is back.");

        // The one code the book has left, on the credits spread (index 11), on its right leaf.
        using var credits = Image.Load<Rgba32>(pages[11]);

        int minX = credits.Width, creditsWhite = 0;
        for (var x = 0; x < credits.Width; x++)
        {
            for (var y = 0; y < credits.Height; y++)
            {
                var pixel = credits[x, y];
                if (pixel.R < 250 || pixel.G < 250 || pixel.B < 250) continue;

                creditsWhite++;
                if (x < minX) minX = x;
            }
        }

        Assert.True(creditsWhite > 500, $"The credits QR tile was not found ({creditsWhite} white pixels).");
        Assert.True(minX > credits.Width / 2, $"The credits QR must sit on the right leaf; it starts at x={minX}.");
    }

    /// <summary>
    /// The credits page carries exactly one raster — the approved Beki mark, at its own pixels —
    /// and the QR beside it is vector.
    ///
    /// Two findings meet on this page. Amendment A1 made the press resolution gate measure effective
    /// PPI per PLACED IMAGE rather than per page, and the mark is a 32 mm placement: without
    /// <c>UseOriginalImage</c> QuestPDF re-rasters it at 288 DPI, which is about 363 px across, and
    /// the one image in the book that was never short of pixels fails the gate. So the embedded
    /// raster has to still be the approved pose's own 2048 px. And the QR has to remain vector — a
    /// scanner reads edges, and a resampled, colour-converted bitmap module edge is the reason the
    /// supplier's preflight rejected raster codes — so the page must carry no second image at all.
    /// </summary>
    [Fact]
    public void The_credits_page_keeps_a_vector_qr_and_the_marks_own_pixels()
    {
        using var document = PdfReader.Open(
            new MemoryStream(ComposeFixtureBook()), PdfDocumentOpenMode.Import);

        // Cover, front endpaper, intro, eight spreads — the credits spread is page index 11.
        var images = ImageXObjects(document.Pages[11]);

        var mark = Assert.Single(images);
        Assert.True(mark.Elements.GetInteger("/Width") >= 1024,
            $"The credits mark is embedded {mark.Elements.GetInteger("/Width")} px wide. It is a "
            + "32 mm placement re-rastered by QuestPDF — UseOriginalImage is missing, and the press "
            + "resolution gate fails on it (amendment A1).");

        // The blank-URL book, for contrast: the same single image, because the code was never one.
        var noQr = BekiLayoutFixture.ScreenProofLayout();
        noQr.ReviewQrUrl = string.Empty;

        using var withoutCode = PdfReader.Open(
            new MemoryStream(ComposeFixtureBook(noQr)), PdfDocumentOpenMode.Import);

        Assert.Single(ImageXObjects(withoutCode.Pages[11]));
    }

    private static IReadOnlyList<PdfSharp.Pdf.PdfDictionary> ImageXObjects(PdfSharp.Pdf.PdfPage page)
    {
        var xObjects = page.Elements.GetDictionary("/Resources")?.Elements.GetDictionary("/XObject");
        if (xObjects is null) return [];

        var images = new List<PdfSharp.Pdf.PdfDictionary>();

        foreach (var key in xObjects.Elements.Keys)
        {
            if (xObjects.Elements.GetObject(key) is PdfSharp.Pdf.PdfDictionary dictionary
                && dictionary.Elements.GetName("/Subtype") == "/Image")
            {
                images.Add(dictionary);
            }
        }

        return images;
    }

    [Fact]
    public void StoryFontSizeFor_ReturnsCorrectSizeBasedOnAge()
    {
        var layout = DefaultLayout;
        layout.StoryFontSizeAges2To4 = 20f;
        layout.StoryFontSizeAges5To8 = 17.5f;
        layout.StoryFontSize = 16f;

        Assert.Equal(20f, BekiPrintLayoutOptions.StoryFontSizeFor(3, layout));
        Assert.Equal(17.5f, BekiPrintLayoutOptions.StoryFontSizeFor(6, layout));
        Assert.Equal(17.5f, BekiPrintLayoutOptions.StoryFontSizeFor(10, layout));
        Assert.Equal(16f, BekiPrintLayoutOptions.StoryFontSizeFor(null, layout));
    }

    private static byte[] ComposeFixtureBook(BekiPrintLayoutOptions? layout = null)
    {
        var plan = BekiLayoutFixture.EightSpreadPlan();
        var spreads = plan.Spreads
            .Select(s => new BekiSpreadArtwork(s.Number, BekiLayoutFixture.SheetPng((0, 255, 0))))
            .ToList();

        return new BekiPdfComposer(Options.Create(layout ?? BekiLayoutFixture.ScreenProofLayout()))
            .ComposeWithReceipts(plan, BekiLayoutFixture.LeafPng((255, 0, 0)), spreads,
                BekiLayoutFixture.Personalization()).Pdf;
    }

    private static byte[] SheetPng(int width)
    {
        var height = (int)MathF.Round(width * MediaHeightMm / MediaWidthMm);
        return Solid(width, height, (0, 255, 0));
    }

    private static byte[] Solid(int width, int height, (byte R, byte G, byte B) colour) =>
        SyntheticImages.SolidPng(width, height, colour);

    /// <summary>
    /// Draw-text calls in the file — a copy of the counter BekiPdfComposerTests uses, asking the
    /// same single question of a different book.
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
                using var zlib = new System.IO.Compression.ZLibStream(
                    source, System.IO.Compression.CompressionMode.Decompress);
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
}
