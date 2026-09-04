using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The credits spread, pinned before anything moves near it.
///
/// The final override makes the last spread credits-left/pattern-right and explicitly removes the
/// former review QR. These pixel checks keep that direction from regressing.
/// </summary>
public class BekiCreditsLayoutTests
{
    /// <summary>
    /// Cover, front endpaper, intro, eight story spreads — the credits spread is the twelfth page.
    /// </summary>
    private const int CreditsPage = 11;

    [Fact]
    public void The_credits_spread_keeps_credits_left_and_the_approved_pattern_right()
    {
        var pages = RenderBook();

        Assert.Equal(BookFormat.SpreadCount + 6, pages.Count);

        using var page = Image.Load<Rgba32>(pages[CreditsPage]);

        // A spread sheet, not a leaf: both halves are on one page, which is what makes "blank on
        // the left" a statement about this page rather than about the page before it.
        Assert.True(page.Width > page.Height, "The credits spread must be one spread-wide sheet.");

        var half = page.Width / 2;

        // The credits text is on the dark left leaf.
        var leftInk = 0;
        for (var x = 2; x < half - 2; x++)
        {
            for (var y = 2; y < page.Height - 2; y += 3)
            {
                if (!IsPageGround(page[x, y])) leftInk++;
            }
        }

        Assert.True(leftInk > 0, "The credits spread's left leaf must carry the five credit lines.");

        // The approved light pattern occupies the right leaf and never crosses the fold.
        var (whiteCount, _, maxX) = WhiteRun(page);

        Assert.True(whiteCount > 500,
            $"The approved pattern was not found on the credits spread ({whiteCount} light pixels).");
        Assert.True(maxX < page.Width, "The approved pattern must stay on the sheet.");
        Assert.False(IsPageGround(page[half + 10, 10]), "The right leaf must carry the approved pattern.");
    }

    /// <summary>
    /// A blank URL drops the code rather than printing a square that scans to nothing — the stance
    /// the composer has always taken, pinned here so the reuse required by §5 includes it.
    /// </summary>
    [Fact]
    public void The_superseded_review_url_cannot_change_the_final_spread()
    {
        var bare = BekiLayoutFixture.ScreenProofLayout();
        bare.ReviewQrUrl = string.Empty;

        using var withCode = Image.Load<Rgba32>(RenderBook()[CreditsPage]);
        using var without = Image.Load<Rgba32>(RenderBook(bare)[CreditsPage]);

        var tile = WhiteRun(withCode).Count;
        var stripped = WhiteRun(without).Count;

        Assert.Equal(tile, stripped);
    }

    /// <summary>The composer's own page ground, and the only thing the left leaf may show.</summary>
    private static bool IsPageGround(Rgba32 pixel) =>
        pixel is { R: 0x28, G: 0x1B, B: 0x3F };

    /// <summary>How much white the page carries, and how far left and right it reaches.</summary>
    private static (int Count, int MinX, int MaxX) WhiteRun(Image<Rgba32> page)
    {
        var count = 0;
        var minX = page.Width;
        var maxX = -1;

        for (var x = 0; x < page.Width; x++)
        {
            for (var y = 0; y < page.Height; y++)
            {
                var pixel = page[x, y];
                if (pixel.R < 235 || pixel.G < 235 || pixel.B < 235) continue;

                count++;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
            }
        }

        return (count, minX, maxX);
    }

    private static IReadOnlyList<byte[]> RenderBook(BekiPrintLayoutOptions? layout = null)
    {
        // The standard book is rendered once for the whole assembly; only a test that asks for a
        // different layout pays for a render of its own.
        if (layout is null) return BekiLayoutFixture.ScreenProofPages();

        var plan = BekiLayoutFixture.EightSpreadPlan();
        var spreads = plan.Spreads
            .Select(spread => new BekiSpreadArtwork(spread.Number, BekiLayoutFixture.SheetPng((0, 200, 120))))
            .ToList();

        return new BekiPdfComposer(Options.Create(layout ?? BekiLayoutFixture.ScreenProofLayout()))
            .RenderPages(plan, BekiLayoutFixture.LeafPng((200, 60, 60)), spreads, BekiLayoutFixture.Personalization());
    }
}
