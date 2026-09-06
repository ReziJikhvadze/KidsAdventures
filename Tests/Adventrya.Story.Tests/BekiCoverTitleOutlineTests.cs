using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Pdf;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using Microsoft.Extensions.Options;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The cover title has a real border again.
///
/// The owner's complaint on 2026-09-06 is the one ruling 2026-09-01's third already answered and
/// the September gates then took away: cream (<c>#FFF8EB</c>) Ottia sits on a pale sky and the book's
/// own name cannot be read on its own cover. The rim it used to have was sixteen offset copies of
/// the same glyphs, which <c>SINGLE_TEXT_LAYER</c> now refuses by name — so
/// <see cref="BekiPdfComposer"/> was shipping a title with no rim at all and no gate could tell.
///
/// <see cref="BekiTitleOutline"/> puts it back the way PDF has always drawn rims: text rendering
/// mode 2, one text object, one set of glyphs, filled AND stroked. Four things have to hold at once
/// for that to be the answer rather than another trade, and each has a test here — the operators are
/// in the file, no other page acquired them, the pixels show a dark ring around cream letters, and
/// the two preflight gates that killed the previous treatment both still pass.
/// </summary>
public class BekiCoverTitleOutlineTests
{
    /// <summary>The rim ink, <c>#0D071D</c>, as the three operands a content stream carries.</summary>
    private const string OutlineInkOperands = "0.05098 0.027451 0.113725";

    /// <summary>
    /// A twelve-page canonical book, composed once: the cover, the endpaper, the intro, eight story
    /// spreads and the credits. Composing it is the expensive part of every test in this file and
    /// none of them changes it.
    /// </summary>
    private static readonly Lazy<BekiComposedBook> Canonical = new(() => ComposeCanonical());

    // ==============================================================================================
    // The operators
    // ==============================================================================================

