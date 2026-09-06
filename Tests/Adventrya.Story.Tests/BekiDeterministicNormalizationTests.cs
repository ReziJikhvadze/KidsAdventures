using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Extensions;
using AdventurePacks.Api.Services.Pdf;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Adventrya.Story.Tests;

/// <summary>
/// Deterministic print preparation, as the 2026-09-06 decision record defines it.
///
/// The record settled two things and these tests hold them apart. The first is that the default
/// mode needs nothing installed: one local Lanczos3 normalization to the exact locked raster, after
/// a crop that is allowed to be rounding and nothing deeper. The second is that
/// <c>PRESS_RESOLUTION</c> judges the measured output — exact pixels, effective PPI at placement,
/// aspect preserved — and never the name of whatever resized it. So a metadata DPI tag still buys
/// nothing, a raster one pixel short of the locked size still fails and names its page, and a
/// picture stretched into a box of the wrong shape is a <c>PRESS_GEOMETRY</c> defect rather than a
/// resolution one.
/// </summary>
public class BekiDeterministicNormalizationTests
{
    // ------------------------------------------------------------------------------------------
    // The mode, as configuration spells it

    [Theory]
    [InlineData("deterministic_lanczos", BekiPrintPrepMode.DeterministicLanczos)]
    [InlineData("DeterministicLanczos", BekiPrintPrepMode.DeterministicLanczos)]
    [InlineData("Deterministic-Lanczos", BekiPrintPrepMode.DeterministicLanczos)]
    [InlineData("EXTERNAL_SUPER_RESOLUTION", BekiPrintPrepMode.ExternalSuperResolution)]
    [InlineData("ExternalSuperResolution", BekiPrintPrepMode.ExternalSuperResolution)]
    [InlineData("external-super-resolution", BekiPrintPrepMode.ExternalSuperResolution)]
    public void Every_spelling_an_operator_would_type_parses(string configured, BekiPrintPrepMode expected)
    {
        Assert.Equal(expected, BekiPrintPrepOptions.ParseMode(configured));
        Assert.Equal(expected, new BekiPrintPrepOptions { Mode = configured }.ResolvedMode);
    }

