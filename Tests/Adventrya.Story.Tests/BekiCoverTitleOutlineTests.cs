using System.Diagnostics;
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

public class BekiCoverTitleOutlineTests
{
    private const string OutlineInkOperands = "0.05098 0.027451 0.113725";
    private static readonly Lazy<BekiComposedBook> Canonical = new(() => ComposeCanonical());

    // ==============================================================================================
    // The operators
    // ==============================================================================================
    [Fact]
    public void The_cover_title_is_filled_paths_without_any_font_or_text_operator()
    {
        var pdf = Canonical.Value.Pdf;
        var content = PageContent(pdf, 1);
        Assert.Equal(0, Operators(content, "BT"));
        Assert.Equal(0, Operators(content, "Tj"));
        Assert.Equal(0, Operators(content, "TJ"));
        Assert.Equal(0, Operators(content, "Tr"));
        Assert.True(Operators(content, "m") > 20);
        Assert.True(Operators(content, "f") > 10);
        Assert.Contains(OutlineInkOperands + " RG", content);
        using var input = new MemoryStream(pdf);
        using var document = PdfReader.Open(input, PdfDocumentOpenMode.Import);
        Assert.Null(document.Pages[0].Elements.GetDictionary("/Resources")?.Elements.GetDictionary("/Font"));
        if (Environment.GetEnvironmentVariable("BEKI_OUTLINE_PROOF_PATH") is { Length: > 0 } proof)
            File.WriteAllBytes(proof, pdf);
    }

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

    [Fact]
    public void Both_covers_carry_the_rim_and_their_receipts_state_its_width()
    {
        var layout = new BekiPrintLayoutOptions();

        var canonicalTitle = Assert.Single(
            Canonical.Value.Receipts.Pages.Single(page => page.Role == "cover-wrap").Typography);
        var fittedWidth = layout.CoverTitleOutlineWidthPt * 32f / 36f;
        Assert.Equal(fittedWidth, canonicalTitle.TitleOutlineWidthPt!.Value, 6);

        var scaled = fittedWidth * BekiCoverDieline.DigitalScale;
        var reading = ComposeReadingCopy();
        var readingTitle = Assert.Single(
            reading.Receipts.Pages.Single(page => page.Role == "cover-front").Typography);
        Assert.Equal(scaled, readingTitle.TitleOutlineWidthPt!.Value, 6);

        // And the download's own content stream carries that width, not the press one: the pen is
        // divided by whatever the page's transform does to lengths, so this is also the assertion
        // that the arithmetic survives a second page geometry.
        var content = PageContent(reading.Pdf, 1);
        Assert.Equal(0, Operators(content, "BT"));
        Assert.Equal(0, Operators(content, "Tr"));
        Assert.Contains(OutlineInkOperands + " RG", content);
        Assert.True(Operators(content, "S") > 10);

        // A block with no rim says nothing rather than saying zero: the credits page is dark ink on
        // a flat ground and has never had a border.
        Assert.All(
            Canonical.Value.Receipts.Pages.Single(page => page.Role == "credits").Typography,
            type => Assert.Null(type.TitleOutlineWidthPt));
    }

    [Fact]
    public void A_zero_width_still_exports_the_title_as_filled_paths()
    {
        var content = PageContent(Unstroked.Value.Pdf, 1);

        Assert.Equal(0, Operators(content, "Tr"));
        Assert.Equal(0, Operators(content, "BT"));
        Assert.True(Operators(content, "f") > 10);
    }

    [Fact]
    public void The_title_face_the_step_looks_for_is_the_whitelisted_one()
    {
        Assert.Contains(BekiTitleOutline.TitleFaceFileName, PdfFontBootstrap.BekiFontWhitelist);
    }

    // ==============================================================================================
    // The pixels
    // ==============================================================================================
    [SkippableFact]
    public void The_rendered_title_is_cream_glyphs_inside_a_dark_ring()
    {
        Skip.IfNot(PopplerInstalled(), "Poppler (pdftoppm) is not installed on this machine.");

        var stroked = Ring(Canonical.Value.Pdf);
        Assert.True(stroked.Cream > 500, $"only {stroked.Cream} cream pixels were rendered.");
        Assert.True(stroked.Rim > 500, $"only {stroked.Rim} rim pixels were rendered.");
        Assert.True(
            stroked.Rimmed >= 0.99d,
            $"only {stroked.Rimmed:P1} of the bold title's cream pixels have the rim within four pixels.");

        var bare = Ring(Unstroked.Value.Pdf);
        Assert.True(bare.Cream > 500, $"the control rendered only {bare.Cream} cream pixels.");
        Assert.Equal(0, bare.Rim);
        Assert.True(stroked.Cream > bare.Cream * 0.9,
            "The border consumed too much of the bold title's cream fill.");
    }

    // ==============================================================================================
    // The gates the previous rim died on
    // ==============================================================================================
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
    private static byte[] Png(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(180, 170, 160));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }
    private static string PageContent(byte[] pdf, int page)
    {
        using var stream = new MemoryStream(pdf);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);

        return Encoding.Latin1.GetString(
            document.Pages[page - 1].Contents.CreateSingleContent().Stream.UnfilteredValue);
    }
    private static int Operators(string content, string name) =>
        Regex.Matches(content, $@"(?<![A-Za-z0-9/#]){Regex.Escape(name)}(?![A-Za-z0-9])").Count;

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

            image.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        var pixel = row[x];
                        if (pixel is { R: 255, G: 248, B: 235}) cream.Add((x, y));
                        // The outside-only rim is antialiased at 100 DPI. Count dark edge
                        // pixels as well as pure ink, excluding the flat grey fixture ground.
                        else if (pixel is { R: < 100, G: < 90, B: < 110 }) rim.Add((x, y));
                    }
                }
            });

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
        // Bold stems have a wider cream centre than the old regular title.
        for (var dy = -4; dy <= 4; dy++)
        {
            for (var dx = -4; dx <= 4; dx++)
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
        Assert.DoesNotContain("Syntax Error", error, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PopplerInstalled() =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Any(directory => File.Exists(Path.Combine(directory, "pdftoppm")));
}
