using AdventurePacks.Api.Services.Story.Composite;
using AdventurePacks.Api.Services.Story.Composite.Poses;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// Where Beki is placed on a page nobody looked at — the deterministic chooser, against pictures
/// whose right answer is known because the test painted them.
///
/// The defect these exist for is a real one: book 54cba4b3 composited every spread at the config
/// default for its text side, and on four spreads out of eight the model had drawn the child in
/// exactly that place, so the approved character was pasted onto her face and shoulder. Nothing
/// downstream measured it, because under the owner's rule 5 no model looks at a spread at all.
///
/// So the claims below are the ones a printed book rests on. She moves when there is somewhere
/// calmer to move to; she does NOT move when there is not, because the configured anchor is the
/// composition a proof was signed off from; and wherever she lands is inside the window the
/// deterministic checks already enforce — on the canvas, out of the reserved text third, and no
/// closer to the centre fold than the configured anchor already sits.
/// </summary>
public class BekiPlacementChooserTests
{
    private const int CanvasWidth = 1536;
    private const int CanvasHeight = 717;

    /// <summary>
    /// The checkerboard's two colours and their average.
    ///
    /// The average matters: it is what the calm patch is painted, so that the patch is quiet under
    /// BOTH readings the chooser takes — no gradient in it, and its colour is the picture's own mean
    /// rather than a third colour that would read as the most distinct thing on the page.
    /// </summary>
    private static readonly Rgba32 CheckerDark = new(60, 70, 110, 255);

    private static readonly Rgba32 CheckerLight = new(220, 210, 180, 255);

    private static readonly Rgba32 Calm = new(140, 140, 145, 255);

    /// <summary>Big enough to survive the chooser's downscale to 512 columns.</summary>
    private const int CheckerSquare = 24;

    // ---------------------------------------------------------------------------------------
    // Keeping the configured anchor
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A calm picture keeps the approved anchor. There is nothing to run away from, and a chooser
    /// that shuffled Beki around a quiet page would be changing books for no reason at all.
    /// </summary>
    [Theory]
    [InlineData(BekiTextSide.Left)]
    [InlineData(BekiTextSide.Right)]
    public void A_calm_base_keeps_the_configured_anchor(BekiTextSide side)
    {
        var engine = BekiCompositeEngine.Create();
        var choice = Choose(engine, Gentle(), "pose_04_listen", side);

        Assert.False(choice.Moved);
        Assert.Equal(engine.Config.StoryDefaultFor(side), choice.Anchor);
        Assert.Equal(choice.DefaultScore, choice.ChosenScore);
    }

    /// <summary>
    /// And so does a picture that is busy EVERYWHERE — the harder half of the same claim.
    ///
    /// A relative measurement will always find a quietest pixel; the question is whether it is
    /// quiet enough to be worth a different book. On a page with no calm anywhere the honest answer
    /// is the approved composition, and the absolute margin is what enforces it.
    /// </summary>
    [Fact]
    public void Noise_everywhere_keeps_the_configured_anchor()
    {
        var engine = BekiCompositeEngine.Create();
        var choice = Choose(engine, Noise(), "pose_04_listen", BekiTextSide.Left);

        Assert.False(choice.Moved);
        Assert.Equal(engine.Config.StoryDefaultFor(BekiTextSide.Left), choice.Anchor);
    }

