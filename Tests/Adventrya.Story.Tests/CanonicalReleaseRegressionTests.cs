using AdventurePacks.Api.Services.Pdf;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using AdventurePacks.Api.Services.Story.Composite.Poses;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Adventrya.Story.Tests;

public class CanonicalReleaseRegressionTests : CompositePipelineTestBase
{
    [Fact]
    public void Customer_pdf_validation_rejects_truncated_and_empty_pdfs_even_when_print_is_optional()
    {
        Assert.Throws<BekiLayoutException>(() => BekiCustomerPdfValidation.Validate([0x25, 0x50, 0x44, 0x46]));
        using var document = new PdfDocument();
        document.AddPage();
        using var bytes = new MemoryStream();
        document.Save(bytes);
        Assert.Throws<BekiLayoutException>(() => BekiCustomerPdfValidation.Validate(bytes.ToArray()));
    }

    [Fact]
    public async Task Invalid_adopted_story_stops_before_any_image_or_scenario_call()
    {
        var images = new StubImageService();
        var plan = Plan();
        plan = plan with { Concept = plan.Concept with { Outline = [] } };
        var error = await Assert.ThrowsAsync<CompositePipelineException>(() =>
            Pipeline(new ScriptedStoryModelClient(), images)
                .RunAsync(Request() with { ExistingPlan = plan }, CancellationToken.None));
        Assert.Contains("structural validation", error.Message);
        Assert.Equal(0, images.ImageCalls);
    }

    [Fact]
    public async Task Invalid_new_story_gets_one_correction_then_stops_before_images()
    {
        var images = new StubImageService();
        var plan = Plan();
        plan = plan with { Concept = plan.Concept with { Outline = [] } };
        var planner = new ScriptedCompositeStoryService(plan, plan);
        var error = await Assert.ThrowsAsync<CompositePipelineException>(() =>
            Pipeline(new ScriptedStoryModelClient(), images, masterStory: planner)
                .RunAsync(Request() with { ExistingPlan = null }, CancellationToken.None));
        Assert.Contains("after one retry", error.Message);
        Assert.Equal(2, planner.Calls);
        Assert.Contains(planner.Problems[1], p => p.Contains("outline"));
        Assert.Equal(0, images.ImageCalls);
    }

    [Fact]
    public void Print_recomposition_preserves_the_pose_and_anchor_and_rejects_wrong_base()
    {
        var engine = BekiCompositeEngine.Create();
        var basePng = Png(1536, 717);
        var original = engine.CompositeStorySpread(basePng, "base.png", "pose_04_listen",
            BekiTextSide.Left, "composite.png");
        BekiPressComposite.ValidateSource(basePng, original.Png, original.Manifest);
        Assert.Throws<BekiLayoutException>(() =>
            BekiPressComposite.ValidateSource([1, 2, 3], original.Png, original.Manifest));
        var result = BekiPressComposite.Compose(Png(3072, 1434), original.Manifest, "print/spread-01");
        Assert.Equal(original.Manifest.BekiLayer.Sha256, result.Manifest.BekiLayer.Sha256);
        Assert.Equal(original.Manifest.BekiLayer.NormalizedAnchor, result.Manifest.BekiLayer.NormalizedAnchor);
        Assert.Equal(3072, result.Manifest.Canvas.WidthPx);
        Assert.False(result.Manifest.BekiLayer.Redrawn);
        Assert.Equal(1, result.Manifest.BekiLayer.Opacity);
    }

    [Fact]
    public void Approved_logo_uses_exact_native_axial_shading_without_raster_images()
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Elements["/Resources"] = new PdfDictionary(document);
        using var input = new MemoryStream();
        document.Save(input);
        var logo = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
            "Assets", "BekiComposite", "logo", "HiResLight.svg"));
        var pdf = BekiVectorLogo.Apply(input.ToArray(), logo);
        using var output = new MemoryStream(pdf);
        using var read = PdfReader.Open(output, PdfDocumentOpenMode.Modify);
        var resources = read.Pages[0].Elements.GetDictionary("/Resources")!;
        Assert.Null(resources.Elements.GetDictionary("/XObject"));
        var shading = resources.Elements.GetDictionary("/Shading")!
            .Elements.GetDictionary("/BekiApprovedLogo")!;
        Assert.Equal(2, shading.Elements.GetInteger("/ShadingType"));
        var coords = shading.Elements.GetArray("/Coords")!;
        Assert.Equal(227.126551, coords.Elements.GetReal(0), 6);
        Assert.Equal(791.611139, coords.Elements.GetReal(1), 6);
        Assert.Equal(225.321884, coords.Elements.GetReal(2), 6);
        Assert.Equal(754.987243, coords.Elements.GetReal(3), 6);
        Assert.Throws<InvalidOperationException>(() => BekiVectorLogo.Apply(pdf, [1, 2, 3]));
    }
}
