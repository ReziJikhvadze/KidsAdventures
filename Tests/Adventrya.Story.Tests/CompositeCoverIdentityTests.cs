using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story.Composite;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The owner's rule 2 of 2026-09-01, quoted: **"characters must be consistent on cover and
/// spreads"**.
///
/// The observed defect is a shipped book whose front board showed a child who was recognisably not
/// the child on the eight story spreads. Nothing could have caught it downstream: every picture was
/// good, and every picture was judged alone — which is the whole reason the answer is not another
/// review. It is the input. The spread prompt carried a CHILD IDENTITY LOCK and, from spread two,
/// the accepted first spread as an appearance anchor; the cover prompt carried neither and was
/// therefore a ninth independent stylization of a photograph.
///
/// So every test below asks one of two questions. Does the cover receive the same instruction as a
/// spread? And does the prompt agree with the request — because a prompt that says "Image 1 is the
/// anchor" while the anchor is attached third tells the model to take the child's face from a
/// picture of a dinosaur.
/// </summary>
public class CompositeCoverIdentityTests : CompositePipelineTestBase
{
    // ---------------------------------------------------------------------------------------
    // One lock, one builder
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The cover's identity block is the spread's identity block — the same characters, from the
    /// same function, for the same child.
    ///
    /// Asserted as a whole-block comparison rather than as a list of attributes, deliberately. A
    /// test that checked "the hair colour appears" would pass a cover prompt that had quietly
    /// dropped the glasses line, which is the line whose absence lets a child be bespectacled on
    /// the cover and not on page five.
    /// </summary>
    [Fact]
    public void The_cover_carries_the_same_identity_lock_as_the_spreads()
    {
        var scenario = VisualScenarioValidator.Validate(ScenarioFixture()).Scenario!;

        var cover = CoverPrompt(scenario, anchorAttached: false);
        var spreadOne = SpreadPrompt(scenario, page: 1);

        // The block spread one is drawn with, whole, including its last line about the cover.
        var block = CompositeChildIdentity.LockBlock(IdentityFixture, childAge: 1);

        Assert.Contains(block, spreadOne, StringComparison.Ordinal);
        Assert.Contains(block, cover, StringComparison.Ordinal);

        // And the sentence that is the rule itself, present on the picture it names.
        Assert.Contains(
            "These attributes are identical on the cover and on all eight spreads.",
            cover,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The anchored cover, numbered the way the anchored spreads are: the drawing first, the
    /// photograph second, the world third — and the lock told to defer to Image 2.
    ///
    /// The numbering is not cosmetic. It is a claim about the request's own reference order, and
    /// the pipeline test below is what proves the two agree.
    /// </summary>
    [Fact]
    public void An_anchored_cover_leads_with_the_appearance_anchor_and_renumbers_the_lock()
    {
        var scenario = VisualScenarioValidator.Validate(ScenarioFixture()).Scenario!;
        var cover = CoverPrompt(scenario, anchorAttached: true);

        Assert.Contains(
            "Image 1 - " + CompositeIllustrationPrompt.AnchorInstruction, cover,
            StringComparison.Ordinal);
        Assert.Contains("Image 2 - child identity reference photograph", cover, StringComparison.Ordinal);
        Assert.Contains("Image 3 - approved", cover, StringComparison.Ordinal);

        // The lock's own deference moves with the numbering — the same sentence spread two gets.
        Assert.Contains(
            "Image 2 is the identity reference photograph and settles who this child is",
            cover,
            StringComparison.Ordinal);

        // And the outfit is taken from the drawing rather than from the description, which is what
        // "not the cloth" meant on the spreads.
        Assert.Contains("Draw the outfit exactly as rendered in Image 1.", cover, StringComparison.Ordinal);
        Assert.Contains(
            "Keep the outfit consistent with all eight story spreads.", cover, StringComparison.Ordinal);
    }

    /// <summary>
    /// A cover with no anchor is not a defect and does not pretend to have one: the photograph
    /// leads, the lock defers to Image 1, and no sentence mentions a picture the request does not
    /// carry.
    ///
    /// This is exactly spread one's condition — the page that produces the anchor is drawn without
    /// one — and it is what a press rebuild whose stored base images are gone is honestly in.
    /// </summary>
    [Fact]
    public void An_unanchored_cover_is_spread_ones_condition_and_says_nothing_about_an_anchor()
    {
        var scenario = VisualScenarioValidator.Validate(ScenarioFixture()).Scenario!;
        var cover = CoverPrompt(scenario, anchorAttached: false);

        Assert.Contains("Image 1 - child identity reference photograph", cover, StringComparison.Ordinal);
        Assert.Contains("Image 2 - approved", cover, StringComparison.Ordinal);
        Assert.DoesNotContain("Image 3", cover, StringComparison.Ordinal);
        Assert.DoesNotContain("child appearance anchor", cover, StringComparison.Ordinal);
        Assert.DoesNotContain("as rendered in Image 1", cover, StringComparison.Ordinal);

        Assert.Contains(
            "Image 1 is the identity reference photograph and settles who this child is",
            cover,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The parent's entered eye colour reaches the cover, which is where the owner watched it get
    /// lost "almost always".
    /// </summary>
    [Fact]
    public void The_entered_eye_colour_reaches_the_cover_prompt()
    {
        var scenario = VisualScenarioValidator.Validate(ScenarioFixture()).Scenario!;

        var cover = CompositeIllustrationPrompt.ForCover(new CompositeCoverPromptInput
        {
            Geometry = BekiCoverDieline.Geometry,
            ChildAge = 1,
            Theme = CompositeThemeReferences.For("dinosaurs"),
            FrontChildWorldScene = scenario.Cover!.FrontChildWorldScene!,
            BackEnvironment = scenario.Cover.BackEnvironment!,
            ChildOutfit = scenario.VisualLock!.ChildOutfit!,
            IdentitySpec = CompositeChildIdentity.WithParentEyeColor(IdentityFixture, "green"),
        });

        Assert.Contains("Eye colour: green", cover, StringComparison.Ordinal);
        Assert.Contains("The child's eyes are green on every page.", cover, StringComparison.Ordinal);
        Assert.DoesNotContain("Eye colour: brown", cover, StringComparison.Ordinal);
    }

    /// <summary>
    /// v1.1's law survives v1.2: naming a region gets the region painted, and the identity lock
    /// names none. Measured three times in this pipeline — the fold band, the spread-4 translucent
    /// rectangle, the spine bands — so a new block in this prompt is checked against it.
    /// </summary>
    [Fact]
    public void The_identity_lock_names_no_place_on_the_canvas()
    {
        var scenario = VisualScenarioValidator.Validate(ScenarioFixture()).Scenario!;

        foreach (var anchored in (bool[])[false, true])
        {
            // September 5 adds composition reservations to COVER, not to identity.
            var cover = CompositeChildIdentity.LockBlock(IdentityFixture, 1, anchored ? 2 : 1);

            Assert.DoesNotContain("%", cover, StringComparison.Ordinal);

            foreach (var word in (string[])
                     ["spine", "hinge", "fold", "gutter", "zone", "construction", "title-safe",
                      "integration", "back panel", "front panel", "safe area", "bleed",
                      "canvas width", "canvas height"])
            {
                Assert.DoesNotContain(word, cover, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    // The request and the prompt agree
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The wrap call attaches what the prompt says it attached.
    ///
    /// The stub reads the leading reference from the PROMPT's own first line and then reports the
    /// bytes the REQUEST actually carried in that slot, so this assertion fails if the two ever
    /// disagree — which is the failure worth catching, because a numbering that drifts is silent.
    /// </summary>
    [Fact]
    public async Task The_wrap_attaches_the_anchor_the_prompt_numbers_first()
    {
        var images = new StubImageService();
        var pipeline = Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images);
        var scenario = VisualScenarioValidator.Validate(ScenarioFixture()).Scenario!;

        var anchor = BasePng();

        await pipeline.DrawCoverWrapAsync(
            Context(), scenario, Photo(), "image/png", IdentityFixture, anchor,
            CancellationToken.None);

        var prompt = Assert.Single(images.Prompts);
        Assert.Contains("Image 1 - child appearance anchor", prompt, StringComparison.Ordinal);

        // The bytes in the leading slot are the accepted spread's base, and the photograph is still
        // attached behind it — never dropped, because the anchor is one stylization and the
        // photograph is the child.
        Assert.Equal(anchor, Assert.Single(images.AnchorImages));
        Assert.Equal(Photo(), Assert.Single(images.PhotoImages));
    }

    /// <summary>
    /// And with no anchor to give, the photograph leads and exactly two references are sent. Nothing
    /// is invented to fill the slot.
    /// </summary>
    [Fact]
    public async Task A_wrap_without_an_anchor_sends_the_photograph_first_and_nothing_extra()
    {
        var images = new StubImageService();
        var pipeline = Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images);
        var scenario = VisualScenarioValidator.Validate(ScenarioFixture()).Scenario!;

        await pipeline.DrawCoverWrapAsync(
            Context(), scenario, Photo(), "image/png", IdentityFixture, childAnchor: null,
            CancellationToken.None);

        Assert.DoesNotContain(
            "child appearance anchor", Assert.Single(images.Prompts), StringComparison.Ordinal);

        // Two: the photograph and the approved world reference.
        Assert.Equal(2, Assert.Single(images.ReferenceCounts));
        Assert.Null(Assert.Single(images.AnchorImages));
    }

    /// <summary>
    /// A book carries the two things its cover has to be drawn from, and carries them out of a
    /// resume as well.
    ///
    /// The resume half is the one that matters for a rebuild: a run that redrew nothing still hands
    /// back the spec its predecessor derived and the stored first spread, which is what makes a
    /// re-made cover the same child as the pages it is bound around. If it did not, the honest
    /// alternative would be redrawing eight paid images.
    /// </summary>
    [Fact]
    public async Task A_finished_book_carries_the_identity_and_the_anchor_out_for_the_cover()
    {
        var images = new StubImageService();

        var drawn = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images)
            .RunAsync(Request(), CancellationToken.None);

        Assert.Equal(drawn.Identity, drawn.Artifacts.Identity);
        Assert.Equal(drawn.Anchor, drawn.Artifacts.Anchor);
        Assert.NotNull(drawn.Artifacts.Anchor);

        // The same book, resumed with everything already in storage: no image call, and the cover
        // still has a spec and an anchor to be drawn to.
        var stored = new StubImageService();
        var everySpread = Enumerable.Range(1, BookFormat.SpreadCount)
            .ToDictionary(page => page, _ => BasePng());

        var resumed = await Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), stored)
            .RunAsync(
                Request(resume: new CompositeResumeState(ScenarioFixture(), everySpread, everySpread)
                {
                    IdentitySpecJson = CompositeChildIdentity.ToStoredJson(IdentityFixture),
                    AnchorBasePng = BasePng(),
                }),
                CancellationToken.None);

        Assert.Equal(0, stored.ImageCalls);
        Assert.Equal(IdentityFixture, resumed.Artifacts.Identity);
        Assert.Equal(BasePng(), resumed.Artifacts.Anchor);
    }

    // ==============================================================================================
    // Harness
    // ==============================================================================================

    /// <summary>One cover prompt, built from the fixture the way the wrap builds it.</summary>
    private static string CoverPrompt(VisualScenarioV2 scenario, bool anchorAttached) =>
        CompositeIllustrationPrompt.ForCover(new CompositeCoverPromptInput
        {
            Geometry = BekiCoverDieline.Geometry,
            ChildAge = 1,
            Theme = CompositeThemeReferences.For("dinosaurs"),
            FrontChildWorldScene = scenario.Cover!.FrontChildWorldScene!,
            BackEnvironment = scenario.Cover.BackEnvironment!,
            ChildOutfit = scenario.VisualLock!.ChildOutfit!,
            RecurringElements = CompositeIllustrationPrompt.RelevantRecurringElements(
                scenario.VisualLock.RecurringElements, scenario.Cover.FrontChildWorldScene),
            IdentitySpec = IdentityFixture,
            AnchorAttached = anchorAttached,
        });
}
