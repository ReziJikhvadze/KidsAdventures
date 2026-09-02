using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The seam between the two pipelines and the layout stage's crop tolerance.
///
/// The composite pipeline normalizes its bases to 15:7 before it stores them; the legacy generator —
/// the one that draws every book in production while the composite flag is off — used to store the
/// provider's raw 3:2 frame and let the composer centre-crop it at layout time. That crop is now a
/// refusal past <see cref="BekiPrintLayoutOptions.PrintCropTolerance"/>, which would have turned
/// every legacy book into <c>LAYOUT_FAILED</c> the moment this deployed. These tests are the two
/// halves of that: the generator's stored spreads are the sheet's shape, and the raw frame it used
/// to store would indeed have been refused.
/// </summary>
public class BekiLegacySpreadNormalizationTests
{
    /// <summary>The provider's own landscape frame — <see cref="BekiBookGenerator.SpreadImageSize"/>.</summary>
    private const int RawWidth = 1536;
    private const int RawHeight = 1024;

    /// <summary>
    /// What the legacy generator hands on is the sheet's shape, spread by spread — and the cover is
    /// deliberately left at the provider's frame, because a cover's geometry is the printer's wrap
    /// rather than this sheet.
    /// </summary>
    [Fact]
    public async Task The_legacy_generator_stores_spreads_at_the_sheets_own_shape()
    {
        var layout = new BekiPrintLayoutOptions();
        var book = await IllustrateAsync(layout);

        Assert.Equal(BookFormat.SpreadCount, book.Spreads.Count);

        var sheetRatio = (layout.SpreadWidthMm + (layout.BleedMm * 2f))
            / (layout.SpreadHeightMm + (layout.BleedMm * 2f));

        foreach (var spread in book.Spreads)
        {
            var info = Image.Identify(spread.Image!);
            var ratio = (float)info.Width / info.Height;

            Assert.True(Math.Abs(ratio - sheetRatio) < 0.005f,
                $"Spread {spread.SpreadNumber} was stored at {info.Width}×{info.Height} ({ratio:F4}); "
                + $"the sheet is {sheetRatio:F4}.");
        }

        // The provider drew 3:2, so this really did crop rather than passing everything through.
        var first = Image.Identify(book.Spreads[0].Image!);
        Assert.Equal(RawWidth, first.Width);
        Assert.True(first.Height < RawHeight, "Nothing was cropped; the fixture is not the raw frame.");

        // And the cover is untouched: exempt by design, and its print artifact is withheld anyway.
        var cover = Image.Identify(book.Cover.Image!);
        Assert.Equal(RawWidth, cover.Width);
        Assert.Equal(RawHeight, cover.Height);
    }

    /// <summary>
    /// And what the generator produces is what the composer accepts — the actual claim, made against
    /// the actual composer rather than against an arithmetic restatement of its tolerance.
    /// </summary>
    [Fact]
    public async Task The_composer_accepts_what_the_legacy_generator_now_produces()
    {
        var book = await IllustrateAsync(new BekiPrintLayoutOptions());

        var plan = BekiLayoutFixture.EightSpreadPlan();
        var spreads = book.Spreads
            .Select(spread => new BekiSpreadArtwork(spread.SpreadNumber!.Value, spread.Image!))
            .ToList();

        var pdf = new BekiPdfComposer(Options.Create(BekiLayoutFixture.ScreenProofLayout()))
            .ComposeWithReceipts(plan, book.Cover.Image!, spreads,
                BekiLayoutFixture.Personalization()).Pdf;

        Assert.True(pdf.Length > 0);
    }

