using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AdventurePacks.Api.Services.Story.Composite.Poses;

/// <summary>
/// Which anchor one spread's Beki was placed at, and the arithmetic that chose it.
///
/// Kept as a record rather than folded into the composition manifest because the manifest's schema
/// is the supplier's (<c>contracts/composition_manifest_v1.schema.json</c>) and it already records
/// the anchor that was USED. This says how that anchor was arrived at, which is our question and
/// belongs in our own paperwork — the spread's QA document and the log line beside it.
/// </summary>
/// <param name="Anchor">The anchor to composite at: the configured default, or a calmer one.</param>
/// <param name="DefaultAnchor">
/// The configured default this decision started from, carried so that a stored record says what was
/// departed from as well as what was chosen. A reader a year from now has no way to reconstruct it:
/// the config's anchors are data and data changes.
/// </param>
/// <param name="Moved">Whether <paramref name="Anchor"/> differs from the configured default.</param>
/// <param name="DefaultScore">What the configured default's footprint measured.</param>
/// <param name="ChosenScore">What <paramref name="Anchor"/>'s footprint measured — the same number
/// as <paramref name="DefaultScore"/> when nothing was calm enough to be worth moving to.</param>
/// <param name="CandidatesEvaluated">
/// How many placements were legal on this canvas and were scored, the default included. One means
/// the window had nowhere else to offer, which is a different sentence from "nothing was calmer".
/// </param>
/// <param name="Reason">One sentence, for the log line and the stored record.</param>
public sealed record BekiPlacementChoice(
    BekiCompositeAnchor Anchor,
    BekiCompositeAnchor DefaultAnchor,
    bool Moved,
    double DefaultScore,
    double ChosenScore,
    int CandidatesEvaluated,
    string Reason)
{
    /// <summary>The algorithm that produced this choice, so a stored record says what read it.</summary>
    public string Version => BekiPlacementChooser.Version;
}

/// <summary>
/// Picks the calmest place on a generated spread for the approved Beki to stand.
///
/// **The observed defect.** Book 54cba4b3 (prompt v1.7) composites every story spread at the
/// configured default for its text side — LEFT text puts her at x 0.594, RIGHT text at 0.406, both
/// at y 0.458 and height 0.333. Those three numbers were proven against one approved printed proof
/// and they are correct for that page; they are not a statement about where the child is standing
/// in the next eight pictures. On spreads 3, 5, 7 and 8 of that book the model drew the child
/// exactly there, so the approved character was pasted against the child's face and shoulder. The
/// image prompt asks the model to keep that area calm (v1.7 <c>BekiReserveBlock</c>) and the model
/// often ignores it, and nothing downstream measured whether it had.
///
/// **What may be done about it.** §14 of the supplier handoff is explicit: "a failed placement
/// should first adjust deterministic anchors, not redraw Beki." So this class returns three
/// numbers. There is no mirror, no rotation, no recolour and no redraw here for the same reason
/// there is none in <see cref="BekiCompositeEngine"/>: the pipeline's entire claim is that Beki is
/// never generated. It also never asks a model anything — this is ImageSharp arithmetic on bytes
/// that are already paid for, and it costs a few milliseconds per spread rather than a vision call
/// (owner's rule 5, 2026-09-01: "we don't need additional reviews for images").
///
/// **How it reads a page.** Two measurements of one downscale, mixed half and half: how detailed a
/// place is (<see cref="ActivityMap.ContourStrength"/>) and how far its colour sits from the page's
/// own wash (<see cref="ActivityMap.ColourDistinctness"/>). Neither is enough alone — a knitted
/// sweater has no gradient in it and a white dog on a white cloud has no edge, while a pastel sky is
/// nothing but soft texture. The mixture is then spread over the width a drawn figure occupies
/// (<see cref="SubjectSpreadFraction"/>), which is what turns an edge detector into a map of where
/// somebody is standing, and re-zeroed on the picture's own quietest ground so that one threshold
/// works on a cloudscape and a bedroom alike.
///
/// **The window.** The anchor may only move where the deterministic checks would already have
/// allowed it: fully inside the canvas, and never one pixel inside the third the Georgian text is
/// printed over (<see cref="CompositeMinimalQa.CompositeProblems"/>). Two further bounds are this
/// class's own. She never comes CLOSER to the centre fold than the configured default already sits,
/// because the default's inner clearance is the number the approved proof was signed off with and
/// tightening it is a print decision nobody made here; and she stays
/// <see cref="OuterMargin"/>/<see cref="VerticalMargin"/> off the outer edges, which is where the
/// bleed and the trim live.
///
/// **Why the default is sticky.** Moving her is only worth it when the alternative is materially
/// calmer — see <see cref="CalmerRatio"/> and <see cref="CalmerMargin"/>. Two things fall out of
/// that, and both are deliberate. A picture whose reserved area the model DID keep calm keeps the
/// approved anchor, so the pages that were already right do not change. And a picture that is busy
/// everywhere keeps it too: there is no calm place to move to, and shuffling her around a crowded
/// spread would trade a known composition for an unknown one.
/// </summary>
public static class BekiPlacementChooser
{
    /// <summary>The version this algorithm is recorded under in a spread's QA document.</summary>
    public const string Version = "beki-placement-v1";

