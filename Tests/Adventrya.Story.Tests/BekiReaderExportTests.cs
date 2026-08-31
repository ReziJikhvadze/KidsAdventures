using System.Text;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Pdf;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using Microsoft.Extensions.Options;
using PdfSharp.Pdf.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The customer's own book — audit P0-01, P0-02 and P0-08, and the D1/D3 half of the correction
/// plan.
///
/// Three defects, one document. The reading copy that was rejected carried printer bleed and no
/// CropBox, so a parent opening it saw five millimetres of overrun on every edge; its front cover
/// was a separately AI-redrawn design with a Beki that is not the approved asset; and its back cover
/// was a flat purple page with `beki.ge` typed on it. What replaces all three is one method — the
/// pages are the finished trim, and the covers are crops of the same wrap the press cover is made
/// from.
///
/// The strongest assertion in the file is the last one: the composed document is handed to
/// <see cref="BekiDigitalPrep"/>, which is the gate that actually judges a download, and it comes
/// back PASS.
/// </summary>
public class BekiReaderExportTests
{
    private const float FrontBackWidthMm = 220f;
    private const float SpreadWidthMm = 440f;
    private const float PageHeightMm = 200f;

    private const double MmToPt = 72.0 / 25.4;

    // ==============================================================================================
    // Geometry — P0-08
    // ==============================================================================================

    /// <summary>
    /// Fourteen trim-size pages, a CropBox on every one of them, and not one printer-only box in
    /// the file.
    ///
    /// The audited PDF stated 230 × 210 mm covers and 450 × 210 mm spreads with a TrimBox inside
    /// them and no CropBox at all. Every clause of that sentence is inverted here.
    /// </summary>
    [Fact]
    public void The_reading_copy_is_trim_sized_with_a_crop_box_and_no_printer_geometry()
    {
        var reading = ComposeReading();

        using var document = PdfReader.Open(
            new MemoryStream(reading.Pdf), PdfDocumentOpenMode.ReadOnly);

        Assert.Equal(14, document.PageCount);

        for (var index = 0; index < document.PageCount; index++)
        {
            var page = document.Pages[index];
            var isCover = index == 0 || index == document.PageCount - 1;
            var expectedWidthMm = isCover ? FrontBackWidthMm : SpreadWidthMm;

            Assert.True(Math.Abs(page.MediaBox.Width - (expectedWidthMm * MmToPt)) < 0.5,
                $"Page {index + 1}: MediaBox is {page.MediaBox.Width / MmToPt:F2} mm wide, "
                + $"expected {expectedWidthMm} mm.");
            Assert.True(Math.Abs(page.MediaBox.Height - (PageHeightMm * MmToPt)) < 0.5,
                $"Page {index + 1}: MediaBox is {page.MediaBox.Height / MmToPt:F2} mm tall.");

            Assert.True(page.Elements.ContainsKey("/CropBox"),
                $"Page {index + 1} has no CropBox — a viewer would show the MediaBox (P0-08).");
            Assert.Equal(page.MediaBox.Width, page.CropBox.Width, 1);
            Assert.Equal(page.MediaBox.Height, page.CropBox.Height, 1);

            Assert.False(page.Elements.ContainsKey("/BleedBox"),
                $"Page {index + 1} carries a BleedBox; the download has no bleed.");
            Assert.False(page.Elements.ContainsKey("/TrimBox"),
                $"Page {index + 1} carries a TrimBox; the download is already at trim.");
        }

        // Georgian, said out loud (P2-2).
        Assert.Equal("ka-GE", document.Internals.Catalog.Elements.GetString("/Lang"));

        // And nothing press-only anywhere in the bytes.
        var text = Encoding.Latin1.GetString(reading.Pdf);
        Assert.DoesNotContain("/GTS_PDFX", text);
        Assert.DoesNotContain("/OutputIntents", text);
    }

