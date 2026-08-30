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
/// The handoff (§5, spread 11) says to reuse the existing credits and QR layout exactly, and the
/// layout campaign changes the pages on either side of it — the eight story spreads before, the
/// rear endpaper after. A change that reached this page would be a redesign nobody asked for, and
/// the symptom would be a printed book rather than a red test. So the shape is written down here
/// as pixels: the left leaf deliberately blank, the review QR and everything else on the right.
///
/// Deliberately not pinned: the QR's own modules (its URL is configuration), the exact wording of
/// the sign-off, and the face the credits are set in — R10 moves the interior onto Noto Sans
/// Georgian, which is a font change and not a layout one.
/// </summary>
public class BekiCreditsLayoutTests
{
    /// <summary>
    /// Cover, front endpaper, intro, eight story spreads — the credits spread is the twelfth page.
    /// </summary>
    private const int CreditsPage = 11;

    [Fact]
    public void The_credits_spread_keeps_its_blank_left_leaf_and_its_review_qr_on_the_right()
    {
        var pages = RenderBook();

        Assert.Equal(BookFormat.SpreadCount + 6, pages.Count);

        using var page = Image.Load<Rgba32>(pages[CreditsPage]);

        // A spread sheet, not a leaf: both halves are on one page, which is what makes "blank on
        // the left" a statement about this page rather than about the page before it.
        Assert.True(page.Width > page.Height, "The credits spread must be one spread-wide sheet.");

        var half = page.Width / 2;

        // Nothing is drawn on the left leaf. Sampled over the whole half rather than at a point,
        // because the failure this guards against — the credits column drifting left across the
        // fold, or a stray mark landing there — moves ink somewhere, not everywhere.
        var leftInk = 0;
        for (var x = 2; x < half - 2; x++)
        {
            for (var y = 2; y < page.Height - 2; y += 3)
            {
                if (!IsPageGround(page[x, y])) leftInk++;
            }
        }

        Assert.True(leftInk == 0, $"The credits spread's left leaf must be blank; {leftInk} sampled pixels carry ink.");

        // And the review QR is on the right leaf, where it has always been. Its tile is white by
        // construction — the quiet zone the code needs to be scannable — so white pixels are the
        // one thing on this page a program can find without decoding anything.
        var (whiteCount, minX, maxX) = WhiteRun(page);

        Assert.True(whiteCount > 500,
            $"The review QR's white tile was not found on the credits spread ({whiteCount} white pixels).");
        Assert.True(minX >= half,
            $"The review QR must sit entirely on the right leaf; its white tile starts at x={minX}, fold at {half}.");
        Assert.True(maxX < page.Width, "The review QR must stay on the sheet.");
    }

    /// <summary>
    /// A blank URL drops the code rather than printing a square that scans to nothing — the stance
    /// the composer has always taken, pinned here so the reuse required by §5 includes it.
    /// </summary>
    [Fact]
    public void A_blank_review_url_leaves_the_credits_spread_without_a_qr()
    {
        var bare = BekiLayoutFixture.ScreenProofLayout();
        bare.ReviewQrUrl = string.Empty;

        using var withCode = Image.Load<Rgba32>(RenderBook()[CreditsPage]);
        using var without = Image.Load<Rgba32>(RenderBook(bare)[CreditsPage]);

        var tile = WhiteRun(withCode).Count;
        var stripped = WhiteRun(without).Count;

        // The mark above the code carries a few light pixels of its own, so the comparison is
        // between the two books rather than against zero: the white tile is a 46mm square and
        // dwarfs anything else on the page.
        Assert.True(stripped * 4 < tile,
            $"With no review URL the credits spread must lose its QR tile; white pixels went {tile} → {stripped}.");
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
