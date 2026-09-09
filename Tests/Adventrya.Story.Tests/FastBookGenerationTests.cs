using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using AdventurePacks.Api.Services.Story.Composite.Poses;
using Xunit;
using System.Text.Json;
using System.Text;
using AdventurePacks.Api.Domain.Enums;

namespace Adventrya.Story.Tests;

public class FastBookGenerationTests : CompositePipelineTestBase
{
    [Fact]
    public void Fast_cover_frame_preserves_height_without_increasing_the_pixel_budget()
    {
        var dimensions = new BekiOptions().CoverWrapImageSize.Split('x').Select(int.Parse).ToArray();
        Assert.True(dimensions[0] * dimensions[1] <= 700_000);
        var cropped = SpreadArtCrop.CropToRatio(Png(dimensions[0], dimensions[1]), BekiCoverDieline.AspectRatio);
        using var image = SixLabors.ImageSharp.Image.Load(cropped);
        Assert.True(image.Height >= dimensions[1] - 2, "The cover frame must not crop away the child's head and feet.");
    }

    [Fact]
    public async Task Testing_flow_draws_only_two_spreads_and_still_provides_the_cover_anchor()
    {
        Assert.False(new BekiOptions().TestingFlow);
        var images = new StubImageService();
        var pipeline = Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images,
            insertBekiInGeneration: true, testingFlow: true);
        var result = await pipeline.RunAsync(Request(), CancellationToken.None);
        Assert.Equal([1, 2], result.Spreads.Select(s => s.Page));
        Assert.Equal(2, images.ImageCalls);
        Assert.Equal(BookFormat.SpreadCount, result.Plan.Spreads.Count);
        Assert.Equal(BookFormat.SpreadCount, result.Scenario.Spreads!.Count);
        Assert.NotEmpty(result.Anchor!);
        await pipeline.DrawCoverWrapAsync(Context(), result.Scenario, Photo(), "image/png",
            result.Identity, result.Anchor, CancellationToken.None);
        Assert.Equal(3, images.ImageCalls);
    }

    [Fact]
    public async Task Testing_sample_completes_with_two_reader_spreads_and_cannot_be_printed_after_flag_is_off()
    {
        var world = new CompositePipelineFulfillmentTests.PackWorld { TestingFlow = true, PressStalls = true };
        // Keep the cover and first two story pages from a valid screen PDF.
        using (var source = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(BekiCanonicalBookFixtures.CanonicalScreenBook()),
                   PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
        using (var sample = new PdfSharp.Pdf.PdfDocument())
        using (var stream = new MemoryStream())
        {
            foreach (var index in new[] { 0, 3, 4 }) sample.AddPage(source.Pages[index]);
            sample.Save(stream);
            world.Composer.CanonicalPdf = stream.ToArray();
        }
        await world.Job().ProcessAsync(world.PackId, world.RunId, CancellationToken.None);
        Assert.Equal(AdventurePackStatus.Completed, world.Packs.Status);
        Assert.True(world.Composer.LastTestingFlow);
        Assert.False(world.Composer.LastPrepareForPrint);
        Assert.Equal(2, world.Composer.LastSpreadCount);
        Assert.Null(world.Packs.PrintPdfUrl);
        var pack = await world.Packs.GetByIdNoOwnershipAsync(world.PackId, CancellationToken.None);
        using var content = JsonDocument.Parse(pack!.GeneratedJson!);
        Assert.Equal(4, content.RootElement.GetProperty("storyPages").GetArrayLength());
        using var manifest = JsonDocument.Parse(world.Blobs.Uploaded[BekiPackBlobs.ManifestName(world.UserId, world.PackId)]);
        Assert.True(manifest.RootElement.GetProperty("testingFlow").GetBoolean());
        Assert.Equal(2, manifest.RootElement.GetProperty("entries").GetArrayLength());
        // A new service instance with the flag disabled must still recognize the persisted sample.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => world.Job(testingFlow: false)
            .RepreparePrintAsync(world.PackId, CancellationToken.None));
        Assert.Contains("testing-flow sample", error.Message);
    }

    [Fact]
    public async Task Printing_a_reference_generated_spread_does_not_add_a_second_Beki()
    {
        var world = new CompositePipelineFulfillmentTests.PackWorld();
        world.Composer.CanonicalPdf = BekiCanonicalBookFixtures.CanonicalScreenBook();
        await world.Job().ProcessAsync(world.PackId, world.RunId, CancellationToken.None);
        Assert.Equal(AdventurePackStatus.Completed, world.Packs.Status);
        var readingName = BekiPackBlobs.ReadingPdfName(world.UserId, world.PackId);
        var reading = world.Blobs.Uploaded[readingName];
        var png = BasePng();
        var receipt = BekiGeneratedArtwork.Receipt(png, BekiGeneratedArtwork.Reference(), "spread.png");
        world.Blobs.Seed(BekiPackBlobs.SpreadName(world.UserId, world.PackId, 1), png);
        world.Blobs.Seed(BekiPackBlobs.SpreadBaseName(world.UserId, world.PackId, 1), png);
        world.Blobs.Seed(BekiPackBlobs.CompositionManifestName(world.UserId, world.PackId, 1),
            Encoding.UTF8.GetBytes(receipt.ToJson()));
        world.Composer.CanonicalPdf = BekiCanonicalBookFixtures.CanonicalPressBook();
        await world.Job().RepreparePrintAsync(world.PackId, CancellationToken.None);
        Assert.True(world.Composer.LastPrepareForPrint);
        Assert.Equal(reading, world.Blobs.Uploaded[readingName]);
        using var prepared = JsonDocument.Parse(world.Blobs.Uploaded[
            $"{world.UserId}/{world.PackId}/print/spread-01-composition.json"]);
        Assert.False(prepared.RootElement.GetProperty("additional_beki_layer").GetBoolean());
        Assert.Equal(receipt.Output.Sha256, prepared.RootElement.GetProperty("source_sha256").GetString());
    }

    [Fact]
    public async Task Generated_Beki_uses_reference_once_per_spread_without_compositing_or_review()
    {
        var images = new StubImageService();
        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images,
            insertBekiInGeneration: true).RunAsync(Request(), CancellationToken.None);

        Assert.True(new BekiOptions().InsertBekiInGeneration);
        Assert.Equal(BookFormat.SpreadCount, images.ImageCalls);
        Assert.Equal(0, images.ReviewCalls);
        Assert.All(images.StrictFlags, Assert.True);
        var reference = BekiGeneratedArtwork.Reference();
        Assert.All(images.BekiReferences, bytes => Assert.Equal(reference, bytes));
        foreach (var spread in result.Spreads)
        {
            Assert.Equal(spread.BasePng, spread.CompositePng);
            Assert.True(BekiGeneratedArtwork.IsGenerated(spread.Manifest!));
            Assert.True(spread.Manifest!.BekiLayer.Redrawn);
            BekiPressComposite.ValidateSource(spread.BasePng, spread.CompositePng, spread.Manifest);
            Assert.NotNull(CompositeSpreadQa.TryReadStored(spread.QaJson));
            Assert.Contains("NO human fingers", spread.Prompt);
            Assert.Contains("eye size", spread.Prompt);
            Assert.Contains("exactly FOUR rounded digits on EACH hand", spread.Prompt);
            Assert.Contains("Four total, NOT four plus a thumb", spread.Prompt);
            Assert.Contains("golden-yellow/amber irises", spread.Prompt);
            Assert.Contains("MOUTH: preserve the same small open upturned", spread.Prompt);
            Assert.Contains("those images are NOT Beki design", spread.Prompt);
            Assert.Contains("(FINAL IMAGE)", spread.Prompt);
            Assert.EndsWith(BekiIdentity.GenerationFinalCheck, spread.Prompt.Trim());
            Assert.DoesNotContain("five-lobed", spread.Prompt);
            Assert.DoesNotContain("pose/expression adjustment", spread.Prompt);
            Assert.DoesNotContain("Do not generate Beki", spread.Prompt);
            Assert.DoesNotContain("later exact Beki PNG compositing", spread.Prompt);
        }
    }

    [Fact]
    public void Stronger_identity_lock_versions_new_generation_but_preserves_old_book_provenance()
    {
        var receipt = BekiGeneratedArtwork.Receipt(BasePng(), BekiGeneratedArtwork.Reference(), "spread.png");
        Assert.Equal("beki-reference-generated-v2", receipt.CompositionVersion);
        Assert.True(BekiGeneratedArtwork.IsGenerated(receipt with { CompositionVersion = "beki-reference-generated-v1" }));
        Assert.False(BekiGeneratedArtwork.IsGenerated(receipt with { CompositionVersion = "unrecognized" }));
        Assert.Equal(BekiGeneratedArtwork.Version,
            BekiCompositeContractTerms.Current("dinosaurs", true).ImagePromptVersion);
    }

    [Fact]
    public void Generated_artwork_cannot_pass_source_validation_after_tampering()
    {
        var png = BasePng();
        var receipt = BekiGeneratedArtwork.Receipt(png, BekiGeneratedArtwork.Reference(), "spread.png");
        var changed = png.ToArray();
        changed[^1] ^= 1;
        Assert.Throws<BekiLayoutException>(() => BekiPressComposite.ValidateSource(png, changed, receipt));
    }

    [Fact]
    public void Fast_cover_default_is_supported_and_smaller_than_spreads()
    {
        var options = new BekiOptions();
        Assert.Equal("1200x576", options.CoverWrapImageSize);
        Assert.Equal("medium", options.CoverImageQuality);
        Assert.Equal("gpt-image-2.5-flare", new OpenAiOptions().ImageEditModel);
        Assert.Null(BekiImageRequestValidation.Validate(options, new OpenAiOptions(), new AiProviderOptions()));
        Assert.NotEqual(BekiCompositeContractTerms.Current("dinosaurs", true),
            BekiCompositeContractTerms.Current("dinosaurs", false));
    }
}