    /// <summary>
    /// The regression this exists to prevent, stated as the failure it would have been: the raw 3:2
    /// frame the generator used to store loses about three tenths of its height to the sheet, and
    /// the layout stage stops the book rather than taking it.
    ///
    /// Kept as a test rather than a comment because the tolerance is configuration and the
    /// normalization is code: if either moves, the two halves of this file have to be looked at
    /// together.
    /// </summary>
    [Fact]
    public void The_raw_provider_frame_the_generator_used_to_store_would_have_stopped_the_book()
    {
        var plan = BekiLayoutFixture.EightSpreadPlan();
        var raw = RawFrame();
        var spreads = plan.Spreads
            .Select(spread => new BekiSpreadArtwork(spread.Number, raw))
            .ToList();

        var failure = Assert.Throws<BekiLayoutException>(
            () => new BekiPdfComposer(Options.Create(BekiLayoutFixture.ScreenProofLayout()))
                .ComposeWithReceipts(plan, BekiLayoutFixture.LeafPng((200, 60, 60)), spreads,
                    BekiLayoutFixture.Personalization()));

        Assert.Equal(CompositeFailureCodes.LayoutFailed, failure.FailureCode);
        Assert.Contains("PrintCropTolerance", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Both pipelines aim at the same shape.
    ///
    /// The composite pipeline crops to a constant 15:7; the legacy generator crops to the sheet the
    /// layout options describe. They agree today because the handoff's 450 × 210 mm sheet *is* 15:7 —
    /// and if a future geometry change parted them, one pipeline's books would start failing the
    /// other's tolerance, which is the kind of thing worth finding here.
    /// </summary>
    [Fact]
    public void The_two_pipelines_normalize_to_the_same_ratio()
    {
        var layout = new BekiPrintLayoutOptions();
        var sheetRatio = (layout.SpreadWidthMm + (layout.BleedMm * 2f))
            / (layout.SpreadHeightMm + (layout.BleedMm * 2f));

        Assert.Equal(CompositeDeterministicChecks.TargetAspect, sheetRatio, 4);
    }

    private static async Task<BekiBookResult> IllustrateAsync(BekiPrintLayoutOptions layout)
    {
        var generator = new BekiBookGenerator(
            new UnusedStoryModelClient(),
            new FlatFrameImageService(),
            Options.Create(layout),
            Options.Create(new BekiOptions()),
            NullLogger<BekiBookGenerator>.Instance);

        return await generator.IllustrateAsync(
            BekiLayoutFixture.EightSpreadPlan(),
            ChildPhoto(),
            "image/png",
            existingCover: null,
            onImage: null,
            CancellationToken.None);
    }

    /// <summary>The provider's frame, in one flat colour: this is about geometry, not pixels.</summary>
    private static byte[] RawFrame()
    {
        using var image = new Image<Rgba32>(RawWidth, RawHeight, new Rgba32(40, 140, 110, 255));
        using var buffer = new MemoryStream();
        image.Save(buffer, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        return buffer.ToArray();
    }

    private static byte[] ChildPhoto()
    {
        using var image = new Image<Rgba32>(256, 256, new Rgba32(220, 190, 170, 255));
        using var buffer = new MemoryStream();
        image.Save(buffer, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        return buffer.ToArray();
    }

    /// <summary>Every image call answers with the provider's raw 3:2 frame, as gpt-image does.</summary>
    private sealed class FlatFrameImageService : IOpenAiService
    {
        public Task<byte[]> GenerateStoryImageAsync(
            string imagePrompt, StoryImageReference? reference, CancellationToken cancellationToken,
            string? imageSize = null, bool requireReferences = false, string? imageQuality = null)
        {
            Assert.Equal(BekiBookGenerator.SpreadImageSize, imageSize);
            return Task.FromResult(RawFrame());
        }

        public Task<AdventureContentDto> GenerateAdventureContentAsync(
            AdventureGenerationInput input, Guid adventureId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<string> ReviewIllustrationAsync(
            byte[] imageBytes, string reviewPrompt,
            IReadOnlyList<(byte[] Bytes, string ContentType, string Label)> references,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<string> DescribeCharacterFromPhotoAsync(
            byte[] imageBytes, string contentType, string promptText, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<string> CompleteTextAsync(string promptText, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    /// <summary>IllustrateAsync is handed a finished plan, so the planner is never called.</summary>
    private sealed class UnusedStoryModelClient : IStoryModelClient
    {
        public Task<ModelResult<T>> CompleteAsync<T>(
            string model, string systemPrompt, string userPrompt, string schemaName,
            JsonElement schema, CancellationToken cancellationToken)
            => throw new NotSupportedException("IllustrateAsync must not plan.");
    }
}
