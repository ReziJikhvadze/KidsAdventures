using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Extensions;
using AdventurePacks.Api.Services.Pdf;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Prompts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PdfSharp.Pdf.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Adventrya.Story.Tests;

/// <summary>
/// The print-preparation stage under the Locked Print Specification v1 and the deliverables audit
/// of 2026-08-31: the exact FOGRA39 profile ships with the assets and is hash-pinned, all-CMYK is
/// the printer's locked ruling, Ghostscript performs the conversion — and, since the audit, two
/// things the old stage asserted are measured instead.
///
/// The first is resolution. P0-04 found a 2528×1210 cover raster placed at 512×245 mm — about 125
/// PPI — passing a preflight that read <c>/ColorSpace</c> and never <c>/Width</c>. So these tests
/// care a great deal about the difference between a page and a placement: amendment A1 makes the
/// gate measure where each image actually lands, and the test that proves it places a small image
/// on a large page, where the page-size shortcut would be wrong by a factor of six.
///
/// The second is text colour. P0-07 found cream credits text converted to black and shipped
/// invisible. <c>-dBlackText=true</c> is gone from the conversion, and what replaces it is a pair
/// of checks that look at the converted file — at its content stream, and at its pixels.
///
/// The conversion tests exercise the real Ghostscript binary; spec §5 makes it a required
/// deployment dependency, so a machine without it fails these tests the way a deployment
/// without it would fail print prep: loudly and by name.
/// </summary>
public class BekiPrintPrepTests
{
    [Fact]
    public void The_locked_profile_ships_with_the_assets_and_matches_its_pinned_hash()
    {
        var options = new BekiPrintPrepOptions();

        var path = Path.Combine(AppContext.BaseDirectory, options.OutputIntentIccPath);
        Assert.True(File.Exists(path), $"the locked ICC profile is not in the published output at {path}");

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(121_368, bytes.Length);
        Assert.Equal(
            options.OutputIntentIccSha256,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant());
    }

