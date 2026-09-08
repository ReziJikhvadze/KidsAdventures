using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AdventurePacks.Api.Services.Story.Composite;

/// <summary>
/// Where one book's title is set on its cover, and the arithmetic that chose it.
///
/// Millimetres from the top-left of the full 512 × 245 mm wrap, the coordinate system every other
/// cover rectangle is stated in — so the press page can pad to it directly and the customer's page
/// can take <see cref="BekiCoverDieline.InsideFrontBoardCrop"/> of it and land in the same place.
/// </summary>
/// <param name="ApprovedScore">What the approved box (x 285.5, y 34) measured on this wrap.</param>
/// <param name="ChosenScore">
/// What the chosen box measured, the departure toll included — the same number as
/// <paramref name="ApprovedScore"/> when the title did not move.
/// </param>
/// <param name="CandidatesEvaluated">
/// How many boxes were legal on this cover and were scored, the approved one included. One means the
/// front board had nowhere else to offer, which is a different sentence from "nothing was calmer".
/// </param>
/// <param name="Reason">One sentence, for the log line and the page's receipt.</param>
public sealed record BekiCoverTitleChoice(
    double LeftMm,
    double TopMm,
    double WidthMm,
    double HeightMm,
    bool Moved,
    double ApprovedScore,
    double ChosenScore,
    int CandidatesEvaluated,
    string Reason)
{
    /// <summary>The algorithm that produced this choice, so a stored record says what read it.</summary>
    public string Version => BekiCoverTitlePlacement.Version;

    /// <summary>The approved box, as the answer for a cover nothing could be measured on.</summary>
    public static BekiCoverTitleChoice Approved(string reason) => new(
        BekiCoverDieline.TitleSafeLeftMm,
        BekiCoverDieline.TitleSafeTopMm,
        BekiCoverDieline.TitleSafeWidthMm,
        BekiCoverDieline.TitleSafeHeightMm,
        Moved: false,
        ApprovedScore: 0,
        ChosenScore: 0,
        CandidatesEvaluated: 0,
        reason);
}

/// <summary>
/// Picks the calmest place on a finished cover wrap for the book's title to sit.
///
/// **The observed defect, 2026-09-08 (owner).** "On a production book the cover TITLE was printed
/// over the child's face." It was. <see cref="BekiCoverDieline.TitleSafeLeftMm"/> and its three
/// companions are a FIXED rectangle — x 285.5, y 34, 136 × 46 mm, the upper front board — and
/// <c>BekiPdfComposer</c> set the title there on every book ever printed. The cover prompt does ask
/// the model to keep the child's head out of it
/// (<see cref="BekiCoverDieline.ReservedArtworkInstructions"/>), in millimetre coordinates on a
/// canvas the model cannot measure, and on a book whose child it drew tall and centred the head
/// landed in the top band anyway. Nothing downstream looked at the picture:
/// <see cref="BekiCoverLayoutSafety"/> compares HUMAN-recorded bounds against that same fixed
/// rectangle, and production records <c>NOT_REVIEWED</c>.
///
/// **What may be done about it.** The same thing §14 of the supplier handoff licenses for Beki
/// herself — "a failed placement should first adjust deterministic anchors" — applied to the one
/// other thing this pipeline places by number. So this class returns a rectangle. It never resizes
/// the title (the box stays 136 × 46 mm, so <c>CoverTitleSizePt</c>'s ladder is untouched), never
/// paints anything on the artwork, never asks a model anything, and costs a few milliseconds of
/// ImageSharp arithmetic on bytes the book has already paid for — owner's rule 5, 2026-09-01: "we
/// don't need additional reviews for images".
///
/// **What it reads.** The wrap COMPOSITE, which is the base with the approved Beki already pasted
/// onto it — which is also what the composer is handed. That is deliberate and it is what makes
/// Beki free: she is part of the picture by the time this looks at it, and a rectangle over her
/// measures busy for exactly the reason a rectangle over the child does. There is no pose geometry
/// here and there is nothing to keep in step with the engine.
///
/// The reading itself is <see cref="BekiActivityMap"/> — the same contour-and-colour occupancy map
/// <see cref="Poses.BekiPlacementChooser"/> was calibrated on, shared rather than copied.
///
/// **The window.** Down the front board and no further: y from the approved 34 mm to
/// <see cref="BekiCoverDieline.BoardBottomMm"/> less <see cref="BoardMarginMm"/> and the box's own
/// height, in <see cref="VerticalStepMm"/> steps; x on <see cref="LeftsMm"/>, each of which keeps
/// the box <see cref="BoardMarginMm"/> inside both board edges. Never above the approved top, where
/// the turn-in is. Never overlapping the logo's rectangle plus its
/// <see cref="BekiCoverDieline.LogoClearSpaceMm"/> of clear space, which in practice is what makes
/// the three right-hand columns available only below the logo.
///
/// **Why the approved box is sticky.** Moving the title is a change to a cover design somebody
/// approved, so it happens only when the alternative is materially calmer — the same two-part test
/// <see cref="Poses.BekiPlacementChooser"/> uses (<see cref="CalmerRatio"/> and
/// <see cref="CalmerMargin"/>), over a score that already includes a toll for the walk. A cover
/// whose top band the model DID keep calm keeps the approved box, and so does a cover that is busy
/// everywhere: there is nowhere calmer to go, and shuffling the title around a crowded board trades
/// a known design for an unknown one.
/// </summary>
public static class BekiCoverTitlePlacement
{
    /// <summary>The version this algorithm is recorded under in a page's layout receipt.</summary>
    public const string Version = "cover-title-placement-v1";

