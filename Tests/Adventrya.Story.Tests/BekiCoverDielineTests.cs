using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using Microsoft.Extensions.Options;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The Locked Print Specification's hardcover wrap, as code: the millimetres must sum the way
/// the spec's tables sum, the developer-chosen front-panel rectangles must sit inside the front
/// board, the wrap pipeline must compose the exact pose against them with a receipt, and the
/// press cover page must be the 512 × 245 canvas with a vector Ottia title.
/// </summary>
public class BekiCoverDielineTests : CompositePipelineTestBase
{
    [Fact]
    public void The_locked_millimetres_sum_exactly_as_the_spec_tables_do()
    {
        // Horizontal: 20 + 222.5 + 8 + 11 + 8 + 222.5 + 20 = 512.
        Assert.Equal(
            BekiCoverDieline.CanvasWidthMm,
            (BekiCoverDieline.TurnInMm * 2)
            + (BekiCoverDieline.BoardWidthMm * 2)
            + (BekiCoverDieline.HingeMm * 2)
            + BekiCoverDieline.SpineMm,
            2);

        // Vertical: 20 + 205 + 20 = 245.
        Assert.Equal(
            BekiCoverDieline.CanvasHeightMm,
            (BekiCoverDieline.TurnInMm * 2) + BekiCoverDieline.BoardHeightMm,
            2);

        // The centre construction is 27 mm of hinge+spine+hinge, not a printable spine.
        Assert.Equal(27f, (BekiCoverDieline.HingeMm * 2) + BekiCoverDieline.SpineMm, 2);

        Assert.Equal(269.5f, BekiCoverDieline.FrontBoardLeftMm, 2);
        Assert.Equal(492f, BekiCoverDieline.FrontBoardRightMm, 2);
    }

    [Fact]
    public void The_front_panel_rectangles_sit_inside_the_front_board()
    {
        // The title-safe rectangle, wholly inside the front board.
        Assert.True(BekiCoverDieline.TitleSafeLeftMm >= BekiCoverDieline.FrontBoardLeftMm);
        Assert.True(
            BekiCoverDieline.TitleSafeLeftMm + BekiCoverDieline.TitleSafeWidthMm
            <= BekiCoverDieline.FrontBoardRightMm);
        Assert.True(BekiCoverDieline.TitleSafeTopMm >= BekiCoverDieline.BoardTopMm);
        Assert.True(
            BekiCoverDieline.TitleSafeTopMm + BekiCoverDieline.TitleSafeHeightMm
            <= BekiCoverDieline.BoardBottomMm);

        // The Beki anchor: her whole visible extent inside the front board, clear of the hinge
        // and the turn-in — a pose across the spine would fold the character in half.
        var anchor = BekiCoverDieline.FrontBekiAnchor;
        var centreXmm = anchor.VisibleCenterX * BekiCoverDieline.CanvasWidthMm;
        var centreYmm = anchor.VisibleCenterY * BekiCoverDieline.CanvasHeightMm;
        var halfHeightMm = anchor.VisibleHeight * BekiCoverDieline.CanvasHeightMm / 2;

        Assert.InRange(
            centreXmm,
            BekiCoverDieline.FrontBoardLeftMm + halfHeightMm,
            BekiCoverDieline.FrontBoardRightMm - halfHeightMm);
        Assert.InRange(
            centreYmm,
            BekiCoverDieline.BoardTopMm + halfHeightMm,
            BekiCoverDieline.BoardBottomMm - halfHeightMm);
    }

    [Fact]
    public void The_cover_prompt_resolves_against_the_locked_geometry()
    {
        var scenario = VisualScenarioValidator.Validate(ScenarioFixture()).Scenario!;

        var prompt = CompositeIllustrationPrompt.ForCover(
            BekiCoverDieline.Geometry,
            childAge: 5,
            CompositeThemeReferences.For("dinosaurs"),
            scenario.Cover!.FrontChildWorldScene!,
            scenario.Cover.BackEnvironment!,
            scenario.VisualLock!.ChildOutfit!,
            recurringElements: []);

        // The locked panels, resolved into the prompt — and the exact-Beki rule intact.
        Assert.Contains("hardcover wrap", prompt);
        Assert.Contains("Back panel: from 4% to 47%", prompt);
        Assert.Contains("Centre construction: from 47% to 53%", prompt);
        Assert.Contains("Front panel: from 53% to 96%", prompt);
        Assert.Contains("Do not generate Beki.", prompt);
    }

    /// <summary>
    /// The wrap pipeline end to end against a stubbed provider: generated, cropped to 512:245,
    /// the pose composited at the locked anchor, and the receipt saying so.
    /// </summary>
    [Fact]
    public async Task The_wrap_is_cropped_to_the_dieline_and_carries_an_exact_Beki_receipt()
    {
        var images = new StubImageService();
        var pipeline = Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images);
        var scenario = VisualScenarioValidator.Validate(ScenarioFixture()).Scenario!;

        var wrap = await pipeline.DrawCoverWrapAsync(
            Context(), scenario, Png(64, 64), "image/png", CancellationToken.None);

        // The base is the wrap's own shape, from the provider's 3:2 frame.
        using (var image = SixLabors.ImageSharp.Image.Load(wrap.BasePng))
        {
            Assert.Equal(
                BekiCoverDieline.AspectRatio,
                (float)image.Width / image.Height,
                2);
        }

        // The receipt: exact pose, exact anchor, nothing mirrored, warped, or redrawn.
        using var manifest = JsonDocument.Parse(wrap.ManifestJson);
        var layer = manifest.RootElement.GetProperty("beki_layer");

        Assert.Equal(wrap.PoseId, layer.GetProperty("pose_id").GetString());
        Assert.Equal(1.0, layer.GetProperty("opacity").GetDouble());
        Assert.False(layer.GetProperty("redrawn").GetBoolean());
        Assert.Equal(
            BekiCoverDieline.FrontBekiAnchor.VisibleCenterX,
            layer.GetProperty("normalized_anchor").GetProperty("visible_center_x").GetDouble(),
            3);

        // And the generation call itself was told the panels and forbidden Beki.
        Assert.Contains("hardcover wrap", wrap.Prompt);
        Assert.Contains("Do not generate Beki.", wrap.Prompt);
    }

    [Fact]
    public void The_press_cover_page_is_the_wrap_canvas_with_a_typeset_title()
    {
        var composer = new BekiPdfComposer(Options.Create(BekiLayoutFixture.ScreenProofLayout()));

        var pdf = composer.ComposeCoverPress(
            "სინათლის პატარა ქალაქი", BekiLayoutFixture.SheetPng((40, 90, 60)));

        var text = System.Text.Encoding.Latin1.GetString(pdf);

        // One page, at 512 × 245 mm in points.
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(text, @"/Type\s*/Page[^s]").Count);

        var media = System.Text.RegularExpressions.Regex.Match(
            text, @"/MediaBox\s*\[\s*0\s+0\s+([\d.]+)\s+([\d.]+)\s*\]");
        Assert.True(media.Success, "no MediaBox found");

        // Half a point of slack (0.18 mm): QuestPDF rounds page sizes slightly, and a press
        // cares about tenths of a millimetre, not hundredths.
        var width = float.Parse(media.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var height = float.Parse(media.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(Math.Abs(width - (512f / 25.4f * 72f)) < 0.5f, $"width {width}pt");
        Assert.True(Math.Abs(height - (245f / 25.4f * 72f)) < 0.5f, $"height {height}pt");
    }
}
