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
/// The print-preparation stage: what it refuses, and what a file that passes it can prove.
///
/// The supplier's audit found the previous "print" file was a bare layout export — PDF 1.7, no
/// PDF/X identification, no output intent, no preflight — and nothing recorded that the real
/// stage had been skipped. These pin the replacement's two halves: every missing input is a
/// named refusal, and a prepared file carries the claims a press-side preflight looks for.
/// </summary>
public class BekiPrintPrepTests
{
    [Fact]
    public void With_no_icc_profile_configured_the_stage_refuses_and_names_the_owner_item()
    {
        var failure = Assert.Throws<BekiLayoutException>(() =>
            BekiPrintPrep.Prepare(InteriorPdf(), "ტესტი", new BekiPrintPrepOptions()));

        Assert.Equal("PRINT_PREFLIGHT_FAILED", failure.FailureCode);
        Assert.Contains("OutputIntentIccPath", failure.Message);
    }

    [Fact]
    public void A_cmyk_ruling_the_stage_cannot_honour_is_refused_rather_than_faked()
    {
        var options = ConfiguredOptions();
        options.RequireAllCmyk = true;

        var failure = Assert.Throws<BekiLayoutException>(() =>
            BekiPrintPrep.Prepare(InteriorPdf(), "ტესტი", options));

        Assert.Equal("PRINT_PREFLIGHT_FAILED", failure.FailureCode);
        Assert.Contains("CMYK", failure.Message);
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
                    new BekiPrintPrepOptions { OutputIntentIccPath = path }));

            Assert.Contains("acsp", failure.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The happy path, checked the way a press-side preflight checks it: the PDF/X-4 claim in
    /// XMP, the GTS_PDFX output intent with the profile embedded, the boxes intact through the
    /// rewrite, and a report that says what was and was not done.
    /// </summary>
    [Fact]
    public void A_prepared_interior_carries_the_pdfx_claims_and_a_truthful_report()
    {
        var (pdf, reportJson) = BekiPrintPrep.Prepare(InteriorPdf(), "ტესტი", ConfiguredOptions());

        var text = System.Text.Encoding.Latin1.GetString(pdf);
        Assert.Contains("/GTS_PDFX", text);
        Assert.Contains("/OutputIntents", text);
        Assert.Contains("FOGRA39", text);
        Assert.Contains("PDF/X-4", text);
        Assert.Contains("/Metadata", text);
        Assert.Contains("/Trapped", text);

        // The rewrite kept every page and its print boxes.
        Assert.Equal(
            BookFormat.SpreadCount + 4,
            System.Text.RegularExpressions.Regex.Matches(text, @"/Type\s*/Page[^s]").Count);
        Assert.Contains("/TrimBox", text);
        Assert.Contains("/BleedBox", text);

        // And the file still opens as a PDF — a claim stapled onto a broken document would be
        // worse than no claim.
        using var reopened = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.InformationOnly);

        using var report = JsonDocument.Parse(reportJson);
        var root = report.RootElement;

        Assert.Equal("PDF/X-4", root.GetProperty("pdfx").GetProperty("version").GetString());
        Assert.Equal(
            "Coated FOGRA39",
            root.GetProperty("pdfx").GetProperty("output_condition_info").GetString());

        // Every font the inspector found is embedded — QuestPDF subsets and embeds, and the
        // stage fails outright on one that is not.
        var fonts = root.GetProperty("fonts").EnumerateArray().ToList();
        Assert.NotEmpty(fonts);
        Assert.All(fonts, font => Assert.True(font.GetProperty("embedded").GetBoolean()));

        // Twelve interior pages, each with its boxes on the record.
        Assert.Equal(BookFormat.SpreadCount + 4, root.GetProperty("pages").GetArrayLength());

        // The report never claims the renderer checks it did not run, and records that the CMYK
        // ruling is still the printer's to give.
        Assert.Contains("not run", root.GetProperty("renderers").GetProperty("poppler").GetString());
        Assert.Contains(
            "unconfirmed",
            root.GetProperty("colour").GetProperty("ruling").GetString());
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

    private static BekiPrintPrepOptions ConfiguredOptions() =>
        new() { OutputIntentIccPath = FakeIccPath.Value };

    /// <summary>
    /// A structurally valid stand-in profile — the 'acsp' signature is all the stage verifies,
    /// because verifying colorimetry is the real profile's job and the real profile is the
    /// owner-side deliverable these tests must not wait for.
    /// </summary>
    private static readonly Lazy<string> FakeIccPath = new(() =>
    {
        var bytes = new byte[200];
        bytes[36] = (byte)'a';
        bytes[37] = (byte)'c';
        bytes[38] = (byte)'s';
        bytes[39] = (byte)'p';

        var path = Path.Combine(Path.GetTempPath(), $"fake-fogra39-{Guid.NewGuid():N}.icc");
        File.WriteAllBytes(path, bytes);
        return path;
    });
}