    /// <summary>
    /// How far inside the board's sides and its foot the box is kept — the margin the approved box
    /// already keeps on the left (285.5 mm is <see cref="BekiCoverDieline.FrontBoardLeftMm"/> + 16),
    /// applied to the other three edges so that a moved title is no closer to a fold than the one
    /// that was signed off.
    ///
    /// Not applied to the TOP: the approved box's own 34 mm is 14 mm from the board's head, and the
    /// window never goes above it anyway.
    /// </summary>
    public const float BoardMarginMm = 16f;

    /// <summary>
    /// How far apart the vertical candidates sit. Eight millimetres is about a sixth of the box's
    /// own height — fine enough that the grid can find the gap between a child's head and her
    /// shoulder, coarse enough that the whole board is 17 rows rather than 130.
    /// </summary>
    public const float VerticalStepMm = 8f;

    /// <summary>
    /// The four left edges on offer, in wrap millimetres: the approved one, and three steps right.
    ///
    /// A short explicit list rather than a step, because the horizontal room is small and unevenly
    /// useful. The box is 136 mm wide in a 222.5 mm board with 16 mm margins, so the whole
    /// horizontal travel available is 54.5 mm — and every millimetre of it to the right walks the
    /// box under the logo, whose clear space then excludes most of the upper rows. Four columns
    /// describe that honestly; a 4 mm sweep would describe it fourteen times over and choose between
    /// candidates a reader could not tell apart on the printed cover.
    /// </summary>
    public static readonly float[] LeftsMm = [BekiCoverDieline.TitleSafeLeftMm, 300f, 320f, 340f];

    /// <summary>
    /// How wide the band read around the box is, in wrap millimetres.
    ///
    /// The box alone is not enough. A calm rectangle exactly the title's size between the child's
    /// hair and her raised hand scores beautifully and reads as wedged; the band is what tells that
    /// apart from open sky. Four millimetres is roughly the title's own leading.
    /// </summary>
    public const float MarginBandMm = 4f;

    /// <summary>
    /// What the band counts for beside the box itself. Half, as it is for Beki: the band is
    /// context, and a box should never be rejected for what is happening a finger's width away.
    /// </summary>
    public const double MarginBandWeight = 0.5;

    /// <summary>
    /// What moving costs, per unit of distance from the approved box — distance measured in canvas
    /// fractions, so that the vertical travel the board actually offers (0.53 of the height) is
    /// priced as the larger journey it is.
    ///
    /// A fifth of Beki's own <see cref="Poses.BekiPlacementChooser.DepartureCost"/>, and the reason
    /// is worth stating because it changes what the term DOES here. She chooses among a hundred and
    /// eighty places scattered over a whole spread, and her toll's job is to stop the calmest pixel
    /// in the picture pulling her into an empty corner. The title chooses among four columns of a
    /// single board, in a window that already forbids the edges — there is no corner to flee to, and
    /// the honest destination on a cover whose upper board the model filled IS the calm band lower
    /// down. At 0.60 the toll simply refused every real move (measured: contract §8), so this term
    /// is not a shaper of the destination but a surcharge on the DECISION to move at all: it is
    /// added before <see cref="CalmerRatio"/> reads the two numbers, and what it buys is that a
    /// distant band has to be calmer than a near one by more than the walk is worth.
    ///
    /// One consequence is deliberate and should not be discovered by surprise: because a step down
    /// the grid costs only 0.0065 of toll, the calmest legal rectangle almost always wins among the
    /// candidates that clear the bar. The title moves rarely; when it moves, it goes where the
    /// picture is quietest.
    /// </summary>
    public const double DepartureCost = 0.20;

