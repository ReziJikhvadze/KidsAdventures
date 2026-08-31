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

        // The de-zoned composition block, resolved into the prompt — and the exact-Beki rule intact.
        //
        // These three assertions replace three that are void. The prompt used to be checked for
        // "Back panel: from 4% to 47%", "Centre construction: from 47% to 53%" and "Front panel:
        // from 53% to 96%" — the percentages audit P0-03 found painted into the delivered cover as
        // vertical tonal bands at 250.5 mm and 261.5 mm, which are those numbers in the printer's
        // own millimetres. The geometry still governs compositing and typesetting; it simply stopped
        // being something an image model is told.
        Assert.Contains("one continuous panoramic scene", prompt);
        Assert.Contains("right side of the picture", prompt);
        Assert.DoesNotContain("%", prompt);
        Assert.Contains("Do not generate Beki.", prompt);
    }

    /// <summary>
    /// The installed composition block is the contract's published block, character for character.
    ///
    /// Read out of <c>BEKI_Cover_Base_Prompt_Template_v1.md</c>'s own fenced block rather than
    /// restated here, because a copy in a test is a second source of truth and the whole of P0-03 is
    /// that the words the model receives are the words somebody approved.
    /// </summary>
    [Fact]
    public void The_installed_panel_instructions_are_the_contracts_published_block()
    {
        var contract = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Assets", "BekiComposite", "contracts",
            "BEKI_Cover_Base_Prompt_Template_v1.md"));

        var published = FencedBlockContaining(contract, "one continuous panoramic scene");

        Assert.Equal(published, BekiCoverDieline.PanelInstructions.Replace("\r\n", "\n"));

        // And the law the block exists to obey.
        Assert.DoesNotContain("%", BekiCoverDieline.PanelInstructions);
        Assert.DoesNotContain("panel", BekiCoverDieline.PanelInstructions.Replace(
            "blank panel", string.Empty, StringComparison.Ordinal));
    }

    private static string FencedBlockContaining(string markdown, string needle)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var block = new List<string>();
        var inside = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                if (inside && block.Any(entry => entry.Contains(needle, StringComparison.Ordinal)))
                {
                    return string.Join('\n', block);
                }

                inside = !inside;
                block.Clear();
                continue;
            }

            if (inside) block.Add(line);
        }

        throw new Xunit.Sdk.XunitException(
            $"No fenced block in the cover prompt contract contains '{needle}'.");
    }

    /// <summary>
    /// The customer's cover pages are crops of the press cover's own master, and the crop does not
    /// distort — amendment A3, against audit P0-01 and P0-02.
    ///
    /// The arithmetic is the whole finding: a 222.5 × 205 mm board resized onto a 220 × 200 mm page
    /// is a 1.3% squash in one axis, which nobody would report as a defect and everybody would see
    /// as "the download looks slightly different from the print". So the window carries the page's
    /// own ratio and the placement is a uniform scale.
    /// </summary>
    [Fact]
    public void The_digital_crop_window_carries_the_customer_pages_own_ratio()
    {
        // 222.5 ÷ 1.1 — the board's full width, the page's height ratio.
        Assert.Equal(222.5f, BekiCoverDieline.DigitalCropWidthMm, 3);
        Assert.Equal(202.2727f, BekiCoverDieline.DigitalCropHeightMm, 3);

        Assert.Equal(
            BekiCoverDieline.DigitalPageWidthMm / BekiCoverDieline.DigitalPageHeightMm,
            BekiCoverDieline.DigitalCropWidthMm / BekiCoverDieline.DigitalCropHeightMm,
            4);

        // Centred in the board: the same sliver given up top and bottom, and both inside it.
        var top = BekiCoverDieline.DigitalCropTopMm - BekiCoverDieline.BoardTopMm;
        var bottom = BekiCoverDieline.BoardBottomMm - BekiCoverDieline.DigitalCropBottomMm;
        Assert.Equal(top, bottom, 3);
        Assert.Equal(1.3636f, top, 3);
        Assert.True(BekiCoverDieline.DigitalCropTopMm >= BekiCoverDieline.BoardTopMm);
        Assert.True(BekiCoverDieline.DigitalCropBottomMm <= BekiCoverDieline.BoardBottomMm);
    }

    /// <summary>
    /// And the same window in pixels on a real wrap: both boards, inside the canvas, the same size,
    /// and each one landing where its board is rather than where the other board is.
    /// </summary>
    [Theory]
    [InlineData(2528, 1210)]
    [InlineData(6047, 2894)]
    public void The_board_crops_land_on_their_own_boards(int widthPx, int heightPx)
    {
        var front = BekiCoverDieline.FrontBoardDigitalCrop(widthPx, heightPx);
        var back = BekiCoverDieline.BackBoardDigitalCrop(widthPx, heightPx);

        foreach (var window in new[] { front, back })
        {
            Assert.True(window.XPx >= 0 && window.YPx >= 0);
            Assert.True(window.XPx + window.WidthPx <= widthPx);
            Assert.True(window.YPx + window.HeightPx <= heightPx);

            // The window's own ratio is the customer page's, to within a pixel of rounding.
            Assert.Equal(
                BekiCoverDieline.DigitalPageWidthMm / BekiCoverDieline.DigitalPageHeightMm,
                (float)window.WidthPx / window.HeightPx,
                2);
        }

        Assert.Equal(front.WidthPx, back.WidthPx);
        Assert.Equal(front.HeightPx, back.HeightPx);
        Assert.Equal(front.YPx, back.YPx);

        // The back board is left of the centre construction and the front board is right of it.
        var centreLeftPx = 242.5f / BekiCoverDieline.CanvasWidthMm * widthPx;
        var centreRightPx = 269.5f / BekiCoverDieline.CanvasWidthMm * widthPx;

        Assert.True(back.XPx + back.WidthPx <= centreLeftPx + 1);
        Assert.True(front.XPx >= centreRightPx - 1);
    }

    /// <summary>
    /// The front-panel Beki anchor, decided by measurement rather than by taste — amendment A10b,
    /// against audit P1-09 ("the exact Beki asset overlaps the child's torso and its top curl
    /// reaches the face area").
    ///
    /// Every pose in the approved registry is placed at the anchor exactly as
    /// <c>BekiCompositeEngine</c> places it — the visible height is the anchor's fraction of the
    /// canvas, the width follows from that pose's own alpha bounding box, and the anchor addresses
    /// the centre of the visible box — and the resulting rectangle has to sit inside the front
    /// board, keep clear of the title-safe rectangle, and stay inside the 96.1% turn-in line. The
    /// anchor ships only with this green; a change to it that any pose cannot satisfy is a change
    /// that would put a wing over the hinge on some book nobody thought to check.
    /// </summary>
    [Fact]
    public void Every_approved_pose_lands_inside_the_front_board_at_the_shipped_anchor()
    {
        var registry = AdventurePacks.Api.Services.Story.Composite.Poses.BekiPoseRegistry.Load();
        var anchor = BekiCoverDieline.FrontBekiAnchor;

        var heightMm = (float)(anchor.VisibleHeight * BekiCoverDieline.CanvasHeightMm);
        var centreXmm = (float)(anchor.VisibleCenterX * BekiCoverDieline.CanvasWidthMm);
        var centreYmm = (float)(anchor.VisibleCenterY * BekiCoverDieline.CanvasHeightMm);

        var topMm = centreYmm - (heightMm / 2f);
        var bottomMm = centreYmm + (heightMm / 2f);

        // 96.1% of the canvas: where the right turn-in begins folding round the board.
        const float TurnInLineMm = 0.961f * BekiCoverDieline.CanvasWidthMm;

        Assert.NotEmpty(registry.Poses);

        foreach (var pose in registry.Poses)
        {
            var aspect = VisibleAspect(registry.ApprovedPoseBytes(pose.Id));
            var widthMm = heightMm * aspect;
            var leftMm = centreXmm - (widthMm / 2f);
            var rightMm = centreXmm + (widthMm / 2f);

            Assert.True(leftMm >= BekiCoverDieline.FrontBoardLeftMm,
                $"{pose.Id}: left edge {leftMm:F1} mm is off the front board.");
            Assert.True(rightMm <= BekiCoverDieline.FrontBoardRightMm,
                $"{pose.Id}: right edge {rightMm:F1} mm is off the front board.");
            Assert.True(topMm >= BekiCoverDieline.BoardTopMm,
                $"{pose.Id}: top edge {topMm:F1} mm is above the board.");
            Assert.True(bottomMm <= BekiCoverDieline.BoardBottomMm,
                $"{pose.Id}: bottom edge {bottomMm:F1} mm is below the board.");
            Assert.True(rightMm <= TurnInLineMm,
                $"{pose.Id}: right edge {rightMm:F1} mm reaches the {TurnInLineMm:F1} mm turn-in.");

            // Clear of the title, not merely beside it: no overlap in either axis.
            var titleRight = BekiCoverDieline.TitleSafeLeftMm + BekiCoverDieline.TitleSafeWidthMm;
            var titleBottom = BekiCoverDieline.TitleSafeTopMm + BekiCoverDieline.TitleSafeHeightMm;

            var overlaps = leftMm < titleRight
                && rightMm > BekiCoverDieline.TitleSafeLeftMm
                && topMm < titleBottom
                && bottomMm > BekiCoverDieline.TitleSafeTopMm;

            Assert.False(overlaps,
                $"{pose.Id}: the placed pose ({leftMm:F1}–{rightMm:F1} × {topMm:F1}–{bottomMm:F1} mm) "
                + "overlaps the title-safe rectangle.");
        }
    }

    /// <summary>
    /// A pose's visible alpha box, aspect only — the same box <c>BekiCompositeEngine</c> crops to
    /// before it resizes, recomputed here rather than borrowed, because the point of the test is
    /// that the anchor holds for the artwork as it actually is.
    /// </summary>
    private static float VisibleAspect(byte[] posePng)
    {
        using var image = SixLabors.ImageSharp.Image.Load<
            SixLabors.ImageSharp.PixelFormats.Rgba32>(posePng);

        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    if (row[x].A == 0) continue;

                    if (x < left) left = x;
                    if (x > right) right = x;
                    if (y < top) top = y;
                    if (y > bottom) bottom = y;
                }
            }
        });

        Assert.True(right >= left && bottom >= top, "the pose has no visible alpha content.");
        return (float)(right - left + 1) / (bottom - top + 1);
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

        // And the generation call itself was told the composition in painter's language — no
        // percentage, no named region (P0-03) — and forbidden Beki.
        Assert.Contains("one continuous panoramic scene", wrap.Prompt);
        Assert.Contains("Do not generate Beki.", wrap.Prompt);
    }

    [Fact]
    public void The_press_cover_page_is_the_wrap_canvas_with_a_typeset_title()
    {
        var composer = new BekiPdfComposer(Options.Create(BekiLayoutFixture.ScreenProofLayout()));

        var pdf = composer.ComposeCoverPressWithReceipts(
            "სინათლის პატარა ქალაქი", BekiLayoutFixture.SheetPng((40, 90, 60))).Pdf;

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
