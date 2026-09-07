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
/// **How it reads a page.** <see cref="BekiActivityMap"/> — two measurements of one downscale, mixed
/// half and half: how detailed a place is, and how far its colour sits from the page's own wash.
/// Neither is enough alone — a knitted sweater has no gradient in it and a white dog on a white
/// cloud has no edge, while a pastel sky is nothing but soft texture. The mixture is then spread
/// over the width a drawn figure occupies
/// (<see cref="BekiActivityMap.SubjectSpreadFraction"/>), which is what turns an edge detector into
/// a map of where somebody is standing, and re-zeroed on the picture's own quietest ground so that
/// one threshold works on a cloudscape and a bedroom alike. The cover title's own placement
/// (<see cref="BekiCoverTitlePlacement"/>) reads a picture with the same code, for the same reason.
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

        var map = BekiActivityMap.Measure(canvas);

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
                ScoreAt(map, sprite, placement)
                + (departureCost * Math.Max(0, distance - DepartureFreeRadius))));
        }
    }

    /// <summary>
    /// What one placement costs: the occupancy Beki's silhouette actually covers, plus half the
    /// occupancy in the band around her.
    ///
    /// Lower is better, and nothing here is a probability — it is a comparable number, and the
    /// only claims made of it are "smaller is calmer" and "the same bytes give the same number".
    /// </summary>
    private static double ScoreAt(
        BekiActivityMap map, SpriteFootprint sprite, SpritePlacement placement)
    {
        var (left, top) = map.Place(
            placement.X, placement.Y, sprite.MaskWidth, sprite.MaskHeight);

        return map.MaskedMean(
                   left, top, sprite.MaskWidth, sprite.MaskHeight, sprite.Alpha, sprite.AlphaTotal)
               + (MarginBandWeight * map.BandMean(
                   left, top, sprite.MaskWidth, sprite.MaskHeight, map.Band(MarginBandFraction)));
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
            BekiActivityMap map)
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
}
