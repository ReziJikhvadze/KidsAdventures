using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Prompts;

namespace Adventrya.Story.Tests;

/// <summary>
/// The rules added by the production QA pass, tested where they can be tested without a model.
///
/// Everything else in that pass is prompt wording, and a test that asserts a prompt contains a
/// sentence only proves the sentence was not deleted. These three are different: each is a
/// decision made in code from evidence in a shipped book, and each would fail silently.
///
/// Book 1's cover carried a white-and-blue robot as the child's companion — the Beki cover path
/// had failed and the fallback prompt said nothing about companions, so the model invented one
/// from a scene written for a book that has one. Book 1 also had a spread whose scene had Beki
/// acting in it while the spread's own characters list did not mention Beki, so the master
/// reference was never attached and the model drew a Beki of its own devising.
/// </summary>
public class BekiQaPassBatchATests
{
    [Fact]
    public void The_child_only_cover_fallback_forbids_the_companion_it_cannot_draw()
    {
        // The v5 cover scene, written for a book whose companion is Beki, reaching a prompt that
        // has no Beki reference attached to it. This is the exact input that produced the robot.
        var prompt = IllustrationPrompt.ComposeChildOnlyCover(
            "A four-year-old girl with dark curly hair, a red jumper and yellow boots.",
            "Omiko stands on a hillside at sunrise beside a grape flyer, ready to set off.",
            "night, rain");

        // The scene, the lock and the plan's own avoid list still reach the model unchanged.
        Assert.Contains("grape flyer", prompt);
        Assert.Contains("red jumper", prompt);
        Assert.Contains("night, rain", prompt);

        // And the clause that the legacy cover prompt did not have.
        Assert.Contains("shows the child alone", prompt);
        Assert.Contains("no companion", prompt);
        Assert.Contains("robot", prompt);

        /*
          No companion language in the inviting sense: nothing here may ask for, describe or
          attach a second character. The Beki continuity strings are what a working Beki cover
          sends, and a fallback that quoted any of them would be describing a character whose
          reference image is not in the request — which is how an invented Beki gets drawn.
        */
        Assert.DoesNotContain(BekiIdentity.CoverContinuity, prompt);
        Assert.DoesNotContain(BekiIdentity.SpreadContinuity, prompt);
        Assert.DoesNotContain("hovering beside the child", prompt);
        Assert.DoesNotContain("lovable companion", prompt);
    }

