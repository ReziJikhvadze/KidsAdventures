using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Services.Story;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using Xunit.Abstractions;

namespace Adventrya.Story.Tests;

/// <summary>
/// Owner ruling 2026-09-01, rule 3, verbatim: <b>"text must have a STRONGER border so it is readable
/// on all backgrounds."</b> And the fourth ruling of the same day, on the same copy: <b>"we need
/// good background on the texts to be readable — transparent-like background, but not too
/// transparent."</b>
///
/// "All backgrounds" is the part that decides how this is tested. The worst case for cream type is
/// not a busy picture: it is a background the EXACT colour of the fill. When the rim was the only
/// thing under the copy, on that ground the fill disappeared completely and the rim was the only
/// thing drawing the letter at all — and that was measured here, and the rim's strength was set by
/// it. The fourth ruling is the owner's answer to what the measurement could not fix: a rim can make
/// a hollow letter legible, but it cannot make a cream letter cream on a cream picture. The panel
/// can, and so the same worst-case ground is now composed WITH the panel, and what is measured is
/// the whole arrangement: the fill is visibly the lightest thing in the block, the panel under it
/// is a shade the ground still shows through, and the rim is still there between the two.
///
/// So these tests compose that ground — a spread whose artwork is solid #FFF8EB — render it, and
/// count pixels. Not "does it look right": the rim was 0.6 pt of hairline for a whole campaign and
/// it looked fine on a screen at a hundred per cent.
///
/// The inverse is measured too, because a rim strong enough on cream could in principle have been
/// bought by drowning the letter: on near-black artwork the cream fill still has to be what carries
/// the word, and the fill's own coverage must not have moved.
///
/// Rendered at <see cref="MeasureDpi"/> rather than at the 96 the proof render uses. A rim two pixels
/// wide is one pixel of rim and one of antialiasing, and rule 3's evidence has to be about the
/// letterform rather than about the rasteriser.
/// </summary>
public class BekiTextRimReadabilityTests(ITestOutputHelper output)
{
    /// <summary>Cover, front endpaper, intro, then the story: spread 1 is page index 3.</summary>
    private const int FirstStorySpreadPage = 3;

    /// <summary>
    /// The density the rim is measured at — press-ish, and far enough above the 96 the proof render
    /// uses that a 20 pt glyph is 61 px of em rather than 27.
    /// </summary>
    private const int MeasureDpi = 220;

    /// <summary>The fill, and therefore the worst ground in the book to set the fill on.</summary>
    private static readonly (byte R, byte G, byte B) Cream = (0xFF, 0xF8, 0xEB);

    /// <summary>The other end of it: artwork as dark as the rim itself.</summary>
    private static readonly (byte R, byte G, byte B) NearBlack = (0x08, 0x06, 0x0C);

