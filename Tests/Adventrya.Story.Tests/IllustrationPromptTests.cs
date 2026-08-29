using AdventurePacks.Api.Services.Story;

namespace Adventrya.Story.Tests;

/// <summary>
/// The legacy Beki prompt, pinned whole.
///
/// This assembler draws the cover of every v0 book — the composite cover is refused until the
/// printer's dieline arrives — and every spread whenever the composite flag is off. It had no
/// test of its own, which is how it came to open a cover with "for a two-page spread", name the
/// fold three times, and then forbid one in its last line. The first real run drew exactly what
/// it was told about: a full-height shadow down the centre of the cover, which the centre crop
/// then keeps at the centre of the printed page.
///
/// So the two prompts are snapshotted in full rather than sampled. A sentence-contains assertion
/// only proves a sentence was not deleted; what went wrong here was a prompt arguing with itself
/// across paragraphs, and only the whole text shows that. Both snapshots include the shared
/// negative list from <c>MasterStorySchema.DefaultNegativePrompt</c>: a deliberate edit there
/// lands here too, which is the point of a characterization test.
///
/// Line endings are normalized on both sides — the repo normalizes them on checkout, and a
/// snapshot that fails only on Windows is a snapshot nobody keeps.
/// </summary>
public class IllustrationPromptTests
{
    private const string Lock =
        "A four-year-old girl with dark curly hair, a red jumper and yellow boots.";

    private const string World = "A sunlit valley of soft green hills under a wide pale sky.";

    private const string Continuity =
        "Include the companion exactly as shown in the master reference, beside the child.";

    /// <summary>Words the cover may not contain: it is not a spread and has nothing down its middle.</summary>
    private static readonly string[] CoverForbids = ["spread", "fold", "gutter", "seam"];

    /// <summary>A spread is a spread. What it may not name is the thing print does at its centre.</summary>
    private static readonly string[] SpreadForbids = ["fold", "gutter", "seam"];

    [Fact]
    public void The_cover_prompt_is_exactly_this() => AssertPrompt(ExpectedCover, Cover());

    [Fact]
    public void The_spread_prompt_is_exactly_this() => AssertPrompt(ExpectedSpread, Spread());

    /// <summary>
    /// Not one of these words, in any casing. The cover is one upright leaf: told it is half of
    /// something, it draws the half it is missing.
    /// </summary>
    [Fact]
    public void The_cover_never_names_a_spread_or_anything_running_down_its_middle()
    {
        var prompt = Cover();

        foreach (var word in CoverForbids)
        {
            Assert.DoesNotContain(word, prompt, StringComparison.OrdinalIgnoreCase);
        }

        // And it says the positive thing instead, in the place the old wording contradicted
        // itself: hero central, nothing reserved at the centre.
        // Substrings that do not cross a line break: the composition block is hard-wrapped in
        // source and reaches the model wrapped the same way.
        Assert.Contains("one upright book cover", prompt);
        Assert.Contains("outer left and right edges may be", prompt);
        Assert.Contains("trimmed away in print", prompt);
        Assert.DoesNotContain("low-information", prompt);
    }

