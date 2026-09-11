namespace AdventurePacks.Api.Services.Story;

/// <summary>
/// Beki's canonical identity, in one place — v2, the selected leaf-spirit design.
///
/// Source of truth: the partner production pack (BEKI_Character_Production_Pack_v2). The image
/// at <see cref="ReferenceAssetPath"/> is byte-identical to the pack's approved source master
/// (SHA-256 535e4d9c…f21ed) and is the only visual authority; the strings here are the pack's
/// own AI identity-lock prompt, condensed for the calls that carry it. v2 replaced a lamb
/// design outright — the lock says "never a lamb" in as many words — which is why the words
/// live here once: an illustrator prompt, a reviewer checklist and a story rule that each
/// remembered a different Beki is how a book ships with two of them.
/// </summary>
public static class BekiIdentity
{
    /// <summary>Matches the pack's versioning; bump only when partners approve a new master.</summary>
    public const string Version = "beki-canonical-v2";

    public const string ReferenceAssetPath = "Assets/Beki/beki-canonical-v2.png";

    /// <summary>The label every reference attachment carries, so the model knows which file rules.</summary>
    public const string ReferenceLabel = "Beki master reference - the sole authority for Beki's design";

    public const string HandsReferenceLabel = "Beki hand details - enlarged crops of the approved master, not generated artwork";

    /// <summary>
    /// The identity lock, condensed from the pack's AI prompt. Sent with every image that
    /// contains Beki: the reference shows the design, and this names the features the design
    /// must not lose — the exact drift the pack's avoid-list saw coming.
    /// </summary>
    public const string Lock =
        "Beki is a small abstract floating leaf spirit - never a lamb, sheep, animal, human, "
        + "ghost, fairy or robed figure. Locked identity: plum-violet face with no nose and no "
        + "ears; large warm golden eyes; a sincere open smile; a layered cream-gold leaf body "
        + "that is anatomy, not clothing; one broad leaf spiral rising above the head; one long "
        + "rear leaf-ribbon curling behind; a glowing golden memory core in the chest; short "
        + "violet arms with exactly four rounded digits per hand (three finger lobes and one "
        + "thumb lobe), never human fingers; a tapered "
        + "floating lower body with no legs or feet. Do not redesign, restyle or reinterpret "
        + "Beki: no ears, horns, wings, hair, clothes, teeth or animal anatomy.";

    /// <summary>Continuity for a story spread that lists Beki.</summary>
    public const string GenerationLock =
        "The explicitly labelled Beki master reference is the sole authority for Beki. "
        + "It comes before the world and previous-spread references so those cannot redefine Beki. "
        + "Include exactly one Beki in the finished scene. Treat Beki as the SAME fixed character "
        + "asset on every page, not a new interpretation. Ignore any Beki visible in child anchors, "
        + "previous spreads, continuity images or theme references: those images are NOT Beki design "
        + "references and their mistakes must not propagate. Copy the canonical reference almost exactly: "
        + "preserve the silhouette, body and head proportions, eye shape, eye size, eye spacing, "
        + "golden eye colour, face, smile, leaf layers, head spiral, rear ribbon and chest core. "
        + AnatomyLock
        + "Do not enlarge Beki's head, eyes, hands or body or change "
        + "their relative sizes. Keep Beki a small companion at the same scale relative to the "
        + "child throughout the book. No redesign, reinterpretation, costume, new anatomy or "
        + "style conversion. Only gentle arm movement, body tilt, position and scene lighting may vary; "
        + "lighting must preserve the local violet skin and golden eye colours. Keep the reference "
        + "face and mouth expression unchanged, even when the story asks Beki to react. Convey the "
        + "supporting action with body position and open palms, never a changed face or hand anatomy. "
        + "The child remains the active hero. Keep Beki clear of the child's face, hands, text "
        + "area and centre fold. The reference overrides any conflicting scene description. " + Lock;

