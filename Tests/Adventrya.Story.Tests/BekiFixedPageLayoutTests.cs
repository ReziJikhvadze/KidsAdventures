using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite.Poses;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The three fixed pages the handoff describes exactly (§5, §9): the opening endpaper, the theme
/// intro with Beki on it, and the rear endpaper.
/// </summary>
public class BekiFixedPageLayoutTests
{
    /// <summary>The sheet the proof's millimetres are measured on.</summary>
    private const double SheetWidthMm = 450d;
    private const double SheetHeightMm = 210d;

    /// <summary>Page indices in the fourteen-page book, counted the way Build composes it.</summary>
    private const int OpeningEndpaperPage = 1;
    private const int IntroPage = 2;
    private const int RearEndpaperPage = BookFormat.SpreadCount + 4;

    /// <summary>
    /// Beki lands exactly where the supplier's proof puts her: visible left edge 292 mm, visible
    /// bottom edge 19 mm, visible height 164 mm, on the 450 × 210 mm sheet.
    ///
    /// This is the golden test the origin conversion needs. <c>pipeline_config_v1.json</c> states
    /// the intro centre as 0.48095 measured up from the bottom; the composite engine reads
    /// <c>visible_center_y</c> down from the top. The three millimetre figures are the supplier's own
    /// <c>source_proof_position_mm</c> block, so they are the thing to check the arithmetic against
    /// rather than the fraction that produces them.
    /// </summary>
    [Fact]
    public void The_intro_places_Beki_at_the_proofs_millimetres()
    {
        var engine = BekiCompositeEngine.Create();
        var canvas = Canvas();

        var result = engine.CompositeIntro(
            canvas, "background.png", "intro.png", BekiPdfComposer.IntroAnchor(engine.Config));

        var layer = result.Manifest.BekiLayer;

        Assert.Equal("pose_07_curious_lean", layer.PoseId);
        Assert.Equal(5315, result.Manifest.Canvas.WidthPx);
        Assert.Equal(2480, result.Manifest.Canvas.HeightPx);

        var leftMm = layer.PlacementPx.XPx * SheetWidthMm / result.Manifest.Canvas.WidthPx;
        var heightMm = layer.RenderedSizePx.HeightPx * SheetHeightMm / result.Manifest.Canvas.HeightPx;
        var bottomMm = (result.Manifest.Canvas.HeightPx - layer.PlacementPx.YPx - layer.RenderedSizePx.HeightPx)
            * SheetHeightMm / result.Manifest.Canvas.HeightPx;

        Assert.True(Math.Abs(leftMm - 292d) <= 1d, $"Visible left is {leftMm:F1}mm; the proof is 292mm.");
        Assert.True(Math.Abs(bottomMm - 19d) <= 1d, $"Visible bottom is {bottomMm:F1}mm; the proof is 19mm.");
        Assert.True(Math.Abs(heightMm - 164d) <= 1d, $"Visible height is {heightMm:F1}mm; the proof is 164mm.");
    }

    /// <summary>
    /// The conversion is load-bearing, and this is what says so.
    ///
    /// Handing the engine the config's <c>visible_center_y</c> unconverted puts Beki about 8 mm
    /// below the proof — a difference nobody finds by looking at a page, and one that a test
    /// asserting only "she is somewhere on the right" would never catch. So the unconverted anchor
    /// is composited too, and its bottom edge must be clearly wrong.
    /// </summary>
    [Fact]
    public void The_unconverted_bottom_origin_anchor_would_miss_the_proof()
    {
        var engine = BekiCompositeEngine.Create();
        var canvas = Canvas();

        var converted = engine.CompositeIntro(
            canvas, "background.png", "intro.png", BekiPdfComposer.IntroAnchor(engine.Config));
        var raw = engine.CompositeIntro(canvas, "background.png", "intro.png", engine.Config.IntroAnchor);

        var driftMm = Math.Abs(converted.Manifest.BekiLayer.PlacementPx.YPx
                               - raw.Manifest.BekiLayer.PlacementPx.YPx)
            * SheetHeightMm / converted.Manifest.Canvas.HeightPx;

        Assert.True(driftMm > 5d,
            $"The origin conversion moves Beki only {driftMm:F1}mm, so it is not doing the job it exists for.");

        // And the config itself still holds the supplier's bottom-origin numbers, unedited — their
        // proof is measured against those, and rewriting the config would make our tree disagree
        // with theirs about what was approved.
        Assert.Equal(0.48095d, engine.Config.IntroAnchor.VisibleCenterY, 5);
        Assert.Equal(0.51905d, BekiPdfComposer.IntroAnchor(engine.Config).VisibleCenterY, 5);
        Assert.Equal(0.77885d, BekiPdfComposer.IntroAnchor(engine.Config).VisibleCenterX, 5);
        Assert.Equal(0.78095d, BekiPdfComposer.IntroAnchor(engine.Config).VisibleHeight, 5);
    }