    /// <summary>
    /// Page 1's title is filled and stroked from the text objects QuestPDF already wrote.
    ///
    /// Every clause of the treatment is asserted separately, because each of them is load bearing:
    /// <c>2 Tr</c> is fill-and-stroke rather than stroke-only (which would have thrown the cream
    /// away and taken <c>TEXT_COLOR_INTEGRITY</c>'s evidence with it), the pen is the configured
    /// width, the ink is the rim's and not the fill's, and round joins and caps are what keep a
    /// Georgian letter's corners from growing spikes at the weight the owner asked for.
    ///
    /// The count is the single-layer contract restated in the file itself: as many stroked states as
    /// there are text objects, and the glyphs shown once inside them.
    /// </summary>
    [Fact]
    public void The_cover_title_is_stroked_and_filled_from_one_text_object_per_line()
    {
        var content = PageContent(Canonical.Value.Pdf, 1);

        Assert.Contains("2 Tr", content, StringComparison.Ordinal);
        Assert.Contains(
            $"2 Tr {Pen(new BekiPrintLayoutOptions().CoverTitleOutlineWidthPt)} w "
            + $"{OutlineInkOperands} RG 1 j 1 J",
            content,
            StringComparison.Ordinal);

        // The wrap's title breaks over two lines, and Skia writes one text object per line.
        var textObjects = Operators(content, "BT");
        Assert.Equal(2, textObjects);
        Assert.Equal(textObjects, Operators(content, "Tr"));
        Assert.Equal(textObjects, Operators(content, "ET"));

        // The fill is exactly what the layout authored. A rim that had replaced it would read as a
        // pass to the colour gate while printing a title nobody can see — audit P0-07's own shape.
        Assert.Contains("1 .9725 .9216 rg", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing but the cover acquired a rendering mode.
    ///
    /// The interior's copy has sat on its own translucent panel since ruling 2026-09-01's fourth and
    /// is dark ink on cream, which needs no border; a stroke that had leaked onto those pages would
    /// be a change to the book's typography that nobody asked for. The post-processing step touches
    /// page 1 and this is the assertion that says so.
    /// </summary>
    [Fact]
    public void No_interior_page_carries_a_text_rendering_mode()
    {
        var composed = Canonical.Value;

        using var stream = new MemoryStream(composed.Pdf);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        Assert.Equal(12, document.PageCount);

        for (var page = 2; page <= document.PageCount; page++)
        {
            Assert.Equal(0, Operators(PageContent(composed.Pdf, page), "Tr"));
        }
    }

    /// <summary>
    /// The rim is on the page the customer downloads too, scaled with the type it surrounds, and
    /// both covers say in their receipts how wide it was drawn.
    ///
    /// Audit P0-01 was about exactly this class of divergence — the printed cover and the downloaded
    /// one being two different designs — so a rim that only the press file carried would be the same
    /// finding with a different noun.
    /// </summary>
    [Fact]
    public void Both_covers_carry_the_rim_and_their_receipts_state_its_width()
    {
        var layout = new BekiPrintLayoutOptions();

        var canonicalTitle = Assert.Single(
            Canonical.Value.Receipts.Pages.Single(page => page.Role == "cover-wrap").Typography);
        Assert.Equal(layout.CoverTitleOutlineWidthPt, canonicalTitle.TitleOutlineWidthPt!.Value, 6);

        var scaled = layout.CoverTitleOutlineWidthPt * BekiCoverDieline.DigitalScale;
        var reading = ComposeReadingCopy();
        var readingTitle = Assert.Single(
            reading.Receipts.Pages.Single(page => page.Role == "cover-front").Typography);
        Assert.Equal(scaled, readingTitle.TitleOutlineWidthPt!.Value, 6);

        // And the download's own content stream carries that width, not the press one: the pen is
        // divided by whatever the page's transform does to lengths, so this is also the assertion
        // that the arithmetic survives a second page geometry.
        var content = PageContent(reading.Pdf, 1);
        Assert.Equal(Operators(content, "BT"), Operators(content, "Tr"));
        Assert.Contains(
            $"2 Tr {Pen(scaled)} w {OutlineInkOperands} RG 1 j 1 J",
            content,
            StringComparison.Ordinal);

        // A block with no rim says nothing rather than saying zero: the credits page is dark ink on
        // a flat ground and has never had a border.
        Assert.All(
            Canonical.Value.Receipts.Pages.Single(page => page.Role == "credits").Typography,
            type => Assert.Null(type.TitleOutlineWidthPt));
    }

    /// <summary>
    /// The width is configuration, and zero is honestly zero — which is also the control the pixel
    /// test below measures itself against.
    /// </summary>
    [Fact]
    public void A_zero_width_leaves_the_title_exactly_as_it_was()
    {
        var content = PageContent(Unstroked.Value.Pdf, 1);

        Assert.Equal(0, Operators(content, "Tr"));
        Assert.Contains("1 .9725 .9216 rg", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// The face is named by the file the bootstrap already permits, so the two cannot drift apart
    /// into a step that silently strokes nothing.
    /// </summary>
    [Fact]
    public void The_title_face_the_step_looks_for_is_the_whitelisted_one()
    {
        Assert.Contains(BekiTitleOutline.TitleFaceFileName, PdfFontBootstrap.BekiFontWhitelist);
    }

    // ==============================================================================================
    // The pixels
    // ==============================================================================================

    /// <summary>
    /// What the owner actually asked about: on the rendered cover, every cream pixel of the title
    /// has the rim's near-black within three pixels of it, and without the rim none of them does.
    ///
    /// A hundred dots per inch is the density this is honest at — the rim is 1.5 pt, which is two
    /// pixels here, so a ring that survives being resolved at all is a ring a press will hold. The
    /// control matters as much as the measurement: the same page composed with the width at zero is
    /// the file that shipped this morning, and it scores zero on the same test.
    /// </summary>
    [SkippableFact]
    public void The_rendered_title_is_cream_glyphs_inside_a_dark_ring()
    {
        Skip.IfNot(PopplerInstalled(), "Poppler (pdftoppm) is not installed on this machine.");

        var stroked = Ring(Canonical.Value.Pdf);
        Assert.True(stroked.Cream > 500, $"only {stroked.Cream} cream pixels were rendered.");
        Assert.True(stroked.Rim > 500, $"only {stroked.Rim} rim pixels were rendered.");
        Assert.True(
            stroked.Rimmed >= 0.99d,
            $"only {stroked.Rimmed:P1} of the title's cream pixels have the rim within three pixels.");

        var bare = Ring(Unstroked.Value.Pdf);
        Assert.True(bare.Cream > 500, $"the control rendered only {bare.Cream} cream pixels.");
        Assert.Equal(0, bare.Rim);
    }

    // ==============================================================================================
    // The gates the previous rim died on
    // ==============================================================================================

    /// <summary>
    /// The two gates that refused the offset-copy outline both pass on the stroked one.
    ///
    /// <c>SINGLE_TEXT_LAYER</c> counts text-showing operators and finds the layout's own budget;
    /// <c>TEXT_COLOR_INTEGRITY</c> reads the fill colour in force at each of them and finds the
    /// cream. Neither is asked to be lenient about anything: the rim is simply not made of text
    /// draws, and it is not made of the fill.
    ///
    /// The fixture's low-resolution spreads still fail <c>PRESS_RESOLUTION</c>, as they do
    /// everywhere else in this suite; that is the fixture, not the cover.
    /// </summary>
    [Fact]
    public void The_stroked_title_passes_single_text_layer_and_text_colour_integrity()
    {
        var composed = Canonical.Value;

        var prepared = BekiPrintPrep.PrepareWithGates(
            composed.Pdf,
            "cover title rim",
            new BekiPrintPrepOptions { OutputIntentIccPath = "missing.icc", GhostscriptPath = "missing-gs" },
            probe: new BekiPrintProbe(
                composed.Receipts.LightTextPages,
                composed.Receipts.FlatGroundTextProbes,
                composed.Receipts.MaximumVisibleTextDrawsByPage),
            canonicalMixedGeometry: true,
            acceptRgbForScopedDelivery: true);

        Assert.DoesNotContain(BekiPrintPrep.SingleTextLayerGate, prepared.FailedGates);
        Assert.DoesNotContain(BekiPrintPrep.TextColorIntegrityGate, prepared.FailedGates);
        Assert.Contains($"\"{BekiPrintPrep.SingleTextLayerGate}\"", prepared.ReportJson);
    }

    // ==============================================================================================
    // Fixtures and measurement
    // ==============================================================================================

    /// <summary>The same book with the rim turned off — the cover as it shipped before today.</summary>
    private static readonly Lazy<BekiComposedBook> Unstroked =
        new(() => ComposeCanonical(width: 0f));

    private static BekiComposedBook ComposeCanonical(float? width = null)
    {
        var layout = BekiLayoutFixture.ScreenProofLayout();
        if (width is { } configured) layout.CoverTitleOutlineWidthPt = configured;

        var plan = BekiLayoutFixture.EightSpreadPlan();

        return new BekiPdfComposer(Options.Create(layout)).ComposeCanonicalWithReceipts(
            plan,
            Png(512, 245),
            plan.Spreads.Select(spread => new BekiSpreadArtwork(spread.Number, Png(150, 70))).ToList(),
            BekiLayoutFixture.Personalization());
    }

    private static BekiComposedBook ComposeReadingCopy()
    {
        var plan = BekiLayoutFixture.EightSpreadPlan();

        return new BekiPdfComposer(Options.Create(BekiLayoutFixture.ScreenProofLayout())).ComposeReading(
            plan,
            Png(1024, 490),
            plan.Spreads.Select(spread => new BekiSpreadArtwork(spread.Number, Png(150, 70))).ToList(),
            BekiLayoutFixture.Personalization());
    }

    /// <summary>A flat sheet of the fixture's own grey, which is neither the cream nor the rim.</summary>
    private static byte[] Png(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(180, 170, 160));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// One page's content operators, decoded — the array's pieces concatenated the way a viewer
    /// concatenates them, so an assertion never depends on where the file happened to cut them.
    /// </summary>
    private static string PageContent(byte[] pdf, int page)
    {
        using var stream = new MemoryStream(pdf);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);

        return Encoding.Latin1.GetString(
            document.Pages[page - 1].Contents.CreateSingleContent().Stream.UnfilteredValue);
    }

    /// <summary>
    /// How many times an operator is executed. None of the three names counted here can occur
    /// inside a hex string — <c>T</c> is not a hex digit — and the lookbehind is what keeps a
    /// resource name such as <c>/Tr1</c> from being read as one.
    /// </summary>
    private static int Operators(string content, string name) =>
        Regex.Matches(content, $@"(?<![A-Za-z0-9/#]){Regex.Escape(name)}(?![A-Za-z0-9])").Count;

    private static string Pen(double widthPt) =>
        widthPt.ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>
    /// The rendered cover, counted: how much of the title is cream, how much is rim, and what share
    /// of the cream has rim within three pixels of it.
    ///
    /// Exact colours rather than thresholds, because both are flat inks printed over a flat fixture
    /// ground and the question is whether the operators reached the raster at all. Antialiased edge
    /// pixels belong to neither count and are not asked to.
    /// </summary>
    private static (int Cream, int Rim, double Rimmed) Ring(byte[] pdf)
    {
        var work = Path.Combine(Path.GetTempPath(), $"beki-title-rim-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);

        try
        {
            var input = Path.Combine(work, "cover.pdf");
            File.WriteAllBytes(input, pdf);
            Render(input, Path.Combine(work, "page"));

            var rendered = Directory.EnumerateFiles(work, "page*.png").Single();
            using var image = Image.Load<Rgb24>(rendered);

            var cream = new List<(int X, int Y)>();
            var rim = new HashSet<(int X, int Y)>();

            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    var pixel = image[x, y];
                    if (pixel is { R: 255, G: 248, B: 235}) cream.Add((x, y));
                    else if (pixel is { R: 13, G: 7, B: 29 }) rim.Add((x, y));
                }
            }

            var rimmed = cream.Count == 0
                ? 0d
                : (double)cream.Count(point => Near(rim, point)) / cream.Count;

            return (cream.Count, rim.Count, rimmed);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { /* temp only */ }
        }
    }

    private static bool Near(HashSet<(int X, int Y)> rim, (int X, int Y) point)
    {
        for (var dy = -3; dy <= 3; dy++)
        {
            for (var dx = -3; dx <= 3; dx++)
            {
                if (rim.Contains((point.X + dx, point.Y + dy))) return true;
            }
        }

        return false;
    }

    private static void Render(string input, string output)
    {
        using var process = Process.Start(new ProcessStartInfo("pdftoppm")
        {
            ArgumentList = { "-f", "1", "-l", "1", "-r", "100", "-png", input, output },
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;

        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"pdftoppm exited {process.ExitCode}: {error}");
    }

    private static bool PopplerInstalled() =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Any(directory => File.Exists(Path.Combine(directory, "pdftoppm")));
}
