namespace AdventurePacks.Api.Services.Story.Prompts;

/// <summary>
/// QA IMAGE — prompt C of the Beki handoff's three.
///
/// Kept as the handoff wrote it, with the scene, the child's character lock and the requested
/// text side filled in, because the point of a QA pass is to be a fixed yardstick: a checklist
/// that drifts cannot tell you whether the images got better or the standard got looser.
///
/// Three checks are ours, and each one is a fault that got past this list into a finished book.
/// The child's wardrobe changed colour between spreads 1 and 2 of a shipped book and no rule
/// here mentioned clothing at all — hence the character lock being passed in, since a reviewer
/// cannot check a likeness it was never shown. A spread arrived where the story's action was
/// present but incidental, so the "clearly visible" rule passed it while the page read as
/// nothing happening. And a legible handwritten "For you" note sat inside the artwork of
/// another spread, which the old "no unwanted text" wording apparently did not cover.
/// </summary>
public static class BekiImageQaPrompt
{
    // Two dollars, so a single brace is literal JSON and {{…}} is the interpolation. The verdict
    // shapes below are the payload the model must copy, and they are full of braces.
    public static string For(string scene, string textSide, string characterLock, bool ctaSafe = false) =>
        $$"""
        Review this generated children's book illustration.

        Compare it with the provided scene and reference images.

        Scene it was asked for: {{scene.Trim()}}

        The child's fixed appearance, which every picture in this book was required to match:
        {{characterLock.Trim()}}

        {{TextSideRule(textSide, ctaSafe)}}

        Check only:
        1. the child resembles the reference and is rendered as a stylized 3D animated character,
           not photorealistically,
        2. the child's hairstyle, clothing, footwear and accessories match the fixed appearance
           above — judge wardrobe against that description alone, not against the clothes in the
           reference photograph — unless this scene states a change,
        3. recurring characters and recurring story objects remain recognizable and consistent,
        4. {{BekiIdentity.QaRule}}
        5. within about two seconds, the scene's main story beat is obvious: the required action
           or discovery reads at a glance rather than being incidental or hidden,
        6. important faces and actions are away from the center gutter,
        7. the reserved third named above holds quiet background only — no character, face, hands
           or main action anywhere inside it,
        8. there is no legible or pseudo-legible lettering anywhere — no words, letter-like marks,
           signs, notes, labels, logos, frames or QR codes,
        9. there are no incorrect or unnecessary story characters.

        Fail only on a fault a parent would see in the printed book. Minor stylistic differences,
        small-scale simplification and matters of taste are not faults.

        Return JSON only.

        If usable:
        {"status":"PASS","issues":[]}

        If not usable:
        {"status":"FAIL","issues":["specific issue"]}
        """;

    /// <summary>
    /// The reviewer is told the rule in the words the illustrator was given it in.
    ///
    /// They were phrased differently before — the prompt asked for "calm visual space", the
    /// checklist asked whether there was "usable calm space" — and two soft phrasings of one rule
    /// is how an image passes review and still cannot be typeset on. A cover, which carries no
    /// text over it, is told there is no reserved side rather than being told "either", which the
    /// reviewer read as a side it could not find.
    /// </summary>
    private static string TextSideRule(string textSide, bool ctaSafe = false)
    {
        var side = textSide.Trim().ToLowerInvariant();

        var ctaClause = ctaSafe ? " The lower part of the reserved third was additionally required to stay clear for a printed module." : string.Empty;

        return side is "left" or "right"
            ? $"The {side} third of this image was reserved for story text printed over it: that "
              + "third was required to hold quiet, naturally light background only, with no "
              + "character, face, hands or main action inside it." + ctaClause + " Faces and the key action were "
              + "also required to stay inside the central horizontal band, because the top and "
              + "bottom sixths may be "
              + $"trimmed in print. {GutterRule}"
            : "No story text is printed over this image, so no side was reserved. It is printed "
              + "as a single upright page cut from this wider frame, so the outer left and right "
              + "edges may be trimmed away: the hero and the calm title space were required to "
              + $"sit within the central portion. {GutterRule} Judge the composition on those terms.";
    }

    /// <summary>
    /// The same words <see cref="IllustrationPrompt"/> gives the illustrator, verbatim — a
    /// reviewer checking a rule phrased differently from how it was asked for is how a spread
    /// passes review carrying exactly the fault the rule exists to catch.
    /// </summary>
    private const string GutterRule =
        "A narrow vertical strip at the exact centre of the frame was a low-information zone "
        + "crossing the printed fold: background could continue through it, but no face, no eyes, "
        + "no hands, no key object and no part of the main action was allowed to sit inside it.";
}
