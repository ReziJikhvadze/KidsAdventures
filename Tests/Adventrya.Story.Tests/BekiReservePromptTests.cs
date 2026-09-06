using AdventurePacks.Api.Services.Story.Composite;
using AdventurePacks.Api.Services.Story.Composite.Poses;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// Owner finding 2026-09-06: Beki was composited over the hero on three of eight spreads. The fix
/// is the simplest one — the picture's order now says where Beki will stand and keeps the child
/// out of it. No detector, no review, no redraw.
/// </summary>
public class BekiReservePromptTests
{
    private static string Prompt(int page) => CompositeIllustrationPrompt.ForSpread(new CompositeSpreadPromptInput
    {
        Page = page,
        ChildAge = 5,
        Theme = CompositeThemeReferences.For("dinosaurs"),
        ChildWorldScene = "The child steps into the valley.",
        ChildOutfit = "a mustard tunic",
        IdentitySpec = CompositePipelineTests.IdentityFixture,
    });

    [Fact]
    public void A_left_text_spread_reserves_the_area_right_of_the_middle_for_beki()
    {
        // Page 1 carries its text on the LEFT (story defaults), so Beki lands right of the middle.
        var prompt = Prompt(1);

        Assert.Contains("just to the right of the picture's middle", prompt, StringComparison.Ordinal);
        Assert.Contains("about a third of the picture tall", prompt, StringComparison.Ordinal);
        Assert.Contains("child's whole head, face, hair, hands, and arms must stay clearly outside", prompt, StringComparison.Ordinal);
        Assert.Contains("outer right edge", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_right_text_spread_mirrors_the_reserve()
    {
        var prompt = Prompt(2);

        Assert.Contains("just to the left of the picture's middle", prompt, StringComparison.Ordinal);
        Assert.Contains("outer left edge", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void The_reserve_is_painters_language_not_geometry()
    {
        var prompt = Prompt(1);

        Assert.DoesNotContain("%", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("0.594", prompt, StringComparison.Ordinal);
        Assert.Contains("not a zone, panel, frame, window, or shape", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void The_sentence_follows_the_configured_anchor()
    {
        var text = CompositeIllustrationPrompt.BekiReserveBlock(new BekiCompositeAnchor(0.406, 0.458, 0.333));

        Assert.Contains("left of the picture's middle", text, StringComparison.Ordinal);
        Assert.Contains("at mid-height", text, StringComparison.Ordinal);
    }
}
