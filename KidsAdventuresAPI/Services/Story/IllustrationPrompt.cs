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
    /// <param name="textSide">
    /// left | right — which half stays calm enough to set text on. Anything else ("either", from
    /// the cover, which carries no story text at all) selects the cover composition, and with it
    /// the cover's own opening sentence and identity paragraph.
    /// </param>
    /// <param name="shotInstruction">One short sentence. The rhythm is code's decision, not the story's.</param>
    /// <param name="extraExclusions">What would go wrong in this picture specifically.</param>
    /// <param name="worldLock">
    /// The world's unchanging look, quoted verbatim the way the character lock is. Optional, and
    /// absent for every plan written before it existed — an empty one leaves the prompt exactly as
    /// it was, so a half-drawn book is finished under the prompts it was started under.
    /// </param>
    /// <summary>
    /// The parent's own eye colour, written into the character lock for one picture.
    ///
    /// It exists because of a hole with two ends. The composite planner is forbidden to invent the
    /// child's appearance — correctly, since the pipeline reads it from the photograph — so a
    /// composite plan's <c>characterLock</c> is deliberately stored as an empty string. The preview
    /// cover is then composed from that empty lock, which means the one picture a parent sees before
    /// paying carried no eye colour at all, even for a run where the parent had typed one into the
    /// form. The owner's report was that the eye colour goes wrong "almost always, especially on the
    /// cover"; for a composite preview it could not have gone right.
    ///
    /// Applied to the string handed to the prompt and to nothing else. The stored plan keeps its
    /// empty lock, and the planner is never shown this — an appearance in the plan is exactly what
    /// the boundary exists to prevent, and this is a fact the parent supplied rather than one a
    /// model invented.
    ///
    /// Appended unconditionally when a colour is present, the way the character-lock rule already
    /// does it: a lock that happens to mention "green jumper" is not a lock that states the eyes.
    /// </summary>
    public static string WithParentEyeColour(string? characterLock, string? eyeColour)
    {
        var lockText = (characterLock ?? string.Empty).TrimEnd();
        var colour = (eyeColour ?? string.Empty).Trim();

        if (colour.Length == 0)
        {
            return lockText;
        }

        var sentence =
            $"The child's eyes are {colour}. This is the parent's explicit choice and overrides "
            + "anything the photograph or the description above suggests.";

        return lockText.Length == 0 ? sentence : lockText + " " + sentence;
    }

    public static string ComposeBeki(
        string characterLock,
        string scene,
        string continuity,
        string textSide,
        string shotInstruction,
        string? extraExclusions,
        bool ctaSafe = false,
        string? worldLock = null)
    {
        var exclusions = string.IsNullOrWhiteSpace(extraExclusions)
            ? Prompts.MasterStorySchema.DefaultNegativePrompt
            : $"{extraExclusions.Trim()}, {Prompts.MasterStorySchema.DefaultNegativePrompt}";

        var continuityBlock = string.IsNullOrWhiteSpace(continuity)
            ? string.Empty
            : continuity.Trim() + "\n\n";

        // Its own paragraph, immediately after the child's, because the two are the same kind of
        // thing: a description quoted into every prompt rather than remembered between them.
        var worldBlock = string.IsNullOrWhiteSpace(worldLock)
            ? string.Empty
            : WorldParagraph(worldLock) + "\n\n";

        // One question, asked once, and every part of the prompt that differs between the two
        // pictures reads the same answer.
        var cover = !NamesATextSide(textSide);

        // The cover gets the photograph directive, and gets it second — after the one line that
        // says what shape of picture this is and before anything else. This class opens by saying
        // identity is put first because an image model weights the start of a prompt most heavily
        // and the child's face is what a parent checks first; the cover is the picture that claim
        // was written about, and until now it was the one prompt here that did not carry the
        // directive at all. Spreads are unchanged: they lean on the stylization paragraph below,
        // whose balance is deliberately different.
        var identityBlock = cover ? PhotographDirective + "\n\n" : string.Empty;

        return $"""
            {(cover ? CoverOpening : SpreadOpening)}

            {identityBlock}Scene: {scene.Trim()}

            Render the child as a polished stylized 3D animated children's-film character, not
            photorealistic, while preserving the recognizable facial features, hair, approximate age
            and eye colour from the provided child reference.

            {characterLock.Trim()}

            {worldBlock}{WardrobeRule}

            {continuityBlock}{shotInstruction.Trim()} {ShotDistanceRule}

            {FocusRule}

            {ComposeTextSide(textSide, ctaSafe)}

            {StyleDirective}

            No text, letters, logos, captions, frames or QR codes anywhere in the image.

            {(cover ? CoverWholePictureRule : SpreadWholePictureRule)}

            Do not include: {exclusions}
            """;
    }

    /// <summary>
    /// Whether a side was actually named. Anything else — "either", empty, a value nobody meant —
    /// is the cover, the one picture in a Beki book that carries no story text over it.
    ///
    /// Asked in one place so the opening sentence and <see cref="ComposeTextSide"/> can never
    /// disagree about which of the two pictures they are describing, which is the disagreement
    /// that shipped: a cover told in its first line that it was a two-page spread, and told in its
    /// last line not to draw the fold of one.
    /// </summary>
    private static bool NamesATextSide(string textSide) =>
        textSide.Trim().ToLowerInvariant() is "left" or "right";

    /// <summary>
    /// The first sentence, which is where an image model decides what shape of thing it is
    /// painting.
    ///
    /// Every cover this class has ever drawn opened by asking for a two-page spread, because the
    /// cover borrowed the spread assembler whole. Three later sentences then told it where the
    /// fold would fall, and one final sentence told it not to draw one. It drew one: the newest
    /// cover carries a full-height shadow at the exact centre of the frame, and the centre crop
    /// that turns the wide render into an upright leaf keeps that shadow at the centre of the
    /// printed page. One trailing negative does not beat three positives, so the positives are
    /// gone — the cover is never told it is a spread in the first place.
    /// </summary>
    private const string CoverOpening =
        "Create one single upright children's book cover illustration.";

    /// <summary>The spreads, which really are one picture across two pages, are unchanged.</summary>
    private const string SpreadOpening =
        "Create one continuous children's book illustration for a two-page spread.";

    /// <summary>
    /// What may not run down the middle of a spread, said without naming it.
    ///
    /// The wording this replaces forbade a "fold line, crease, seam, gutter shadow" and then
    /// explained, helpfully, where the book would be bound. That is four nouns for the artefact
    /// plus a reason it exists, and a model reads a noun it has been handed as a thing that
    /// belongs in the picture — the same way "do not think of the centre" puts something there.
    /// Same rule, described as an unbroken surface rather than as the break.
    /// </summary>
    private const string SpreadWholePictureRule =
        "Paint this as one single unbroken picture: the artwork runs continuously across the whole "
        + "frame, with no vertical dividing line, no crease, no darker vertical band, no page edge "
        + "and nothing that divides the image into two halves.";

    /// <summary>
    /// The same rule for the cover, minus every word that would tell it there are two halves to
    /// divide. It forbids the artefact that was actually measured on the shipped cover — a
    /// full-height vertical band at centre — and names nothing else.
    /// </summary>
    private const string CoverWholePictureRule =
        "Paint the cover as one single unbroken picture: the artwork is continuous across the "
        + "whole frame, with no vertical dividing line, no crease and no darker vertical band "
        + "anywhere in it.";

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
    /// what it guesses is the centre, which on a spread is the worst place to put a face.
    ///
    /// An empty or "either" side means no text is set over this image at all — the cover, whose
    /// title is typeset later. That case used to reach the model as "on the either", which is not
    /// a place.
    ///
    /// The cover branch used to be the spread rules with a cover sentence bolted on, and the two
    /// halves contradicted each other outright: keep nothing important at the centre, then put the
    /// hero and the title space in the central portion. A contradiction is resolved by the model,
    /// not by us, and it resolved it by drawing the centre band it had been told about. The cover
    /// now describes only the picture it is: one upright leaf, hero large, one calm area where the
    /// title lands, and the outer edges as the part print takes.
    ///
    /// It also asks for that third to be *light*. "Quiet background" was read as "dark background"
    /// often enough — foliage in shadow was one of the examples this rule itself gave — and the
    /// printed page then needs a heavy wash behind the text to keep it legible over the artwork.
    /// A naturally bright reserved side is what lets that wash stay faint, so the fix is asked for
    /// here as well as applied at layout time.
    /// </summary>
    private static string ComposeTextSide(string textSide, bool ctaSafe = false)
    {
        if (!NamesATextSide(textSide))
        {
            // The cover. Printed as a single upright leaf centre-cut from a wider render, so the
            // outer edges are the part the trim takes and roughly half the width never prints
            // (BekiPdfComposer.CropToSheet). The title is typeset over the finished image at the
            // bottom, centred (BekiPdfComposer.ComposeCover) — which is why the calm area is asked
            // for down there and not left to the model to place.
            return """
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
                """;
        }

        var side = textSide.Trim().ToLowerInvariant();
        var heroSide = side == "left" ? "right" : "left";
        var ctaClause = ctaSafe ? " The lower part of the reserved side must stay especially clear — a printed continuation module sits there in the finished book." : string.Empty;

        return $"""
            Composition, which is a hard requirement of this page and not a preference: the
            {side} third of the image is reserved for story text that will be printed over it.
            Fill that third with quiet, naturally light background only — bright open sky, mist,
            sunlit water, pale distant landscape, or a softly lit wall. Keep it light and airy:
            not shadow, not darkness, and not a dark panel. No character, no face, no hands and no
            part of the main action may enter it.{ctaClause}

            Place the child and the story's action in the {heroSide} two thirds instead, and keep
            every face clear of the vertical centre line of the image.
            {CentreBandRule}

            Keep every face and the story's key action inside the central horizontal band of the
            image as well: the printed spread is wider than it is tall, so the top and bottom
            sixths of this picture may be trimmed away. Sky, canopy and ground belong there —
            nothing the story cannot lose.
            """;
    }

    /// <summary>
    /// The centre line was already a rule — no face crosses it — but a line has no width and what
    /// print takes at the middle of a spread does: a narrow strip on either side of centre, not
    /// one pixel down the middle. So the rule is a band, and it is a band the picture continues
    /// straight through: the only thing that changes across it is how much the story keeps there.
    ///
    /// It says that, and no longer says why. Naming the thing at the centre of the page was the
    /// defect — see <see cref="SpreadWholePictureRule"/> — and this sentence named it while
    /// describing a zone, which is the worst of both.
    ///
    /// Spreads only. The cover has no centre to keep clear; its hero belongs there.
    ///
    /// <see cref="Prompts.BekiImageQaPrompt"/> holds its own past-tense copy of the older wording
    /// for the reviewer. That reviewer is switched off (<c>QaReviewEnabled</c>), so the two are
    /// not in force at once; if it is ever switched back on, its copy is the thing to reconcile.
    /// </summary>
    private const string CentreBandRule =
        "A narrow vertical strip at the exact centre of the frame is a low-information zone: "
        + "background may continue through it unchanged, but no face, no eyes, no hands, no key "
        + "object and no part of the main action may sit inside it.";

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
    ///
    /// It takes the world lock too. This cover is stored and later adopted into the book without
    /// being drawn again, so a cover drawn in a different world from the spreads is a difference
    /// nothing downstream can correct.
    /// </summary>
    public static string ComposeChildOnlyCover(
        string characterLock, string scene, string? extraExclusions, string? worldLock = null)
    {
        // Compose puts the scene straight after the character lock, so prepending the world here
        // lands it in the same place ComposeBeki puts it — and leaves the prompt untouched, to the
        // byte, for a plan that carries no world lock.
        var body = string.IsNullOrWhiteSpace(worldLock)
            ? $"{scene.Trim()}\n\n{ChildOnlyCoverDirective}"
            : $"{WorldParagraph(worldLock)}\n\n{scene.Trim()}\n\n{ChildOnlyCoverDirective}";

        return Compose(characterLock, body, extraExclusions);
    }

    /// <summary>The world lock as the image model reads it, in both prompts that carry one.</summary>
    private static string WorldParagraph(string worldLock) =>
        $"The world, identical in every illustration of this book: {worldLock.Trim()}";

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