    /// <summary>
    /// The width the activity map is measured at, whatever the base arrived as.
    ///
    /// Fixed rather than proportional so that the same picture scores the same on a 1536-wide
    /// proof and a 5315-wide press raster: the Sobel kernel is three pixels across, and three
    /// pixels is a different physical distance on each. Downscaling first also does the noise
    /// suppression a blur would otherwise have to, for free.
    /// </summary>
    public const int ActivityMapWidth = 512;

    /// <summary>
    /// The one alternative size Beki may be drawn at — the same 10% reduction the placement retry
    /// uses (<see cref="BekiCompositeAnchor.RecompositeHeightScale"/>), rounded to the two figures
    /// the config's anchors are written in.
    ///
    /// Offered because the two knobs interact: a slightly smaller Beki fits between things a
    /// full-height one cannot, and the calm gap beside a child is often exactly that shape. Only
    /// ever offered — a reduced height wins only when it is materially calmer, and a tie goes to
    /// the full size.
    /// </summary>
    public const double ReducedHeight = 0.30;

    /// <summary>How far apart the horizontal candidates sit, as a fraction of the canvas width.</summary>
    public const double CentreStepX = 0.04;

    /// <summary>And the vertical ones, as a fraction of the height.</summary>
    public const double CentreStepY = 0.05;

    /// <summary>
    /// The least clearance kept between Beki's inner edge and the centre fold, when the configured
    /// default itself keeps less than this. The approved proof puts her left edge 82 pixels from
    /// the centre line of an 1836-wide sheet, which is about 4.5%; a pose wide enough to sit almost
    /// on the fold at the configured anchor is not an invitation to move a second one there.
    /// </summary>
    public const double MinimumFoldClearance = 0.03;

    /// <summary>How far Beki stays off the left and right canvas edges.</summary>
    public const double OuterMargin = 0.05;

    /// <summary>And off the top and bottom, where the spread is trimmed.</summary>
    public const double VerticalMargin = 0.08;

    /// <summary>
    /// How wide the band read around the sprite is, as a fraction of the canvas width.
    ///
    /// The footprint alone is not enough. A gap exactly Beki's size between two busy objects scores
    /// beautifully and looks wedged; the band is what tells those apart from an open sky.
    /// </summary>
    public const double MarginBandFraction = 0.03;

    /// <summary>
    /// What the band counts for beside the footprint. Half: the band is context, and a candidate
    /// should never be rejected for what is happening a hand's width away from it.
    /// </summary>
    public const double MarginBandWeight = 0.5;

    /// <summary>
    /// Which quantile of a reading is treated as its full scale. See
    /// <see cref="ActivityMap.NormaliseByPercentile"/>.
    /// </summary>
    public const float EdgePercentile = 0.95f;

    /// <summary>
    /// How far a subject's cost spreads, as a fraction of the canvas width.
    ///
    /// This is the single number that makes the difference between an algorithm that avoids the
    /// child and one that walks straight onto her. A drawn character is an OUTLINE around a FLAT
    /// interior: dark hair against pale sky, a rim-lit shoulder, eyes — and then a knitted sweater
    /// that a gradient operator finds calmer than a cloud. Scored on raw gradient alone, the
    /// quietest rectangle in every one of the eight real bases of book 54cba4b3 is the child's own
    /// chest, which is the exact defect this class was written to remove.
    ///
    /// So the reading is spread before it is read. Each pixel's cost becomes the mean over a box
    /// this wide around it, which fills a subject in from its own contour and makes standing just
    /// beside somebody nearly as expensive as standing on them. Six per cent of the width is about
    /// a third of Beki's own height on a story spread — wide enough to close a child, narrow enough
    /// that an open sky two figures away is still open sky.
    /// </summary>
    public const double SubjectSpreadFraction = 0.06;

    /// <summary>
    /// Which part of the spread map counts as "as quiet as this picture gets" — the ground every
    /// score is measured from. See <see cref="ActivityMap.AboveTheQuietest"/>.
    /// </summary>
    public const float QuietFloorPercentile = 0.05f;

    /// <summary>
    /// How the two readings of the base are mixed: how detailed a place is, and how far its colour
    /// is from the page's own wash. Half each — see
    /// <see cref="ActivityMap.ColourDistinctness"/> for what each half catches alone.
    /// </summary>
    public const double ColourDistinctnessWeight = 0.5;

    /// <summary>
    /// How much the combined reading favours the loud over the merely present, before it is spread.
    ///
    /// Raising a value in [0, 1] to this power leaves 1 where it is and pushes everything below it
    /// down — a strong contour keeps its weight, a soft cloud shoulder loses most of hers. It is
    /// what stops a pastel sky, which is nothing but gentle texture, from out-scoring the child
    /// standing in front of it once both have been averaged over a box. One and a half rather than
    /// two: measured on the eight real bases, a square emphasis leaves the map so sparse that the
    /// interior of a figure reads as empty again.
    /// </summary>
    public const double SubjectEmphasis = 1.5;

