namespace AdventurePacks.Api.Services.Story;

/// <summary>
/// Assembles the string an image model actually reads, out of the parts that vary and the parts
/// that never do.
///
/// The model used to write the whole thing, nine times per book. Measured on a real run: of
/// 6,334 completion tokens, roughly two thirds were the same paragraphs typed out again — the
/// photograph instruction, the house style, the format line, the character description and a
/// near-identical exclusion list. About seven hundred characters per prompt were the scene.
///
/// Everything invariant is written here instead. That buys back most of the generation time,
/// and it makes the invariant parts genuinely invariant: the model can no longer paraphrase the
/// photograph instruction on page six, which is precisely the drift the character lock exists to
/// prevent.
///
/// Order matters. Identity first, because an image model weights the opening of a prompt most
/// heavily and the child's face is the thing a parent checks first.
/// </summary>
public static class IllustrationPrompt
{
    /// <summary>
    /// Tells the image model the attached photograph is the authority on the face. Written once
    /// here rather than nine times by the model.
    /// </summary>
    public const string PhotographDirective =
        "Use the attached reference photograph as the primary and authoritative identity "
        + "reference. Preserve the person's recognizable facial identity, facial geometry, eye "
        + "shape and spacing, eyebrows, nose, lips, smile, cheeks, jawline, skin tone, hairstyle, "
        + "hair color, age appearance, body build and natural body proportions as accurately as "
        + "possible while translating them into a polished cinematic 3D animated character. Apply "
        + "moderate stylization only; identity accuracy has priority over exaggerated cartoon "
        + "features. Do not change ethnicity, skin tone or body type, and add no makeup or "
        + "accessories that are not in the photograph.";

    /// <summary>The house look. Every picture in every book shares it, so nothing decides it per page.</summary>
    public const string StyleDirective =
        "High-quality cinematic 3D animated family-film aesthetic, expressive characters, rounded "
        + "and appealing forms, detailed environments, warm emotional storytelling, soft global "
        + "illumination, polished textures, vibrant but harmonious colors, cinematic composition, "
        + "child-friendly atmosphere.";

    /// <summary>
    /// A page of its own means the picture may fill the frame. This is the instruction that keeps
    /// an image model from leaving a polite empty band for a caption that will never be printed.
    /// </summary>
    public const string FormatDirective =
        "Portrait format, full-frame illustration. No text, no lettering, no caption and no "
        + "reserved space for text anywhere in the image.";

    /// <summary>
    /// The Beki spread prompt. A separate assembler, not a parameter on <see cref="Compose"/>.
    ///
    /// The two formats disagree about the one thing <see cref="FormatDirective"/> states most
    /// firmly. An A5 picture owns its page and must fill it — "no reserved space for text
    /// anywhere" — while a Beki spread carries its story text over the artwork and must keep a
    /// calm band clear on a named side. Threading that through the existing function would mean a
    /// branch inside the string every production book is drawn from.
    ///
    /// The identity line is also different in kind: A5 asks for a photographic likeness moved
    /// into 3D, Beki asks for a polished animated character that stays recognisable. Same intent,
    /// different balance, and the balance is the whole argument.
    /// </summary>
    /// <param name="characterLock">The child's unchanging appearance, quoted verbatim.</param>
    /// <param name="scene">This spread's scene, and nothing else.</param>
    /// <param name="continuity">
    /// Either the visual description of a character appearing for the first time, or the
    /// instruction to match an attached anchor. Empty when the child is alone in the spread.
    /// </param>
    /// <param name="textSide">left | right — which half stays calm enough to set text on.</param>
    /// <param name="shotInstruction">One short sentence. The rhythm is code's decision, not the story's.</param>
    /// <param name="extraExclusions">What would go wrong in this picture specifically.</param>
    public static string ComposeBeki(
        string characterLock,
        string scene,
        string continuity,
        string textSide,
        string shotInstruction,
        string? extraExclusions)
    {
        var exclusions = string.IsNullOrWhiteSpace(extraExclusions)
            ? Prompts.MasterStorySchema.DefaultNegativePrompt
            : $"{extraExclusions.Trim()}, {Prompts.MasterStorySchema.DefaultNegativePrompt}";

        var continuityBlock = string.IsNullOrWhiteSpace(continuity)
            ? string.Empty
            : continuity.Trim() + "\n\n";

        return $"""
            Create one continuous children's book illustration for a two-page spread.

            Scene: {scene.Trim()}

            Render the child as a polished stylized 3D animated children's-film character, not
            photorealistic, while preserving the recognizable facial features, hair, approximate age
            and eye colour from the provided child reference.

            {characterLock.Trim()}

            {WardrobeRule}

            {continuityBlock}{shotInstruction.Trim()} {ShotDistanceRule}

            {FocusRule}

            {ComposeTextSide(textSide)}

            {StyleDirective}

            No text, letters, logos, captions, frames or QR codes anywhere in the image.

            Do not include: {exclusions}
            """;
    }