    /// <summary>
    /// The press file and the download break their lines in the same places.
    ///
    /// The customer gate asks for exactly this — "visual content, line breaks, text colors, and
    /// asset versions match the canonical master" — and it is the reason the composer measures its
    /// text column on the trim in both modes rather than on each page's own sheet. A press page is
    /// five millimetres wider on every edge; if the column were measured from the sheet, the two
    /// books would wrap differently and nobody would notice until a proof was compared to a
    /// download side by side.
    /// </summary>
    [Fact]
    public void The_download_and_the_press_interior_wrap_their_copy_identically()
    {
        var reading = ComposeReading();
        var press = Composer().ComposeInteriorWithReceipts(
            Plan(), Spreads(), BekiLayoutFixture.Personalization());

        foreach (var role in new[] { "spread-01", "spread-04", "spread-08", "intro" })
        {
            var fromReading = reading.Receipts.Pages.Single(page => page.Role == role);
            var fromPress = press.Receipts.Pages.Single(page => page.Role == role);

            Assert.Equal(fromPress.TextLines, fromReading.TextLines);
            Assert.Equal(
                fromPress.Typography.Select(type => (type.Role, type.SizePt, type.Colour)),
                fromReading.Typography.Select(type => (type.Role, type.SizePt, type.Colour)));

            // The wash is the same shape on both, offset by the bleed the press page carries.
            Assert.NotNull(fromReading.Wash);
            Assert.NotNull(fromPress.Wash);
            Assert.Equal(fromPress.Wash!.WidthMm, fromReading.Wash!.WidthMm, 2);
            Assert.Equal(fromPress.Wash.HeightMm, fromReading.Wash.HeightMm, 2);
            Assert.Equal(fromPress.Wash.XMm - 5d, fromReading.Wash.XMm, 2);
            Assert.Equal(fromPress.Wash.FoldClearanceMm, fromReading.Wash.FoldClearanceMm, 2);
            Assert.Equal(fromPress.Wash.TrimClearanceMm, fromReading.Wash.TrimClearanceMm, 2);
        }
    }

    // ==============================================================================================
    // Covers from the one master — P0-01, P0-02
    // ==============================================================================================

    /// <summary>
    /// The two cover pages are the two boards of the wrap, and the crop does not squash anything.
    ///
    /// Proved on pixels rather than on the PDF: the wrap fixture is a horizontal gradient, so where
    /// a crop landed on the canvas can be read straight off the colours it contains. The front board
    /// begins at 269.5 mm of 512 and the back board at 20 mm, which on this gradient are two clearly
    /// different bands.
    /// </summary>
    [Fact]
    public void The_cover_pages_are_the_wraps_own_boards_and_the_crop_does_not_distort()
    {
        var composer = Composer();
        var wrap = WrapPng(2528, 1210);

        var front = composer.CropFrontBoard(wrap);
        var back = composer.CropBackBoard(wrap);

        using var frontImage = Image.Load<Rgba32>(front);
        using var backImage = Image.Load<Rgba32>(back);

        // The customer page's own ratio, so placing it on 220 × 200 mm is a uniform scale.
        Assert.Equal(
            FrontBackWidthMm / PageHeightMm,
            (float)frontImage.Width / frontImage.Height,
            2);
        Assert.Equal(frontImage.Width, backImage.Width);
        Assert.Equal(frontImage.Height, backImage.Height);

        // Both boards are the same width of canvas, from different places on it: on a left-to-right
        // red ramp the back board is darker than the front board everywhere.
        Assert.True(backImage[0, backImage.Height / 2].R < frontImage[0, frontImage.Height / 2].R,
            "the back board crop is not left of the front board crop on the wrap.");

        // And the centre construction — hinge, spine, hinge — is in neither of them. Its left edge
        // is at 242.5 mm of 512; the back board must stop before it.
        var backRightFraction =
            (BekiCoverDieline.BackBoardLeftMm + BekiCoverDieline.DigitalCropWidthMm)
            / BekiCoverDieline.CanvasWidthMm;
        Assert.True(backRightFraction <= 242.5f / BekiCoverDieline.CanvasWidthMm + 0.001f);
    }

