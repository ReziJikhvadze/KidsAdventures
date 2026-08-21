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
    public const string ReferenceLabel = "Beki master reference — the sole authority for Beki's design";

    /// <summary>
    /// The identity lock, condensed from the pack's AI prompt. Sent with every image that
    /// contains Beki: the reference shows the design, and this names the features the design
    /// must not lose — the exact drift the pack's avoid-list saw coming.
    /// </summary>
    public const string Lock =
        "Beki is a small abstract floating leaf spirit — never a lamb, sheep, animal, human, "
        + "ghost, fairy or robed figure. Locked identity: plum-violet face with no nose and no "
        + "ears; large warm golden eyes; a sincere open smile; a layered cream-gold leaf body "
        + "that is anatomy, not clothing; one broad leaf spiral rising above the head; one long "
        + "rear leaf-ribbon curling behind; a glowing golden memory core in the chest; short "
        + "violet arms with soft rounded five-lobed hands, never human fingers; a tapered "
        + "floating lower body with no legs or feet. Do not redesign, restyle or reinterpret "
        + "Beki: no ears, horns, wings, hair, clothes, teeth or animal anatomy.";

    /// <summary>Continuity for a story spread that lists Beki.</summary>
    public const string SpreadContinuity =
        "Beki appears in this scene: depict the exact character in the Beki master reference — "
        + "same face, leaf anatomy, upper spiral, rear ribbon and chest glow — hovering at the "
        + "child's level as their warm companion, never leading the action, and never "
        + "duplicated. " + Lock;

    /// <summary>Continuity for the cover, where the relationship is the subject.</summary>
    public const string CoverContinuity =
        "Include Beki exactly as shown in the Beki master reference, hovering beside the child "
        + "at their level as a warm, lovable companion — with the child, never in front of "
        + "them. " + Lock;

    /// <summary>
    /// What the QA reviewer holds an image to when Beki is present. Named features rather than
    /// "matches the reference": the reviewer that judged the lamb era proved it will call a
    /// close-enough design a match unless told which parts are not negotiable.
    /// </summary>
    public const string QaRule =
        "Beki, if present, matches the canonical Beki master reference: an abstract floating "
        + "leaf spirit with a plum-violet face, warm golden eyes, a layered cream-gold leaf "
        + "body, one leaf spiral above the head, a rear leaf-ribbon, a glowing chest core, "
        + "five-lobed hands and no legs. Flag as a fault any lamb, animal, human or robed "
        + "interpretation, any ears, wings or legs, or a missing spiral, ribbon or chest glow.";
}