    /// <summary>
    /// The character lock describes the child once; this says the description is not a suggestion
    /// that expires after the first spread.
    ///
    /// A shipped book changed the child's jumper from red to blue between spread 1 and spread 2.
    /// Nothing had asked for the change and nothing forbade it: the lock names the clothing, but
    /// an image model reads a description of a person as a description of that moment, and
    /// redresses them for the next one the way a film would. So the constancy is stated as its own
    /// rule, and the exception — a scene that deliberately changes what the child is wearing — is
    /// stated with it, because a rule with no stated exception gets broken silently rather than
    /// deliberately.
    /// </summary>
    private const string WardrobeRule =
        "The child's hairstyle, clothing, footwear and accessories are exactly as described above "
        + "in this and every other scene of the book, down to colour and pattern. Do not restyle, "
        + "recolour or redress the child between scenes; the only exception is a change this "
        + "scene explicitly asks for.";

    /// <summary>
    /// The shot instruction names a camera distance, and it is worth saying that it means it.
    ///
    /// Two whole books came back as runs of medium and close compositions whatever the rhythm
    /// asked for — the model's own comfortable framing for "a child and a friend doing something"
    /// — so a book that was supposed to open wide and breathe read as eight variations of the
    /// same shot. Naming the default it falls back to is what stops it being fallen back to.
    /// </summary>
    private const string ShotDistanceRule =
        "Obey that camera distance exactly; do not default to a medium close-up.";

    /// <summary>
    /// One thing in the picture is the story; the rest is where the story happens.
    ///
    /// The planner now names exactly one visual focus per scene, and this is the half of that
    /// rule the illustrator needs: a scene brief listing a discovery among four other true details
    /// comes back as a picture where all five are drawn at the same weight, which is a picture of
    /// nothing in particular.
    /// </summary>
    private const string FocusRule =
        "The one focus the scene names must be the visually dominant element — large, clearly lit "
        + "and readable at a glance. Everything else supports it and never competes with it.";

    /// <summary>
    /// Where the words go, said as geometry rather than as a mood.
    ///
    /// "Leave calm visual space on the left" was the handoff's wording and it was the single most
    /// common reason a spread was refused — three of the three failures in the first whole-book
    /// run. Read back, the refusals say why: the model treated it as a request for a *less busy*
    /// side and then put the child there anyway, which is calm in the sense of uncluttered and
    /// useless in the sense that story text would land on her face.
    ///
    /// So it names a fraction of the frame, says what may be in it, and says who may not. And it
    /// says where the hero goes instead — a rule that only forbids leaves the model to guess, and
    /// what it guesses is the centre, which is where the fold is.
    ///
    /// An empty or "either" side means no text is set over this image at all — the cover, whose
    /// title is typeset later. That case used to reach the model as "on the either", which is not
    /// a place.
    ///
    /// It also asks for that third to be *light*. "Quiet background" was read as "dark background"
    /// often enough — foliage in shadow was one of the examples this rule itself gave — and the
    /// printed page then needs a heavy wash behind the text to keep it legible over the artwork.
    /// A naturally bright reserved side is what lets that wash stay faint, so the fix is asked for
    /// here as well as applied at layout time.
    /// </summary>
    private static string ComposeTextSide(string textSide)
    {
        var side = textSide.Trim().ToLowerInvariant();
        if (side is not ("left" or "right"))
        {
            // The cover. Printed as a single upright leaf cut from a wider render, so its outer
            // edges are the part the trim takes.
            return "Keep faces and important story action away from the centre of the spread, "
                + "where the fold falls. " + GutterRule + " Compose for a single upright page: "
                + "keep the hero and the calm title space within the central portion of the "
                + "frame, because the outer left and right edges may be trimmed away in print.";
        }

        var heroSide = side == "left" ? "right" : "left";

        return $"""
            Composition, which is a hard requirement of this page and not a preference: the
            {side} third of the image is reserved for story text that will be printed over it.
            Fill that third with quiet, naturally light background only — bright open sky, mist,
            sunlit water, pale distant landscape, or a softly lit wall. Keep it light and airy:
            not shadow, not darkness, and not a dark panel. No character, no face, no hands and no
            part of the main action may enter it.

            Place the child and the story's action in the {heroSide} two thirds instead, and keep
            every face clear of the vertical centre line, where the fold of the spread falls.
            {GutterRule}

            Keep every face and the story's key action inside the central horizontal band of the
            image as well: the printed spread is wider than it is tall, so the top and bottom
            sixths of this picture may be trimmed away. Sky, canopy and ground belong there —
            nothing the story cannot lose.
            """;
    }