    /// <summary>
    /// What moving costs, per unit of distance from the configured anchor.
    ///
    /// The default is not an arbitrary starting point — it is the composition a printed proof was
    /// signed off from, and the spread prompt asks the model to compose around it. So a candidate
    /// does not merely have to be calmer, it has to be calmer by enough to be worth the walk: past
    /// <see cref="DepartureFreeRadius"/>, crossing to the opposite corner of the window costs about
    /// a quarter of the range a real base's scores span. Without it the calmest pixel in the picture
    /// wins outright, and Beki ends up alone in whichever corner the model left empty — measured, on
    /// the eight real bases, as a jump to the extreme edge on four spreads out of eight.
    ///
    /// It is the FULL price and not the price every page pays: see <see cref="CrowdedAnchorScore"/>,
    /// which discounts it on a page whose configured anchor the model painted the child through.
    /// </summary>
    public const double DepartureCost = 0.60;

    /// <summary>
    /// How far she may step before <see cref="DepartureCost"/> starts charging.
    ///
    /// A step this size is a nudge within the same composition — the reserved area moved a little,
    /// which is what §14's "adjust deterministic anchors" describes. Beyond it she is being relocated,
    /// and relocation should have to justify itself.
    /// </summary>
    public const double DepartureFreeRadius = 0.12;

    /// <summary>
    /// The configured anchor's own cost at which the walk away from it becomes free of charge —
    /// or rather, charged at <see cref="MinimumDepartureShare"/> instead of in full.
    ///
    /// <see cref="DepartureCost"/> exists to defend a composition somebody approved. That defence
    /// is worth having when the model actually left the reserved area alone, and worth much less
    /// when it painted the child straight through it: there is then no approved composition left to
    /// protect, only the memory of where one used to be. So the toll is charged in proportion to
    /// how much of the default is still worth defending.
    ///
    /// Read literally it is the score at which the toll would reach zero, and because
    /// <see cref="MinimumDepartureShare"/> stops the taper first, what it actually sets is the
    /// KNEE — the toll falls from full price at a score of 0 to its floor at
    /// <c>CrowdedAnchorScore × (1 − MinimumDepartureShare)</c>, which at these two values is 0.12,
    /// and every page at least that crowded pays the same. On the eight real bases of book
    /// 54cba4b3 that is every page but spread 2, whose configured anchor measures 0.097 and which
    /// pays about two thirds. The taper is therefore gentle where it exists and mostly a flat
    /// discount, which is the honest description of it.
    ///
    /// It buys one page and one page only, and that is worth saying plainly. Spread 8 of that book
    /// is an interior whose child fills the entire half Beki is allowed to stand in; at full toll
    /// the calmest thing she can afford is the child's own hair, and at the tapered toll she can
    /// afford the bedroom wall above the nightstand, which is where a person would have put her.
    /// </summary>
    public const double CrowdedAnchorScore = 0.30;

    /// <summary>
    /// The least <see cref="DepartureCost"/> is ever charged at, as a share of itself.
    ///
    /// A floor rather than a taper to nothing, and this is the whole reason it exists: with no toll
    /// the calmest pixel in the picture wins outright and Beki ends up alone in whichever corner the
    /// model left empty — measured, on the eight real bases, as a jump to the extreme edge on four
    /// spreads out of eight.
    ///
    /// Three fifths is where those eight bases stop agreeing with each other, and the number is a
    /// measured compromise rather than a principle. Below about 0.55 the toll is cheap enough that
    /// spread 4 crosses its whole window to the aeroplane's tail; at 0.62 and above spread 8 can no
    /// longer reach the bedroom wall and settles back onto the child's sweater. 0.60 buys the wall
    /// on spread 8 and takes the tail on spread 4 — a placement that is on a solid object and clear
    /// of the child, but is the outermost the window allows, and is the one thing here a reviewer
    /// should look at rather than take on the arithmetic's word.
    /// </summary>
    public const double MinimumDepartureShare = 0.60;

    /// <summary>
    /// How much calmer the best candidate must be before Beki moves at all: no more than this
    /// fraction of the default's score.
    /// </summary>
    public const double CalmerRatio = 0.85;

    /// <summary>
    /// And by how much in absolute terms, so that two nearly-silent places do not trade an approved
    /// anchor for a rounding difference.
    /// </summary>
    public const double CalmerMargin = 0.03;

    /// <summary>
    /// Candidates scoring within this much of the best are treated as equally calm, and the one
    /// nearest the configured default wins. The default is the composition somebody approved; when
    /// the arithmetic cannot tell two places apart, the smaller departure is the better answer.
    /// </summary>
    public const double NearBestTolerance = 1.05;