    [Theory]
    // English, whole word, in the forms a scene brief actually writes it.
    [InlineData("Beki points toward the far ridge as the child looks up.")]
    [InlineData("beki hovers at her shoulder")]
    [InlineData("BEKI's chest glow lights the cave wall.")]
    [InlineData("The child and Beki, side by side.")]
    // Georgian, which inflects by suffix — the reason a prefix match is used there.
    [InlineData("ბეკი ხესთან მიფრინავს.")]
    [InlineData("ბეკიმ გაიღიმა და ხელი გაუწოდა.")]
    [InlineData("ბავშვი ბეკისთან ერთად დგას ბორცვზე.")]
    public void A_scene_that_names_beki_is_detected(string scene) =>
        Assert.True(BekiPlanValidator.NamesBeki(scene));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("The child crosses the meadow with her lamb.")]
    // English is matched whole precisely so a longer name starting the same way does not count.
    [InlineData("Bekim waves from the gate.")]
    [InlineData("The bakery window is full of bread.")]
    // Georgian words that merely start with the same two letters — ბეკონი is bacon.
    [InlineData("მაგიდაზე ბეკონი და პური დევს.")]
    [InlineData("ბავშვი მარტო დგას ტყეში.")]
    public void Text_that_does_not_name_beki_is_not_a_false_positive(string? scene) =>
        Assert.False(BekiPlanValidator.NamesBeki(scene));

    [Fact]
    public void A_plan_whose_scene_names_beki_while_its_characters_omit_it_is_reported()
    {
        var plan = ValidPlan();

        // Spread 3 is one of the three that do not carry Beki. Its scene now says otherwise —
        // which is the shipped fault: the prompt describes Beki, the reference is not attached.
        var broken = WithSpread(plan, 3, spread => spread with
        {
            Illustration = new IllustrationBrief
            {
                Scene = "ბეკიმ ციცაბო ბილიკზე მიუთითა, ბავშვი კი ზემოთ იყურება.",
            },
        });

        var problems = BekiPlanValidator.Validate(broken, 8);

        Assert.Contains(problems, problem => problem.Contains("Spread 3") && problem.Contains("names Beki"));

        // The same plan with the id present is clean, so the rule is about the disagreement and
        // not about the word appearing.
        var fixedUp = WithSpread(broken, 3, spread => spread with { Characters = ["child", "beki"] });
        Assert.Empty(BekiPlanValidator.Validate(fixedUp, 8));
    }

    [Fact]
    public void Only_an_avoid_that_explicitly_forbids_beki_suppresses_the_problem()
    {
        // The one spread of a book where the child is deliberately alone. Its brief names Beki in
        // order to say Beki must not be drawn — an explicit forbid, which is a consistent plan.
        var forbade = WithSpread(ValidPlan(), 3, spread => spread with
        {
            Illustration = new IllustrationBrief
            {
                Scene = "The child stands alone at the cave mouth, Beki nowhere in sight.",
                Avoid = "Do not show Beki in this picture; any companion",
            },
        });

        Assert.Empty(BekiPlanValidator.Validate(forbade, 8));

        // But an Avoid that merely mentions Beki forbids a detail, not the character: "Beki with
        // wings" wants a wingless Beki in the picture, and treating the mention as an absence was
        // how a scene naming Beki could be drawn with no reference attached — an invented Beki.
        var mentioned = WithSpread(ValidPlan(), 3, spread => spread with
        {
            Illustration = new IllustrationBrief
            {
                Scene = "Beki hovers beside the child at the cave mouth.",
                Avoid = "Beki with wings",
            },
        });

        Assert.Contains(
            BekiPlanValidator.Validate(mentioned, 8),
            problem => problem.Contains("Spread 3") && problem.Contains("names Beki"));
    }

    [Fact]
    public void The_qa_checklist_is_given_the_appearance_it_is_asked_to_check()
    {
        const string characterLock =
            "A four-year-old girl with dark curly hair, a red jumper and yellow boots.";

        var prompt = BekiImageQaPrompt.For("The child kneels beside a glowing stone.", "left", characterLock);

        // Without the lock in the prompt, the wardrobe check has only the photograph to compare
        // against — and the photograph shows the child's real clothes, not the book's.
        Assert.Contains(characterLock, prompt);
        Assert.Contains("footwear", prompt);
        Assert.Contains("two seconds", prompt);
        Assert.Contains("pseudo-legible", prompt);
    }

    /// <summary>
    /// A plan that passes every rule in <see cref="BekiPlanValidator"/>, so a test that breaks one
    /// thing sees one problem. Beki is in spreads 1, 2, 4, 6 and 8 — spread 1, the last, and three
    /// more, which is the minimum the format promises.
    /// </summary>
    private static MasterStory ValidPlan()
    {
        var bekiSpreads = new[] { 1, 2, 4, 6, 8 };

        var spreads = Enumerable.Range(1, 8).Select(number => new StorySpread
        {
            Number = number,
            Title = string.Empty,
            Caption = string.Empty,
            Text = $"ტექსტი {number}.",
            TextEn = $"Text {number}.",
            Characters = bekiSpreads.Contains(number) ? ["child", "beki"] : ["child"],
            Illustration = new IllustrationBrief { Scene = $"The child on the hillside, moment {number}." },
        }).ToList();

        return new MasterStory
        {
            Concept = new StoryConcept { Title = "ომიკო და მთის ბილიკი", Outline = ["a", "b", "c", "d", "e"] },
            Spreads = spreads,
            CharacterLock = "A four-year-old boy with short dark hair and a green coat.",
            Cover = new IllustrationBrief { Scene = "The child on a hillside at sunrise." },
            TitleEn = "Omiko and the mountain path",
            Cast = [],
        };
    }

    private static MasterStory WithSpread(MasterStory plan, int number, Func<StorySpread, StorySpread> edit) =>
        plan with
        {
            Spreads = plan.Spreads.Select(spread => spread.Number == number ? edit(spread) : spread).ToList(),
        };
}