    /// <summary>
    /// The opening spread patterns the pastedown and leaves the free endpaper blank (§5, spread 1);
    /// the rear spread patterns both leaves (§5, spread 12).
    /// </summary>
    [Fact]
    public void The_endpapers_follow_the_handoffs_opening_and_rear_pattern()
    {
        var pages = RenderBook();

        using var opening = Image.Load<Rgba32>(pages[OpeningEndpaperPage]);
        Assert.True(Variation(opening, leftHalf: true) > 4d,
            "The opening endpaper's left leaf must carry the approved pattern.");
        Assert.True(Variation(opening, leftHalf: false) < 1d,
            "The opening endpaper's right leaf is the free endpaper and must be blank.");

        using var rear = Image.Load<Rgba32>(pages[RearEndpaperPage]);
        Assert.True(Variation(rear, leftHalf: true) > 4d,
            "The rear endpaper's left leaf must carry the approved pattern.");
        Assert.True(Variation(rear, leftHalf: false) > 4d,
            "The rear endpaper's right leaf must carry the approved pattern too.");
    }

    /// <summary>
    /// The pattern is placed once across the whole sheet, not once per leaf.
    ///
    /// This is the failure the naive version of this page has: give each half the same 450 × 210
    /// artwork and each half centre-crops its middle band, so the printed spread shows the pattern's
    /// centre twice, mirrored about a fold the artwork does not have. Both versions "carry the
    /// pattern" and only one of them is the approved page — so the test compares the rendered rear
    /// endpaper against the approved file itself, resized to the same box, and asks how far apart
    /// they are.
    /// </summary>
    [Fact]
    public void The_endpaper_pattern_is_placed_once_across_the_whole_sheet()
    {
        var pages = RenderBook();

        using var rendered = Image.Load<Rgba32>(pages[RearEndpaperPage]);
        using var approved = Image.Load<Rgba32>(BekiLayoutAssets.Current.EndpaperPatternBytes());

        approved.Mutate(ctx => ctx.Resize(rendered.Width, rendered.Height, KnownResamplers.Bicubic));

        var whole = MeanDifference(rendered, approved, 0, rendered.Width, 0, rendered.Height);

        // What the sliced version would look like: the artwork's own centre band, stretched over
        // each half. Built here so the test states the alternative it is ruling out rather than
        // asserting a bare threshold.
        using var sliced = Sliced(rendered.Width, rendered.Height);
        var slicedDifference = MeanDifference(rendered, sliced, 0, rendered.Width, 0, rendered.Height);

        Assert.True(whole < 12d,
            $"The rear endpaper differs from the approved pattern by {whole:F1} per channel; it is "
            + "not the approved artwork placed across the sheet.");
        Assert.True(slicedDifference > whole * 2d,
            $"The rendered page is as close to a per-half centre crop ({slicedDifference:F1}) as it "
            + $"is to the whole approved pattern ({whole:F1}), so this test cannot tell them apart.");
    }

    /// <summary>
    /// The intro spread is the approved theme background, not a drawn ground: the page the composer
    /// produces is close to the approved file for the world the book was ordered in, and far from
    /// the one next to it in the registry.
    /// </summary>
    [Fact]
    public void The_intro_spread_is_the_approved_background_for_the_books_own_world()
    {
        var pages = RenderBook();

        using var rendered = Image.Load<Rgba32>(pages[IntroPage]);
        using var ordered = Approved(BekiLayoutFixture.CanonicalThemeId, rendered.Width, rendered.Height);
        using var other = Approved("space", rendered.Width, rendered.Height);

        // Only the upper-left corner is compared: Beki is composited onto the right half and the
        // child's lines sit in a cream box down the middle of the left leaf, so this is the one
        // region of the page where the approved background is still itself.
        var mine = MeanDifference(rendered, ordered, 0, rendered.Width / 3, 0, rendered.Height / 6);
        var theirs = MeanDifference(rendered, other, 0, rendered.Width / 3, 0, rendered.Height / 6);

        Assert.True(mine < theirs / 2d,
            $"The intro spread is no closer to its own world's approved background ({mine:F1}) than "
            + $"to another world's ({theirs:F1}).");
    }

