using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Services.Pdf;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using Microsoft.Extensions.Options;
using PdfSharp.Pdf.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Adventrya.Story.Tests;

public class BekiSeptemberScopeTests
{
    [Fact]
    public void Colored_logo_visible_bounds_have_exact_fold_clearance_and_title_clear_space()
    {
        Assert.Equal(20f, BekiCoverDieline.LogoTopMm - BekiCoverDieline.BoardTopMm);
        Assert.Equal(20f, BekiCoverDieline.FrontBoardRightMm - BekiCoverDieline.LogoRightMm);
        Assert.Equal(436f, BekiCoverDieline.LogoLeftMm);
        Assert.Equal(40f, BekiCoverDieline.LogoTopMm);
        Assert.InRange(BekiCoverDieline.LogoWidthMm, 25f, 36f);
        Assert.InRange(BekiCoverDieline.LogoHeightMm, 12.791f, 12.792f);
        Assert.True(BekiCoverDieline.TitleSafeLeftMm + BekiCoverDieline.TitleSafeWidthMm
            <= BekiCoverDieline.LogoLeftMm - BekiCoverDieline.LogoClearSpaceMm);
        Assert.Contains("face, head, hairline, eyes", BekiCoverDieline.Geometry.PanelInstructions);
        Assert.Contains("436..472", BekiCoverDieline.Geometry.PanelInstructions);
    }

    [Fact]
    public void Final_raster_sizing_can_only_preserve_or_downsample_real_prepared_detail()
    {
        var native = Png(150, 70);
        Assert.Same(native, BekiPressRaster.FinalSize(native, 150, 70));
        using var sized = Image.Load(BekiPressRaster.FinalSize(Png(300, 140), 150, 70));
        Assert.Equal(150, sized.Width);
        Assert.Equal(70, sized.Height);
        Assert.Throws<BekiLayoutException>(() => BekiPressRaster.FinalSize(native, 300, 140));
        Assert.Throws<BekiLayoutException>(() => BekiPressRaster.FinalSize(Png(300, 300), 150, 70));
    }

    [Fact]
    public void Website_QR_and_cover_prompt_versions_are_explicit()
    {
        Assert.Equal("https://beki.ge", BekiOptions.WebsiteQrDestination);
        Assert.Equal("visual-scenario-v2.4", CompositeVisualScenarioPrompt.Version);
        Assert.Equal("cover-child-world-v1.3", CompositeIllustrationPrompt.CoverVersion);
        Assert.Equal("composite-v1.2", MasterStoryPromptComposite.Version);
    }

    [Fact]
    public void Scoped_RGB_preparation_still_rejects_duplicate_layers_without_a_layout_budget()
    {
        var failure = Assert.Throws<BekiLayoutException>(() => BekiPrintPrep.PrepareWithGates(
            BekiPressPrepFixtures.DuplicateTextOnInk(), "duplicate fixture", new BekiPrintPrepOptions(),
            acceptRgbForScopedDelivery: true));
        Assert.Contains("SINGLE_TEXT_LAYER", failure.Message);
        Assert.Contains("painted 4 times", failure.Message);
    }

    [Fact]
    public void Scoped_RGB_preparation_does_not_require_ICC_or_Ghostscript_or_claim_PDFX()
    {
        var plan = BekiLayoutFixture.EightSpreadPlan();
        var composer = new BekiPdfComposer(Options.Create(BekiLayoutFixture.ScreenProofLayout()));
        var spreads = plan.Spreads.Select(s => new BekiSpreadArtwork(s.Number, Png(150, 70))).ToList();
        var composed = composer.ComposeCanonicalWithReceipts(plan, Png(512, 245), spreads,
            BekiLayoutFixture.Personalization() with { ContinuationUrl = BekiOptions.WebsiteQrDestination });
        using (var imported = PdfReader.Open(new MemoryStream(composed.Pdf), PdfDocumentOpenMode.Import))
        using (var modified = PdfReader.Open(new MemoryStream(composed.Pdf), PdfDocumentOpenMode.Modify))
        {
            for (var page = 0; page < imported.PageCount; page++)
            {
                Assert.InRange(BekiContentWalker.Walk(imported.Pages[page], page + 1).TextDraws.Sum(d => d.Occurrences),
                    0, composed.Receipts.MaximumVisibleTextDrawsByPage.GetValueOrDefault(page + 1, 0));
                Assert.Equal(BekiContentWalker.Walk(imported.Pages[page], page + 1).TextDraws,
                    BekiContentWalker.Walk(modified.Pages[page], page + 1).TextDraws);
            }
        }
        var prepared = BekiPrintPrep.PrepareWithGates(composed.Pdf, plan.Concept.Title,
            new BekiPrintPrepOptions { OutputIntentIccPath = "missing.icc", GhostscriptPath = "missing-gs" },
            probe: new BekiPrintProbe(composed.Receipts.LightTextPages,
                composed.Receipts.FlatGroundTextProbes, composed.Receipts.MaximumVisibleTextDrawsByPage),
            canonicalMixedGeometry: true, acceptRgbForScopedDelivery: true);
        Assert.Contains("not_certified", prepared.ReportJson);
        Assert.Contains("BEKI_FINAL_SCOPE_2026-09-05", prepared.ReportJson);
        Assert.Contains("PRESS_RESOLUTION", prepared.FailedGates); // Low-resolution fixture never becomes print-approved.
        using var stream = new MemoryStream(prepared.Pdf);
        using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        Assert.Equal(12, pdf.PageCount);
        Assert.Null(pdf.Internals.Catalog.Elements["/OutputIntents"]);
        var credits = composed.Receipts.Pages.Single(p => p.Role == "credits");
        Assert.DoesNotContain("-სთვის", string.Join(" ", credits.TextLines));
    }

    private static byte[] Png(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(180, 170, 160));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }
}