    /// <summary>
    /// The back cover is the wrap's back panel with the address on it — not a flat colour, and not
    /// a Beki.
    ///
    /// P0-02 is measured here as the absence of the placeholder: the audited page was a single flat
    /// dark purple, so a back cover built from artwork has to show more than one colour. And the
    /// mark that used to sit on this page is gone by Locked Spec §6 — the crop is environment-only
    /// by construction, and the page draws no image but the crop.
    /// </summary>
    [Fact]
    public void The_back_cover_is_artwork_rather_than_a_flat_purple_placeholder()
    {
        var reading = ComposeReading();
        var back = reading.Receipts.Pages.Single(page => page.Role == "cover-back");

        // One image on the page, and it is the board crop.
        Assert.Single(back.ImageSha256);
        Assert.Null(back.Wash);
        Assert.Equal(["beki.ge"], back.TextLines);

        // Rendered, the page is a gradient rather than one flat colour.
        var pages = RenderReading();
        using var image = Image.Load<Rgba32>(pages[^1]);

        var left = image[image.Width / 10, image.Height / 2];
        var right = image[image.Width * 9 / 10, image.Height / 2];

        Assert.True(Math.Abs(left.R - right.R) > 8,
            $"the back cover reads as one flat colour (#{left.R:X2}{left.G:X2}{left.B:X2} to "
            + $"#{right.R:X2}{right.G:X2}{right.B:X2}) — the placeholder is back.");

        // And the page ground is nowhere to be seen: artwork covers the leaf edge to edge.
        Assert.DoesNotContain(
            new[] { image[2, 2], image[image.Width - 3, image.Height - 3] },
            pixel => pixel is { R: 0x28, G: 0x1B, B: 0x3F });
    }

    /// <summary>
    /// The front cover carries the book's title, in the same face and the same relative place the
    /// press cover sets it — the title-safe rectangle mapped into the crop's own coordinates.
    /// </summary>
    [Fact]
    public void The_front_cover_sets_the_same_title_in_the_same_place_as_the_press_cover()
    {
        var reading = ComposeReading();
        var front = reading.Receipts.Pages.Single(page => page.Role == "cover-front");

        var titleType = Assert.Single(front.Typography);
        Assert.Equal("cover-title", titleType.Role);

        // The licensed Ottia, under the family name PdfFontBootstrap registers it as.
        Assert.Equal(PdfFontBootstrap.TitleFamily, titleType.Family);
        Assert.Equal("#FFF8EB", titleType.Colour);

        var pressCover = Composer().ComposeCoverPressWithReceipts(
            Plan().Concept.Title, WrapPng(2528, 1210));
        var pressType = Assert.Single(pressCover.Receipts.Pages[0].Typography);

        Assert.Equal(pressType.Family, titleType.Family);
        Assert.Equal(pressType.Colour, titleType.Colour);

        // The same size on a board reproduced 1.12% smaller — type scales with the picture, which
        // is why the two break the title in the same places.
        Assert.Equal(pressType.SizePt * BekiCoverDieline.DigitalScale, titleType.SizePt, 3);
        Assert.Equal(pressCover.Receipts.Pages[0].TextLines, front.TextLines);
    }

    // ==============================================================================================
    // The wash — P1-04, P0-07
    // ==============================================================================================