    // ---------------------------------------------------------------------------------------
    // Moving her
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The defect, painted: everything is busy except one band, and the configured anchor is not in
    /// it. She moves — and the anchor she moves to is one the composite engine and the deterministic
    /// checks both accept, which is the part that makes this safe rather than merely nicer.
    ///
    /// Both text sides, because the window is mirrored rather than shared: Beki stands in the half
    /// the Georgian does not occupy, so a left-text spread's calm band is on the right of the sheet
    /// and a right-text spread's is on the left. A chooser that had the mirror backwards would find
    /// the calm band, walk into the reserved third, and be refused by the engine — which is exactly
    /// what the window assertions below would catch.
    /// </summary>
    [Theory]
    [InlineData(BekiTextSide.Left)]
    [InlineData(BekiTextSide.Right)]
    public void A_busy_default_footprint_moves_her_to_the_calm_band(BekiTextSide side)
    {
        var engine = BekiCompositeEngine.Create();
        const string PoseId = "pose_04_listen";

        var basePng = CheckerboardWithCalmBands();
        var choice = Choose(engine, basePng, PoseId, side);

        Assert.True(choice.Moved, "Beki stayed on a footprint the test painted busy.");
        Assert.True(
            choice.ChosenScore < choice.DefaultScore,
            $"The chosen placement ({choice.ChosenScore}) is not calmer than the default "
            + $"({choice.DefaultScore}).");
        Assert.True(choice.CandidatesEvaluated > 1);

        // The engine accepts it, and so do the checks that read the receipt afterwards. Nothing
        // below is a restatement of the chooser's own arithmetic: this is the composite that would
        // actually ship.
        var moved = engine.CompositeStorySpread(
            basePng, "base.png", PoseId, side, "spread.png", choice.Anchor);

        Assert.Empty(CompositeDeterministicChecks.CompositeProblems(
            moved.Manifest, engine.Registry, side));

        var layer = moved.Manifest.BekiLayer;
        var left = layer.PlacementPx.XPx;
        var right = left + layer.RenderedSizePx.WidthPx;

        // On the canvas, and out of the third the Georgian is printed over.
        Assert.True(left >= 0 && right <= CanvasWidth);
        Assert.True(layer.PlacementPx.YPx >= 0);
        Assert.True(layer.PlacementPx.YPx + layer.RenderedSizePx.HeightPx <= CanvasHeight);

        var third = CanvasWidth / 3.0;
        if (side == BekiTextSide.Left) Assert.True(left >= third);
        else Assert.True(right <= CanvasWidth - third);

        // And no closer to the centre fold than the configured anchor already sits — the clearance
        // the approved proof was signed off with, which a placement improvement may not spend.
        var configured = engine.CompositeStorySpread(
            basePng, "base.png", PoseId, side, "spread.png");
        var configuredHalfWidth =
            configured.Manifest.BekiLayer.RenderedSizePx.WidthPx / 2.0 / CanvasWidth;
        var clearance = BekiPlacementChooser.FoldClearance(
            side, engine.Config.StoryDefaultFor(side), configuredHalfWidth);

        // One pixel of slack for the engine's own two roundings on the way to a corner.
        var slack = 1.0 / CanvasWidth;

        if (side == BekiTextSide.Left)
        {
            Assert.True(
                ((double)left / CanvasWidth) - 0.5 >= clearance - slack,
                $"Beki's inner edge came within {((double)left / CanvasWidth) - 0.5:F4} of the fold, "
                + $"and the configured anchor keeps {clearance:F4}.");
        }
        else
        {
            Assert.True(
                0.5 - ((double)right / CanvasWidth) >= clearance - slack,
                $"Beki's inner edge came within {0.5 - ((double)right / CanvasWidth):F4} of the "
                + $"fold, and the configured anchor keeps {clearance:F4}.");
        }
    }

    /// <summary>
    /// She never comes out bigger than the approved proof draws her. The only alternative size on
    /// offer is the smaller one, and it is only ever an alternative.
    /// </summary>
    [Theory]
    [InlineData(BekiTextSide.Left)]
    [InlineData(BekiTextSide.Right)]
    public void A_moved_Beki_is_never_drawn_larger_than_the_configured_height(BekiTextSide side)
    {
        var engine = BekiCompositeEngine.Create();
        var choice = Choose(engine, CheckerboardWithCalmBands(), "pose_04_listen", side);

        Assert.True(
            choice.Anchor.VisibleHeight <= engine.Config.StoryDefaultFor(side).VisibleHeight);
    }

    // ---------------------------------------------------------------------------------------
    // What the walk costs
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The toll for walking away from the configured anchor is charged in proportion to how much of
    /// that anchor is still worth defending.
    ///
    /// This is the whole of the amendment made on 2026-09-07 and it is worth stating as arithmetic
    /// rather than only as a picture. The configured anchor is defended because a proof was signed
    /// off at it; on a page where the model painted the child straight through it there is no
    /// composition left to defend, only the memory of where one used to be, and charging full price
    /// to leave it is charging for something that is not there. So: a calm anchor pays nearly in
    /// full, a crowded one pays the floor, and the floor is not zero — without a floor the calmest
    /// pixel in the picture wins outright and Beki ends up alone in a corner.
    /// </summary>
    [Fact]
    public void Walking_away_is_cheaper_the_worse_the_configured_anchor_measures()
    {
        var full = BekiPlacementChooser.DepartureCost;
        var floor = full * BekiPlacementChooser.MinimumDepartureShare;

        // A page the model composed as asked pays in full; the discount is not a general rebate.
        Assert.Equal(full, BekiPlacementChooser.DepartureCostFor(0), 12);

        // Between there and the knee it falls, and it falls smoothly rather than in a step — a
        // cliff would make two pages that measure a thousandth apart take visibly different books.
        Assert.True(
            BekiPlacementChooser.DepartureCostFor(0.02)
            > BekiPlacementChooser.DepartureCostFor(0.08),
            "The toll is not monotonic between the full price and the floor.");

        // The knee: where the taper reaches its floor. Every page at least this crowded pays the
        // same, which on book 54cba4b3 is every page but spread 2 — a floor, not a slope, is what
        // stops the worst-measuring page from also being the freest to wander.
        var knee = BekiPlacementChooser.CrowdedAnchorScore
                   * (1 - BekiPlacementChooser.MinimumDepartureShare);

        Assert.Equal(floor, BekiPlacementChooser.DepartureCostFor(knee), 12);
        Assert.Equal(floor, BekiPlacementChooser.DepartureCostFor(0.26), 12);

        // And nothing below the floor for a reading outside the range a real base has produced:
        // the map is not clamped, so the arithmetic must be.
        Assert.Equal(floor, BekiPlacementChooser.DepartureCostFor(4.0), 12);
    }

