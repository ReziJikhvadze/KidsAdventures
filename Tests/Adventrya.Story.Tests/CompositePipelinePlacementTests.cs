using System.Text.Json;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using AdventurePacks.Api.Services.Story.Composite.Poses;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The placement decision as a whole book sees it — under the policy the owner actually ships,
/// where no model looks at a spread and the only thing that ever judges where Beki landed is
/// arithmetic.
///
/// <see cref="BekiPlacementChooserTests"/> proves the chooser's own claims against pictures it
/// painted. These two prove the seam: that the pipeline asks it at all, composites at the answer,
/// and writes the answer down where a release gate and a human can read it. A chooser nobody called
/// would pass every test in that class and change no book.
/// </summary>
public class CompositePipelinePlacementTests : CompositePipelineTestBase
{
    /// <summary>
    /// A book drawn on bases whose configured anchor is busy: every spread's receipt records an
    /// anchor that is not the configured one, and every spread's QA document says so and why.
    ///
    /// The manifest is the load-bearing half. It is what a reprint reads, what the release gates
    /// check, and what the placement retry starts from if a reviewer is ever turned back on — so a
    /// chooser whose answer reached the picture but not the receipt would produce a book nobody
    /// could reproduce.
    /// </summary>
    [Fact]
    public async Task A_busy_base_moves_Beki_off_the_configured_anchor_and_records_it()
    {
        var engine = BekiCompositeEngine.Create();
        var images = new StubImageService { NextImage = BusyProviderFrame() };

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(context: Skipping()), CancellationToken.None);

        Assert.Equal(BookFormat.SpreadCount, result.Spreads.Count);

        // Not one vision call: this is the whole point. The page moved because arithmetic said so.
        Assert.Equal(0, images.ReviewCalls);

        Assert.All(result.Spreads, spread =>
        {
            var side = BekiCompositeConfig.ParseTextSide(spread.TextSide);
            var configured = engine.Config.StoryDefaultFor(side);
            var placed = spread.Manifest!.BekiLayer.NormalizedAnchor;

            Assert.NotEqual(configured.VisibleCenterX, placed.VisibleCenterX);

            using var record = JsonDocument.Parse(spread.QaJson!);
            var placement = record.RootElement.GetProperty("beki_placement");

            Assert.Equal(BekiPlacementChooser.Version, placement.GetProperty("version").GetString());
            Assert.True(placement.GetProperty("moved").GetBoolean());

            // The two anchors as the receipt tells them apart, and the chosen one agreeing with the
            // manifest — the same three numbers in both files or the paperwork disagrees with the
            // picture.
            Assert.Equal(
                configured.VisibleCenterX,
                placement.GetProperty("default").GetProperty("visible_center_x").GetDouble());
            Assert.Equal(
                placed.VisibleCenterX,
                placement.GetProperty("chosen").GetProperty("visible_center_x").GetDouble());
            Assert.Equal(
                placed.VisibleHeight,
                placement.GetProperty("chosen").GetProperty("visible_height").GetDouble());

            // And the evidence for the decision, so a person reading the file a year from now can
            // see what was measured rather than being asked to trust it.
            Assert.True(
                placement.GetProperty("chosen_score").GetDouble()
                < placement.GetProperty("default_score").GetDouble());
            Assert.True(placement.GetProperty("candidates_evaluated").GetInt32() > 1);
            Assert.NotEmpty(placement.GetProperty("reason").GetString()!);
        });
    }

    /// <summary>
    /// And a book drawn on calm bases keeps the configured anchor on every page, with the record
    /// saying it was considered and kept.
    ///
    /// This is the half that protects the existing books. The anchors in the pipeline config were
    /// proven against a printed proof; a chooser that moved Beki on a page with nothing wrong with
    /// it would be re-composing signed-off artwork for nothing, and several frozen tests read the
    /// configured 0.594 straight out of a manifest.
    /// </summary>
    [Fact]
    public async Task A_calm_base_keeps_the_configured_anchor_and_says_it_looked()
    {
        var engine = BekiCompositeEngine.Create();
        var images = new StubImageService();

        var result = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(context: Skipping()), CancellationToken.None);

        Assert.All(result.Spreads, spread =>
        {
            var configured = engine.Config.StoryDefaultFor(
                BekiCompositeConfig.ParseTextSide(spread.TextSide));
            var placed = spread.Manifest!.BekiLayer.NormalizedAnchor;

            Assert.Equal(configured.VisibleCenterX, placed.VisibleCenterX);
            Assert.Equal(configured.VisibleCenterY, placed.VisibleCenterY);
            Assert.Equal(configured.VisibleHeight, placed.VisibleHeight);

            using var record = JsonDocument.Parse(spread.QaJson!);
            var placement = record.RootElement.GetProperty("beki_placement");

            Assert.False(placement.GetProperty("moved").GetBoolean());
            Assert.Equal(
                placement.GetProperty("default_score").GetDouble(),
                placement.GetProperty("chosen_score").GetDouble());
        });
    }

    // ==============================================================================================
    // Harness
    // ==============================================================================================

    /// <summary>The shipped policy, whose <c>image_review</c> is a flag: no model looks at a page.</summary>
    private static CompositeBookContext Skipping() =>
        Context() with { ReleasePolicy = BekiReleasePolicySnapshot.Defaults };

    /// <summary>
    /// A picture in the shape the image provider actually returns — 3:2, cropped to the printed 15:7
    /// on the way in — that is busy where both configured anchors sit and calm in two outer bands.
    ///
    /// The calm bands run the full height so that the centred crop cannot move them, and both sit
    /// well clear of the middle so the checkerboard runs unbroken across the fold: a calm band
    /// straddling the centre line would be a full-height step at exactly 50% of the canvas, which is
    /// the defect the centre-fold gate buys a regeneration for, and this fixture would be testing
    /// that gate instead of the placement.
    /// </summary>
    private static byte[] BusyProviderFrame()
    {
        const int Square = 24;
        var dark = new Rgba32(60, 70, 110, 255);
        var light = new Rgba32(220, 210, 180, 255);
        var calm = new Rgba32(140, 140, 145, 255);

        using var image = new Image<Rgba32>(ProviderWidth, ProviderHeight);

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = (x is >= 140 and < 440) || (x is >= 1100 and < 1400)
                        ? calm
                        : ((x / Square) + (y / Square)) % 2 == 0 ? dark : light;
                }
            }
        });

        using var buffer = new MemoryStream();
        image.SaveAsPng(buffer);
        return buffer.ToArray();
    }
}