    /// <summary>
    /// Every page that carries copy carries a wash, and every wash obeys the three rules audit
    /// P1-04 states: local to its page, clear of the fold, inside the trim safety margin.
    /// </summary>
    [Fact]
    public void Every_story_wash_is_local_copy_sized_and_off_the_fold()
    {
        var reading = ComposeReading();

        var washed = reading.Receipts.Pages
            .Where(page => page.Wash is not null)
            .ToList();

        // Eight spreads and the intro.
        Assert.Equal(9, washed.Count);

        foreach (var page in washed)
        {
            var wash = page.Wash!;

            Assert.Equal("#F2FFF8EB", wash.Ink);
            Assert.Equal(7d, wash.PaddingMm, 2);

            Assert.True(wash.FoldClearanceMm >= 10d,
                $"{page.Role}: the wash comes within {wash.FoldClearanceMm:F1} mm of the fold.");
            Assert.True(wash.TrimClearanceMm >= 12d,
                $"{page.Role}: the wash comes within {wash.TrimClearanceMm:F1} mm of the trim.");

            // Local to one leaf, and copy-sized rather than a half-page mask: the fixture's spreads
            // are one short sentence each.
            var fold = SpreadWidthMm / 2d;
            if (wash.PageSide == "left")
            {
                Assert.True(wash.XMm + wash.WidthMm <= fold);
            }
            else
            {
                Assert.True(wash.XMm >= fold);
            }

            Assert.True(wash.HeightMm < PageHeightMm / 2d,
                $"{page.Role}: the wash is {wash.HeightMm:F1} mm tall — that is a panel, not a box "
                + "around the copy.");

            // A story wash lives in the third of the spread the illustrator was told to leave
            // quiet; the intro's has the whole left leaf's text area, which is the layout the
            // approved intro proof has.
            var widest = page.Role == "intro" ? SpreadWidthMm / 2d : SpreadWidthMm / 3d;
            Assert.True(wash.WidthMm <= widest,
                $"{page.Role}: the wash is {wash.WidthMm:F1} mm wide, past the {widest:F1} mm its "
                + "column allows.");
        }

        // Both leaves are used: the text-side rhythm alternates, so the washes must too.
        Assert.Contains(washed, page => page.Wash!.PageSide == "left");
        Assert.Contains(washed, page => page.Wash!.PageSide == "right");
    }

    /// <summary>
    /// Dark ink on the cream, light cream on the dark — audit P0-07's colour rule, read off the
    /// receipts rather than guessed at.
    /// </summary>
    [Fact]
    public void The_copy_is_dark_on_its_wash_and_the_credits_stay_light_on_the_ground()
    {
        var reading = ComposeReading();

        foreach (var page in reading.Receipts.Pages.Where(page => page.Wash is not null))
        {
            Assert.All(page.Typography, type =>
                Assert.Contains("241A33", type.Colour, StringComparison.OrdinalIgnoreCase));
        }

        var credits = reading.Receipts.Pages.Single(page => page.Role == "credits");
        Assert.Null(credits.Wash);
        Assert.All(credits.Typography, type => Assert.Equal("#FFF8EB", type.Colour));
    }

    /// <summary>
    /// And the wash is really drawn, in cream, under the copy — not merely recorded.
    ///
    /// Rendered and sampled at the millimetres the receipt claims, which is the only way a receipt
    /// is evidence of anything: the wash's own centre must be cream, and a point beyond its right
    /// edge, on the same row, must not be.
    /// </summary>
    [Fact]
    public void The_recorded_wash_is_the_one_that_was_drawn()
    {
        var reading = ComposeReading();
        var pages = RenderReading();

        var spread = reading.Receipts.Pages.First(page => page.Role.StartsWith("spread-"));
        var wash = spread.Wash!;

        using var image = Image.Load<Rgba32>(pages[spread.Page - 1]);
        var pxPerMm = image.Width / SpreadWidthMm;

        var inside = image[
            (int)((wash.XMm + (wash.WidthMm / 2)) * pxPerMm),
            (int)((wash.YMm + 2) * pxPerMm)];

        var beyond = image[
            (int)((wash.XMm + wash.WidthMm + 8) * pxPerMm),
            (int)((wash.YMm + 2) * pxPerMm)];

        Assert.True(inside.R > 200 && inside.G > 195 && inside.B > 170 && inside.B < inside.R,
            $"the wash's own middle is #{inside.R:X2}{inside.G:X2}{inside.B:X2}, not cream.");
        Assert.False(beyond.R > 200 && beyond.G > 195 && beyond.B > 170,
            "the cream reaches well past the wash the receipt records — it is not copy-sized.");
    }

    // ==============================================================================================
    // Receipts — A4 / D7
    // ==============================================================================================