    /// <summary>
    /// Chooses an anchor for one spread.
    /// </summary>
    /// <param name="basePng">The generated child/world base, before Beki is pasted onto it.</param>
    /// <param name="posePng">
    /// The approved pose bytes, hash-verified by the registry before they get here. Its alpha is
    /// read twice: for the visible bounding box the engine sizes from, and as the weight mask the
    /// footprint score is taken under — a pose is not a rectangle, and scoring her bounding box
    /// would judge her by scenery she does not cover.
    /// </param>
    /// <param name="textSide">Which third the Georgian occupies; Beki takes the other half.</param>
    /// <param name="defaultAnchor">
    /// The configured default for that side. It is the starting point, the thing every candidate is
    /// measured against, and the answer whenever nothing is materially better.
    /// </param>
    public static BekiPlacementChoice Choose(
        byte[] basePng,
        byte[] posePng,
        BekiTextSide textSide,
        BekiCompositeAnchor defaultAnchor)
    {
        ArgumentNullException.ThrowIfNull(basePng);
        ArgumentNullException.ThrowIfNull(posePng);
        ArgumentNullException.ThrowIfNull(defaultAnchor);
        defaultAnchor.Validate();

        using var canvas = Image.Load<Rgba32>(basePng);
        var canvasWidth = canvas.Width;
        var canvasHeight = canvas.Height;

        using var pose = Image.Load<Rgba32>(posePng);
        var alphaBox = BekiCompositeEngine.VisibleAlphaBounds(pose)
            ?? throw new InvalidOperationException(
                "The approved Beki pose has no visible alpha content, so there is no footprint to "
                + "place.");

        var map = ActivityMap.Measure(canvas);

        // Every candidate at one height shares a sprite, so the resize and the alpha read happen
        // once per height rather than once per candidate — two resizes instead of three hundred.
        var sizes = CandidateHeights(defaultAnchor.VisibleHeight)
            .Select(height => SpriteFootprint.For(pose, alphaBox, height, canvasWidth, canvasHeight, map))
            .ToList();

        var defaultSize = sizes[0];
        var foldClearance = FoldClearance(textSide, defaultAnchor, defaultSize.HalfWidth);

        var candidates = new List<Candidate>();
        var seen = new HashSet<(int X, int Y, int Height)>();

        // Read once the default has been scored, because it is the default's own score that sets
        // it. Until then the toll is the full one, which is what the default itself pays: nothing,
        // since it has not walked anywhere.
        var departureCost = DepartureCost;

        // The default first, and on its own: it is what a kept decision returns, and every other
        // candidate is judged against its score.
        AddCandidate(defaultAnchor.VisibleCenterX, defaultAnchor.VisibleCenterY, defaultSize);

        // Its own window may refuse it — a config anchor that no longer fits the canvas it is being
        // applied to. Nothing here can fix that, and inventing a score for a placement the engine
        // is about to refuse would only bury the real error one stage further on.
        if (candidates.Count == 0)
        {
            return new BekiPlacementChoice(
                defaultAnchor,
                defaultAnchor,
                Moved: false,
                DefaultScore: 0,
                ChosenScore: 0,
                CandidatesEvaluated: 0,
                "kept the configured anchor: it does not itself fit this canvas, so nothing here is "
                + "in a position to choose a better one.");
        }

        // How much of the approved composition is still there to be defended. See
        // <see cref="CrowdedAnchorScore"/>.
        departureCost = DepartureCostFor(candidates[0].Score);

        foreach (var size in sizes)
        {
            Sweep(size);
        }

        var configured = candidates[0];
        var best = candidates.MinBy(candidate => candidate.Score)!;
        var scored = candidates.Count;

        var materiallyCalmer =
            best.Score <= CalmerRatio * configured.Score
            && configured.Score - best.Score >= CalmerMargin;

        if (!materiallyCalmer)
        {
            return new BekiPlacementChoice(
                defaultAnchor,
                defaultAnchor,
                Moved: false,
                configured.Score,
                configured.Score,
                scored,
                scored == 1
                    ? "kept the configured anchor: no other placement fits the canvas, the reserved "
                      + "text third and the fold clearance on this spread."
                    : $"kept the configured anchor: it measures {configured.Score:F3} and the "
                      + $"calmest of {scored} candidates measures {best.Score:F3}, which is not "
                      + $"materially calmer.");
        }

        // Everything as calm as the best, resolved towards the approved composition: nearest the
        // default first, then the largest height among those, then a fixed sweep so that two
        // equidistant candidates cannot depend on the order a loop happened to build them in.
        var chosen = candidates
            .Where(candidate => candidate.Score <= best.Score * NearBestTolerance)
            .OrderBy(candidate => candidate.DistanceFrom(defaultAnchor))
            .ThenByDescending(candidate => candidate.Sprite.Height)
            .ThenBy(candidate => candidate.CentreX)
            .ThenBy(candidate => candidate.CentreY)
            .First();

        return new BekiPlacementChoice(
            chosen.Anchor,
            defaultAnchor,
            Moved: true,
            configured.Score,
            chosen.Score,
            scored,
            $"moved Beki off the configured anchor: it measures {configured.Score:F3} against "
            + $"{chosen.Score:F3} at {chosen.CentreX:F3},{chosen.CentreY:F3} height "
            + $"{chosen.Sprite.Height:F3} — {1 - (chosen.Score / configured.Score):P0} calmer, "
            + $"chosen from {scored} candidates.");

        // Every legal centre on the grid, at one height.
        void Sweep(SpriteFootprint size)
        {
            var (lowestX, highestX) = HorizontalWindow(textSide, foldClearance, size.HalfWidth);
            var lowestY = VerticalMargin + size.HalfHeight;
            var highestY = 1 - VerticalMargin - size.HalfHeight;

            for (var x = lowestX; x <= highestX + Tolerance; x += CentreStepX)
            {
                for (var y = lowestY; y <= highestY + Tolerance; y += CentreStepY)
                {
                    AddCandidate(x, y, size);
                }
            }
        }

        void AddCandidate(double centreX, double centreY, SpriteFootprint sprite)
        {
            if (!(centreX > 0 && centreX < 1 && centreY > 0 && centreY < 1))
            {
                return;
            }

            var placement = sprite.PlacementOf(centreX, centreY, canvasWidth, canvasHeight);

            if (!placement.IsLegal(textSide, canvasWidth, canvasHeight, sprite))
            {
                return;
            }

            // Keyed on the PIXEL placement rather than on the fractions, because that is what
            // actually distinguishes two composites: the engine rounds twice on the way to a
            // corner, so two grid steps can land on one picture.
            if (!seen.Add((placement.X, placement.Y, sprite.RenderedHeight)))
            {
                return;
            }

            var distance = Math.Sqrt(
                Math.Pow(centreX - defaultAnchor.VisibleCenterX, 2)
                + Math.Pow(centreY - defaultAnchor.VisibleCenterY, 2));

            // The score the decision is made on: what the picture costs here, plus what the walk
            // from the approved anchor costs. The default's own walk is zero, so the number reported
            // as DefaultScore is the picture alone.
            candidates.Add(new Candidate(
                centreX,
                centreY,
                sprite,
                map.ScoreAt(sprite, placement)
                + (departureCost * Math.Max(0, distance - DepartureFreeRadius))));
        }
    }

