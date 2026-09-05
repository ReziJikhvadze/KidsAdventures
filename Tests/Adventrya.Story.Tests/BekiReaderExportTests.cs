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

            // And both carry the same panel under the words (owner ruling 2026-09-01, the fourth),
            // at the same place on the TRIM: the press page's rectangle is the download's moved in
            // by the bleed on both axes, its size and its ink are identical, and the clearances —
            // measured to the trim and to the fold, which move with the bleed — do not move at all.
            var readingPanel = fromReading.Wash;
            var pressPanel = fromPress.Wash;

            Assert.NotNull(readingPanel);
            Assert.NotNull(pressPanel);

            var bleedMm = (double)ReadingLayout().BleedMm;
            Assert.Equal(readingPanel!.XMm + bleedMm, pressPanel!.XMm, 2);
            Assert.Equal(readingPanel.YMm + bleedMm, pressPanel.YMm, 2);
            Assert.Equal(readingPanel.WidthMm, pressPanel.WidthMm, 2);
            Assert.Equal(readingPanel.HeightMm, pressPanel.HeightMm, 2);
            Assert.Equal(readingPanel.Ink, pressPanel.Ink);
            Assert.Equal(readingPanel.PageSide, pressPanel.PageSide);
            Assert.Equal(readingPanel.FoldClearanceMm, pressPanel.FoldClearanceMm, 2);
            Assert.Equal(readingPanel.TrimClearanceMm, pressPanel.TrimClearanceMm, 2);
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
    // No box behind the words — owner ruling 2026-09-01, third and final
    // ==============================================================================================

    /// <summary>
    /// The intro and the eight story spreads each record the panel under their copy, no other page
    /// records one, and the story copy is the cream the outline stack fills with.
    ///
    /// The receipt half of owner ruling 2026-09-01 (the fourth: "transparent-like background, but
    /// not too transparent"), made on the receipts because the receipts are what the gates read: a
    /// panel drawn but not recorded would still fail
    /// <see cref="The_copy_sits_on_a_translucent_copy_sized_panel"/>, which looks at pixels, and a
    /// panel recorded but not drawn would fail this one. Between them there is nowhere for the
    /// panel to be other than where the receipt says it is.
    /// </summary>
    [Fact]
    public void Every_copy_page_records_its_cream_wash_and_dark_story_copy()
    {
        var reading = ComposeReading();
        var layout = ReadingLayout();

        // The September 4 local cream wash at 86%, spelled as QuestPDF's ARGB value.
        const string PanelInk = "#DBFFF8EB";

        var copyPages = reading.Receipts.Pages
            .Where(page => page.Role == "intro" || page.Role.StartsWith("spread-"))
            .ToList();

        Assert.Equal(9, copyPages.Count);

        foreach (var page in copyPages)
        {
            var panel = page.Wash;

            Assert.True(panel is not null,
                $"{page.Role} records no panel; owner ruling 2026-09-01 (the fourth) puts a "
                + "translucent panel under every block of story and intro copy.");

            Assert.Equal(PanelInk, panel!.Ink);
            Assert.Equal(layout.WashPaddingMm, panel.PaddingMm, 3);
            Assert.Equal(layout.WashCornerRadiusMm, panel.CornerRadiusMm, 3);

            // Inside the trim by the safe margin, and never within the fold safety area — the same
            // two numbers the composer refuses a column on.
            Assert.True(panel.TrimClearanceMm >= layout.SafeMarginMm - 0.05,
                $"{page.Role}'s panel comes within {panel.TrimClearanceMm:0.#} mm of the trim.");
            Assert.True(panel.FoldClearanceMm >= layout.FoldSafetyMm - 0.05,
                $"{page.Role}'s panel comes within {panel.FoldClearanceMm:0.#} mm of the fold.");

            // Stated from the download's own top-left corner, which has no bleed on it.
            Assert.True(panel.XMm >= layout.SafeMarginMm - 0.05);
            Assert.True(panel.YMm >= layout.SafeMarginMm - 0.05);
            Assert.True(panel.XMm + panel.WidthMm <= SpreadWidthMm - layout.SafeMarginMm + 0.05);
            Assert.True(panel.YMm + panel.HeightMm <= PageHeightMm - layout.SafeMarginMm + 0.05);

            // Copy-sized: the column's height is the measured block plus the inset on each side, so
            // on the fixture's one-sentence spreads and the intro's four lines it is well short of
            // the leaf's safe area. A panel the height of the safe area is the slab the shipped
            // book had.
            var safeAreaMm = PageHeightMm - (2 * layout.SafeMarginMm);
            Assert.True(panel.HeightMm < safeAreaMm / 2,
                $"{page.Role}'s panel is {panel.HeightMm:0.#} mm tall on a leaf whose safe area is "
                + $"{safeAreaMm:0.#} mm; that is a slab, not a shade.");

            Assert.NotEmpty(page.Typography);

            // The override sets one dark vector layer over the cream wash.
            Assert.All(page.Typography, type =>
                Assert.Equal("#281B3F", type.Colour));

            Assert.Contains(page.Typography, type => type.Colour == "#281B3F");
        }

        // No panel where no copy sits over artwork: the two covers, the endpapers, and the credits
        // page, whose ground is now the opening cream and whose type needs neither rim nor shade.
        foreach (var page in reading.Receipts.Pages
            .Where(page => page.Role != "intro" && !page.Role.StartsWith("spread-")))
        {
            Assert.True(page.Wash is null,
                $"{page.Role} records a panel, and it sets no story or intro copy over artwork.");
        }

        var credits = reading.Receipts.Pages.Single(page => page.Role == "credits");
        Assert.All(credits.Typography, type => Assert.Equal("#281B3F", type.Colour));

        // The JSON a gate actually opens carries the block under the name receipts have always
        // used for the shape under the copy, with the ink in it — and, read back, a page with no
        // panel is still a page. Receipts written during the rim-only campaign carry no block at
        // all, and they have to go on parsing.
        var json = reading.Receipts.ToJson();
        Assert.Contains("\"wash\"", json, StringComparison.Ordinal);
        Assert.Contains(PanelInk, json, StringComparison.Ordinal);

        var readBack = JsonSerializer.Deserialize<BekiLayoutReceipts>(json, BekiLayoutReceipts.JsonOptions);
        Assert.NotNull(readBack);
        Assert.Equal(PanelInk, readBack!.Pages.Single(page => page.Role == "intro").Wash!.Ink);
        Assert.Null(readBack.Pages.Single(page => page.Role == "credits").Wash);
    }

    /// <summary>
    /// And on the page itself the panel is there, it is translucent, and it is the copy's size —
    /// the three clauses of "transparent-like background, but not too transparent", each measured
    /// against the receipt's own rectangle on the fixture's flat green artwork, which is what makes
    /// every answer unambiguous.
    ///
    /// The strip between the panel's top edge and its first line is the panel with no type on it.
    /// There the picture must be darker than the bare artwork and still green: the plum at sixty
    /// per cent over (0, 200, 120) leaves green the strongest channel, where an opaque plum would
    /// leave blue the strongest and no panel would leave the green untouched. Below the receipt's
    /// rectangle the artwork must be exactly itself, because the panel stops where the copy stops.
    /// And the panel's own edges, found on the page, must agree with the receipt: the top and the
    /// left where the column starts, the bottom where the column's height says, the right no
    /// further than the column's width — a panel shrink-wrapped to its widest line may stop sooner.
    /// </summary>
    [Fact]
    public void The_copy_sits_on_a_translucent_copy_sized_panel()
    {
        var reading = ComposeReading();
        var pages = RenderReading();

        var spread = reading.Receipts.Pages.First(page => page.Role.StartsWith("spread-"));
        var panel = spread.Wash;
        Assert.NotNull(panel);

        using var image = Image.Load<Rgba32>(pages[spread.Page - 1]);
        var pxPerMm = image.Width / SpreadWidthMm;

        int Px(double mm) => (int)Math.Round(mm * pxPerMm);

        // The artwork, read off the leaf's own lower half rather than assumed.
        var artwork = image[Px(60), Px(150)];
        Assert.True(IsArtwork(artwork),
            $"the leaf's ground is #{artwork.R:X2}{artwork.G:X2}{artwork.B:X2}, not the fixture's green.");

        // 1. Inside, on the padding strip along the panel's top edge — from 2 mm in to 2 mm short
        //    of the copy, and from 6 mm in on the left (clear of the 4 mm corner) to 20 mm, which
        //    is inside the widest line of any spread the fixture sets.
        var shaded = new List<Rgba32>();
        for (var y = Px(panel!.YMm + 2); y < Px(panel.YMm + panel.PaddingMm - 2); y++)
        {
            for (var x = Px(panel.XMm + 6); x < Px(panel.XMm + 20); x++)
            {
                shaded.Add(image[x, y]);
            }
        }

        Assert.True(shaded.Count >= 20, "the padding strip is too small to sample at this raster.");

        var ratio = shaded.Average(Luma) / Luma(artwork);

        Assert.True(ratio > 1.2,
            $"Inside the wash the artwork is only {ratio:P0} as bright as outside it; the cream "
            + "contrast support is missing or too transparent.");
        Assert.True(ratio < 1.8,
            $"Inside the wash the artwork is {ratio:P0} as bright as outside it; the illustration "
            + "no longer shows through the local cream wash.");
        Assert.All(shaded, pixel => Assert.True(pixel.G > pixel.B && pixel.G > pixel.R,
            $"#{pixel.R:X2}{pixel.G:X2}{pixel.B:X2} inside the panel is not green any more; the "
            + "picture has to show through the shade."));

        // 2. Below the rectangle the receipt states, the artwork is itself again: the panel stops
        //    where the copy stops.
        for (var y = Px(panel.YMm + panel.HeightMm + 2); y < Px(panel.YMm + panel.HeightMm + 10); y++)
        {
            for (var x = Px(panel.XMm + 6); x < Px(panel.XMm + 20); x++)
            {
                var pixel = image[x, y];
                Assert.True(IsArtwork(pixel),
                    $"#{pixel.R:X2}{pixel.G:X2}{pixel.B:X2} at ({x / pxPerMm:0.#}, {y / pxPerMm:0.#}) mm "
                    + "is below the panel and is not the artwork; the panel has outgrown its copy.");
            }
        }

        // 3. The panel's edges, found on the page, against the receipt's rectangle — scanned along
        //    a row through the padding strip and a column through the copy, where the only thing
        //    that is not artwork is the panel and what sits on it.
        //
        //    Both scans leave out the outermost millimetre of the sheet. Ghostscript paints the
        //    page box's own edge row white where the placed raster falls a fraction of a pixel
        //    short of it, and that white row belongs to the rasteriser rather than to the book: it
        //    would otherwise be found as the topmost thing that is not artwork and read as a panel
        //    starting at 0 mm. Nothing the book draws comes within a millimetre of the trim — the
        //    safe margin is twelve — so nothing this test is about is inside the strip skipped.
        var probeRow = Px(panel.YMm + (panel.PaddingMm / 2));
        var probeColumn = Px(panel.XMm + 10);
        var edge = Px(1);

        var found = Enumerable.Range(edge, image.Width - (2 * edge))
            .Where(x => !IsArtwork(image[x, probeRow])).ToList();
        var foundRows = Enumerable.Range(edge, image.Height - (2 * edge))
            .Where(y => !IsArtwork(image[probeColumn, y])).ToList();

        Assert.NotEmpty(found);
        Assert.NotEmpty(foundRows);

        var left = found.Min() / pxPerMm;
        var right = (found.Max() + 1) / pxPerMm;
        var top = foundRows.Min() / pxPerMm;
        var bottom = (foundRows.Max() + 1) / pxPerMm;

        // A pixel and a half at this raster.
        var tolerance = 1.5 / pxPerMm;

        Assert.InRange(left, panel.XMm - tolerance, panel.XMm + tolerance);
        Assert.InRange(top, panel.YMm - tolerance, panel.YMm + tolerance);
        Assert.InRange(bottom, panel.YMm + panel.HeightMm - tolerance, panel.YMm + panel.HeightMm + tolerance);
        Assert.True(right <= panel.XMm + panel.WidthMm + tolerance,
            $"the panel runs to {right:0.#} mm; its column ends at {panel.XMm + panel.WidthMm:0.#} mm.");
        Assert.True(right >= panel.XMm + 20,
            $"the panel ends at {right:0.#} mm, inside the strip this test samples as panel.");

        // 4. And type is still type: dark ink is a small share of the wash's own area.
        var ink = 0;
        var total = 0;
        for (var y = Px(panel.YMm); y < Px(panel.YMm + panel.HeightMm); y++)
        {
            for (var x = Px(panel.XMm); x < Px(right); x++)
            {
                total++;
                if (IsStoryInk(image[x, y])) ink++;
            }
        }

        Assert.True(ink > 0, "no dark story ink was found on the cream wash.");

        var share = (double)ink / total;
        Assert.True(share < 0.25,
            $"{share:P0} of the wash is dark ink; a quarter or more is not a text-sized treatment.");
    }

    private static bool IsCream(Rgba32 pixel)
        => pixel.R > 200 && pixel.G > 195 && pixel.B > 170 && pixel.B < pixel.R;

    private static bool IsStoryInk(Rgba32 pixel)
        => pixel.R < 90 && pixel.G < 80 && pixel.B < 120;

    /// <summary>The fixture's flat green, (0, 200, 120), with room for the rasteriser.</summary>
    private static bool IsArtwork(Rgba32 pixel)
        => pixel.R < 25 && Math.Abs(pixel.G - 200) < 25 && Math.Abs(pixel.B - 120) < 25;

    private static double Luma(Rgba32 pixel)
        => (0.2126d * pixel.R) + (0.7152d * pixel.G) + (0.0722d * pixel.B);

    // ==============================================================================================
    // Receipts — A4 / D7
    // ==============================================================================================

    /// <summary>
    /// The receipts are per page, in page order, and they carry what amendment A4 asks a layout to
    /// answer for: the hashes of the rasters actually placed, the wash, the type, the lines, and —
    /// for the credits page — a rectangle the press probe can sample.
    /// </summary>
    [Fact]
    public void The_layout_receipts_describe_every_page_and_dark_credits_need_no_light_probe()
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

        // September 5 credits are dark-on-cream, not a light-on-dark conversion probe.
        Assert.Empty(receipts.FlatGroundTextProbes);

        // It is the shape print prep takes, so an integrator hands it over rather than converting.
        BekiPrintProbe handover = new(receipts.LightTextPages, receipts.FlatGroundTextProbes);
        Assert.DoesNotContain(12, handover.LightTextPages);

        // And the per-page JSON is what fulfillment stores.
        var creditsPage = receipts.Pages[11];
        Assert.Equal("page-12-layout.json", creditsPage.FileName);

        using var json = JsonDocument.Parse(creditsPage.ToJson());
        Assert.Equal("credits", json.RootElement.GetProperty("role").GetString());
        Assert.False(json.RootElement.TryGetProperty("text_probe", out _));

        using var whole = JsonDocument.Parse(receipts.ToJson());
        Assert.Equal(14, whole.RootElement.GetProperty("pages").GetArrayLength());
    }

    /// <summary>
    /// A copy column that would cross the fold stops the book rather than printing.
    ///
    /// Story Spread 4 of the rejected release carried "a large raster rectangle crossing the fold",
    /// and nothing in the pipeline was in a position to see it. The rectangle is gone with the wash
    /// (owner ruling 2026-09-01, third and final) and the rule is not: words that run into the
    /// gutter are unreadable whether or not there is a box behind them. Provoked here by widening
    /// the text column until it reaches the centre — the one geometry change that can produce the
    /// defect from inside the layout.
    /// </summary>
    [Fact]
    public void A_copy_column_that_would_reach_the_fold_stops_the_book()
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
    ///
    /// And, since pack 597344af, one more: every ICC profile the finished file points at is a
    /// profile a strict viewer can read. That book passed every gate there was and opened in Chrome
    /// as fourteen pages of flat colour, because Ghostscript had written one of its two colour
    /// spaces as `&lt;&lt;/N 3/Length 0&gt;&gt;` and nothing looked inside. The proof runs on the
    /// real composed book here — the one whose covers and credits mark are the PNGs that carry the
    /// profile the defect turns on.
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

        // Every colour space carries a profile, and Poppler renders the book without a word about
        // any of them. The second half is what would have caught the shipped book: its renderer is
        // forgiving enough to draw the pages anyway, and the complaint was only ever on stderr.
        Assert.Empty(BekiDigitalPrep.IccProfileProblems(prepared));
        Assert.DoesNotContain(
            "/N 3/Length 0", Encoding.Latin1.GetString(prepared), StringComparison.Ordinal);

        Poppler.AssertRendersCleanly(prepared);
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
