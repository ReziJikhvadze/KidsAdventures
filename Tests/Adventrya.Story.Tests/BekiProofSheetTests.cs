using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story;
using Microsoft.Extensions.Options;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SixLabors.ImageSharp;
using Xunit;
using Xunit.Abstractions;

namespace Adventrya.Story.Tests;

/// <summary>
/// The frozen fixture book, rendered at full print resolution for a human to approve (R15).
///
/// The supplier's remediation order is intro and both endpapers first, then one story spread, then
/// the whole book — none of which a program can sign off. So this writes the pages out and says what
/// it wrote; every mechanical claim about them is asserted by the other layout tests, at screen
/// resolution, where they run in seconds.
///
/// Skipped unless <c>ADVENTRYA_BEKI_PROOF</c> names a folder, because it is a slow job: fourteen
/// sheets resampled to 5315 × 2480 and JPEG-encoded, plus the approved intro background carried at
/// full size through every page render.
/// </summary>
public class BekiProofSheetTests(ITestOutputHelper output)
{
    private static string? ProofDirectory => Environment.GetEnvironmentVariable("ADVENTRYA_BEKI_PROOF");

    [SkippableFact]
    public void Write_the_fixture_books_proof_sheets()
    {
        Skip.If(string.IsNullOrWhiteSpace(ProofDirectory),
            "Set ADVENTRYA_BEKI_PROOF to a folder to write the fixture book's print proofs.");

        var folder = ProofDirectory!;
        Directory.CreateDirectory(folder);

        // The real defaults: 450 × 210 mm at 300 PPI, which is the book a printer would receive.
        var layout = new BekiPrintLayoutOptions();
        var plan = BekiLayoutFixture.EightSpreadPlan();
        var spreads = plan.Spreads
            .Select(spread => new BekiSpreadArtwork(
                spread.Number, BekiLayoutFixture.SheetPng((30, 120, 90), width: 5315)))
            .ToList();

        var composer = new BekiPdfComposer(Options.Create(layout));
        var personalization = BekiLayoutFixture.Personalization();
        var cover = BekiLayoutFixture.LeafPng((160, 60, 60), width: 2716);

        var pdf = composer.ComposeWithReceipts(plan, cover, spreads, personalization).Pdf;
        var pdfPath = Path.Combine(folder, "beki-fixture-book.pdf");
        File.WriteAllBytes(pdfPath, pdf);

        var pages = composer.RenderPages(plan, cover, spreads, personalization);
        for (var index = 0; index < pages.Count; index++)
        {
            File.WriteAllBytes(Path.Combine(folder, $"page-{index + 1:00}.png"), pages[index]);
        }

        output.WriteLine($"{pages.Count} pages, {pdf.Length / 1024 / 1024} MB → {pdfPath}");
        output.WriteLine($"opening endpaper → page-02.png, intro → page-03.png, spread 1 → page-04.png");

        // The raster contract, on the pages that carry approved artwork rather than the fixture's
        // flat colour: exactly the working raster, at 300 PPI, in sRGB.
        foreach (var (name, bytes) in new[]
                 {
                     ("endpaper", BekiLayoutAssets.Current.EndpaperPatternBytes()),
                     ("intro background", BekiLayoutAssets.Current.IntroBackgroundBytes(BekiLayoutFixture.CanonicalThemeId)),
                 })
        {
            using var image = Image.Load(bytes);
            output.WriteLine($"{name}: {image.Width}×{image.Height}");
            Assert.Equal(5315, image.Width);
            Assert.Equal(2480, image.Height);
        }

        Assert.Equal(BookFormat.SpreadCount + 6, pages.Count);

        // And what actually reached the file: every interior sheet in the PDF is the working
        // raster, not something QuestPDF resampled on its way in. The cover and the back cover are
        // single leaves and come out half as wide, which is the geometry and not a fault; the small
        // images — the two QR codes and the cover title's outline — are not sheets at all.
        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        var interiorSheets = 0;
        var leaves = 0;

        for (var index = 0; index < document.Pages.Count; index++)
        {
            var xObjects = document.Pages[index].Elements
                .GetDictionary("/Resources")?.Elements.GetDictionary("/XObject");
            if (xObjects is null) continue;

            var isLeaf = index == 0 || index == document.Pages.Count - 1;

            foreach (var key in xObjects.Elements.Keys)
            {
                if (xObjects.Elements.GetObject(key) is not PdfDictionary image) continue;
                if (image.Elements.GetName("/Subtype") != "/Image") continue;

                var width = image.Elements.GetInteger("/Width");
                var height = image.Elements.GetInteger("/Height");
                // A sheet is the page; the cover title's outline raster is wide and short, and the
                // QR codes are small squares. Both axes have to be sheet-sized to count.
                if (width < 1000 || height < 1000) continue;

                var expectedWidth = isLeaf ? 2717 : 5315;
                Assert.True(width == expectedWidth && height == 2480,
                    $"Page {index + 1}'s sheet layer is {width}×{height}; it should be {expectedWidth}×2480.");

                if (isLeaf) leaves++;
                else interiorSheets++;
            }
        }

        output.WriteLine($"{interiorSheets} interior sheets at 5315×2480, {leaves} cover leaves at 2717×2480");
        Assert.True(interiorSheets >= BookFormat.SpreadCount, "The story spreads are not full-sheet layers.");
    }
}