    /// <summary>
    /// **The measurement the two rulings are settled by together.** Cream copy on a cream ground, and
    /// the letters are cream, on a shade, with a rim between.
    ///
    /// Measured inside the copy's own tight box — from the first rim pixel to the last on both axes,
    /// which is the rectangle the letters actually occupy and is inside the panel by construction —
    /// and every pixel in it put in one of three classes by its luminance. The rim ink #0D071D is
    /// about 10; the shipped panel, plum at sixty per cent over cream, is about 118; the fill is 248.
    /// Under 64 is rim, 200 and over is fill, and the rest is the panel and the antialiasing at
    /// each edge. Three things have to hold:
    ///
    /// * the FILL is there — with no panel this number is meaningless on a cream ground, because
    ///   the ground itself is fill-coloured, which is exactly the fault the panel fixes; with the
    ///   panel, a fill pixel inside the box can only be a letter;
    /// * the PANEL is a shade and not a box — its pixels sit between the rim and the fill in
    ///   luminance, and there are enough of them that the block is mostly shaded picture. Opaque plum
    ///   would measure about 33 and land in the rim class; no panel would measure 248 and land in
    ///   the fill class; either way this class would be nearly empty;
    /// * the RIM is still there between the two — the fourth ruling added a panel and did not take
    ///   the border away.
    ///
    /// The contrast is stated as a number as well: the fill is brighter than the panel by well over a
    /// hundred luminance steps. On the rim-only book, on this ground, that number was zero.
    /// </summary>
    [Fact]
    public void Cream_copy_on_a_cream_ground_is_cream_on_a_shade_with_its_rim_between()
    {
        var box = Classify(StorySpread(Default));

        output.WriteLine(
            $"story copy on cream: fill {box.FillShare:P2}, rim {box.RimShare:P2}, panel "
            + $"{box.PanelShare:P2} of {box.Width}×{box.Height} px; fill luma {box.FillLuma:0}, "
            + $"panel luma {box.PanelLuma:0}");

        Assert.True(box.FillShare >= 0.04d,
            $"Only {box.FillShare:P2} of the copy's box is cream fill. On a cream ground the fill "
            + "is invisible without the panel, and the panel exists so that it is not (owner ruling "
            + "2026-09-01, fourth).");

        Assert.True(box.PanelShare >= 0.40d,
            $"Only {box.PanelShare:P2} of the copy's box reads as a shade between rim and fill. "
            + "Either there is no panel or it is opaque; the ruling asks for one the picture shows "
            + "through.");

        Assert.InRange(box.PanelLuma, 90d, 150d);

        Assert.True(box.RimShare >= 0.08d,
            $"Only {box.RimShare:P2} of the copy's box is rim. The panel did not replace the border "
            + "(owner ruling 2026-09-01, rule 3); a rim this thin is a hairline that closes on press.");

        Assert.True(box.FillLuma - box.PanelLuma >= 100d,
            $"The fill is only {box.FillLuma - box.PanelLuma:0} luminance steps brighter than the "
            + "panel it sits on; that is not a readable letter on a shade.");
    }

    /// <summary>
    /// And the rim is stronger than what it replaced, measured against it rather than asserted
    /// about it.
    ///
    /// The comparison book is the rim the code shipped before rule 3: the flat
    /// <see cref="BekiPrintLayoutOptions.TextOutlineWidth"/> with no proportion on top of it, drawn
    /// in eight directions. "Stronger" is a claim about a difference, so the test is a difference.
    /// Both are rendered with the panel, and the rim is counted as rim ink — darker than the panel
    /// could ever be — so the shade under both is not what is being compared.
    /// </summary>
    [Fact]
    public void The_rim_is_measurably_stronger_than_the_one_rule_3_replaced()
    {
        var now = Classify(StorySpread(Default)).RimShare;
        var before = Classify(StorySpread(PreRuling)).RimShare;

        output.WriteLine($"rim coverage: {before:P2} before rule 3 → {now:P2} now");

        Assert.True(now >= before * 2d,
            $"The rim went from {before:P2} to {now:P2} of the copy's box. Rule 3 asks for a "
            + "STRONGER border; less than double is a tweak, not an answer.");
    }

    /// <summary>
    /// The inverse, so that "readable on all backgrounds" is proven at both ends: on artwork as dark
    /// as the rim, the cream FILL is what carries the word — and thickening the rim has not eaten
    /// into it, because the fill is drawn last and at full size on top of the stack. The panel over
    /// near-black is near-black still, so bright is fill and only fill.
    /// </summary>
    [Fact]
    public void Cream_copy_on_a_near_black_ground_is_carried_by_its_fill()
    {
        var now = InkBox(StorySpread(Default, NearBlack), Bright);
        var before = InkBox(StorySpread(PreRuling, NearBlack), Bright);

        output.WriteLine($"story copy on near-black: fill covers {now.Share:P2} (was {before.Share:P2})");

        Assert.True(now.Share >= 0.05d,
            $"Only {now.Share:P2} of the copy's ink box is cream on a near-black ground; the fill is "
            + "what carries the word there and it has gone missing.");

        Assert.True(now.Share >= before.Share * 0.9d,
            $"The cream fill fell from {before.Share:P2} to {now.Share:P2} when the rim was "
            + "strengthened. The rim goes UNDER the fill; a rim that reduces the fill is being drawn "
            + "over it.");
    }