    /// <summary>
    /// How far the box may step before <see cref="DepartureCost"/> starts charging: about one
    /// vertical step of the grid.
    ///
    /// Smaller than Beki's 0.12 because the two departures are not the same act. She is one figure
    /// among many in a picture and a tenth of the canvas is a nudge within the same composition; the
    /// title is the book's name in the place a cover puts its name, and every step it takes is
    /// visible as such.
    /// </summary>
    public const double DepartureFreeRadius = 0.035;

    /// <summary>
    /// How much calmer the best candidate must be before the title moves at all: no more than this
    /// fraction of the approved box's score.
    /// </summary>
    public const double CalmerRatio = 0.85;

    /// <summary>
    /// And by how much in absolute terms, so that two nearly-silent places do not trade an approved
    /// design for a rounding difference.
    /// </summary>
    public const double CalmerMargin = 0.03;

    /// <summary>
    /// Candidates scoring within this much of the best are treated as equally calm, and the one
    /// nearest the approved box wins. The approved box is the design somebody signed off; when the
    /// arithmetic cannot tell two places apart, the smaller departure is the better answer.
    /// </summary>
    public const double NearBestTolerance = 1.05;

    /// <summary>
    /// Chooses the title's rectangle on one cover.
    /// </summary>
    /// <param name="wrapCompositePng">
    /// The finished wrap: the generated 512 × 245 panorama with the approved Beki already composited
    /// onto it. Any pixel size — the reading downscales to
    /// <see cref="BekiActivityMap.MapWidth"/> columns, so the 1536-wide screen master and the
    /// 6047-wide press raster of one cover give the same answer.
    /// </param>
    public static BekiCoverTitleChoice Choose(byte[] wrapCompositePng)
    {
        ArgumentNullException.ThrowIfNull(wrapCompositePng);

        using var wrap = Image.Load<Rgba32>(wrapCompositePng);
        var map = BekiActivityMap.Measure(wrap);

        var widthMm = BekiCoverDieline.TitleSafeWidthMm;
        var heightMm = BekiCoverDieline.TitleSafeHeightMm;
        var approvedLeftMm = BekiCoverDieline.TitleSafeLeftMm;
        var approvedTopMm = BekiCoverDieline.TitleSafeTopMm;

        var band = map.Band(MarginBandMm / BekiCoverDieline.CanvasWidthMm);
        var approvedScore = Measure(map, approvedLeftMm, approvedTopMm, widthMm, heightMm, band);

        var candidates = new List<Candidate>();
        var lowestTopMm = BekiCoverDieline.BoardBottomMm - BoardMarginMm - heightMm;

        foreach (var leftMm in LeftsMm)
        {
            if (leftMm < BekiCoverDieline.FrontBoardLeftMm + BoardMarginMm
                || leftMm + widthMm > BekiCoverDieline.FrontBoardRightMm - BoardMarginMm)
            {
                continue;
            }

            for (var topMm = approvedTopMm; topMm <= lowestTopMm + Tolerance; topMm += VerticalStepMm)
            {
                if (TouchesLogo(leftMm, topMm, widthMm, heightMm))
                {
                    continue;
                }

                // Distance from the approved box's own corner, in canvas fractions: the two axes are
                // not weighted against each other, because the question the toll asks is "how far is
                // this from the design that was approved" and a cover is looked at whole.
                var distance = Math.Sqrt(
                    Math.Pow((leftMm - approvedLeftMm) / BekiCoverDieline.CanvasWidthMm, 2)
                    + Math.Pow((topMm - approvedTopMm) / BekiCoverDieline.CanvasHeightMm, 2));

                candidates.Add(new Candidate(
                    leftMm,
                    topMm,
                    Measure(map, leftMm, topMm, widthMm, heightMm, band)
                    + (DepartureCost * Math.Max(0, distance - DepartureFreeRadius)),
                    distance));
            }
        }

        // The approved box is always legal by construction — it is where the title has always been
        // set — but say so rather than assume it: a dieline edited without this window in mind must
        // not silently leave the cover with no candidate at all.
        if (candidates.Count == 0)
        {
            return new BekiCoverTitleChoice(
                approvedLeftMm, approvedTopMm, widthMm, heightMm,
                Moved: false,
                approvedScore,
                approvedScore,
                CandidatesEvaluated: 0,
                "kept the approved title box: the front board offered no candidate rectangle at "
                + "all, so nothing here is in a position to choose a better one.");
        }

        var best = candidates.MinBy(candidate => candidate.Score)!;
        var scored = candidates.Count;

        var materiallyCalmer =
            best.Score <= CalmerRatio * approvedScore
            && approvedScore - best.Score >= CalmerMargin;

        if (!materiallyCalmer)
        {
            return new BekiCoverTitleChoice(
                approvedLeftMm, approvedTopMm, widthMm, heightMm,
                Moved: false,
                approvedScore,
                approvedScore,
                scored,
                scored == 1
                    ? "kept the approved title box: no other rectangle fits the front board's "
                      + "margins and the logo's clear space on this cover."
                    : $"kept the approved title box: it measures {approvedScore:F3} and the calmest "
                      + $"of {scored} candidates measures {best.Score:F3}, which is not materially "
                      + "calmer.");
        }

        // Everything as calm as the best, resolved back towards the approved design: nearest it
        // first, then a fixed sweep so that two equidistant rectangles cannot depend on the order a
        // loop happened to build them in.
        var chosen = candidates
            .Where(candidate => candidate.Score <= best.Score * NearBestTolerance)
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.TopMm)
            .ThenBy(candidate => candidate.LeftMm)
            .First();

