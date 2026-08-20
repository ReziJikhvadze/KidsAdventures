using AdventurePacks.Api.Services.Story;

namespace Adventrya.Story.Tests;

/// <summary>
/// What a refused illustration is told to fix.
///
/// The reviewer files its clean findings in the same array as its faults — "no unwanted text is
/// visible", "no Beki is present, which is correct" — and the whole array used to be pasted onto
/// the prompt for the redraw. Five retries across two measured runs fixed nothing and one added a
/// duplicate character, which is what a model does when it is asked to correct seven things that
/// were already right.
///
/// These are the real verdicts from those runs.
/// </summary>
public class BekiCorrectionTests
{
    [Fact]
    public void Findings_that_report_nothing_wrong_are_not_sent_back_as_faults()
    {
        // Verbatim from the whole-book run, spread 8. Two real faults among six observations.
        const string verdict = """
            {"status":"FAIL","issues":[
              "Characters Pafu and Bluewing are present but two identical Bluewing creatures are shown instead of Pafu and Bluewing.",
              "The story text was requested on the left side but there is very little calm space for text on the left side where the child is located.",
              "There is no Beki character present, which is correct as not requested.",
              "No unwanted text, lettering, logo, frame, or QR code is visible.",
              "The scene showing the glowing blue stone with the thin blue glow pointing toward a distant valley is clear and correct."
            ]}
            """;

        var corrections = BekiBookGenerator.Corrections(verdict);

        Assert.Contains("two identical Bluewing", corrections);
        Assert.Contains("left side", corrections);

        // The three that say nothing is wrong must not become instructions.
        Assert.DoesNotContain("which is correct as not requested", corrections);
        Assert.DoesNotContain("No unwanted text", corrections);
        Assert.DoesNotContain("clear and correct", corrections);
    }

    [Fact]
    public void An_absence_that_is_itself_the_fault_survives()
    {
        // From spread 5: the missing claw marks are a real omission, not a clean finding.
        const string verdict = """
            {"status":"FAIL","issues":[
              "No visible small claw marks leading upward on the path as requested in the scene description.",
              "No unwanted logos, text, frame, or QR code observed."
            ]}
            """;

        var corrections = BekiBookGenerator.Corrections(verdict);

        Assert.Contains("claw marks", corrections);
        Assert.DoesNotContain("No unwanted logos", corrections);
    }

    [Fact]
    public void A_verdict_that_will_not_parse_is_still_passed_on()
    {
        // A noisy correction beats redrawing with none, so unparsable text goes through as-is.
        var corrections = BekiBookGenerator.Corrections("the composition is wrong");

        Assert.Contains("the composition is wrong", corrections);
    }

    [Fact]
    public void A_refusal_with_no_usable_faults_still_asks_for_a_different_picture()
    {
        const string verdict = """{"status":"FAIL","issues":["No unwanted text is visible."]}""";

        var corrections = BekiBookGenerator.Corrections(verdict);

        Assert.False(string.IsNullOrWhiteSpace(corrections));
        Assert.DoesNotContain("No unwanted text", corrections);
    }
}