    /// <summary>
    /// The centre line was already a rule — no face crosses it — but a line has no width, and the
    /// fold it stands for does: the gutter swallows a narrow strip on either side of it, not one
    /// pixel down the middle. Written out as its own sentence, and shared verbatim with
    /// <see cref="Prompts.BekiImageQaPrompt"/>'s text-side rule, so the illustrator and the
    /// reviewer are working from the same words rather than two paraphrases of one rule.
    /// </summary>
    private const string GutterRule =
        "A narrow vertical strip at the exact centre of the frame is a low-information zone "
        + "crossing the printed fold: background may continue through it, but no face, no eyes, "
        + "no hands, no key object and no part of the main action may sit inside it.";

    /// <summary>
    /// The cover a Beki book falls back to when the Beki cover could not be drawn or was refused.
    ///
    /// The fallback used to be <see cref="Compose"/> with the cover scene and nothing else, and
    /// what shipped was a cover whose companion character was a white-and-blue robot. Nothing had
    /// asked for a robot: the scene was about a grape flyer, the story's companion is Beki, and
    /// this prompt carried no Beki reference and no rule about companions — so the model read a
    /// cover scene for a book about a child and a friend, found no friend, and invented one.
    ///
    /// A wrong companion on the cover is worse than no companion at all, because the cover is
    /// where a parent decides whether the book is about their child. So the fallback says the one
    /// thing the Beki path would have said with a picture: this cover is the child, alone.
    ///
    /// It wraps <see cref="Compose"/> rather than branching inside it — that function draws every
    /// A5 book in production and is not worth a conditional for a case only v5 can reach.
    /// </summary>
    public static string ComposeChildOnlyCover(string characterLock, string scene, string? extraExclusions) =>
        Compose(characterLock, $"{scene.Trim()}\n\n{ChildOnlyCoverDirective}", extraExclusions);

    /// <summary>The clause that makes <see cref="ComposeChildOnlyCover"/> child-only.</summary>
    public const string ChildOnlyCoverDirective =
        "This cover shows the child alone. Do not invent, add or imply any second character of "
        + "any kind: no companion, friend, sidekick, creature, animal, spirit, robot, toy brought "
        + "to life, and no vehicle or machine given a face or otherwise drawn as a character. The "
        + "child is the only character in the frame, and the setting behind them stays simple.";

    public static string Compose(string characterLock, string scene, string? extraExclusions)
    {
        var exclusions = string.IsNullOrWhiteSpace(extraExclusions)
            ? Prompts.MasterStorySchema.DefaultNegativePrompt
            : $"{extraExclusions.Trim()}, {Prompts.MasterStorySchema.DefaultNegativePrompt}";

        return $"""
            {PhotographDirective}

            {characterLock.Trim()}

            {scene.Trim()}

            {StyleDirective}

            {FormatDirective}

            Do not include: {exclusions}
            """;
    }
}