    /// <summary>
    /// The receipts are per page, in page order, and they carry what amendment A4 asks a layout to
    /// answer for: the hashes of the rasters actually placed, the wash, the type, the lines, and —
    /// for the credits page — a rectangle the press probe can sample.
    /// </summary>
    [Fact]
    public void The_layout_receipts_describe_every_page_and_name_the_credits_probe()
    {
        var reading = ComposeReading();
        var receipts = reading.Receipts;

        Assert.Equal("reading", receipts.Mode);
        Assert.Equal(14, receipts.Pages.Count);
        Assert.Equal(Enumerable.Range(1, 14), receipts.Pages.Select(page => page.Page));

        Assert.Equal(
            [
                "cover-front", "endpaper-front", "intro",
                "spread-01", "spread-02", "spread-03", "spread-04",
                "spread-05", "spread-06", "spread-07", "spread-08",
                "credits", "endpaper-rear", "cover-back",
            ],
            receipts.Pages.Select(page => page.Role));

        // Every page that places a raster hashes it, and a hash is a hash.
        foreach (var page in receipts.Pages.Where(page => page.ImageSha256.Count > 0))
        {
            Assert.All(page.ImageSha256, hash => Assert.Matches("^[0-9a-f]{64}$", hash));
        }

        // The credits probe: the one page in the book whose ground is flat and whose type is light,
        // which is the page audit P0-07 found unreadable after CMYK conversion.
        var probe = Assert.Single(receipts.FlatGroundTextProbes);
        Assert.Equal("credits-text", probe.Role);
        Assert.Equal(12, probe.Page);
        Assert.True(probe.XMm >= SpreadWidthMm / 2d,
            $"the credits text probe starts at {probe.XMm:F1} mm — it must be on the right leaf.");
        Assert.True(probe.XMm + probe.WidthMm <= SpreadWidthMm);
        Assert.True(probe.HeightMm > 0 && probe.YMm > 0);

        // It is the shape print prep takes, so an integrator hands it over rather than converting.
        BekiPrintProbe handover = new(receipts.LightTextPages, receipts.FlatGroundTextProbes);
        Assert.Contains(12, handover.LightTextPages);

        // And the per-page JSON is what fulfillment stores.
        var creditsPage = receipts.Pages[11];
        Assert.Equal("page-12-layout.json", creditsPage.FileName);

        using var json = JsonDocument.Parse(creditsPage.ToJson());
        Assert.Equal("credits", json.RootElement.GetProperty("role").GetString());
        Assert.Equal(
            "credits-text",
            json.RootElement.GetProperty("text_probe").GetProperty("role").GetString());
        Assert.Equal(12, json.RootElement.GetProperty("text_probe").GetProperty("page").GetInt32());

        using var whole = JsonDocument.Parse(receipts.ToJson());
        Assert.Equal(14, whole.RootElement.GetProperty("pages").GetArrayLength());
    }

