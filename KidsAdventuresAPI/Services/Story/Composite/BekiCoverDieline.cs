using AdventurePacks.Api.Services.Story.Composite.Poses;

namespace AdventurePacks.Api.Services.Story.Composite;

/// <summary>
/// The v0 hardcover wrap, in the Locked Print Specification's own millimetres
/// (contracts/BEKI_Print_Production_Locked_Spec_v1.md §2) — constants rather than options,
/// because the spec locks them: "if this exact fixed geometry is unavailable in code, use
/// LAYOUT_FAILED; do not estimate or substitute dimensions." Options invite estimating.
///
/// Back to front across 512 mm: a 20 mm turn-in, the 222.5 mm back board, an 8 mm hinge, the
/// 11 mm spine, an 8 mm hinge, the 222.5 mm front board, a 20 mm turn-in. Down 245 mm: 20 mm
/// turn-in, 205 mm board, 20 mm turn-in. The 27 mm centre is hinge+spine+hinge, not a printable
/// spine, and v0 puts no title on it.
///
/// The three front-panel rectangles the cover contract leaves to the developer — title-safe,
/// child/action, Beki integration — are chosen here and recorded in every wrap's composition
/// manifest, per the contract's own instruction that the developer "must store the approved
/// front-panel Beki anchor in cover configuration and return it in the first-run manifest".
/// </summary>
public static class BekiCoverDieline
{
    public const float CanvasWidthMm = 512f;
    public const float CanvasHeightMm = 245f;

    public const float TurnInMm = 20f;
    public const float BoardWidthMm = 222.5f;
    public const float HingeMm = 8f;
    public const float SpineMm = 11f;
    public const float BoardHeightMm = 205f;

    /// <summary>Where the front board begins: turn-in + back board + hinge + spine + hinge.</summary>
    public const float FrontBoardLeftMm = TurnInMm + BoardWidthMm + HingeMm + SpineMm + HingeMm;

    public const float FrontBoardRightMm = FrontBoardLeftMm + BoardWidthMm;

    public const float BoardTopMm = TurnInMm;
    public const float BoardBottomMm = TurnInMm + BoardHeightMm;

    /// <summary>The wrap's aspect, for cropping the generated panorama to the canvas.</summary>
    public const float AspectRatio = CanvasWidthMm / CanvasHeightMm;

    /// <summary>
    /// The Ottia title's home: the upper front board, held 16 mm in from the board's sides and
    /// sitting between 14% and 33% of the canvas height — calm by generation-time instruction,
    /// typeset at layout time.
    /// </summary>
    public const float TitleSafeLeftMm = FrontBoardLeftMm + 16f;
    public const float TitleSafeWidthMm = BoardWidthMm - 32f;
    public const float TitleSafeTopMm = 34f;
    public const float TitleSafeHeightMm = 46f;

    /// <summary>
    /// Where the approved pose lands on the wrap, normalized over the full 512 × 245 canvas the
    /// way every engine anchor is: centred at 80% of the width and 62% of the height, one third
    /// of the canvas tall. That puts her whole visible extent inside the front board, beside the
    /// child's zone, well clear of the hinge at 52.6% and the right turn-in at 96.1%.
    /// </summary>
    public static readonly BekiCompositeAnchor FrontBekiAnchor = new(0.80, 0.62, 0.34);

    /// <summary>
    /// The resolved panel and safe-zone block the cover prompt interpolates — the spec's
    /// millimetres restated as canvas percentages, which are the units an image model can
    /// actually hold.
    /// </summary>
    public const string PanelInstructions =
        "The canvas is one continuous hardcover wrap: back cover on the left, spine in the "
        + "middle, front cover on the right.\n"
        + "Back panel: from 4% to 47% of the canvas width. Environment only — no child, no "
        + "characters, no story action, and do not duplicate the front panel's composition.\n"
        + "Centre construction: from 47% to 53% of the canvas width. Keep it continuous, "
        + "low-information environment; no face, hand, character, text-critical feature, or "
        + "story action may sit there, and do not blur it.\n"
        + "Front panel: from 53% to 96% of the canvas width. The child and the one inviting "
        + "story action live here.\n"
        + "Front title area: the upper front panel, from 14% to 33% of the canvas height between "
        + "56% and 93% of the canvas width. Keep it naturally calm and readable without a blank "
        + "panel, artificial blur, dark rectangle, or hard-edged box.\n"
        + "Keep the area centred at about 80% of the canvas width and 62% of the canvas height, "
        + "roughly one third of the canvas height tall, naturally lit and calm — free of "
        + "characters, faces, hands, hard foreground edges, and story-critical details. It is "
        + "ordinary continuous environment exactly like its surroundings, never a zone, shape, "
        + "panel, or region to mark or draw in any way.\n"
        + "The outer 4% on the left and right and the outer 8% at the top and bottom wrap around "
        + "the cover boards: continue the environment through them naturally and keep everything "
        + "important out of them.";

    /// <summary>The locked wrap, in the shape the cover prompt template takes.</summary>
    public static readonly CompositeCoverGeometry Geometry = new(PanelInstructions, FrontBekiAnchor);
}