    private static Image<Rgba32> Approved(string themeId, int width, int height)
    {
        var image = Image.Load<Rgba32>(BekiLayoutAssets.Current.IntroBackgroundBytes(themeId));
        image.Mutate(ctx => ctx.Resize(width, height, KnownResamplers.Bicubic));
        return image;
    }

    /// <summary>A 5315 × 2480 ground, the working raster the intro anchors are stated against.</summary>
    private static byte[] Canvas()
    {
        using var image = new Image<Rgba32>(5315, 2480, new Rgba32(200, 200, 210, 255));
        using var buffer = new MemoryStream();
        image.Save(buffer, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        return buffer.ToArray();
    }

    /// <summary>The approved pattern as the per-half centre-crop path would have laid it down.</summary>
    private static Image<Rgba32> Sliced(int width, int height)
    {
        using var source = Image.Load<Rgba32>(BekiLayoutAssets.Current.EndpaperPatternBytes());

        var halfWidth = width / 2;
        var cropWidth = (int)Math.Round((double)source.Height * halfWidth / height);
        using var half = source.Clone(ctx => ctx
            .Crop(new Rectangle((source.Width - cropWidth) / 2, 0, cropWidth, source.Height))
            .Resize(halfWidth, height, KnownResamplers.Bicubic));

        var sliced = new Image<Rgba32>(width, height);
        sliced.Mutate(ctx => ctx
            .DrawImage(half, new Point(0, 0), 1f)
            .DrawImage(half, new Point(halfWidth, 0), 1f));
        return sliced;
    }

    /// <summary>Mean absolute channel difference over a region of two same-sized images.</summary>
    private static double MeanDifference(
        Image<Rgba32> left, Image<Rgba32> right, int fromX, int toX, int fromY, int toY)
    {
        double total = 0;
        var samples = 0;

        for (var x = fromX; x < toX; x += 3)
        {
            for (var y = fromY; y < toY; y += 3)
            {
                var a = left[x, y];
                var b = right[x, y];
                total += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
                samples += 3;
            }
        }

        return samples == 0 ? 0 : total / samples;
    }

    /// <summary>
    /// How much a half of a page varies from its own mean, averaged over the three channels.
    ///
    /// Per channel and not against one grey mean: a leaf of plain endpaper stock is a strong warm
    /// colour, so a single mean across R, G and B would report a perfectly uniform blank leaf as
    /// wildly varied. A patterned leaf varies within each channel; a blank one does not.
    /// </summary>
    private static double Variation(Image<Rgba32> page, bool leftHalf)
    {
        var from = leftHalf ? 4 : (page.Width / 2) + 4;
        var to = leftHalf ? (page.Width / 2) - 4 : page.Width - 4;

        double redSum = 0, greenSum = 0, blueSum = 0;
        var samples = 0;
        for (var x = from; x < to; x += 3)
        {
            for (var y = 4; y < page.Height - 4; y += 3)
            {
                redSum += page[x, y].R;
                greenSum += page[x, y].G;
                blueSum += page[x, y].B;
                samples++;
            }
        }

        var (red, green, blue) = (redSum / samples, greenSum / samples, blueSum / samples);

        double deviation = 0;
        for (var x = from; x < to; x += 3)
        {
            for (var y = 4; y < page.Height - 4; y += 3)
            {
                deviation += Math.Abs(page[x, y].R - red)
                    + Math.Abs(page[x, y].G - green)
                    + Math.Abs(page[x, y].B - blue);
            }
        }

        return deviation / (samples * 3);
    }

    private static IReadOnlyList<byte[]> RenderBook()
    {
        var plan = BekiLayoutFixture.EightSpreadPlan();
        var spreads = plan.Spreads
            .Select(spread => new BekiSpreadArtwork(spread.Number, BekiLayoutFixture.SheetPng((0, 200, 120))))
            .ToList();

        return new BekiPdfComposer(Options.Create(BekiLayoutFixture.ScreenProofLayout()))
            .RenderPages(plan, BekiLayoutFixture.LeafPng((200, 60, 60)), spreads, BekiLayoutFixture.Personalization());
    }
}
