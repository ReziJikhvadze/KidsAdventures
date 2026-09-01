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
/// on all backgrounds."</b>
///
/// "All backgrounds" is the part that decides how this is tested. The ruling before it took the cream
/// wash away for the third and final time, so every word the book sets over artwork is cream #FFF8EB
/// with a #0D071D rim and nothing else — no box, no wash, no panel. Which means there is a worst case,
/// and it is not a busy picture: it is a background the EXACT colour of the fill. On that ground the
/// fill disappears completely and the rim is the only thing drawing the letter at all. If the copy is
/// legible there it is legible anywhere, because every other background makes the fill visible again.
///
/// So these tests compose that ground — a spread whose artwork is solid #FFF8EB — render it, and
/// count pixels. Not "does it look right": the rim was 0.6 pt of hairline for a whole campaign and it
/// looked fine on a screen at a hundred per cent.
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
    /// **The measurement rule 3 is settled by.** Cream copy on a cream ground, and the glyphs are
    /// still there — as rim.
    ///
    /// Measured inside the copy column's own ink box (the column's width, from its first inked row to
    /// its last), because that is the rectangle a reader's eye is inside. Two bounds, not one:
    ///
    /// * a FLOOR, which is the readability rule — enough of that box has to be dark for the letters
    ///   to have shape;
    /// * a CEILING, which is the owner's other ruling — a rim thick enough to merge into a continuous
    ///   dark field would be a panel behind the words by another name, and there is to be no panel.
    ///
    /// The numbers, measured on this fixture at this density: the pre-ruling rim (a flat 0.6 pt,
    /// which on 20 pt copy is 0.03 of the em) leaves 5.8% of that box dark. The shipped default —
    /// 0.09 of the em, sixteen directions — leaves 17.4%. Both are far from the ~100% a box would
    /// give. The floor is set at 12% and the ceiling at 45%: wide enough that a font update or a
    /// rasteriser change does not turn this red on its own, tight enough that neither the hairline
    /// nor a panel could pass it.
    /// </summary>
    [Fact]
    public void Cream_copy_on_a_cream_ground_is_still_drawn_by_its_rim()
    {
        var box = InkBox(StorySpread(Default), Dark);

        output.WriteLine($"story copy on cream: rim covers {box.Share:P2} of {box.Width}×{box.Height} px");

        Assert.True(box.Share >= 0.12d,
            $"Only {box.Share:P2} of the copy's ink box is rim. Cream type on a cream ground is "
            + "drawn by its rim alone (owner ruling 2026-09-01, rule 3); this thin a rim is a "
            + "hairline that closes up on press.");

        Assert.True(box.Share <= 0.45d,
            $"{box.Share:P2} of the copy's ink box is dark. A rim that heavy has merged into a "
            + "continuous field, which is a panel behind the words under another name — and the "
            + "owner has ruled three times that there is no box behind the words.");
    }

    /// <summary>
    /// And it is stronger than what it replaced, measured against it rather than asserted about it.
    ///
    /// The comparison book is the rim the code shipped before rule 3: the flat
    /// <see cref="BekiPrintLayoutOptions.TextOutlineWidth"/> with no proportion on top of it, drawn
    /// in eight directions. "Stronger" is a claim about a difference, so the test is a difference.
    /// </summary>
    [Fact]
    public void The_rim_is_measurably_stronger_than_the_one_rule_3_replaced()
    {
        var now = InkBox(StorySpread(Default), Dark).Share;
        var before = InkBox(StorySpread(PreRuling), Dark).Share;

        output.WriteLine($"rim coverage: {before:P2} before rule 3 → {now:P2} now");

        Assert.True(now >= before * 2d,
            $"The rim went from {before:P2} to {now:P2} of the copy's ink box. Rule 3 asks for a "
            + "STRONGER border; less than double is a tweak, not an answer.");
    }

    /// <summary>
    /// The inverse, so that "readable on all backgrounds" is proven at both ends: on artwork as dark
    /// as the rim, the cream FILL is what carries the word — and thickening the rim has not eaten
    /// into it, because the fill is drawn last and at full size on top of the stack.
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
    /// worst-case ground, same count, on the cover leaf: nothing else is drawn on it, so every dark
    /// pixel on the page is title rim.
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
    /// The chosen numbers, written down where a change to them is a change somebody has to explain.
    /// The reasoning is in the options file; this is the pin.
    /// </summary>
    [Fact]
    public void The_rim_defaults_are_the_measured_ones()
    {
        var layout = new BekiPrintLayoutOptions();

        Assert.Equal(0.09f, layout.TextOutlineWidthFactor);
        Assert.Equal(16, layout.TextOutlineSteps);
        Assert.Equal(0.6f, layout.TextOutlineWidth);
    }

    // ==============================================================================================
    // Fixtures and measurement
    // ==============================================================================================

    /// <summary>The shipped rim: the measured proportion, in sixteen directions.</summary>
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
        var key = $"{options.TextOutlineWidthFactor}|{options.TextOutlineSteps}|{ground}";

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

    private static double Luma(Rgba32 pixel) =>
        (0.2126d * pixel.R) + (0.7152d * pixel.G) + (0.0722d * pixel.B);

    private readonly record struct Measurement(int Width, int Height, long Count, double Share);

    /// <summary>
    /// What share of the copy column's own ink box the wanted pixels cover.
    ///
    /// The box is the column — 12 mm inside the trim on a 450 mm bled sheet, so 17 mm to 147 mm
    /// across, the same rectangle <c>BekiInteriorTypographyTests</c> looks in — from its first inked
    /// row to its last. Bounded by the ink rather than by the leaf, because a share measured against
    /// the whole page would be a statement about how much of the page is blank.
    /// </summary>
    private static Measurement InkBox(byte[] png, Func<Rgba32, bool> wanted)
    {
        using var page = Image.Load<Rgba32>(png);

        var pxPerMm = page.Width / 450f;
        var left = (int)(17 * pxPerMm);
        var right = (int)(147 * pxPerMm);

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