    /// <summary>
    /// The gates document is read at runtime, so it has to be on the deployment — and until this
    /// campaign the csproj copied <c>.json</c> from the composite assets but never the contracts
    /// beside them. A gate whose threshold cannot be loaded is a gate that does not run.
    /// </summary>
    [Fact]
    public void The_acceptance_gates_document_ships_with_the_assets_and_states_the_locked_values()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory, "Assets", "BekiComposite", "contracts",
            "BEKI_Acceptance_Gates_v1.json");

        Assert.True(File.Exists(path), $"the acceptance gates are not in the published output at {path}");

        using var gates = JsonDocument.Parse(File.ReadAllText(path));
        var locked = gates.RootElement.GetProperty("locked_values");

        Assert.Equal(300, locked.GetProperty("required_press_raster_ppi").GetInt32());
        Assert.Equal(1, locked.GetProperty("qr_count").GetInt32());
        Assert.Equal("https://beki.ge", locked.GetProperty("qr_destination").GetString());
        Assert.Equal("all_hard_gates_must_pass", gates.RootElement.GetProperty("release_policy").GetString());
    }

    /// <summary>The audit contracts are read by later stages and packaged; they must ship too.</summary>
    [Fact]
    public void The_supplied_contract_documents_ship_with_the_assets()
    {
        var contracts = Path.Combine(
            AppContext.BaseDirectory, "Assets", "BekiComposite", "contracts");

        Assert.True(File.Exists(Path.Combine(contracts, "BEKI_Print_Production_Locked_Spec_v1.md")));
        Assert.True(File.Exists(
            Path.Combine(contracts, "BEKI_Deliverables_Audit_and_Claude_Correction_Brief_v1.md")));
    }

    [Fact]
    public void An_unset_profile_path_is_refused()
    {
        var options = new BekiPrintPrepOptions { OutputIntentIccPath = string.Empty };

        var failure = Assert.Throws<BekiLayoutException>(() =>
            BekiPrintPrep.Prepare(BekiPressPrepFixtures.LightTextOnInk(), "ტესტი", options));

        Assert.Equal("PRINT_PREFLIGHT_FAILED", failure.FailureCode);
        Assert.Contains("OutputIntentIccPath", failure.Message);
    }

    [Fact]
    public void A_profile_that_does_not_match_the_locked_hash_is_refused()
    {
        var path = Path.Combine(Path.GetTempPath(), $"swapped-{Guid.NewGuid():N}.icc");
        var bytes = new byte[200];
        bytes[36] = (byte)'a';
        bytes[37] = (byte)'c';
        bytes[38] = (byte)'s';
        bytes[39] = (byte)'p';
        File.WriteAllBytes(path, bytes);

        try
        {
            var failure = Assert.Throws<BekiLayoutException>(() =>
                BekiPrintPrep.Prepare(
                    BekiPressPrepFixtures.LightTextOnInk(), "ტესტი",
                    new BekiPrintPrepOptions { OutputIntentIccPath = path }));

            Assert.Contains("not the locked", failure.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_file_that_is_not_an_icc_profile_is_refused()
    {
        var path = Path.Combine(Path.GetTempPath(), $"not-an-icc-{Guid.NewGuid():N}.icc");
        File.WriteAllText(path, "definitely not a profile");

        try
        {
            var failure = Assert.Throws<BekiLayoutException>(() =>
                BekiPrintPrep.Prepare(
                    BekiPressPrepFixtures.LightTextOnInk(), "ტესტი",
                    new BekiPrintPrepOptions
                    {
                        OutputIntentIccPath = path,
                        // The hash gate would fire first and correctly; blank it so this test
                        // reaches the signature check it is about.
                        OutputIntentIccSha256 = string.Empty,
                    }));

            Assert.Contains("acsp", failure.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_missing_ghostscript_is_refused_by_name()
    {
        var options = new BekiPrintPrepOptions
        {
            GhostscriptPath = "/definitely/not/gs-" + Guid.NewGuid().ToString("N"),
        };

        var failure = Assert.Throws<BekiLayoutException>(() =>
            BekiPrintPrep.Prepare(BekiPressPrepFixtures.LightTextOnInk(), "ტესტი", options));

        Assert.Equal("PRINT_PREFLIGHT_FAILED", failure.FailureCode);
        Assert.Contains("Ghostscript", failure.Message);
    }

    /// <summary>
    /// The gates document is read from the base directory, and a deployment that has not shipped it
    /// is refused rather than defaulted — the threshold belongs to the printer.
    /// </summary>
    [Fact]
    public void A_deployment_without_the_gates_document_is_refused()
    {
        var empty = Path.Combine(Path.GetTempPath(), $"beki-no-gates-{Guid.NewGuid():N}");
        Directory.CreateDirectory(empty);

        try
        {
            var options = new BekiPrintPrepOptions
            {
                // Absolute, so the missing base directory does not fail on the profile first.
                OutputIntentIccPath = Path.Combine(
                    AppContext.BaseDirectory, new BekiPrintPrepOptions().OutputIntentIccPath),
            };

            var failure = Assert.Throws<BekiLayoutException>(() =>
                BekiPrintPrep.Prepare(
                    BekiPressPrepFixtures.LightTextOnInk(), "ტესტი", options, baseDirectory: empty));

            Assert.Contains("acceptance gates", failure.Message);
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    /// <summary>
    /// The full locked pipeline: Ghostscript converts every raster to CMYK through the locked
    /// profile, the PDF/X-4 claims are stamped, the boxes are re-stated on the converted file,
    /// and the report says what happened. Checked the way a press-side preflight checks it.
    /// </summary>
    [Fact]
    public void A_prepared_artifact_is_cmyk_pdfx4_with_a_truthful_report()
    {
        var pages = 3;
        var (pdf, reportJson) = BekiPrintPrep.Prepare(
            BekiPressPrepFixtures.LightTextOnInk(pages), "ტესტი", new BekiPrintPrepOptions());

        var text = System.Text.Encoding.Latin1.GetString(pdf);
        Assert.Contains("/GTS_PDFX", text);
        Assert.Contains("/OutputIntents", text);
        Assert.Contains("FOGRA39", text);
        Assert.Contains("PDF/X-4", text);
        Assert.Contains("/Metadata", text);
        Assert.Contains("/Trapped", text);
        Assert.Contains("/TrimBox", text);
        Assert.Contains("/BleedBox", text);

        // The converted file still opens, with every page intact — the page-count contract that
        // exists because Ghostscript answers a torn input with a valid blank document.
        using (var reopened = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.InformationOnly))
        {
            Assert.Equal(pages, reopened.PageCount);
        }

        using var report = JsonDocument.Parse(reportJson);
        var root = report.RootElement;

        Assert.Equal("PDF/X-4", root.GetProperty("pdfx").GetProperty("version").GetString());
        Assert.Equal(
            new BekiPrintPrepOptions().OutputIntentIccSha256,
            root.GetProperty("pdfx").GetProperty("icc_profile_sha256").GetString());
        Assert.Equal("FOGRA39L Coated", root.GetProperty("pdfx").GetProperty("output_condition_info").GetString());

        // The ruling and the conversion are both on the record …
        Assert.True(root.GetProperty("colour").GetProperty("require_all_cmyk").GetBoolean());
        var conversion = root.GetProperty("colour").GetProperty("conversion").GetString()!;
        Assert.Contains("ghostscript", conversion);

        // … and the record no longer claims the coercion audit P0-07 blamed for the black credits.
        Assert.DoesNotContain("BlackText preserved", conversion);
        Assert.Contains("no BlackText coercion", conversion);

        // … and no colour raster is RGB. Grey soft masks are transparency and stay grey.
        var spaces = root.GetProperty("colour").GetProperty("image_colour_spaces")
            .EnumerateObject()
            .Select(entry => entry.Name)
            .ToList();
        Assert.NotEmpty(spaces);
        Assert.DoesNotContain(spaces, space =>
            !space.Contains("soft mask") && (space.Contains("RGB") || space.Contains("ICCBased(3)")));

        // Fonts survived conversion embedded.
        var fonts = root.GetProperty("fonts").EnumerateArray().ToList();
        Assert.NotEmpty(fonts);
        Assert.All(fonts, font => Assert.True(font.GetProperty("embedded").GetBoolean()));

        Assert.Equal(pages, root.GetProperty("pages").GetArrayLength());

        // The resolution gate ran, named itself, and recorded every placed raster.
        var resolution = root.GetProperty("resolution");
        Assert.Equal("PRESS_RESOLUTION", resolution.GetProperty("gate").GetString());
        Assert.Equal("PASS", resolution.GetProperty("verdict").GetString());
        Assert.Equal(300, resolution.GetProperty("required_press_raster_ppi").GetInt32());
        Assert.Equal(pages, resolution.GetProperty("placed_images").GetArrayLength());

        // And so did the colour gate, with the fills it actually saw.
        var colour = root.GetProperty("text_colour");
        Assert.Equal("TEXT_COLOR_INTEGRITY", colour.GetProperty("gate").GetString());
        Assert.Equal("PASS", colour.GetProperty("verdict").GetString());
        Assert.NotEmpty(colour.GetProperty("fills").EnumerateArray());
    }

    /// <summary>
    /// The cover geometry: the locked spec sets every cover box equal, so a zero trim inset must
    /// produce TrimBox == MediaBox on the prepared file.
    /// </summary>
    [Fact]
    public void A_zero_trim_inset_makes_every_box_the_media_box()
    {
        var (pdf, _) = BekiPrintPrep.Prepare(
            BekiPressPrepFixtures.LightTextOnInk(), "ტესტი", new BekiPrintPrepOptions(),
            trimInsetMm: 0f);

        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.InformationOnly);

        foreach (var page in document.Pages)
        {
            Assert.Equal(page.MediaBox.Width, page.TrimBox.Width, 2);
            Assert.Equal(page.MediaBox.Height, page.TrimBox.Height, 2);
        }
    }

    // ------------------------------------------------------------------------------------------
    // A1 — effective PPI per placement

    /// <summary>
    /// Amendment A1, stated as an experiment: a small image on a large page.
    ///
    /// The mark is 300×300 px placed at 10×10 mm — 762 effective PPI, comfortably over the gate.
    /// Divide its pixels by the page instead, the way the shipped preflight would have, and it
    /// reads 76 PPI and fails. The credits Beki mark is exactly this shape of object, which is why
    /// the plan forbids the page-size shortcut by name.
    /// </summary>
    [Fact]
    public void Effective_ppi_is_measured_where_the_image_lands_not_across_the_page()
    {
        var (_, reportJson) = BekiPrintPrep.Prepare(
            BekiPressPrepFixtures.SmallMarkOnLargePage(), "ტესტი", new BekiPrintPrepOptions());

        using var report = JsonDocument.Parse(reportJson);
        var placed = report.RootElement
            .GetProperty("resolution")
            .GetProperty("placed_images")
            .EnumerateArray()
            .Single();

        Assert.Equal(300, placed.GetProperty("width_px").GetInt32());
        Assert.Equal(10d, placed.GetProperty("placed_width_mm").GetDouble(), 1);
        Assert.Equal(10d, placed.GetProperty("placed_height_mm").GetDouble(), 1);
        Assert.InRange(placed.GetProperty("effective_ppi_x").GetDouble(), 700d, 800d);
        Assert.Equal("PASS", report.RootElement.GetProperty("resolution").GetProperty("verdict").GetString());
    }

    /// <summary>
    /// The audit's own defect, reproduced and still measured — and, since owner ruling 2026-09-01
    /// rule 4, no longer allowed to destroy the press build on its way to being reported.
    ///
    /// The composed book at the suite's screen proof density is 96 PPI art. The gate says so, by
    /// name and with the numbers, exactly as it always did. What has changed is that saying so no
    /// longer throws: "the sizes we have indicated for printing are correct", and a press file that
    /// refuses to exist is not a size being correct. So the prepared PDF comes back, the report
    /// carries the verdict FAIL and the measurements behind it, <c>failed_gates</c> names the gate,
    /// and the release policy is where a failed <c>PRESS_RESOLUTION</c> is weighed against the rest.
    ///
    /// This is still correction-plan risk R4 written as a test — every real press run fails
    /// <c>PRESS_RESOLUTION</c> until the source art is genuinely 300 PPI or an approved upscaler is
    /// configured. The intended state is unchanged; only who acts on it has moved.
    /// </summary>
    [Fact]
    public void A_book_composed_at_screen_resolution_reports_a_failed_press_resolution_gate()
    {
        var interior = InteriorPdf();

        using (var composed = PdfReader.Open(
                   new MemoryStream(interior), PdfDocumentOpenMode.InformationOnly))
        {
            // The page-count contract the composer owes, checked before the press stage measures.
            Assert.Equal(BookFormat.SpreadCount + 4, composed.PageCount);
        }

        var (pdf, reportJson, failedGates) = BekiPrintPrep.PrepareWithGates(
            interior, "ტესტი", new BekiPrintPrepOptions());

        // The press file exists. That is rule 4.
        Assert.NotEmpty(pdf);

        // And it is not pretending: the gate failed, it is named, and the numbers are there to argue
        // with — the same numbers the exception used to carry.
        Assert.Equal([BekiPrintPrep.PressResolutionGate], failedGates);

        using var report = JsonDocument.Parse(reportJson);
        var resolution = report.RootElement.GetProperty("resolution");

        Assert.Equal("FAIL", resolution.GetProperty("verdict").GetString());
        Assert.Equal(
            [BekiPrintPrep.PressResolutionGate],
            report.RootElement.GetProperty("failed_gates").EnumerateArray()
                .Select(gate => gate.GetString()).ToArray());

        var problem = Assert.Single(resolution.GetProperty("problems").EnumerateArray()).GetString()!;
        Assert.Contains("effective PPI", problem, StringComparison.Ordinal);
        Assert.Contains(" px at ", problem, StringComparison.Ordinal);

        // The report says out loud that it withheld the decision rather than passing the file.
        Assert.Contains(
            "BekiReleasePolicy", resolution.GetProperty("decision").GetString()!,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The other half of the resolution gate, and the one arithmetic cannot see: a raster can carry
    /// 300 PPI of pixels and 143 PPI of detail. The receipt is where that is admitted, and admitting
    /// it still fails the gate — in the report, which under rule 4 is where a failed
    /// <c>PRESS_RESOLUTION</c> lives.
    ///
    /// This is now the ORDINARY path rather than the exceptional one: the composer enlarges the
    /// interior to the stated sheet and declares the enlargement in its layout receipts, so a press
    /// build with no super-resolver configured produces files and a failed resolution gate every
    /// time. The gate is what keeps that visible.
    /// </summary>
    [Fact]
    public void A_receipt_that_admits_interpolation_only_upscaling_fails_the_gate_in_the_report()
    {
        var receipt = new BekiResolutionReceipt(
        [
            new BekiResolutionSource(
                "spread-04", 2528, 1180, 5315, 2480, "lanczos3", 2.1d, InterpolationOnly: false),
        ]);

        var (pdf, reportJson, failedGates) = BekiPrintPrep.PrepareWithGates(
            BekiPressPrepFixtures.LightTextOnInk(), "ტესტი", new BekiPrintPrepOptions(),
            resolutionReceipt: receipt);

        Assert.NotEmpty(pdf);
        Assert.Equal([BekiPrintPrep.PressResolutionGate], failedGates);

        using var report = JsonDocument.Parse(reportJson);
        var resolution = report.RootElement.GetProperty("resolution");

        Assert.Equal("FAIL", resolution.GetProperty("verdict").GetString());

        var problem = Assert.Single(resolution.GetProperty("problems").EnumerateArray()).GetString()!;
        Assert.Contains("interpolation alone", problem, StringComparison.Ordinal);
        Assert.Contains("spread-04", problem, StringComparison.Ordinal);

        // The receipt is echoed whatever the verdict: the supplier handback has to keep saying what
        // is real, and "which raster, from what, by what" is the real thing.
        var echoed = resolution.GetProperty("receipt").EnumerateArray().Single();
        Assert.Equal("spread-04", echoed.GetProperty("role").GetString());
        Assert.True(echoed.GetProperty("interpolation_only").GetBoolean());
    }

    /// <summary>
    /// The hard failures are still hard. Rule 4 moved exactly one gate's decision out of this stage;
    /// a page whose cream text converted to device black still refuses to become a press file at all,
    /// and so do a missing profile, an unembedded face, a wrong box and a dropped page.
    /// </summary>
    [Fact]
    public void Only_the_resolution_gate_withholds_its_decision()
    {
        var failure = Assert.Throws<BekiLayoutException>(() =>
            BekiPrintPrep.PrepareWithGates(
                BekiPressPrepFixtures.BlackTextOnInk(), "ტესტი", new BekiPrintPrepOptions(),
                probe: new BekiPrintProbe(LightTextPages: [1])));

        Assert.Equal("PRINT_PREFLIGHT_FAILED", failure.FailureCode);
        Assert.Contains(BekiPrintPrep.TextColorIntegrityGate, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A receipt naming a real super-resolver passes and is echoed into the report, because that is
    /// the provenance a physical proof is later inspected against.
    /// </summary>
    [Fact]
    public void A_receipt_naming_a_real_upscaler_passes_and_is_echoed_into_the_report()
    {
        var receipt = new BekiResolutionReceipt(
        [
            new BekiResolutionSource(
                "cover", 1512, 724, 6048, 2896, "realesrgan-x4plus", 4d, InterpolationOnly: false),
        ]);

        var (_, reportJson) = BekiPrintPrep.Prepare(
            BekiPressPrepFixtures.LightTextOnInk(), "ტესტი", new BekiPrintPrepOptions(),
            resolutionReceipt: receipt);

        using var report = JsonDocument.Parse(reportJson);
        var echoed = report.RootElement
            .GetProperty("resolution").GetProperty("receipt").EnumerateArray().Single();

        Assert.Equal("cover", echoed.GetProperty("role").GetString());
        Assert.Equal("realesrgan-x4plus", echoed.GetProperty("tool").GetString());
        Assert.False(echoed.GetProperty("interpolation_only").GetBoolean());
    }

    // ------------------------------------------------------------------------------------------
    // A10a — text colour integrity

    /// <summary>
    /// P0-07, caught by the content stream. The fixture authors its text black on a page the caller
    /// flags as carrying light text — which is precisely the state the removed
    /// <c>-dBlackText=true</c> used to manufacture out of cream — and the gate refuses it.
    /// </summary>
    [Fact]
    public void Text_that_leaves_the_conversion_device_black_fails_text_colour_integrity()
    {
        var failure = Assert.Throws<BekiLayoutException>(() =>
            BekiPrintPrep.Prepare(
                BekiPressPrepFixtures.BlackTextOnInk(), "ტესტი", new BekiPrintPrepOptions(),
                probe: new BekiPrintProbe(LightTextPages: [1])));

        Assert.Equal("PRINT_PREFLIGHT_FAILED", failure.FailureCode);
        Assert.Contains("TEXT_COLOR_INTEGRITY", failure.Message);
        Assert.Contains("device black", failure.Message);
        Assert.Contains("page 1", failure.Message);
    }

    /// <summary>
    /// The same fixture, unflagged, passes: a page nobody said was light is evidence, not a verdict.
    /// The colour is still recorded, which is the point of recording it.
    /// </summary>
    [Fact]
    public void An_unflagged_page_records_its_black_text_without_failing()
    {
        var (_, reportJson) = BekiPrintPrep.Prepare(
            BekiPressPrepFixtures.BlackTextOnInk(), "ტესტი", new BekiPrintPrepOptions());

        using var report = JsonDocument.Parse(reportJson);
        var colour = report.RootElement.GetProperty("text_colour");

        Assert.Equal("PASS", colour.GetProperty("verdict").GetString());
        Assert.Empty(colour.GetProperty("light_text_pages").EnumerateArray());
        Assert.Contains(
            colour.GetProperty("fills").EnumerateArray()
                .SelectMany(page => page.GetProperty("text_fills").EnumerateArray()),
            fill => fill.GetProperty("device_black").GetBoolean());
    }

    /// <summary>
    /// Cream on dark purple, converted through FOGRA39, still reads as light text — asserted twice
    /// over: no device-black fill in the content stream, and a bright glyph mode over a dark ground
    /// mode in the rendered pixels. This is the regression guard the correction plan's R3 asks for,
    /// now that the global coercion is gone and the profile decides the colour.
    /// </summary>
    [Fact]
    public void Authored_cream_text_survives_the_conversion_and_the_rendered_probe_sees_it()
    {
        var probe = new BekiPrintProbe(
            LightTextPages: [1],
            FlatGroundRects:
            [
                new BekiTextProbeRect(
                    1,
                    BekiPressPrepFixtures.TextRectXMm,
                    BekiPressPrepFixtures.TextRectYMm,
                    BekiPressPrepFixtures.TextRectWidthMm,
                    BekiPressPrepFixtures.TextRectHeightMm,
                    "credits"),
            ]);

        var (_, reportJson) = BekiPrintPrep.Prepare(
            BekiPressPrepFixtures.LightTextOnInk(), "ტესტი", new BekiPrintPrepOptions(), probe: probe);

        using var report = JsonDocument.Parse(reportJson);

        Assert.Equal("PASS", report.RootElement.GetProperty("text_colour").GetProperty("verdict").GetString());

        var probes = report.RootElement.GetProperty("text_pixel_probes");
        Assert.Equal("PASS", probes.GetProperty("verdict").GetString());

        var measurement = probes.GetProperty("probes").EnumerateArray().Single();
        Assert.Equal("credits", measurement.GetProperty("role").GetString());
        Assert.True(measurement.GetProperty("glyph_mode_luma").GetInt32() >= 200);
        Assert.True(measurement.GetProperty("ground_mode_luma").GetInt32() <= 90);
    }

    /// <summary>
    /// The probe pointed at a region the layout says carries text, and finding none: the failure
    /// mode a global colour coercion would produce, and the reason the probe exists at all.
    /// </summary>
    [Fact]
    public void A_probe_rect_with_no_light_glyphs_in_it_fails_text_colour_integrity()
    {
        var probe = new BekiPrintProbe(
            LightTextPages: [1],
            // The top band of the fixture is artwork, deliberately: there is no cream type in it.
            FlatGroundRects: [new BekiTextProbeRect(1, 2, 2, 20, 8, "credits")]);

        var failure = Assert.Throws<BekiLayoutException>(() =>
            BekiPrintPrep.Prepare(
                BekiPressPrepFixtures.LightTextOnInk(), "ტესტი", new BekiPrintPrepOptions(),
                probe: probe));

        Assert.Contains("TEXT_COLOR_INTEGRITY", failure.Message);
    }

    // ------------------------------------------------------------------------------------------
    // Options validation (correction plan D4, amendment A6)

    /// <summary>
    /// P1-02: "empty OutputIntentIccSha256 disables the ICC check". A deployment that unsets it now
    /// refuses to come up, which is the one moment somebody is watching.
    /// </summary>
    [Theory]
    [InlineData("Beki:PrintPrep:OutputIntentIccSha256", "OutputIntentIccSha256")]
    [InlineData("Beki:PrintPrep:OutputIntentIccPath", "OutputIntentIccPath")]
    public void A_blank_output_intent_setting_fails_startup(string key, string named)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = string.Empty })
            .Build();

        var provider = new ServiceCollection()
            .AddAdventurePacksOptions(configuration)
            .BuildServiceProvider();

        var failure = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<BekiOptions>>().Value);

        Assert.Contains(named, failure.Message);
    }

    [Fact]
    public void The_shipped_configuration_binds_and_validates()
    {
        var provider = new ServiceCollection()
            .AddAdventurePacksOptions(new ConfigurationBuilder().Build())
            .BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<BekiOptions>>().Value;

        Assert.False(string.IsNullOrWhiteSpace(options.PrintPrep.OutputIntentIccSha256));
        Assert.Equal("pdftoppm", options.PrintPrep.PopplerPdftoppmPath);
        Assert.Equal(120, options.PrintPrep.RenderDpi);
        Assert.Equal(string.Empty, options.PrintPrep.UpscalerPath);
    }

    // ------------------------------------------------------------------------------------------
    // D5c — the press upscaler, shipped disabled

    /// <summary>
    /// The shipped state, asserted as a state rather than assumed: no binary is installed by this
    /// campaign, so the upscaler answers "not configured" and the press path withholds. It does not
    /// quietly resample — that is the defect (P1-01), not the fallback.
    /// </summary>
    [Fact]
    public async Task An_unconfigured_upscaler_answers_not_configured_rather_than_resampling()
    {
        var upscaler = new CliPressUpscaler(new BekiPrintPrepOptions());

        Assert.False(upscaler.IsConfigured);

        var result = await upscaler.UpscaleAsync(
            BekiPressPrepFixtures.MarkPng(), 1200, 1200, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Png);
        Assert.Equal("none", result.Tool);
        Assert.Equal(1d, result.Factor);
        Assert.Equal(300, result.SourceWidthPx);
        Assert.Contains("UpscalerPath", result.Reason);
        Assert.Contains("PRESS_RESOLUTION", result.Reason);
    }

    /// <summary>
    /// A configured tool that is not there fails by name and does not throw: the press file is
    /// withheld, and the book the parent bought is not stopped by it.
    /// </summary>
    [Fact]
    public async Task A_configured_upscaler_that_is_not_installed_fails_by_name()
    {
        var upscaler = new CliPressUpscaler(new BekiPrintPrepOptions
        {
            UpscalerPath = "/definitely/not/realesrgan-" + Guid.NewGuid().ToString("N"),
            UpscalerArgsTemplate = "-i {in} -o {out} -s {scale}",
        });

        Assert.True(upscaler.IsConfigured);

        var result = await upscaler.UpscaleAsync(
            BekiPressPrepFixtures.MarkPng(), 1200, 1200, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("realesrgan", result.Reason);
    }

    /// <summary>
    /// A tool named with no arguments to drive it is a misconfiguration, not an upscale.
    /// </summary>
    [Fact]
    public async Task A_configured_upscaler_with_no_argument_template_is_refused()
    {
        var upscaler = new CliPressUpscaler(new BekiPrintPrepOptions { UpscalerPath = "/usr/bin/true" });

        var result = await upscaler.UpscaleAsync(
            BekiPressPrepFixtures.MarkPng(), 1200, 1200, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("UpscalerArgsTemplate", result.Reason);
    }

    // ------------------------------------------------------------------------------------------

    private static byte[]? _interior;

    /// <summary>One real composed interior, shared: composing it is the slow part of these tests.</summary>
    private static byte[] InteriorPdf()
    {
        if (_interior is not null)
        {
            return _interior;
        }

        var plan = new MasterStory
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

        var spreads = plan.Spreads
            .Select(spread => new BekiSpreadArtwork(spread.Number, PixelPng()))
            .ToList();

        var composer = new BekiPdfComposer(Options.Create(BekiLayoutFixture.ScreenProofLayout()));

        _interior = composer.ComposeInteriorWithReceipts(
            plan, spreads, BekiLayoutFixture.Personalization()).Pdf;
        return _interior;
    }

    private static byte[] PixelPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
}

/// <summary>
/// Press fixtures built for the gates that judge them, rather than a whole book borrowed from
/// somewhere else.
///
/// The shared fixture book is composed at 96 PPI on purpose — <c>BekiLayoutFixture</c> explains
/// why, and the suite would take minutes if it were not — which since amendment A1 means it fails
/// the press resolution gate by design. One test uses it for exactly that. Everything that needs to
/// get past the gate uses a small page carrying a genuinely over-300-PPI raster: the gate is about
/// pixels per inch of paper, and a 100 mm page needs a hundredth of the pixels a 450 mm one does to
/// prove the same arithmetic.
/// </summary>
internal static class BekiPressPrepFixtures
{
    public const float PageWidthMm = 100f;

    public const float PageHeightMm = 70f;

    /// <summary>The artwork band across the top; everything below it is flat ground and type.</summary>
    public const float ArtHeightMm = 20f;

    /// <summary>The rect the rendered-pixel probe is pointed at — the type block, not the art.</summary>
    public const double TextRectXMm = 4d;

    public const double TextRectYMm = 26d;

    public const double TextRectWidthMm = 92d;

    public const double TextRectHeightMm = 40d;

    /// <summary>The credits ground and its type, in the composer's own two colours.</summary>
    private const string Ink = "#281B3F";

    private const string Cream = "#F5EFE0";

    private const string Black = "#000000";

    private static readonly Dictionary<string, byte[]> Cache = new(StringComparer.Ordinal);

    private static readonly object Gate = new();

    /// <summary>Cream type on the dark ground, over a 360-PPI band of artwork.</summary>
    public static byte[] LightTextOnInk(int pages = 1) => Build($"light-{pages}", pages, Cream);

    /// <summary>The same page with its type authored black — P0-07's end state, manufactured.</summary>
    public static byte[] BlackTextOnInk(int pages = 1) => Build($"black-{pages}", pages, Black);

    /// <summary>The mark on its own, for the upscaler tests.</summary>
    public static byte[] MarkPng() => Cached("mark-png", () => Raster(300, 300));

    /// <summary>
    /// A 300×300 px mark placed at 10×10 mm on a full-size page: the credits Beki mark's shape, and
    /// the case the page-size shortcut gets wrong by a factor of six.
    /// </summary>
    public static byte[] SmallMarkOnLargePage() => Cached("small-mark", () =>
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var mark = Raster(300, 300);

        return Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageWidthMm, PageHeightMm, Unit.Millimetre);
                page.Margin(0);
                page.PageColor(Ink);

                page.Content()
                    .AlignCenter()
                    .AlignMiddle()
                    .Width(10, Unit.Millimetre)
                    .Height(10, Unit.Millimetre)
                    .Image(mark)
                    .FitUnproportionally()
                    .UseOriginalImage();
            });
        }).GeneratePdf();
    });

    private static byte[] Build(string key, int pages, string textColour) => Cached(key, () =>
    {
        QuestPDF.Settings.License = LicenseType.Community;

        // 360 PPI at the band's placed size: comfortably over the locked 300, and small enough that
        // Ghostscript converts the whole fixture in well under a second.
        var art = Raster(
            Pixels(PageWidthMm, 360), Pixels(ArtHeightMm, 360));

        return Document.Create(document =>
        {
            for (var number = 1; number <= pages; number++)
            {
                document.Page(page =>
                {
                    page.Size(PageWidthMm, PageHeightMm, Unit.Millimetre);
                    page.Margin(0);
                    page.PageColor(Ink);

                    page.Content().Column(column =>
                    {
                        column.Item()
                            .Height(ArtHeightMm, Unit.Millimetre)
                            .Image(art)
                            .FitUnproportionally()
                            .UseOriginalImage();

                        column.Item()
                            .PaddingHorizontal(4, Unit.Millimetre)
                            .PaddingVertical(6, Unit.Millimetre)
                            .Text("BEKI BEKI BEKI\nBEKI BEKI BEKI\nBEKI BEKI BEKI")
                            .FontSize(22)
                            .LineHeight(1.4f)
                            .FontColor(textColour);
                    });
                });
            }
        }).GeneratePdf();
    });

    private static int Pixels(float mm, int ppi) => (int)Math.Round(mm / 25.4f * ppi);

    /// <summary>
    /// A raster with real variation in it. A flat fill would be a legitimate thing for a converter
    /// to collapse into something that is no longer an image object, and then the resolution gate
    /// would have nothing to measure and the test would prove nothing.
    /// </summary>
    private static byte[] Raster(int width, int height)
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

        using var buffer = new MemoryStream();
        image.Save(buffer, new PngEncoder());
        return buffer.ToArray();
    }

    private static byte[] Cached(string key, Func<byte[]> build)
    {
        lock (Gate)
        {
            if (!Cache.TryGetValue(key, out var bytes))
            {
                bytes = build();
                Cache[key] = bytes;
            }

            return bytes.ToArray();
        }
    }
}