        return new BekiCoverTitleChoice(
            chosen.LeftMm, chosen.TopMm, widthMm, heightMm,
            Moved: true,
            approvedScore,
            chosen.Score,
            scored,
            $"moved the title off the approved box: it measures {approvedScore:F3} against "
            + $"{chosen.Score:F3} at {chosen.LeftMm:F1},{chosen.TopMm:F1} mm - "
            + $"{1 - (chosen.Score / approvedScore):P0} calmer, chosen from {scored} candidates.");
    }

    /// <summary>
    /// What one rectangle costs: the mean occupancy under it, plus half the mean in the band around
    /// it. Lower is better, and nothing here is a probability — the only claims made of it are
    /// "smaller is calmer" and "the same bytes give the same number".
    /// </summary>
    private static double Measure(
        BekiActivityMap map, double leftMm, double topMm, double widthMm, double heightMm, int band)
    {
        var left = OnMap(leftMm, BekiCoverDieline.CanvasWidthMm, map.Width);
        var top = OnMap(topMm, BekiCoverDieline.CanvasHeightMm, map.Height);
        var width = Math.Max(1, OnMap(widthMm, BekiCoverDieline.CanvasWidthMm, map.Width));
        var height = Math.Max(1, OnMap(heightMm, BekiCoverDieline.CanvasHeightMm, map.Height));

        return map.RectangleMean(left, top, width, height)
               + (MarginBandWeight * map.BandMean(left, top, width, height, band));
    }

    /// <summary>One wrap millimetre span, in map pixels — the map is the whole wrap, downscaled.</summary>
    private static int OnMap(double valueMm, float spanMm, int pixels) =>
        (int)Math.Round(valueMm / spanMm * pixels, MidpointRounding.ToEven);

    /// <summary>
    /// Whether a box overlaps the logo's visible rectangle grown by its clear space — the one place
    /// on the front board something else is already promised.
    /// </summary>
    public static bool TouchesLogo(double leftMm, double topMm, double widthMm, double heightMm)
    {
        var clearance = BekiCoverDieline.LogoClearSpaceMm;
        var logoLeft = BekiCoverDieline.LogoLeftMm - clearance;
        var logoTop = BekiCoverDieline.LogoTopMm - clearance;
        var logoRight = BekiCoverDieline.LogoRightMm + clearance;
        var logoBottom = BekiCoverDieline.LogoTopMm + BekiCoverDieline.LogoHeightMm + clearance;

        return leftMm < logoRight && leftMm + widthMm > logoLeft
               && topMm < logoBottom && topMm + heightMm > logoTop;
    }

    /// <summary>Slack on the grid's own end point, so a step that lands on the bound is kept.</summary>
    private const double Tolerance = 1e-6;

    /// <summary>One rectangle that was measured.</summary>
    private readonly record struct Candidate(
        double LeftMm, double TopMm, double Score, double Distance);
}
