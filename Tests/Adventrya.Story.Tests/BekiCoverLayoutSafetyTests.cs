using System.Security.Cryptography;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using Microsoft.Extensions.Options;

namespace Adventrya.Story.Tests;

public class BekiCoverLayoutSafetyTests
{
    private static BekiCoverProtectedArea Head(double x = 355, double y = 90) =>
        new("head", "Whole head including hair and facial features", x, y, 60, 70);

    [Fact]
    public void Head_below_title_and_outside_logo_is_safe() =>
        Assert.Empty(BekiCoverLayoutSafety.Conflicts([Head()]));

    [Theory]
    [InlineData(350, 50, "TITLE")]
    [InlineData(437, 42, "LOGO")]
    public void A_known_head_collision_is_refused(double x, double y, string region)
    {
        var failure = Assert.Throws<BekiLayoutException>(() => BekiCoverLayoutSafety.EnsureClear([Head(x, y)]));
        Assert.Contains(region, failure.Message);
        Assert.Contains("COVER_LAYOUT_SAFETY", failure.Message);
    }

    [Fact]
    public void Important_accents_cannot_be_hidden_under_the_title() =>
        Assert.Throws<BekiLayoutException>(() => BekiCoverLayoutSafety.EnsureClear(
            [Head(), new("important_detail", "Story landmark", 300, 40, 30, 30)]));

    [Fact]
    public void Review_is_bound_to_the_exact_base_pixels()
    {
        byte[] pixels = [1, 2, 3];
        var review = new BekiCoverLayoutReview(Convert.ToHexString(SHA256.HashData(pixels)),
            "operator", DateTimeOffset.UtcNow, [Head()]);
        BekiCoverLayoutSafety.VerifySource(review, pixels);
        Assert.Throws<BekiLayoutException>(() => BekiCoverLayoutSafety.VerifySource(review, [1, 2, 4]));
    }

    [Fact]
    public void Empty_or_invalid_bounds_are_not_an_approval()
    {
        Assert.Throws<BekiLayoutException>(() => BekiCoverLayoutSafety.ValidateAreas([]));
        Assert.Throws<BekiLayoutException>(() => BekiCoverLayoutSafety.ValidateAreas([Head(double.NaN)]));
        Assert.Throws<BekiLayoutException>(() => BekiCoverLayoutSafety.ValidateAreas([Head(500)]));
    }

    [Fact]
    public void Canonical_composer_refuses_known_collisions_before_export()
    {
        var composer = new BekiPdfComposer(Options.Create(BekiLayoutFixture.ScreenProofLayout()));
        var failure = Assert.Throws<BekiLayoutException>(() => composer.ComposeCanonicalWithReceipts(
            BekiLayoutFixture.EightSpreadPlan(), [1], [],
            BekiLayoutFixture.Personalization() with { CoverProtectedAreas = [Head(350, 50)] }));
        Assert.Contains("COVER_LAYOUT_SAFETY", failure.Message);
    }
}
