using AdventurePacks.Api.Services.Story.Composite.Poses;

namespace AdventurePacks.Api.Services.Story.Composite;

/// <summary>
/// One rectangle on a wrap PNG, in that PNG's own pixels.
///
/// A record of four integers rather than an ImageSharp <c>Rectangle</c> so that the dieline stays
/// what it has always been — arithmetic over the Locked Print Specification's millimetres, testable
/// without decoding an image. The composer converts it where it actually crops.
/// </summary>
public readonly record struct BekiCropRect(int XPx, int YPx, int WidthPx, int HeightPx);

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
///
/// Since audit 1.0 this file also carries the *digital* derivation of the same master (P0-01,
/// P0-02, amendment A3): the customer's front and back pages are crops of this wrap, not a second
/// design, and the crop window is the one that does not distort.
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

    /// <summary>Where the back board begins: the left turn-in, and nothing before it.</summary>
    public const float BackBoardLeftMm = TurnInMm;

    public const float BackBoardRightMm = BackBoardLeftMm + BoardWidthMm;

    public const float BoardTopMm = TurnInMm;
    public const float BoardBottomMm = TurnInMm + BoardHeightMm;

    /// <summary>The wrap's aspect, for cropping the generated panorama to the canvas.</summary>
    public const float AspectRatio = CanvasWidthMm / CanvasHeightMm;

    // -------------------------------------------------------------------------------------------
    // A3 / P0-01 / P0-02 — the customer's cover pages, cropped from this same master
    // -------------------------------------------------------------------------------------------

    /// <summary>The customer page the crop is scaled onto: the audit's trim-size front/back leaf.</summary>
    public const float DigitalPageWidthMm = 220f;

    public const float DigitalPageHeightMm = 200f;

    /// <summary>
    /// Why there is a crop window at all, and why it is this one.
    ///
    /// A board is 222.5 × 205 mm and the customer page is 220 × 200. Those are 1.0854 and 1.1, so
    /// the obvious move — take the whole board and resize it onto the page — squashes the artwork by
    /// about 1.3% in one axis. Nobody would name that as the defect; they would simply see a child
    /// whose face is fractionally wrong on the download and right on the printed cover, which is the
    /// same disease audit P0-01 opened with: two products that are not the same book.
    ///
    /// So the window keeps the board's full width and takes the page's ratio out of its height:
    /// 222.5 mm wide by 222.5 ÷ 1.1 = 202.27 mm tall, centred in the 205 mm board — about 1.36 mm
    /// given up at the top and the same at the bottom, into the board's own turn-in-adjacent margin
    /// where nothing important is allowed to sit anyway. What reaches the page is then a uniform
    /// scale, and amendment A3 is that rule for both boards alike.
    /// </summary>
    public const float DigitalCropWidthMm = BoardWidthMm;

    public const float DigitalCropHeightMm =
        DigitalCropWidthMm / (DigitalPageWidthMm / DigitalPageHeightMm);

    /// <summary>Half of what the ratio costs the board's height, given up at the top.</summary>
    public const float DigitalCropTopMm =
        BoardTopMm + ((BoardHeightMm - DigitalCropHeightMm) / 2f);

    public const float DigitalCropBottomMm = DigitalCropTopMm + DigitalCropHeightMm;

    /// <summary>
    /// What the crop window shrinks by on its way onto the customer's page: 220 ÷ 222.5, a little
    /// under 99%.
    ///
    /// Everything on the page scales by this, type included. The press cover sets the title at
    /// 36 pt in a 190.5 mm rectangle; the customer page shows the same board 1.12% smaller, so the
    /// same rectangle is 188.4 mm and the same title has to be 35.6 pt or it breaks its lines
    /// somewhere else — which is precisely the kind of "the download is not quite the book" that
    /// audit P0-01 opened with.
    /// </summary>
    public const float DigitalScale = DigitalPageWidthMm / DigitalCropWidthMm;

    /// <summary>The front board's crop window on a wrap of <paramref name="widthPx"/> × <paramref name="heightPx"/>.</summary>
    public static BekiCropRect FrontBoardDigitalCrop(int widthPx, int heightPx)
        => BoardCrop(FrontBoardLeftMm, widthPx, heightPx);

    /// <summary>The back board's crop window — the same rule, the other end of the canvas.</summary>
    public static BekiCropRect BackBoardDigitalCrop(int widthPx, int heightPx)
        => BoardCrop(BackBoardLeftMm, widthPx, heightPx);

    private static BekiCropRect BoardCrop(float leftMm, int widthPx, int heightPx)
    {
        if (widthPx <= 0 || heightPx <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(widthPx),
                $"A wrap of {widthPx}×{heightPx} px has no board to crop from.");
        }

        var x = Round(leftMm / CanvasWidthMm * widthPx);
        var y = Round(DigitalCropTopMm / CanvasHeightMm * heightPx);
        var w = Round(DigitalCropWidthMm / CanvasWidthMm * widthPx);
        var h = Round(DigitalCropHeightMm / CanvasHeightMm * heightPx);

        // Clamped rather than trusted: a wrap one pixel narrower than the arithmetic expects is a
        // rounding difference, and a crop that runs one pixel off the sheet is an exception in the
        // middle of a paid book.
        w = Math.Clamp(w, 1, widthPx - Math.Clamp(x, 0, widthPx - 1));
        h = Math.Clamp(h, 1, heightPx - Math.Clamp(y, 0, heightPx - 1));

        return new BekiCropRect(Math.Clamp(x, 0, widthPx - 1), Math.Clamp(y, 0, heightPx - 1), w, h);
    }

    /// <summary>
    /// A rectangle stated in wrap millimetres, expressed as a fraction of one board's crop window —
    /// which is how the title travels from the press cover to the customer's front page unchanged.
    /// </summary>
    public static (float LeftFraction, float TopFraction, float WidthFraction, float HeightFraction)
        InsideFrontBoardCrop(float leftMm, float topMm, float widthMm, float heightMm) => (
            (leftMm - FrontBoardLeftMm) / DigitalCropWidthMm,
            (topMm - DigitalCropTopMm) / DigitalCropHeightMm,
            widthMm / DigitalCropWidthMm,
            heightMm / DigitalCropHeightMm);

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.ToEven);

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
    /// way every engine anchor is.
    ///
    /// It used to be (0.80, 0.62, 0.34), and audit P1-09 read the printed result exactly as it
    /// looks: "the exact Beki asset overlaps the child's torso and its top curl reaches the face
    /// area … reposition it beside the child, clear of the face and torso". So the anchor moves
    /// right and down and Beki gets slightly smaller: centred at 87% of the width and 64% of the
    /// height, three tenths of the canvas tall.
    ///
    /// The value is not an opinion. <c>BekiCoverDielineTests</c> places EVERY pose in the approved
    /// registry at this anchor — each one's own alpha-box aspect, the engine's own arithmetic — and
    /// asserts that the placed rectangle stays inside the front board (269.5–492 × 20–225 mm), never
    /// touches the title-safe rectangle, and keeps its right edge inside the 96.1% turn-in line. The
    /// widest pose in the registry is the forward glide at aspect 1.186: at 73.5 mm tall it is
    /// 87.1 mm wide, so its right edge lands at 489.0 mm — three millimetres clear of the board and
    /// well clear of the turn-in. The tightest constraint the anchor answers to is that one; the
    /// title-safe rectangle ends at 80 mm and the pose's top edge is at 120.1 mm, so the two cannot
    /// meet at any pose aspect. Change the anchor and that test decides whether the change is legal.
    /// </summary>
    public static readonly BekiCompositeAnchor FrontBekiAnchor = new(0.87, 0.64, 0.30);

    /// <summary>
    /// What the cover geometry says to the image model — and, since audit P0-03, the whole of what
    /// it is allowed to say.
    ///
    /// This block used to restate the dieline as canvas percentages: "Back panel: from 4% to 47%",
    /// "Centre construction: from 47% to 53%", a Beki area "centred at about 80% of the canvas
    /// width". The audit found the consequence on the delivered artwork — vertical tonal jumps at
    /// x ≈ 1236 and 1291 px on a 2528 px cover, which are 250.5 mm and 261.5 mm, which are the spine
    /// boundaries those percentages describe. A model told a region exists paints the region. So the
    /// geometry now speaks the way a painter speaks: sides of a picture, a calm middle, quiet edges,
    /// and not one number a pixel could be measured against.
    ///
    /// The text is published in <c>contracts/BEKI_Cover_Base_Prompt_Template_v1.md</c> and this
    /// constant is that publication verbatim; the contract-side assertion lives in
    /// <c>CompositeContractAmendmentTests</c> and the installed-text assertion in
    /// <c>BekiCoverDielineTests</c>. The internal millimetres are unchanged and still govern
    /// compositing, cropping and typesetting — they simply stopped being something the model reads.
    /// </summary>
    public const string PanelInstructions =
        "This is one continuous panoramic scene, painted as a single picture from edge to edge.\n"
        + "The child and the one inviting story action belong on the right side of the picture.\n"
        + "The left side is the same world continuing outward as quieter environment: no child, "
        + "no other character, and no story action there, and never a second version of the "
        + "composition on the right.\n"
        + "Through the middle of the picture the scene stays simple, calm, and low in detail — "
        + "open sky, far ground, quiet water or foliage — carrying the same light, colour, and "
        + "finish as everything around it, with nothing marked, tinted, framed, blurred, or edged "
        + "there and no face, hand, character, or story-critical detail sitting there.\n"
        + "The upper right of the picture stays naturally calm and open, readable without a blank "
        + "panel, artificial blur, dark rectangle, or hard-edged box.\n"
        + "Let the scene run off all four outer edges naturally, and keep everything important "
        + "well away from those edges.";

    /// <summary>The locked wrap, in the shape the cover prompt template takes.</summary>
    public static readonly CompositeCoverGeometry Geometry = new(PanelInstructions, FrontBekiAnchor);
}