    /// <summary>
    /// The spread keeps the rule — a quiet strip at centre, background flowing through it — and
    /// keeps it without naming what print does there. The rule is the whole reason faces stay out
    /// of the middle, so losing it would be a worse defect than the one being fixed.
    /// </summary>
    [Fact]
    public void The_spread_keeps_its_quiet_centre_band_without_naming_what_is_at_the_centre()
    {
        var prompt = Spread();

        foreach (var word in SpreadForbids)
        {
            Assert.DoesNotContain(word, prompt, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains(
            "A narrow vertical strip at the exact centre of the frame is a low-information zone",
            prompt);
        Assert.Contains("background may continue through it unchanged", prompt);
        Assert.Contains("no face, no eyes, no hands, no key object", prompt);
        Assert.Contains("one continuous children's book illustration for a two-page spread", prompt);
    }

    /// <summary>
    /// The cover carries the photograph directive, and carries it above the scene.
    ///
    /// A cover is where a parent decides whether the book is about their child, and this class
    /// puts identity first precisely because an image model weights the opening most heavily —
    /// yet the cover was the one prompt here that never carried the directive at all. Spreads keep
    /// their own, differently balanced identity paragraph and do not take this one.
    /// </summary>
    [Fact]
    public void The_cover_carries_the_photograph_directive_before_the_scene()
    {
        var prompt = Cover();

        Assert.Contains(IllustrationPrompt.PhotographDirective, prompt);
        Assert.True(
            prompt.IndexOf(IllustrationPrompt.PhotographDirective, StringComparison.Ordinal)
            < prompt.IndexOf("Scene:", StringComparison.Ordinal),
            "The photograph directive must come before the scene on the cover.");

        Assert.DoesNotContain(IllustrationPrompt.PhotographDirective, Spread());
    }

    /// <summary>
    /// "either" is what the cover path passes, but the branch turns on what a side is not, so
    /// every not-a-side lands on the same composition rather than on "on the either".
    /// </summary>
    [Theory]
    [InlineData("either")]
    [InlineData("EITHER")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("centre")]
    public void Anything_that_is_not_a_named_side_is_composed_as_the_cover(string textSide) =>
        AssertPrompt(Cover(), Cover(textSide));

    /// <summary>Both sides normalized, so a snapshot cannot fail on line endings alone.</summary>
    private static void AssertPrompt(string expected, string actual) =>
        Assert.Equal(expected.ReplaceLineEndings("\n"), actual.ReplaceLineEndings("\n"));

    private static string Cover(string textSide = "either") => IllustrationPrompt.ComposeBeki(
        Lock,
        "Omiko stands on a hillside at sunrise beside a grape flyer, ready to set off.",
        Continuity,
        textSide,
        "A warm hero portrait of the child, inviting the reader in.",
        "night, rain",
        worldLock: World);

    private static string Spread() => IllustrationPrompt.ComposeBeki(
        Lock,
        "Omiko wades into the shallows and lifts a shell above the water.",
        Continuity,
        "left",
        "A wide establishing shot.",
        "night, rain",
        worldLock: World);

    private const string ExpectedCover =
        """
            Create one single upright children's book cover illustration.

            Use the attached reference photograph as the primary and authoritative identity reference. Preserve the person's recognizable facial identity, facial geometry, eye shape and spacing, eyebrows, nose, lips, smile, cheeks, jawline, skin tone, hairstyle, hair color, age appearance, body build and natural body proportions as accurately as possible while translating them into a polished cinematic 3D animated character. Apply moderate stylization only; identity accuracy has priority over exaggerated cartoon features. Do not change ethnicity, skin tone or body type, and add no makeup or accessories that are not in the photograph.

            Scene: Omiko stands on a hillside at sunrise beside a grape flyer, ready to set off.

            Render the child as a polished stylized 3D animated children's-film character, not
            photorealistic, while preserving the recognizable facial features, hair, approximate age
            and eye colour from the provided child reference.

            A four-year-old girl with dark curly hair, a red jumper and yellow boots.

            The world, identical in every illustration of this book: A sunlit valley of soft green hills under a wide pale sky.

            The child's hairstyle, clothing, footwear and accessories are exactly as described above in this and every other scene of the book, down to colour and pattern. Do not restyle, recolour or redress the child between scenes; the only exception is a change this scene explicitly asks for.

            Include the companion exactly as shown in the master reference, beside the child.

            A warm hero portrait of the child, inviting the reader in. Obey that camera distance exactly; do not default to a medium close-up.

            The one focus the scene names must be the visually dominant element — large, clearly lit and readable at a glance. Everything else supports it and never competes with it.

            Composition, which is a hard requirement of this cover and not a preference: this
            is one upright book cover, a single whole picture with the child as its subject.
            Draw the child and the story's action large, clearly lit and unmistakable — the
            hero is the one thing this cover is about and must read at a glance, even at
            thumbnail size.

            Keep the lower fifth of the image calm and naturally light: quiet background only,
            and no face, no hands and no part of the main action inside it. The book's title
            is set across that area afterwards, so it must stay clear.

            Keep the hero, that calm lower area and everything else the cover cannot lose
            inside the central portion of the frame: the outer left and right edges may be
            trimmed away in print.

            High-quality cinematic 3D animated family-film aesthetic, expressive characters, rounded and appealing forms, detailed environments, warm emotional storytelling, soft global illumination, polished textures, vibrant but harmonious colors, cinematic composition, child-friendly atmosphere.

            No text, letters, logos, captions, frames or QR codes anywhere in the image.

            Paint the cover as one single unbroken picture: the artwork is continuous across the whole frame, with no vertical dividing line, no crease and no darker vertical band anywhere in it.

            Do not include: night, rain, changed identity, generic face, excessive facial stylization, inaccurate facial proportions, different eye shape, different nose, different hairstyle, different skin tone, incorrect age, altered body type, unrealistic body proportions, changed clothing, extra accessories, asymmetrical eyes, distorted face, malformed hands, extra fingers, missing fingers, duplicate person, blurry face, low detail, frightening expression, text, captions, watermark, logo
            """;

    private const string ExpectedSpread =
        """
            Create one continuous children's book illustration for a two-page spread.

            Scene: Omiko wades into the shallows and lifts a shell above the water.

            Render the child as a polished stylized 3D animated children's-film character, not
            photorealistic, while preserving the recognizable facial features, hair, approximate age
            and eye colour from the provided child reference.

            A four-year-old girl with dark curly hair, a red jumper and yellow boots.

            The world, identical in every illustration of this book: A sunlit valley of soft green hills under a wide pale sky.

            The child's hairstyle, clothing, footwear and accessories are exactly as described above in this and every other scene of the book, down to colour and pattern. Do not restyle, recolour or redress the child between scenes; the only exception is a change this scene explicitly asks for.

            Include the companion exactly as shown in the master reference, beside the child.

            A wide establishing shot. Obey that camera distance exactly; do not default to a medium close-up.

            The one focus the scene names must be the visually dominant element — large, clearly lit and readable at a glance. Everything else supports it and never competes with it.

            Composition, which is a hard requirement of this page and not a preference: the
            left third of the image is reserved for story text that will be printed over it.
            Fill that third with quiet, naturally light background only — bright open sky, mist,
            sunlit water, pale distant landscape, or a softly lit wall. Keep it light and airy:
            not shadow, not darkness, and not a dark panel. No character, no face, no hands and no
            part of the main action may enter it.

            Place the child and the story's action in the right two thirds instead, and keep
            every face clear of the vertical centre line of the image.
            A narrow vertical strip at the exact centre of the frame is a low-information zone: background may continue through it unchanged, but no face, no eyes, no hands, no key object and no part of the main action may sit inside it.

            Keep every face and the story's key action inside the central horizontal band of the
            image as well: the printed spread is wider than it is tall, so the top and bottom
            sixths of this picture may be trimmed away. Sky, canopy and ground belong there —
            nothing the story cannot lose.

            High-quality cinematic 3D animated family-film aesthetic, expressive characters, rounded and appealing forms, detailed environments, warm emotional storytelling, soft global illumination, polished textures, vibrant but harmonious colors, cinematic composition, child-friendly atmosphere.

            No text, letters, logos, captions, frames or QR codes anywhere in the image.

            Paint this as one single unbroken picture: the artwork runs continuously across the whole frame, with no vertical dividing line, no crease, no darker vertical band, no page edge and nothing that divides the image into two halves.

            Do not include: night, rain, changed identity, generic face, excessive facial stylization, inaccurate facial proportions, different eye shape, different nose, different hairstyle, different skin tone, incorrect age, altered body type, unrealistic body proportions, changed clothing, extra accessories, asymmetrical eyes, distorted face, malformed hands, extra fingers, missing fingers, duplicate person, blurry face, low detail, frightening expression, text, captions, watermark, logo
            """;
}
