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

            Leave calm visual space on the {textSide.Trim().ToLowerInvariant()} for story text.

            Keep faces and important story action away from the centre of the spread.

            {StyleDirective}

            No text, letters, logos, captions, frames or QR codes anywhere in the image.

            Do not include: {exclusions}
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