    /// <summary>
    /// The cover title gets the stronger rim too, and gets more of it — which is the whole reason the
    /// rim is a proportion rather than a width.
    ///
    /// The cover title is set at twice story size on the busiest artwork in the book, and under the
    /// old flat 0.6 pt it carried the THINNEST rim relative to its letters of anything printed. Same
    /// worst-case ground, same count, on the cover leaf: nothing else is drawn on it — the cover
    /// carries no panel; the fourth ruling is about the story and intro copy — so every dark pixel
    /// on the page is title rim.
    /// </summary>
    [Fact]
    public void The_cover_title_gets_the_stronger_rim_and_more_of_it()
    {
        var now = DarkPixels(CoverLeaf(Default));
        var before = DarkPixels(CoverLeaf(PreRuling));

        output.WriteLine($"cover title rim: {before} dark px before rule 3 → {now} now");

        Assert.True(before > 0, "No cover title rim was found at all; the measurement needs revisiting.");
        Assert.True(now >= before * 2L,
            $"The cover title's rim went from {before} to {now} dark pixels. Its type is twice story "
            + "size, so a rim stated as a proportion of the type has to give it twice the rim — and "
            + "the ruling names the cover title among the copy that must survive any background.");
    }

    /// <summary>
    /// The rim is a proportion of the type, floored at the width the book has always had — asserted
    /// as arithmetic, so that every block in the book is covered and not only the two this suite
    /// renders.
    /// </summary>
    [Fact]
    public void The_rim_scales_with_the_type_and_never_falls_below_the_old_width()
    {
        var layout = new BekiPrintLayoutOptions();
        var composer = new BekiPdfComposer(Options.Create(layout));

        // Twice the type, twice the rim: story copy at 18 pt against the cover title at 36.
        Assert.Equal(
            composer.RimRadiusPt(layout.StoryFontSize) * 2f,
            composer.RimRadiusPt(layout.StoryFontSize * 2f),
            3);

        // The English secondary line is smaller and still gets a rim of its own proportion.
        Assert.True(composer.RimRadiusPt(layout.StoryFontSize * 0.82f)
                    < composer.RimRadiusPt(layout.StoryFontSize));

        // And nothing in the book gets less rim than the book had before rule 3.
        foreach (var size in new[] { 1f, 6f, 8f, 14f, 18f, 20f, 36f })
        {
            Assert.True(composer.RimRadiusPt(size) >= layout.TextOutlineWidth,
                $"{size} pt type gets a {composer.RimRadiusPt(size)} pt rim, under the "
                + $"{layout.TextOutlineWidth} pt floor.");
        }

        // Zero still means no rim at all, whatever the factor says: the one setting a caller who
        // wants plain type reaches for, and the one BekiPdfComposerTests turns the outline off with.
        var plain = new BekiPrintLayoutOptions { TextOutlineWidth = 0f };
        Assert.Equal(0f, plain.TextOutlineWidth);
    }

    /// <summary>
    /// Zero opacity is no panel, in the receipt and on the page — the pre-ruling book, kept
    /// reachable so that a proof can show the owner the thing the panel was ruled in against.
    ///
    /// On the page: with the panel off, on the cream ground, the copy's box holds rim, fill that is
    /// indistinguishable from the ground, and the antialiasing between them — so the middle
    /// luminance class, which the panel fills to more than forty per cent, falls to a sliver. In the
    /// receipt: no page carries a panel block at all.
    /// </summary>
    [Fact]
    public void Panel_opacity_zero_is_the_rim_only_book()
    {
        var box = Classify(StorySpread(PanelOff));

        output.WriteLine(
            $"story copy on cream, no panel: middle class {box.PanelShare:P2} of "
            + $"{box.Width}×{box.Height} px");

        Assert.True(box.PanelShare < 0.20d,
            $"{box.PanelShare:P2} of the copy's box is neither rim nor cream with the panel off; "
            + "something is still being painted under the words.");

        var plan = BekiLayoutFixture.EightSpreadPlan();
        var spreads = plan.Spreads
            .Select(spread => new BekiSpreadArtwork(
                spread.Number, BekiLayoutFixture.SheetPng(Cream)))
            .ToList();

        var receipts = new BekiPdfComposer(Options.Create(PanelOff()))
            .ComposeWithReceipts(
                plan, BekiLayoutFixture.LeafPng(Cream), spreads, BekiLayoutFixture.Personalization())
            .Receipts;

        Assert.All(receipts.Pages, page => Assert.Null(page.Wash));
    }

