using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Pdf;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Prompts;
using Microsoft.Extensions.Options;
using PdfSharp.Pdf.IO;

namespace Adventrya.Story.Tests;

/// <summary>
/// The print-preparation stage under the Locked Print Specification v1: the exact FOGRA39
/// profile ships with the assets and is hash-pinned, all-CMYK is the printer's locked ruling,
/// and Ghostscript performs the conversion. What these pin is the same as before the spec —
/// every missing or wrong input is a named refusal, and a file that passes carries claims a
/// press-side preflight can verify — with the locked values now the defaults.
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

    [Fact]
    public void An_unset_profile_path_is_refused()
    {
        var options = new BekiPrintPrepOptions { OutputIntentIccPath = string.Empty };

        var failure = Assert.Throws<BekiLayoutException>(() =>
            BekiPrintPrep.Prepare(InteriorPdf(), "ტესტი", options));

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
                    InteriorPdf(), "ტესტი",
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
                    InteriorPdf(), "ტესტი",
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
            BekiPrintPrep.Prepare(InteriorPdf(), "ტესტი", options));

        Assert.Equal("PRINT_PREFLIGHT_FAILED", failure.FailureCode);
        Assert.Contains("Ghostscript", failure.Message);
    }

    /// <summary>
    /// The full locked pipeline: Ghostscript converts every raster to CMYK through the locked
    /// profile, the PDF/X-4 claims are stamped, the boxes are re-stated on the converted file,
    /// and the report says what happened. Checked the way a press-side preflight checks it.
    /// </summary>
    [Fact]
    public void A_prepared_interior_is_cmyk_pdfx4_with_a_truthful_report()
    {
        var (pdf, reportJson) = BekiPrintPrep.Prepare(
            InteriorPdf(), "ტესტი", new BekiPrintPrepOptions());

        var text = System.Text.Encoding.Latin1.GetString(pdf);
        Assert.Contains("/GTS_PDFX", text);
        Assert.Contains("/OutputIntents", text);
        Assert.Contains("FOGRA39", text);
        Assert.Contains("PDF/X-4", text);
        Assert.Contains("/Metadata", text);
        Assert.Contains("/Trapped", text);
        Assert.Contains("/TrimBox", text);
        Assert.Contains("/BleedBox", text);

        // The converted file still opens, with every page intact.
        using (var reopened = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.InformationOnly))
        {
            Assert.Equal(BookFormat.SpreadCount + 4, reopened.PageCount);
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
        Assert.Contains("ghostscript", root.GetProperty("colour").GetProperty("conversion").GetString());

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

        Assert.Equal(BookFormat.SpreadCount + 4, root.GetProperty("pages").GetArrayLength());
    }

    /// <summary>
    /// The cover geometry: the locked spec sets every cover box equal, so a zero trim inset must
    /// produce TrimBox == MediaBox on the prepared file.
    /// </summary>
    [Fact]
    public void A_zero_trim_inset_makes_every_box_the_media_box()
    {
        var (pdf, _) = BekiPrintPrep.Prepare(
            InteriorPdf(), "ტესტი", new BekiPrintPrepOptions(), trimInsetMm: 0f);

        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.InformationOnly);

        foreach (var page in document.Pages)
        {
            Assert.Equal(page.MediaBox.Width, page.TrimBox.Width, 2);
            Assert.Equal(page.MediaBox.Height, page.TrimBox.Height, 2);
        }
    }

    // -------------------------------------------------------------------------------------------

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

        _interior = composer.ComposeInterior(plan, spreads, BekiLayoutFixture.Personalization());
        return _interior;
    }

    private static byte[] PixelPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
}