    /// <summary>
    /// What one unit of walking costs on a page whose configured anchor measures
    /// <paramref name="configuredScore"/> — the full <see cref="DepartureCost"/> on a page the model
    /// composed as asked, tapering to <see cref="MinimumDepartureShare"/> of it once the configured
    /// anchor is as crowded as <see cref="CrowdedAnchorScore"/>.
    /// </summary>
    public static double DepartureCostFor(double configuredScore)
        => DepartureCost
           * Math.Clamp(
               (CrowdedAnchorScore - configuredScore) / CrowdedAnchorScore,
               MinimumDepartureShare,
               1.0);

    /// <summary>
    /// The heights offered, largest first. The configured one always, and the reduced one only when
    /// it genuinely is smaller — a config that ever set the default below it must not silently gain
    /// a candidate that draws Beki BIGGER than the approved proof.
    /// </summary>
    private static IEnumerable<double> CandidateHeights(double configuredHeight)
    {
        yield return configuredHeight;

        if (ReducedHeight < configuredHeight)
        {
            yield return ReducedHeight;
        }
    }

    /// <summary>
    /// How much room the configured anchor leaves between Beki's inner edge and the fold — the
    /// clearance every candidate must also keep.
    /// </summary>
    public static double FoldClearance(
        BekiTextSide textSide, BekiCompositeAnchor defaultAnchor, double defaultHalfWidth)
        => Math.Max(
            MinimumFoldClearance,
            textSide == BekiTextSide.Left
                ? defaultAnchor.VisibleCenterX - defaultHalfWidth - 0.5
                : 0.5 - defaultAnchor.VisibleCenterX - defaultHalfWidth);

    /// <summary>
    /// The band of centres a sprite of this width may sit in: the half the text does not occupy,
    /// less the fold clearance on the inside and the bleed margin on the outside.
    /// </summary>
    private static (double Lowest, double Highest) HorizontalWindow(
        BekiTextSide textSide, double foldClearance, double halfWidth)
        => textSide == BekiTextSide.Left
            ? (0.5 + foldClearance + halfWidth, 1 - OuterMargin - halfWidth)
            : (OuterMargin + halfWidth, 0.5 - foldClearance - halfWidth);

    /// <summary>Slack on the grid's own end point, so a step that lands on the bound is kept.</summary>
    private const double Tolerance = 1e-9;

    /// <summary>One placement that was measured.</summary>
    private sealed record Candidate(
        double CentreX, double CentreY, SpriteFootprint Sprite, double Score)
    {
        public BekiCompositeAnchor Anchor => new(CentreX, CentreY, Sprite.Height);

        /// <summary>
        /// How far this sits from the configured anchor, in canvas fractions. Used only to break
        /// ties between equally calm places, which is why the axes are not weighted: the question
        /// is "which of these is the smallest departure", and a spread is looked at whole.
        /// </summary>
        public double DistanceFrom(BekiCompositeAnchor anchor)
            => Math.Sqrt(
                Math.Pow(CentreX - anchor.VisibleCenterX, 2)
                + Math.Pow(CentreY - anchor.VisibleCenterY, 2));
    }

    /// <summary>Where the engine would actually paste a sprite for a given anchor.</summary>
    private readonly record struct SpritePlacement(int X, int Y)
    {
        /// <summary>
        /// The deterministic checks, read forwards. Exactly the two
        /// <see cref="BekiCompositeEngine.Composite"/> refuses on and the one
        /// <see cref="CompositeMinimalQa.CompositeProblems"/> refuses on — restated here rather
        /// than shared, because a candidate that is rejected is skipped and a composite that is
        /// wrong is an exception, and folding the two would make one of them lie.
        /// </summary>
        public bool IsLegal(
            BekiTextSide textSide, int canvasWidth, int canvasHeight, SpriteFootprint sprite)
        {
            if (X < 0 || Y < 0
                || X + sprite.RenderedWidth > canvasWidth
                || Y + sprite.RenderedHeight > canvasHeight)
            {
                return false;
            }

            var third = canvasWidth / 3.0;

            return textSide == BekiTextSide.Left
                ? X >= third
                : X + sprite.RenderedWidth <= canvasWidth - third;
        }
    }