    /// <summary>
    /// A value that names no mode is refused by name rather than defaulted, because the default it
    /// would fall through to decides how every press raster in the product is produced.
    /// </summary>
    [Theory]
    [InlineData("lanczos")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("deterministic lanczos")]
    public void A_value_that_names_no_mode_is_refused_and_names_the_key(string configured)
    {
        var refusal = Assert.Throws<InvalidOperationException>(
            () => new BekiPrintPrepOptions { Mode = configured }.ResolvedMode);

        Assert.Contains("Beki:PrintPrep:Mode", refusal.Message, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------
    // Startup validation and what DI hands the pipeline

    /// <summary>
    /// External mode with no tool fails the deploy that caused it, not a book weeks later. The
    /// message names the key so somebody reading an App Service log knows which box to fill.
    /// </summary>
    [Fact]
    public void External_mode_without_a_tool_refuses_to_start()
    {
        var provider = Provider(new Dictionary<string, string?>
        {
            ["Beki:PrintPrep:Mode"] = BekiPrintPrepModes.ExternalSuperResolution,
        });

        var failure = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<BekiOptions>>().Value);

        Assert.Contains("Beki:PrintPrep:Mode", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The shipped mode with an empty tool path is not a gap: it starts, and what it resolves is the
    /// normalizer that needs nothing.
    /// </summary>
    [Fact]
    public void The_default_mode_starts_with_no_tool_and_resolves_the_normalizer()
    {
        var provider = Provider(new Dictionary<string, string?>
        {
            ["Beki:PrintPrep:Mode"] = BekiPrintPrepModes.DeterministicLanczos,
            ["Beki:PrintPrep:UpscalerPath"] = string.Empty,
        });

        var options = provider.GetRequiredService<IOptions<BekiOptions>>().Value;
        Assert.Equal(BekiPrintPrepMode.DeterministicLanczos, options.PrintPrep.ResolvedMode);

        // The factory is what DI and the fulfilment stage's constructor fallback both call, so
        // proving it here proves the preparer every caller in the product gets.
        var preparer = PressRasterPreparerFactory.Create(options.PrintPrep);
        Assert.IsType<DeterministicLanczosNormalizer>(preparer);
        Assert.True(preparer.IsConfigured);
    }

    [Fact]
    public void External_mode_with_a_tool_resolves_the_external_preparer()
    {
        var preparer = PressRasterPreparerFactory.Create(new BekiPrintPrepOptions
        {
            Mode = BekiPrintPrepModes.ExternalSuperResolution,
            UpscalerPath = "/usr/bin/true",
            UpscalerArgsTemplate = "-i {in} -o {out} -s {scale}",
        });

        Assert.IsType<CliPressUpscaler>(preparer);
    }

    // ------------------------------------------------------------------------------------------
    // NormalizeDeterministic

    /// <summary>
    /// A raster already at the locked size comes back as the same object. Not "equal bytes" — the
    /// same array, because the point is that nothing decoded, resampled or re-encoded it: the press
    /// file stays one generation away from the render.
    /// </summary>
    [Fact]
    public void An_exact_raster_passes_through_by_reference()
    {
        var exact = Png(BekiPressRaster.InteriorWidthPx, BekiPressRaster.InteriorHeightPx);

        var result = BekiPressRaster.NormalizeDeterministic(
            exact, BekiPressRaster.InteriorWidthPx, BekiPressRaster.InteriorHeightPx);

        Assert.True(result.Succeeded);
        Assert.Same(exact, result.Png);
        Assert.Equal("native-source", result.Tool);
        Assert.Equal(1d, result.Factor);
        Assert.Null(result.Crop);
    }

    /// <summary>
    /// The ordinary case: a 1536×717 base onto the interior sheet. The frame the generator returns
    /// is already at the sheet's ratio to within a pixel, so nothing is cropped — and the crop is
    /// recorded anyway, at fraction zero, because "we cropped nothing" is a statement worth making
    /// rather than an absence to interpret.
    /// </summary>
    [Fact]
    public void A_stored_base_normalizes_to_the_exact_interior_raster()
    {
        var result = BekiPressRaster.NormalizeDeterministic(
            Png(1536, 717), BekiPressRaster.InteriorWidthPx, BekiPressRaster.InteriorHeightPx);

        Assert.True(result.Succeeded);
        Assert.Equal(BekiPressRaster.DeterministicTool, result.Tool);
        Assert.Equal(3.46d, result.Factor, 2);
        Assert.Equal(1536, result.SourceWidthPx);
        Assert.Equal(717, result.SourceHeightPx);
        Assert.Equal(BekiPressRaster.InteriorWidthPx, result.DeliveredWidthPx);
        Assert.Equal(BekiPressRaster.InteriorHeightPx, result.DeliveredHeightPx);

        var crop = Assert.IsType<PressCropGeometry>(result.Crop);
        Assert.Equal(0, crop.XPx);
        Assert.Equal(0, crop.YPx);
        Assert.Equal(1536, crop.WidthPx);
        Assert.Equal(717, crop.HeightPx);
        Assert.Equal(0d, crop.CropFractionX);
        Assert.Equal(0d, crop.CropFractionY);

        using var decoded = SixLabors.ImageSharp.Image.Load(result.Png!);
        Assert.Equal(BekiPressRaster.InteriorWidthPx, decoded.Width);
        Assert.Equal(BekiPressRaster.InteriorHeightPx, decoded.Height);
        Assert.Equal(300d, PixelsPerInch(decoded.Metadata, horizontal: true), 1);
        Assert.Equal(300d, PixelsPerInch(decoded.Metadata, horizontal: false), 1);
    }

    /// <summary>
    /// Reduction goes down the same path as enlargement — one crop, one Lanczos3 pass — because a
    /// second code path for "it was already big enough" is a second place for the sizes to drift.
    /// </summary>
    [Fact]
    public void An_oversized_near_ratio_source_is_reduced_to_the_exact_raster()
    {
        var result = BekiPressRaster.NormalizeDeterministic(
            Png(6000, 2800), BekiPressRaster.InteriorWidthPx, BekiPressRaster.InteriorHeightPx);

        Assert.True(result.Succeeded);
        Assert.Equal(BekiPressRaster.DeterministicTool, result.Tool);
        Assert.Equal(BekiPressRaster.InteriorWidthPx, result.DeliveredWidthPx);
        Assert.Equal(BekiPressRaster.InteriorHeightPx, result.DeliveredHeightPx);

        // Whatever the ratio reconciliation took off is rounding: a few pixels at most on either
        // axis, and well inside the allowance.
        var crop = Assert.IsType<PressCropGeometry>(result.Crop);
        Assert.InRange(crop.WidthPx, 5990, 6000);
        Assert.InRange(crop.HeightPx, 2790, 2800);
        Assert.InRange(crop.CropFractionX, 0d, BekiPressRaster.MaxSafeCropFraction);
        Assert.InRange(crop.CropFractionY, 0d, BekiPressRaster.MaxSafeCropFraction);

        using var decoded = SixLabors.ImageSharp.Image.Load(result.Png!);
        Assert.Equal(BekiPressRaster.InteriorWidthPx, decoded.Width);
        Assert.Equal(BekiPressRaster.InteriorHeightPx, decoded.Height);
    }

    /// <summary>
    /// A 3:2 frame is not a spread and not a cover. Squeezing it onto either sheet would throw away
    /// nearly a third of the picture, and that is a composition defect upstream — refused here with
    /// the numbers, never stretched into place.
    /// </summary>
    [Theory]
    [InlineData(BekiPressRaster.InteriorWidthPx, BekiPressRaster.InteriorHeightPx)]
    [InlineData(BekiPressRaster.CoverWidthPx, BekiPressRaster.CoverHeightPx)]
    public void A_source_of_the_wrong_composition_is_refused_rather_than_stretched(
        int targetWidth, int targetHeight)
    {
        var result = BekiPressRaster.NormalizeDeterministic(Png(1536, 1024), targetWidth, targetHeight);

        Assert.False(result.Succeeded);
        Assert.Null(result.Png);
        Assert.Contains("aspect mismatch", result.Reason, StringComparison.Ordinal);
        Assert.Contains($"{targetWidth}×{targetHeight}", result.Reason, StringComparison.Ordinal);
        Assert.Equal(1536, result.SourceWidthPx);
    }

    /// <summary>
    /// Bytes that are not an image are an answer, not an exception: the caller records the problem
    /// and the gate judges the composed output, which is the one thing that cannot be faked.
    /// </summary>
    [Fact]
    public void Undecodable_bytes_answer_rather_than_throw()
    {
        var result = BekiPressRaster.NormalizeDeterministic(
            "this is not a PNG"u8.ToArray(),
            BekiPressRaster.InteriorWidthPx,
            BekiPressRaster.InteriorHeightPx);

        Assert.False(result.Succeeded);
        Assert.Null(result.Png);
        Assert.Equal("none", result.Tool);
        Assert.Contains("could not be decoded", result.Reason, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------
    // What the gate measures

    /// <summary>
    /// The substitution the audit's P1-01 was really about, proved to be worthless: a 1536×717
    /// raster tagged 300 DPI in its own metadata, placed on the full 450×210 mm sheet. The tag says
    /// 300; the paper gets 87. The gate computes pixels over placed millimetres and never reads the
    /// tag, so re-tagging a thin raster changes nothing at all.
    /// </summary>
    [Fact]
    public void A_metadata_dpi_tag_is_not_resolution()
    {
        var page = SheetPage(450, 210, Png(1536, 717, tagged300Dpi: true));

        var (_, _, failedGates) = BekiPrintPrep.PrepareWithGates(
            page, "ტესტი", new BekiPrintPrepOptions(), acceptRgbForScopedDelivery: true);

        Assert.Contains(BekiPrintPrep.PressResolutionGate, failedGates);
    }

    /// <summary>
    /// The canonical shape, measured and passing with no provenance receipt at all. That last part
    /// is the decision record written as a test: a book that carries the locked pixels on the locked
    /// sheets does not owe the gate an account of how it got them.
    /// </summary>
    [Fact]
    public void A_canonical_book_at_the_locked_sizes_passes_with_no_receipt()
    {
        var (_, _, failedGates) = BekiPrintPrep.PrepareWithGates(
            CanonicalBook(), "ტესტი", new BekiPrintPrepOptions(),
            trimInsetMm: 5f,
            resolutionReceipt: null,
            canonicalMixedGeometry: true,
            acceptRgbForScopedDelivery: true);

        Assert.Empty(failedGates);
    }

    /// <summary>
    /// One column short on one page. There is no tolerance on the locked pixel count, and the
    /// problem names the page so an operator knows which spread to re-prepare.
    /// </summary>
    [Fact]
    public void A_spread_one_pixel_short_fails_and_names_its_page()
    {
        var (_, reportJson, failedGates) = BekiPrintPrep.PrepareWithGates(
            CanonicalBook(shortPage: 5), "ტესტი", new BekiPrintPrepOptions(),
            trimInsetMm: 5f,
            canonicalMixedGeometry: true,
            acceptRgbForScopedDelivery: true);

        Assert.Contains(BekiPrintPrep.PressResolutionGate, failedGates);

        using var report = System.Text.Json.JsonDocument.Parse(reportJson);
        var problem = Assert.Single(report.RootElement
            .GetProperty("resolution").GetProperty("problems").EnumerateArray()).GetString()!;

        Assert.Contains("page 5", problem, StringComparison.Ordinal);
        Assert.Contains("5314×2480", problem, StringComparison.Ordinal);
        Assert.Contains(
            $"{BekiPressRaster.InteriorWidthPx}×{BekiPressRaster.InteriorHeightPx}", problem,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The right pixels in the wrong shape. A 5315×2480 raster squeezed into a 450×180 mm box is a
    /// distorted picture, and it measures over 300 PPI the whole time — which is why it is judged as
    /// geometry rather than as resolution, and why the ratio rule exists at all.
    /// </summary>
    [Fact]
    public void A_raster_squeezed_into_the_wrong_box_is_a_geometry_defect()
    {
        var page = SheetPage(
            450, 180, Png(BekiPressRaster.InteriorWidthPx, BekiPressRaster.InteriorHeightPx));

        var (_, reportJson, failedGates) = BekiPrintPrep.PrepareWithGates(
            page, "ტესტი", new BekiPrintPrepOptions(), acceptRgbForScopedDelivery: true);

        Assert.Contains(BekiPrintPrep.PressGeometryGate, failedGates);
        Assert.DoesNotContain(BekiPrintPrep.PressResolutionGate, failedGates);

        using var report = System.Text.Json.JsonDocument.Parse(reportJson);
        var geometry = report.RootElement.GetProperty("resolution").GetProperty("geometry");

        Assert.Equal("FAIL", geometry.GetProperty("verdict").GetString());
        Assert.Contains(
            "stretched",
            Assert.Single(geometry.GetProperty("problems").EnumerateArray()).GetString()!,
            StringComparison.Ordinal);

        // And it travels in the report's own gate list the way PRESS_RESOLUTION does.
        Assert.Equal(
            BekiPrintPrep.PressGeometryGate,
            Assert.Single(report.RootElement.GetProperty("failed_gates").EnumerateArray())
                .GetString());
    }

    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The declared resolution in pixels per inch, whatever unit the file states it in. PNG's pHYs
    /// chunk stores pixels per metre, so a 300 PPI write comes back as 11,811 per metre and reading
    /// the raw number would prove nothing about what was written.
    /// </summary>
    private static double PixelsPerInch(
        SixLabors.ImageSharp.Metadata.ImageMetadata metadata, bool horizontal)
    {
        var value = horizontal ? metadata.HorizontalResolution : metadata.VerticalResolution;

        return metadata.ResolutionUnits switch
        {
            SixLabors.ImageSharp.Metadata.PixelResolutionUnit.PixelsPerMeter => value * 0.0254d,
            SixLabors.ImageSharp.Metadata.PixelResolutionUnit.PixelsPerCentimeter => value * 2.54d,
            _ => value,
        };
    }

    private static ServiceProvider Provider(Dictionary<string, string?> settings) =>
        new ServiceCollection()
            .AddAdventurePacksOptions(
                new ConfigurationBuilder().AddInMemoryCollection(settings).Build())
            .BuildServiceProvider();

    /// <summary>
    /// One page at the given millimetre size carrying one raster across all of it. Built rather than
    /// composed: the gate reads the content stream, and the smallest honest document that exercises
    /// it is a page and an image.
    /// </summary>
    private static byte[] SheetPage(float widthMm, float heightMm, byte[] raster) =>
        Document.Create(document => document.Page(page =>
        {
            QuestPDF.Settings.License = LicenseType.Community;
            page.Size(widthMm, heightMm, Unit.Millimetre);
            page.Margin(0);
            page.Content().Image(raster).FitUnproportionally().UseOriginalImage();
        })).GeneratePdf();

    /// <summary>
    /// The canonical shape: page 1 the cover wrap, pages 2–12 the interior spreads, each carrying
    /// its locked raster across the whole sheet. One raster object per size, reused on every page,
    /// so twelve pages cost two encodes rather than twelve.
    /// </summary>
    /// <param name="shortPage">
    /// A 1-based page whose raster is one column short of the locked width — the defect the exact
    /// size rule exists to catch.
    /// </param>
    private static byte[] CanonicalBook(int shortPage = 0)
    {
        lock (BookGate)
        {
            if (Books.TryGetValue(shortPage, out var cached))
            {
                return cached.ToArray();
            }

            QuestPDF.Settings.License = LicenseType.Community;

            // One QuestPDF image per distinct raster, reused across the pages that share it: a
            // second instance of the same bytes is a second embedded image object, and twelve of
            // these at press size is a fixture nobody wants to wait for.
            var cover = QuestPDF.Infrastructure.Image.FromBinaryData(
                Png(BekiPressRaster.CoverWidthPx, BekiPressRaster.CoverHeightPx));
            var interior = QuestPDF.Infrastructure.Image.FromBinaryData(
                Png(BekiPressRaster.InteriorWidthPx, BekiPressRaster.InteriorHeightPx));
            var shortOne = shortPage > 0
                ? QuestPDF.Infrastructure.Image.FromBinaryData(
                    Png(BekiPressRaster.InteriorWidthPx - 1, BekiPressRaster.InteriorHeightPx))
                : null;

            var book = Document.Create(document =>
            {
                for (var number = 1; number <= 12; number++)
                {
                    var raster = number == 1 ? cover : number == shortPage ? shortOne! : interior;
                    var (widthMm, heightMm) = number == 1 ? (512f, 245f) : (450f, 210f);

                    document.Page(page =>
                    {
                        page.Size(widthMm, heightMm, Unit.Millimetre);
                        page.Margin(0);
                        page.Content().Image(raster).FitUnproportionally().UseOriginalImage();
                    });
                }
            }).GeneratePdf();

            Books[shortPage] = book;
            return book.ToArray();
        }
    }

    private static readonly Dictionary<int, byte[]> Books = [];

    private static readonly object BookGate = new();

    /// <summary>
    /// A raster with variation in it: a flat fill is something a PDF writer may legitimately turn
    /// into a shape rather than an image, and then the gate would have nothing to measure.
    /// </summary>
    private static byte[] Png(int width, int height, bool tagged300Dpi = false)
    {
        using var image = new Image<Rgb24>(width, height);

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = new Rgb24(
                        (byte)(40 + (x * 160 / Math.Max(1, width))),
                        (byte)(90 + (y * 120 / Math.Max(1, height))),
                        (byte)(((x / 8) + (y / 8)) % 2 == 0 ? 200 : 120));
                }
            }
        });

        if (tagged300Dpi)
        {
            image.Metadata.ResolutionUnits =
                SixLabors.ImageSharp.Metadata.PixelResolutionUnit.PixelsPerInch;
            image.Metadata.HorizontalResolution = 300d;
            image.Metadata.VerticalResolution = 300d;
        }

        using var buffer = new MemoryStream();
        // Speed over bytes: these rasters live for one test run, and a press-sized PNG at default
        // compression costs seconds of CPU nobody gets back.
        image.Save(buffer, new PngEncoder { CompressionLevel = PngCompressionLevel.BestSpeed });
        return buffer.ToArray();
    }
}