    /// <summary>
    /// The chosen numbers, written down where a change to them is a change somebody has to explain.
    /// The reasoning is in the options file; this is the pin.
    /// </summary>
    [Fact]
    public void The_rim_and_panel_defaults_are_the_measured_ones()
    {
        var layout = new BekiPrintLayoutOptions();

        Assert.Equal(0.09f, layout.TextOutlineWidthFactor);
        Assert.Equal(16, layout.TextOutlineSteps);
        Assert.Equal(0.6f, layout.TextOutlineWidth);

        // The fourth ruling's panel: the page's own plum, sixty per cent, the wash's reach and
        // corner. "Transparent-like, but not too transparent."
        Assert.Equal("281B3F", layout.StoryPanelInkHex);
        Assert.Equal(0.6f, layout.StoryPanelOpacity);
        Assert.Equal(7f, layout.WashPaddingMm);
        Assert.Equal(4f, layout.WashCornerRadiusMm);
    }

    // ==============================================================================================
    // Fixtures and measurement
    // ==============================================================================================

    /// <summary>The shipped rim and the shipped panel: the measured proportion, in sixteen directions,
    /// on the plum at sixty per cent.</summary>
    private static BekiPrintLayoutOptions Default() => BekiLayoutFixture.ScreenProofLayout();

    /// <summary>
    /// The rim as it stood before rule 3: the flat width, eight directions, no proportion. Kept as a
    /// fixture rather than as a remembered number, so "stronger" stays a measurement.
    /// </summary>
    private static BekiPrintLayoutOptions PreRuling()
    {
        var layout = BekiLayoutFixture.ScreenProofLayout();
        layout.TextOutlineWidthFactor = 0f;
        layout.TextOutlineSteps = 8;
        return layout;
    }

    /// <summary>The shipped rim with the panel switched off: the book between the third ruling and
    /// the fourth.</summary>
    private static BekiPrintLayoutOptions PanelOff()
    {
        var layout = BekiLayoutFixture.ScreenProofLayout();
        layout.StoryPanelOpacity = 0f;
        return layout;
    }

    private static byte[] StorySpread(
        Func<BekiPrintLayoutOptions> layout, (byte R, byte G, byte B)? ground = null) =>
        Render(layout, ground ?? Cream)[FirstStorySpreadPage];

    private static byte[] CoverLeaf(Func<BekiPrintLayoutOptions> layout) => Render(layout, Cream)[0];

    /// <summary>
    /// The fixture book over one flat ground, rendered once per (layout, ground) and remembered.
    /// Composing fourteen pages at <see cref="MeasureDpi"/> is the expensive part of every test here
    /// and none of them changes what the others see.
    /// </summary>
    private static readonly Dictionary<string, IReadOnlyList<byte[]>> Rendered = [];