    public const string AnatomyLock =
        "HANDS: exactly FOUR rounded digits on EACH hand, counting the thumb: three short, soft "
        + "finger lobes plus one thumb lobe. Four total, NOT four plus a thumb. NO human fingers, "
        + "NO five-digit or three-digit redesign, NO extra digits, NO missing or fused digits, "
        + "NO nails or elongated fingers. Keep the canonical violet palm shape and lobe proportions. "
        + "Prefer relaxed open palms in the reference orientation so all four digits are readable; "
        + "do not make fists, grip props, or point with an isolated index finger. A genuinely occluded "
        + "digit stays hidden behind the occluder; never invent extra visible lobes to compensate. "
        + "EYES: the same warm golden-yellow/amber irises, dark round pupils and small bright "
        + "catchlights as the reference; identical eye contours, size, spacing and eyelids. "
        + "No blue, green, brown or violet irises; no recolouring, squinting, winking or resizing. "
        + "FACE: preserve the exact rounded plum-violet face, cheek fullness, forehead, taper toward "
        + "the chin and cream-leaf facial opening. No longer, narrower, wider or more human face; "
        + "no nose, ears or added facial features. MOUTH: preserve the same small open upturned "
        + "smile, curved outline, width, opening and placement as the reference. No teeth, tongue, "
        + "lips, beak, closed-mouth smile, wide grin, frown or surprised O-shaped mouth. ";

    public const string GenerationFinalCheck =
        "FINAL BEKI IDENTITY CHECK: before finishing this image, compare Beki with the labelled "
        + "approved master and its hand details when supplied, never with a previous spread. "
        + "Each fully visible hand has FOUR rounded digits INCLUDING the thumb; "
        + "both eyes retain the same golden irises, dark pupils, size and spacing; the violet face "
        + "outline and small open smile are unchanged. Preserve the same silhouette, proportions, "
        + "head spiral, rear ribbon and chest core. If the action conflicts, simplify the action, "
        + "never Beki's identity. This identity lock overrides the scene, styling and all other images.";

    public const string SpreadContinuity =
        "Beki appears in this scene: depict the exact character in the Beki master reference - "
        + "same face, leaf anatomy, upper spiral, rear ribbon and chest glow - hovering at the "
        + "child's level as their warm companion, never leading the action, and never "
        + "duplicated. " + Lock;

    /// <summary>Continuity for the cover, where the relationship is the subject.</summary>
    public const string CoverContinuity =
        "Include Beki exactly as shown in the Beki master reference, hovering beside the child "
        + "at their level as a warm, lovable companion - with the child, never in front of "
        + "them. " + Lock;

    /// <summary>
    /// What the QA reviewer holds an image to when Beki is present. Named features rather than
    /// "matches the reference" — but proportionate. The first strict wording asked for every
    /// feature to be verifiably present and refused seven spreads out of seven, retries
    /// included: at storybook distance a small Beki cannot show a checklist, and a gate that
    /// refuses everything gates nothing — every image ships flagged and none is ever persisted
    /// as accepted. The rule now names the breaks that actually betray the character, and says
    /// out loud that small-scale softness is not one of them.
    /// </summary>
    public const string QaRule =
        "Beki, if present, reads as the character in the Beki master reference: an abstract "
        + "floating leaf spirit with a plum-violet face, golden eyes and a cream-gold leaf "
        + "body. Flag as a fault only a clear identity break: Beki drawn as a lamb, animal, "
        + "human, ghost or robed figure; visible ears, wings or legs; badly wrong colours; or "
        + "the silhouette losing all three of its signature forms (head spiral, rear ribbon, "
        + "chest glow); a fully visible hand with other than four rounded digits including its thumb; "
        + "changed iris colour, eye proportions, face outline or mouth shape; or added teeth. "
        + "Softness at small scale and genuine occlusion are acceptable; do not invent a digit-count "
        + "fault for a hand that cannot be inspected. Visible anatomical redesign is not simplification.";
}
