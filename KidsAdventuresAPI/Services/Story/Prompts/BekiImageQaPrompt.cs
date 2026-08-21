namespace AdventurePacks.Api.Services.Story.Prompts;

/// <summary>
/// QA IMAGE — prompt C of the Beki handoff's three.
///
/// Kept as the handoff wrote it, with the scene and the requested text side filled in, because
/// the point of a QA pass is to be a fixed yardstick: a checklist that drifts cannot tell you
/// whether the images got better or the standard got looser.
/// </summary>
public static class BekiImageQaPrompt
{
    // Two dollars, so a single brace is literal JSON and {{…}} is the interpolation. The verdict
    // shapes below are the payload the model must copy, and they are full of braces.
    public static string For(string scene, string textSide) =>
        $$"""
        Review this generated children's book illustration.

        Compare it with the provided scene and reference images.

        Scene it was asked for: {{scene.Trim()}}

        {{TextSideRule(textSide)}}

        Check only:
        1. the child resembles the reference and is rendered as a stylized 3D animated character,
           not photorealistically,
        2. recurring characters remain recognizable and consistent,
        3. {{BekiIdentity.QaRule}}
        4. the required scene and action are clearly visible,
        5. important faces and actions are away from the center gutter,
        6. the reserved third named above holds quiet background only — no character, face, hands
           or main action anywhere inside it,
        7. there is no unwanted text, lettering, logo, frame or QR code,
        8. there are no incorrect or unnecessary story characters.

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
    private static string TextSideRule(string textSide)
    {
        var side = textSide.Trim().ToLowerInvariant();

        return side is "left" or "right"
            ? $"The {side} third of this image was reserved for story text printed over it: that "
              + "third was required to hold quiet background only, with no character, face, hands "
              + "or main action inside it. Faces and the key action were also required to stay "
              + "inside the central horizontal band, because the top and bottom sixths may be "
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