    private static IReadOnlyList<byte[]> Render(
        Func<BekiPrintLayoutOptions> layout, (byte R, byte G, byte B) ground)
    {
        var options = layout();
        var key = $"{options.TextOutlineWidthFactor}|{options.TextOutlineSteps}|"
            + $"{options.StoryPanelOpacity}|{ground}";

        lock (Rendered)
        {
            if (Rendered.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var plan = BekiLayoutFixture.EightSpreadPlan();
            var spreads = plan.Spreads
                .Select(spread => new BekiSpreadArtwork(
                    spread.Number, BekiLayoutFixture.SheetPng(ground)))
                .ToList();

            var pages = new BekiPdfComposer(Options.Create(options)).RenderPages(
                plan, BekiLayoutFixture.LeafPng(ground), spreads,
                BekiLayoutFixture.Personalization(), MeasureDpi);

            Rendered[key] = pages;
            return pages;
        }
    }

    /// <summary>Half way. A pixel past it has more rim in it than ground, or the other way round.</summary>
    private static bool Dark(Rgba32 pixel) => Luma(pixel) < 128d;

    private static bool Bright(Rgba32 pixel) => Luma(pixel) >= 128d;

    /// <summary>
    /// Rim ink and nothing else: #0D071D is about 10, and the shipped panel over the cream ground is
    /// about 118. Sixty-four is between them with room on both sides for antialiasing.
    /// </summary>
    private static bool RimInk(Rgba32 pixel) => Luma(pixel) < 64d;

    /// <summary>The fill, #FFF8EB at 248 — nothing under the copy comes near it.</summary>
    private static bool Fill(Rgba32 pixel) => Luma(pixel) >= 200d;

    private static double Luma(Rgba32 pixel) =>
        (0.2126d * pixel.R) + (0.7152d * pixel.G) + (0.0722d * pixel.B);

    private readonly record struct Measurement(int Width, int Height, long Count, double Share);

    /// <summary>
    /// The copy's tight box, classified: what share of it is fill, rim ink, and the shade between,
    /// and how bright the fill and the shade are on average.
    /// </summary>
    private readonly record struct Classification(
        int Width, int Height,
        double FillShare, double RimShare, double PanelShare,
        double FillLuma, double PanelLuma);

    /// <summary>
    /// The copy column's own band — 12 mm inside the trim on a 450 mm bled sheet, so 17 mm to 147 mm
    /// across, the same rectangle <c>BekiInteriorTypographyTests</c> looks in.
    /// </summary>
    private static (int Left, int Right) ColumnBand(Image<Rgba32> page)
    {
        var pxPerMm = page.Width / 450f;
        return ((int)(17 * pxPerMm), (int)(147 * pxPerMm));
    }

    /// <summary>
    /// Every pixel of the copy's tight box — the rectangle from the first rim pixel to the last on
    /// both axes, inside the column band — put in its class.
    ///
    /// Bounded by the rim rather than by the fill or the shade, because on the cream ground the
    /// fill is the ground's colour outside the panel and the shade is the panel's whole rectangle;
    /// the rim occurs only around letters, so its extent is the letters' extent.
    /// </summary>
    private static Classification Classify(byte[] png)
    {
        using var page = Image.Load<Rgba32>(png);
        var (left, right) = ColumnBand(page);

        int? top = null;
        var bottom = 0;
        var first = right;
        var last = left - 1;

        for (var y = 0; y < page.Height; y++)
        {
            for (var x = left; x < right; x++)
            {
                if (!RimInk(page[x, y])) continue;

                top ??= y;
                bottom = y;
                if (x < first) first = x;
                if (x > last) last = x;
            }
        }

        Assert.True(top is not null, "No rim was found in the copy column at all.");

        long fill = 0, rim = 0, panel = 0;
        double fillLuma = 0, panelLuma = 0;

        for (var y = top!.Value; y <= bottom; y++)
        {
            for (var x = first; x <= last; x++)
            {
                var pixel = page[x, y];
                var luma = Luma(pixel);

                if (Fill(pixel))
                {
                    fill++;
                    fillLuma += luma;
                }
                else if (RimInk(pixel))
                {
                    rim++;
                }
                else
                {
                    panel++;
                    panelLuma += luma;
                }
            }
        }

        var width = last - first + 1;
        var height = bottom - top.Value + 1;
        var total = (double)width * height;

        return new Classification(
            width, height,
            fill / total, rim / total, panel / total,
            fill == 0 ? 0d : fillLuma / fill,
            panel == 0 ? 0d : panelLuma / panel);
    }

    /// <summary>
    /// What share of the copy column's own ink box the wanted pixels cover.
    ///
    /// The box is the column band from its first wanted row to its last. Bounded by the ink rather
    /// than by the leaf, because a share measured against the whole page would be a statement about
    /// how much of the page is blank.
    /// </summary>
    private static Measurement InkBox(byte[] png, Func<Rgba32, bool> wanted)
    {
        using var page = Image.Load<Rgba32>(png);
        var (left, right) = ColumnBand(page);

        int? firstRow = null;
        var lastRow = 0;
        long count = 0;

        for (var y = 0; y < page.Height; y++)
        {
            for (var x = left; x < right; x++)
            {
                if (!wanted(page[x, y])) continue;

                firstRow ??= y;
                lastRow = y;
                count++;
            }
        }

        Assert.True(firstRow is not null, "Nothing was found in the copy column at all.");

        var height = lastRow - firstRow!.Value + 1;
        var total = (long)(right - left) * height;

        return new Measurement(right - left, height, count, (double)count / total);
    }

    /// <summary>Every rim pixel on a page whose only ink is type.</summary>
    private static long DarkPixels(byte[] png)
    {
        using var page = Image.Load<Rgba32>(png);

        long count = 0;
        for (var y = 0; y < page.Height; y++)
        {
            for (var x = 0; x < page.Width; x++)
            {
                if (Dark(page[x, y])) count++;
            }
        }

        return count;
    }
}