    // ---------------------------------------------------------------------------------------
    // Determinism
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The same bytes give the same page, twice.
    ///
    /// This is not a nicety here. The whole composite pipeline's claim is that a book can be rebuilt
    /// from its inputs and come out the same; a placement that drifted between runs would make a
    /// reprint a different book from the proof somebody approved, and the manifest would be a
    /// receipt for a page that no longer exists.
    /// </summary>
    [Fact]
    public void The_same_bytes_choose_the_same_anchor_twice()
    {
        var engine = BekiCompositeEngine.Create();
        var basePng = CheckerboardWithCalmBands();

        var first = Choose(engine, basePng, "pose_04_listen", BekiTextSide.Left);
        var second = Choose(engine, basePng.ToArray(), "pose_04_listen", BekiTextSide.Left);

        Assert.Equal(first.Anchor, second.Anchor);
        Assert.Equal(first.DefaultScore, second.DefaultScore, 12);
        Assert.Equal(first.ChosenScore, second.ChosenScore, 12);
        Assert.Equal(first.CandidatesEvaluated, second.CandidatesEvaluated);
        Assert.Equal(first.Reason, second.Reason);
    }

    /// <summary>Every choice carries the version a stored record is read back under.</summary>
    [Fact]
    public void Every_choice_names_the_algorithm_that_made_it()
    {
        var engine = BekiCompositeEngine.Create();
        var choice = Choose(engine, Gentle(), "pose_04_listen", BekiTextSide.Left);

        Assert.Equal("beki-placement-v1", choice.Version);
        Assert.Equal(engine.Config.StoryDefaultFor(BekiTextSide.Left), choice.DefaultAnchor);
        Assert.NotEmpty(choice.Reason);
    }

    // ---------------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------------

    private static BekiPlacementChoice Choose(
        BekiCompositeEngine engine, byte[] basePng, string poseId, BekiTextSide side)
        => engine.ChooseStoryPlacement(basePng, poseId, side);

    /// <summary>
    /// A soft horizontal wash — variation, but nothing anywhere that reads as an object.
    /// </summary>
    private static byte[] Gentle() => Paint((x, _) =>
    {
        var shade = (byte)(180 + (x * 40 / CanvasWidth));
        return new Rgba32(shade, (byte)(shade - 10), (byte)(shade - 20), 255);
    });

    /// <summary>
    /// Busy from edge to edge, with a fixed seed so the picture is the same picture every run.
    /// </summary>
    private static byte[] Noise()
    {
        var random = new Random(4711);
        return Paint((_, _) =>
        {
            var shade = (byte)random.Next(256);
            return new Rgba32(shade, (byte)random.Next(256), (byte)random.Next(256), 255);
        });
    }

    /// <summary>
    /// The shape of the real defect: a page that is busy where the configured anchor sits and calm
    /// somewhere else it is allowed to stand.
    ///
    /// The two calm bands are the mirror of each other — one in each outer half — so the same
    /// picture is a valid case for both text sides. Both are painted well clear of the centre so
    /// that the checkerboard runs unbroken across the fold: a calm band that straddled it would be
    /// a step at exactly 50% of the canvas, which is the defect the centre-fold gate refuses, and
    /// this fixture would then be testing that gate instead of this chooser.
    /// </summary>
    private static byte[] CheckerboardWithCalmBands() => Paint((x, y) =>
        (x is >= 140 and < 440) || (x is >= 1100 and < 1400)
            ? Calm
            : ((x / CheckerSquare) + (y / CheckerSquare)) % 2 == 0 ? CheckerDark : CheckerLight);

    private static byte[] Paint(Func<int, int, Rgba32> shade)
    {
        using var image = new Image<Rgba32>(CanvasWidth, CanvasHeight);

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = shade(x, y);
                }
            }
        });

        using var buffer = new MemoryStream();
        image.SaveAsPng(buffer);
        return buffer.ToArray();
    }
}
