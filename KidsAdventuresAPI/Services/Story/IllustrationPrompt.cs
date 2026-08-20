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

            {continuityBlock}{shotInstruction.Trim()}

            {ComposeTextSide(textSide)}

            {StyleDirective}

            No text, letters, logos, captions, frames or QR codes anywhere in the image.

            Do not include: {exclusions}
            """;
    }

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
    /// </summary>
    private static string ComposeTextSide(string textSide)
    {
        var side = textSide.Trim().ToLowerInvariant();
        if (side is not ("left" or "right"))
        {
            // The cover. Printed as a single upright leaf cut from a wider render, so its outer
            // edges are the part the trim takes.
            return "Keep faces and important story action away from the centre of the spread, "
                + "where the fold falls. Compose for a single upright page: keep the hero and "
                + "the calm title space within the central portion of the frame, because the "
                + "outer left and right edges may be trimmed away in print.";
        }

        var heroSide = side == "left" ? "right" : "left";

        return $"""
            Composition, which is a hard requirement of this page and not a preference: the
            {side} third of the image is reserved for story text that will be printed over it.
            Fill that third with quiet background only — open sky, distant landscape, mist, water,
            or foliage in shadow. No character, no face, no hands and no part of the main action
            may enter it.

            Place the child and the story's action in the {heroSide} two thirds instead, and keep
            every face clear of the vertical centre line, where the fold of the spread falls.

            Keep every face and the story's key action inside the central horizontal band of the
            image as well: the printed spread is wider than it is tall, so the top and bottom
            sixths of this picture may be trimmed away. Sky, canopy and ground belong there —
            nothing the story cannot lose.
            """;
    }

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