    /// <summary>
    /// A wash that would cross the fold stops the book rather than printing.
    ///
    /// Story Spread 4 of the rejected release carried "a large raster rectangle crossing the fold",
    /// and nothing in the pipeline was in a position to see it. Provoked here by widening the text
    /// column until its wash reaches the centre — the one geometry change that can produce the
    /// defect from inside the layout.
    /// </summary>
    [Fact]
    public void A_wash_that_would_reach_the_fold_stops_the_book()
    {
        var layout = ReadingLayout();
        layout.TextColumnShare = 0.7f;
        layout.MaxTextWidthMm = 400f;

        var failure = Assert.Throws<BekiLayoutException>(() =>
            new BekiPdfComposer(Options.Create(layout)).ComposeReading(
                Plan(), WrapPng(1024, 490), Spreads(), BekiLayoutFixture.Personalization()));

        Assert.Equal(CompositeFailureCodes.LayoutFailed, failure.FailureCode);
        Assert.Contains("fold", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ==============================================================================================
    // The gate that judges a download
    // ==============================================================================================

    /// <summary>
    /// The composed reading copy passes <see cref="BekiDigitalPrep"/> — the preflight that exists to
    /// judge exactly this file, written by another agent against the same amendment.
    ///
    /// This is the strongest thing this suite can assert, because it is not a restatement of the
    /// composer's own beliefs: fourteen pages, the exact trim boxes, a CropBox on every page, no
    /// output intent or PDF/X claim, every raster in a screen colour space, `/Lang ka-GE`, and a
    /// linearized file — all read back out of the bytes after Ghostscript has rewritten them.
    /// </summary>
    [Fact]
    public void The_reading_copy_passes_the_digital_preflight()
    {
        var reading = ComposeReading();

        var (prepared, reportJson) = BekiDigitalPrep.Prepare(
            reading.Pdf, new BekiPrintPrepOptions());

        Assert.NotEmpty(prepared);

        using var report = JsonDocument.Parse(reportJson);
        Assert.Equal("PASS", report.RootElement.GetProperty("verdict").GetString());
        Assert.Equal("DIGITAL_GEOMETRY", report.RootElement.GetProperty("gate").GetString());
        Assert.Equal(
            "ka-GE",
            report.RootElement.GetProperty("colour").GetProperty("document_language").GetString());
    }

    // ==============================================================================================
    // Fixtures
    // ==============================================================================================

    private static BekiPrintLayoutOptions ReadingLayout() => new()
    {
        // The download's own rasters are the fixture's, which are small already; what this keeps at
        // its default is the screen ceiling, so the approved 300-PPI fixed pages are reduced the way
        // a real download reduces them and the test measures the file a parent would get.
        MaxPrintUpscale = 0f,
    };

    private static BekiPdfComposer Composer(BekiPrintLayoutOptions? layout = null) =>
        new(Options.Create(layout ?? ReadingLayout()));

    private static MasterStory Plan() => BekiLayoutFixture.EightSpreadPlan();

    private static List<BekiSpreadArtwork> Spreads() => Plan().Spreads
        .Select(spread => new BekiSpreadArtwork(spread.Number, BekiLayoutFixture.SheetPng((0, 200, 120))))
        .ToList();

    /// <summary>
    /// The fixture book's reading copy, composed once for the whole class. Composing it is the
    /// expensive part of every test here — the approved intro background is a 39-megapixel PNG —
    /// and the pages it produces do not depend on which test is looking at them.
    /// </summary>
    private static readonly Lazy<BekiComposedBook> Reading = new(() =>
        Composer().ComposeReading(
            Plan(), WrapPng(2528, 1210), Spreads(), BekiLayoutFixture.Personalization()));

    private static BekiComposedBook ComposeReading() => Reading.Value;

    private static readonly Lazy<IReadOnlyList<byte[]>> ReadingPages = new(() =>
        RasterizeWithGhostscript(Reading.Value.Pdf));

    private static IReadOnlyList<byte[]> RenderReading() => ReadingPages.Value;

    /// <summary>
    /// A synthetic hardcover wrap at the dieline's own 512 : 245, painted as a left-to-right ramp so
    /// that a crop can be told apart from another crop by looking at it. Never a model call: the
    /// campaign runs no generations (correction plan §3).
    /// </summary>
    private static byte[] WrapPng(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var ramp = (byte)(20 + (x * 200 / Math.Max(1, width - 1)));
                    var down = (byte)(40 + (y * 120 / Math.Max(1, height - 1)));
                    row[x] = new Rgba32(ramp, down, (byte)(255 - ramp));
                }
            }
        });

        using var buffer = new MemoryStream();
        image.SaveAsPng(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// The reading copy as page images. Ghostscript rather than QuestPDF's own rasterizer, because
    /// the question these tests ask is what is IN the produced PDF — a second render from the
    /// composer's model would answer a different one.
    /// </summary>
    private static IReadOnlyList<byte[]> RasterizeWithGhostscript(byte[] pdf)
    {
        var work = Path.Combine(Path.GetTempPath(), $"beki-reader-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);

        try
        {
            var input = Path.Combine(work, "reading.pdf");
            File.WriteAllBytes(input, pdf);

            var start = new System.Diagnostics.ProcessStartInfo
            {
                FileName = new BekiPrintPrepOptions().GhostscriptPath,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };

            foreach (var argument in new[]
            {
                "-dBATCH", "-dNOPAUSE", "-dQUIET", "-dSAFER",
                "-sDEVICE=png16m", "-r36",
                $"-sOutputFile={Path.Combine(work, "page-%02d.png")}",
                input,
            })
            {
                start.ArgumentList.Add(argument);
            }

            using var process = System.Diagnostics.Process.Start(start)!;
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit(120_000);

            return Directory.GetFiles(work, "page-*.png")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllBytes)
                .ToList();
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { /* temp only */ }
        }
    }
}