    /// <summary>
    /// One candidate height, resolved to the pixels the engine would draw and to the alpha mask
    /// those pixels cover.
    /// </summary>
    /// <param name="Alpha">
    /// The pose's alpha at ACTIVITY-MAP scale, row-major. The footprint score is the activity
    /// average weighted by this, so scenery Beki's silhouette does not actually cover — the gap
    /// under an outstretched arm — is not counted against the placement.
    /// </param>
    private sealed record SpriteFootprint(
        double Height,
        int RenderedWidth,
        int RenderedHeight,
        int MaskWidth,
        int MaskHeight,
        float[] Alpha,
        double AlphaTotal)
    {
        public double HalfWidth { get; init; }

        public double HalfHeight { get; init; }

        /// <summary>
        /// The engine's own sizing and centring arithmetic, repeated exactly — including its
        /// half-to-even rounding and its order (the centre first, the corner from it). A chooser
        /// that rounded differently would score one rectangle and the book would print another.
        /// </summary>
        public static SpriteFootprint For(
            Image<Rgba32> pose,
            Rectangle alphaBox,
            double height,
            int canvasWidth,
            int canvasHeight,
            ActivityMap map)
        {
            var renderedHeight = BekiCompositeEngine.RoundHalfToEven(canvasHeight * height);
            var renderedWidth = BekiCompositeEngine.RoundHalfToEven(
                (double)alphaBox.Width * renderedHeight / alphaBox.Height);

            var maskWidth = Math.Clamp(
                (int)Math.Round(renderedWidth * map.Scale, MidpointRounding.ToEven), 1, map.Width);
            var maskHeight = Math.Clamp(
                (int)Math.Round(renderedHeight * map.Scale, MidpointRounding.ToEven), 1, map.Height);

            using var mask = pose.Clone(ctx => ctx
                .Crop(alphaBox)
                .Resize(new ResizeOptions
                {
                    Size = new Size(maskWidth, maskHeight),
                    Mode = ResizeMode.Stretch,
                    Sampler = KnownResamplers.Box,
                    Compand = false,
                }));

            var alpha = new float[maskWidth * maskHeight];
            var total = 0.0;

            mask.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        var weight = row[x].A / 255f;
                        alpha[(y * maskWidth) + x] = weight;
                        total += weight;
                    }
                }
            });

            return new SpriteFootprint(
                height, renderedWidth, renderedHeight, maskWidth, maskHeight, alpha, total)
            {
                HalfWidth = renderedWidth / 2.0 / canvasWidth,
                HalfHeight = renderedHeight / 2.0 / canvasHeight,
            };
        }

        public SpritePlacement PlacementOf(
            double centreX, double centreY, int canvasWidth, int canvasHeight)
        {
            var pixelCentreX = BekiCompositeEngine.RoundHalfToEven(canvasWidth * centreX);
            var pixelCentreY = BekiCompositeEngine.RoundHalfToEven(canvasHeight * centreY);

            return new SpritePlacement(
                BekiCompositeEngine.RoundHalfToEven(pixelCentreX - (RenderedWidth / 2.0)),
                BekiCompositeEngine.RoundHalfToEven(pixelCentreY - (RenderedHeight / 2.0)));
        }
    }

    /// <summary>
    /// How occupied each part of the base is: the picture's own contours, spread over the distance
    /// a drawn figure is wide.
    ///
    /// It is read from ONE downscale of the base, which is the only expensive step here: the spread
    /// arrives at 1536 or 5315 pixels wide and is reduced to <see cref="ActivityMapWidth"/>, after
    /// which everything below is arithmetic over about a hundred thousand floats.
    /// </summary>
    private sealed class ActivityMap
    {
        private readonly float[] _activity;

        private ActivityMap(int width, int height, float[] activity, double scale)
        {
            Width = width;
            Height = height;
            _activity = activity;
            Scale = scale;
        }

        public int Width { get; }

        public int Height { get; }

        /// <summary>Map pixels per canvas pixel — how a placement is carried into this map.</summary>
        public double Scale { get; }

        public static ActivityMap Measure(Image<Rgba32> canvas)
        {
            var width = Math.Min(ActivityMapWidth, canvas.Width);
            var height = Math.Max(
                1, (int)Math.Round((double)canvas.Height * width / canvas.Width, MidpointRounding.ToEven));

            using var small = canvas.Clone(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new Size(width, height),
                Mode = ResizeMode.Stretch,
                // Box, not Lanczos3: this is a measurement rather than a picture, and a windowed
                // sinc rings at every edge — which is precisely the quantity being measured.
                Sampler = KnownResamplers.Box,
                Compand = false,
            }));

            var luminance = new float[width * height];
            var red = new float[width * height];
            var green = new float[width * height];
            var blue = new float[width * height];

            small.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        var pixel = row[x];
                        var index = (y * width) + x;

                        luminance[index] =
                            ((0.2126f * pixel.R) + (0.7152f * pixel.G) + (0.0722f * pixel.B)) / 255f;
                        red[index] = pixel.R / 255f;
                        green[index] = pixel.G / 255f;
                        blue[index] = pixel.B / 255f;
                    }
                }
            });

            // Two readings of the same picture, because a drawn subject shows up in both and the
            // background of these books shows up in neither reliably. See ContourStrength and
            // ColourDistinctness for what each catches and what each misses on its own.
            var contours = ContourStrength(luminance, width, height);
            var distinct = ColourDistinctness(red, green, blue);

            var raw = new float[width * height];
            for (var index = 0; index < raw.Length; index++)
            {
                raw[index] = MathF.Pow(
                    ((1 - (float)ColourDistinctnessWeight) * contours[index])
                    + ((float)ColourDistinctnessWeight * distinct[index]),
                    (float)SubjectEmphasis);
            }

            var reach = Math.Max(1, (int)Math.Round(
                SubjectSpreadFraction * width, MidpointRounding.ToEven));

            return new ActivityMap(
                width,
                height,
                AboveTheQuietest(Spread(raw, width, height, reach)),
                (double)width / canvas.Width);
        }

        /// <summary>
        /// How far each pixel's colour sits from the average colour of the whole picture, normalised
        /// the same way the contours are.
        ///
        /// The contour reading alone loses the two things it most needs to find. A child's clothing
        /// is a large flat area with no gradient in it at all, and a white dog against a white cloud
        /// has no luminance edge to detect — while a pastel cloudscape is nothing BUT soft texture,
        /// so an edge detector ranks the sky above the subject standing in it. Colour rescues both:
        /// these pages are a warm cream wash with one child painted in blue, cream and dark brown on
        /// top of it, and the child is by some distance the least average-coloured thing in frame.
        ///
        /// It is the average colour rather than a trained model on purpose. There is no per-book
        /// tuning here and nothing to drift: the reading is a property of the picture in hand, and a
        /// picture with no dominant wash — a busy interior — simply returns a flat map and lets the
        /// contour half decide.
        /// </summary>
        private static float[] ColourDistinctness(float[] red, float[] green, float[] blue)
        {
            var meanRed = 0.0;
            var meanGreen = 0.0;
            var meanBlue = 0.0;

            for (var index = 0; index < red.Length; index++)
            {
                meanRed += red[index];
                meanGreen += green[index];
                meanBlue += blue[index];
            }

            meanRed /= red.Length;
            meanGreen /= red.Length;
            meanBlue /= red.Length;

            var distance = new float[red.Length];

            for (var index = 0; index < red.Length; index++)
            {
                var dr = red[index] - meanRed;
                var dg = green[index] - meanGreen;
                var db = blue[index] - meanBlue;

                distance[index] = (float)Math.Sqrt((dr * dr) + (dg * dg) + (db * db));
            }

            return NormaliseByPercentile(distance);
        }

        /// <summary>
        /// Re-zeroes the map on its own quietest ground: every pixel less its
        /// <see cref="QuietFloorPercentile"/>, clamped at zero.
        ///
        /// The question a candidate is asked is not "how busy is this in the abstract" but "how much
        /// busier is this than the calmest place this particular picture has to offer". Without the
        /// shift, a pastel cloudscape whose every square inch carries some soft texture scores 0.5
        /// nearly everywhere, and the ratio test — is the best candidate 15% calmer than the default
        /// — can never fire, because the constant floor is most of both numbers.
        ///
        /// A shift rather than a rescale, deliberately. Rescaling to the picture's own range would
        /// manufacture a decision out of a picture that is uniformly busy: divide a flat map by its
        /// own vanishing spread and the loudest grain in it becomes a reason to move the approved
        /// anchor. Subtracting leaves such a picture near zero everywhere, where
        /// <see cref="CalmerMargin"/> refuses to act on it — which is the honest answer.
        /// </summary>
        private static float[] AboveTheQuietest(float[] spread)
        {
            var sorted = (float[])spread.Clone();
            Array.Sort(sorted);
            var floor = sorted[(int)(QuietFloorPercentile * (sorted.Length - 1))];

            for (var index = 0; index < spread.Length; index++)
            {
                spread[index] = Math.Max(0f, spread[index] - floor);
            }

            return spread;
        }

        /// <summary>
        /// Each pixel replaced by the mean over a square of side <c>2 × reach + 1</c> around it —
        /// a box blur, computed once from a summed-area table so the radius costs nothing.
        ///
        /// This is the step that turns an edge detector into an occupancy map. See
        /// <see cref="SubjectSpreadFraction"/> for why it has to exist at all; the mechanism is only
        /// that a figure's own outline is what fills the figure in.
        /// </summary>
        private static float[] Spread(float[] source, int width, int height, int reach)
        {
            // One row and column of zeros in front, so every window is four lookups with no
            // special case at the edges.
            var stride = width + 1;
            var sums = new double[stride * (height + 1)];

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    sums[((y + 1) * stride) + x + 1] =
                        source[(y * width) + x]
                        + sums[(y * stride) + x + 1]
                        + sums[((y + 1) * stride) + x]
                        - sums[(y * stride) + x];
                }
            }

            var spread = new float[source.Length];

            for (var y = 0; y < height; y++)
            {
                var top = Math.Max(0, y - reach);
                var bottom = Math.Min(height, y + reach + 1);

                for (var x = 0; x < width; x++)
                {
                    var left = Math.Max(0, x - reach);
                    var right = Math.Min(width, x + reach + 1);

                    var total = sums[(bottom * stride) + right]
                                - sums[(top * stride) + right]
                                - sums[(bottom * stride) + left]
                                + sums[(top * stride) + left];

                    spread[(y * width) + x] = (float)(total / ((bottom - top) * (right - left)));
                }
            }

            return spread;
        }

        /// <summary>
        /// Sobel gradient magnitude on the luminance, normalised the same way the colour reading is.
        ///
        /// This is the "how detailed is it here" half: foliage, architecture, glowing stars, a
        /// child's face. What it cannot see is a flat interior or a white shape on a white ground,
        /// which is what <see cref="ColourDistinctness"/> is for.
        /// </summary>
        private static float[] ContourStrength(float[] luminance, int width, int height)
        {
            var magnitude = new float[width * height];

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var left = Math.Max(0, x - 1);
                    var right = Math.Min(width - 1, x + 1);
                    var above = Math.Max(0, y - 1) * width;
                    var below = Math.Min(height - 1, y + 1) * width;
                    var middle = y * width;

                    var topLeft = luminance[above + left];
                    var top = luminance[above + x];
                    var topRight = luminance[above + right];
                    var midLeft = luminance[middle + left];
                    var midRight = luminance[middle + right];
                    var bottomLeft = luminance[below + left];
                    var bottom = luminance[below + x];
                    var bottomRight = luminance[below + right];

                    var gx = (topRight + (2 * midRight) + bottomRight)
                             - (topLeft + (2 * midLeft) + bottomLeft);
                    var gy = (bottomLeft + (2 * bottom) + bottomRight)
                             - (topLeft + (2 * top) + topRight);

                    magnitude[middle + x] = MathF.Sqrt((gx * gx) + (gy * gy));
                }
            }

            return NormaliseByPercentile(magnitude);
        }

        /// <summary>
        /// Divides a reading by its own <see cref="EdgePercentile"/> and clamps it to [0, 1].
        ///
        /// A percentile rather than the maximum, so that one specular highlight or one dark eyelash
        /// cannot rate everything else in a pastel picture at nearly zero; and a clamp afterwards,
        /// so that the busiest five per cent cannot dominate a mean either. A reading with no
        /// variation at all — a solid fill — comes back as zeros, which is the honest answer and the
        /// one that keeps the configured anchor.
        /// </summary>
        private static float[] NormaliseByPercentile(float[] values)
        {
            var sorted = (float[])values.Clone();
            Array.Sort(sorted);
            var percentile = sorted[(int)(EdgePercentile * (sorted.Length - 1))];

            if (percentile <= 0)
            {
                return new float[values.Length];
            }

            for (var index = 0; index < values.Length; index++)
            {
                values[index] = Math.Clamp(values[index] / percentile, 0f, 1f);
            }

            return values;
        }

        /// <summary>
        /// What one placement costs: the occupancy Beki's silhouette actually covers, plus half the
        /// occupancy in the band around her.
        ///
        /// Lower is better, and nothing here is a probability — it is a comparable number, and the
        /// only claims made of it are "smaller is calmer" and "the same bytes give the same number".
        /// </summary>
        public double ScoreAt(SpriteFootprint sprite, SpritePlacement placement)
        {
            var left = Math.Clamp(
                (int)Math.Round(placement.X * Scale, MidpointRounding.ToEven),
                0,
                Math.Max(0, Width - sprite.MaskWidth));
            var top = Math.Clamp(
                (int)Math.Round(placement.Y * Scale, MidpointRounding.ToEven),
                0,
                Math.Max(0, Height - sprite.MaskHeight));

            var footprint = 0.0;

            for (var y = 0; y < sprite.MaskHeight; y++)
            {
                var mapRow = Math.Min(Height - 1, top + y) * Width;
                var maskRow = y * sprite.MaskWidth;

                for (var x = 0; x < sprite.MaskWidth; x++)
                {
                    var weight = sprite.Alpha[maskRow + x];
                    if (weight <= 0)
                    {
                        continue;
                    }

                    footprint += weight * _activity[mapRow + Math.Min(Width - 1, left + x)];
                }
            }

            var covered = sprite.AlphaTotal > 0 ? sprite.AlphaTotal : 1;

            return (footprint / covered) + (MarginBandWeight * BandMean(sprite, left, top));
        }

        /// <summary>
        /// The mean activity in the ring around the sprite's bounding box — the rectangle grown by
        /// <see cref="MarginBandFraction"/> of the canvas width, less the box itself.
        /// </summary>
        private double BandMean(SpriteFootprint sprite, int left, int top)
        {
            var band = Math.Max(1, (int)Math.Round(MarginBandFraction * Width, MidpointRounding.ToEven));

            var outerLeft = Math.Max(0, left - band);
            var outerTop = Math.Max(0, top - band);
            var outerRight = Math.Min(Width, left + sprite.MaskWidth + band);
            var outerBottom = Math.Min(Height, top + sprite.MaskHeight + band);

            var total = 0.0;
            var counted = 0;

            for (var y = outerTop; y < outerBottom; y++)
            {
                var inside = y >= top && y < top + sprite.MaskHeight;
                var row = y * Width;

                for (var x = outerLeft; x < outerRight; x++)
                {
                    if (inside && x >= left && x < left + sprite.MaskWidth)
                    {
                        continue;
                    }

                    total += _activity[row + x];
                    counted++;
                }
            }

            return counted == 0 ? 0 : total / counted;
        }
    }
}
