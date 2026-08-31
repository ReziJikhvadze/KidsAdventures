using System.Text.Json;
using AdventurePacks.Api.Infrastructure;
using AdventurePacks.Api.Services.Story.Composite.Poses;
using Json.Schema;
using SixLabors.ImageSharp;

namespace AdventurePacks.Api.Services.Story.Composite;

/// <summary>
/// The checks a machine can make about a finished page, made before a model is asked anything.
///
/// The order is the handoff's (§6 Step 7) and it is an economic argument as much as a correctness
/// one: a reviewer call costs money and seconds, and every fault below is one it could only guess
/// at from pixels. A pose file that is not the approved one, a Beki whose box leaves the canvas, an
/// image that will not decode — all of them are already knowable, and asking a vision model to
/// infer a SHA-256 from a picture is the sort of thing that quietly passes.
/// </summary>
public static class CompositeDeterministicChecks
{
    /// <summary>The printed spread's shape. Everything is normalized to it before layout.</summary>
    public const double TargetAspect = 15.0 / 7.0;

    /// <summary>
    /// How much of a rendered image a centre-crop to 15:7 may throw away.
    ///
    /// The handoff permits "a tiny centered crop … to normalize to 15:7" and forbids stretching,
    /// which is a rule about the crop and not about the source shape — any image can be centre
    /// cropped to any ratio, so what has to be bounded is the amount discarded. Half, because the
    /// widest frame today's image providers actually offer is 3:2, and normalizing 3:2 to 15:7
    /// discards 30% of the height. A tighter bound would fail every image this pipeline can
    /// currently generate; a looser one would let a portrait render through and print a spread
    /// assembled from a third of a picture.
    /// </summary>
    public const double MaxNormalizationCropFraction = 0.5;

    /// <summary>
    /// Why a generated base image cannot be used, in the words a log needs. Empty means usable.
    ///
    /// A full decode, for the reason the photograph boundary is a full decode: a header is not a
    /// file. A truncated response keeps its header and reports the right dimensions, so a check
    /// that only read the header passed the bytes along — and the pipeline then failed several
    /// steps later, inside the normalization crop or the compositor, as an ImageSharp exception
    /// about a corrupt stream rather than as <see cref="CompositeFailureCodes.ImageGenerationFailed"/>
    /// naming the page. The whole point of a deterministic check is to be the place that says so.
    /// </summary>
    public static IReadOnlyList<string> BaseImageProblems(byte[]? png)
    {
        if (png is null || png.Length == 0)
        {
            return ["the image call returned no bytes."];
        }

        int width;
        int height;

        try
        {
            // Load, not Identify: the pixels are the question. A refusal, an error page and a
            // half-transferred picture all arrive as bytes with a plausible start.
            using var image = Image.Load(png);
            width = image.Width;
            height = image.Height;
        }
        catch (Exception ex)
        {
            // The type rather than the message: a decoder message can carry a file path.
            return [$"the generated image could not be decoded: {ex.GetType().Name}."];
        }

        if (width <= 0 || height <= 0)
        {
            return [$"the generated image decoded to {width}x{height}, which has no pixels."];
        }

        var crop = NormalizationCropFraction(width, height);
        if (crop > MaxNormalizationCropFraction)
        {
            return
            [
                $"normalizing {width}x{height} to 15:7 would discard "
                + $"{crop:P0} of one dimension, past the {MaxNormalizationCropFraction:P0} this "
                + "pipeline allows; the render is the wrong shape for a printed spread."
            ];
        }

        return [];
    }

    /// <summary>
    /// How far from 15:7 a normalized spread may land.
    ///
    /// A centred crop works in whole pixels, so an exact ratio is not reachable: 1536 wide gives a
    /// height of 717 and an aspect of 2.1423 against a target of 2.1429. One part in a thousand is
    /// comfortably wider than that rounding and far tighter than any real mistake — a base that was
    /// never cropped at all sits at 1.5.
    /// </summary>
    public const double SpreadAspectTolerance = 0.001;

    /// <summary>
    /// Why a normalized base is not the shape the book prints at. Empty means it is.
    ///
    /// Checked rather than assumed because everything downstream now depends on it: the composite
    /// engine places Beki as a fraction of this canvas, the manifest records those pixels as the
    /// page's geometry, and the reviewer judges this frame. If the canvas is not the printed sheet,
    /// all three are describing something nobody will ever see.
    /// </summary>
    public static IReadOnlyList<string> NormalizedSpreadProblems(byte[]? png)
    {
        var problems = BaseImageProblems(png);
        if (problems.Count > 0)
        {
            return problems;
        }

        // Decoded again rather than identified: BaseImageProblems has just proved these bytes
        // decode, so this cannot throw, and reading the size the same way both times is one less
        // thing that can disagree.
        using var image = Image.Load(png!);
        var aspect = (double)image.Width / image.Height;

        return Math.Abs(aspect - TargetAspect) <= SpreadAspectTolerance
            ? []
            :
            [
                $"the normalized base is {image.Width}x{image.Height} ({aspect:F4}), and the printed "
                + $"spread is {TargetAspect:F4}; Beki would be composited onto a canvas the book "
                + "does not print."
            ];
    }

    /// <summary>
    /// Why a normalized base does not read as one continuous painting across the fold. Empty means
    /// it does.
    ///
    /// This is the supplier's own acceptance test, run before a token is spent on review: compare
    /// the material either side of the centre and refuse a centre-aligned discontinuity affecting
    /// most of the image height. It exists because the shipped book it rejects actually shipped —
    /// every spread carried a milky veil over the text-side half ending in a razor edge at the
    /// fold, the interpolating repair rightly declined to touch a band that wide, and nothing else
    /// was looking. The reviewer cannot be the answer: its taxonomy judges content, and a page can
    /// be perfectly composed and still be two pictures joined at the middle.
    ///
    /// What this is NOT, since audit-2 P0-05, is the pipeline's gate. The gate acts on the
    /// ADVISORY pair and acts by buying a redraw — see <c>CompositeBookPipeline.DrawSpreadAsync</c>
    /// — so this pair of helpers is now the tier vocabulary alone: it classifies a reading the way
    /// the two thresholds were calibrated, which is how the logs stay comparable across the
    /// reversal, and it decides nothing.
    /// </summary>
    public static IReadOnlyList<string> CentreFieldProblems(byte[]? png)
    {
        var problems = BaseImageProblems(png);
        if (problems.Count > 0)
        {
            return problems;
        }

        var measured = CompositeSeamRepair.MeasureCentreField(png!);

        // The severe tier: a straight full-height boundary AND a sustained one-way level shift,
        // together — the overlay's own signature. One reading alone is what honest art does: a
        // trunk near centre, a calm side against a busy one. The first live v1.5 book is the
        // calibration, its clean page at 38.7%/49.3% and its sibling refused twice at 69% field
        // with the edge quiet.
        if (!measured.Severe)
        {
            return [];
        }

        return
        [
            "the picture does not continue across the centre: a tonal edge at column "
            + $"{measured.EdgeColumn} runs through {measured.EdgeCoverage:P0} of the rows (severe "
            + $"limit {CompositeSeamRepair.SevereEdgeCoverageLimit:P0}) and the two sides of "
            + $"column {measured.FieldColumn} disagree one-way in {measured.FieldCoverage:P0} of "
            + $"the rows (severe limit {CompositeSeamRepair.SevereFieldCoverageLimit:P0}). Both "
            + "together are an overlay's signature, not a composition's."
        ];
    }

    /// <summary>
    /// The advisory tier: a reading past the ordinary limits but short of severe, as one line for
    /// a log and the human who reads it.
    ///
    /// A classification, not a decision — see <see cref="CentreFieldProblems"/>. This tier is the
    /// one the pipeline's centre-fold gate now acts on, and what it does there is buy a redraw:
    /// the cost of a missed borderline is a fold printed into somebody's book, and the cost of a
    /// false positive is one image call rather than the stopped order it used to be.
    /// </summary>
    public static string? CentreFieldWarning(byte[] png)
    {
        var measured = CompositeSeamRepair.MeasureCentreField(png);

        return measured.Exceeded && !measured.Severe
            ? $"centre-field advisory: edge {measured.EdgeCoverage:P0} at column "
              + $"{measured.EdgeColumn}, one-way field {measured.FieldCoverage:P0} at column "
              + $"{measured.FieldColumn} (advisory limits "
              + $"{CompositeSeamRepair.EdgeCoverageLimit:P0}/"
              + $"{CompositeSeamRepair.FieldCoverageLimit:P0})"
            : null;
    }

    /// <summary>
    /// The fraction of the longer-than-needed dimension a centre-crop to 15:7 removes. Zero for an
    /// image that is already 15:7.
    /// </summary>
    public static double NormalizationCropFraction(int width, int height)
    {
        if (width <= 0 || height <= 0) return 1;

        var aspect = (double)width / height;

        return aspect >= TargetAspect
            // Too wide: the sides go.
            ? 1 - (height * TargetAspect / width)
            // Too tall: the top and bottom go.
            : 1 - (width / TargetAspect / height);
    }

    /// <summary>
    /// Why a composite cannot be shipped, read off its own manifest and the registry it names.
    ///
    /// Every one of these is a fact the composite engine already wrote down, which is the point of
    /// the manifest: the receipt is checkable without the pixels, so a page can be verified months
    /// later, by someone else, from a JSON file.
    /// </summary>
    /// <param name="textSide">
    /// Which third the Georgian will be printed over. Beki must be nowhere near it — she stands in
    /// the half the text does not occupy, and a Beki inside the reserved third would be printed
    /// over.
    /// </param>
    public static IReadOnlyList<string> CompositeProblems(
        BekiCompositionManifest manifest, BekiPoseRegistry registry, BekiTextSide textSide)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(registry);

        var problems = new List<string>();
        var layer = manifest.BekiLayer;
        var canvas = manifest.Canvas;

        // Exactly one approved asset hash, and it is the one the registry vouches for. The engine
        // reads its bytes through the registry so this cannot normally disagree; it is checked
        // anyway because "cannot normally" is not what a print contract rests on.
        var pose = registry.Pose(layer.PoseId);
        if (!string.Equals(layer.Sha256, pose.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add(
                $"the composited Beki layer hashes to {layer.Sha256}, and the registry says "
                + $"{pose.Sha256} for pose '{layer.PoseId}'.");
        }

        if (layer.Mirrored || layer.Rotated || layer.Warped || layer.Redrawn)
        {
            problems.Add("the manifest reports a mirrored, rotated, warped or redrawn Beki.");
        }

        if (layer.Opacity is not 1.0)
        {
            problems.Add($"Beki was composited at opacity {layer.Opacity}, not 1.0.");
        }

        if (layer.RenderedSizePx.WidthPx <= 0 || layer.RenderedSizePx.HeightPx <= 0)
        {
            problems.Add("the rendered Beki has no size.");
        }

        var left = layer.PlacementPx.XPx;
        var top = layer.PlacementPx.YPx;
        var right = left + layer.RenderedSizePx.WidthPx;
        var bottom = top + layer.RenderedSizePx.HeightPx;

        if (left < 0 || top < 0 || right > canvas.WidthPx || bottom > canvas.HeightPx)
        {
            problems.Add(
                $"Beki is not fully inside the canvas: {left},{top} to {right},{bottom} on "
                + $"{canvas.WidthPx}x{canvas.HeightPx}.");
        }

        // The reserved third, as the image prompt described it to the model and as the layout will
        // set type into. The fold is deliberately not checked: Beki stands beside it by design —
        // the approved spread puts her left edge 82 pixels from the centre line — so a fold rule
        // applied to her would fail the one page everybody signed off.
        var third = canvas.WidthPx / 3.0;
        var intrudes = textSide == BekiTextSide.Left ? left < third : right > canvas.WidthPx - third;

        if (intrudes)
        {
            problems.Add(
                $"Beki enters the {textSide.ToString().ToLowerInvariant()} third reserved for story "
                + $"text: {left} to {right} on a {canvas.WidthPx}-wide canvas.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Output.Sha256))
        {
            problems.Add("the composite manifest records no output hash.");
        }

        return problems;
    }
}

/// <summary>What the reviewer answered, once it has been parsed and checked against the schema.</summary>
/// <param name="Status">PASS or FAIL, and nothing else is accepted.</param>
/// <param name="FailedChecks">The contract's nine category names; empty on a pass.</param>
/// <param name="RecommendedAction">pass | regenerate_base | recomposite_beki | human_review.</param>
/// <param name="Notes">Short, concrete, and about the composite rather than the child's photograph.</param>
public sealed record CompositeQaVerdict(
    string Status,
    IReadOnlyList<string> FailedChecks,
    string RecommendedAction,
    IReadOnlyList<string> Notes)
{
    /// <summary>
    /// The reviewer's optional remark that the rendered shot clearly contradicts the shot this page
    /// was asked for — advisory, and only ever advisory (v1.3).
    ///
    /// It is a property with a default rather than a constructor parameter for a reason worth
    /// stating: nothing in the retry ladder may branch on it, and a positional parameter beside
    /// <see cref="FailedChecks"/> invites exactly that. It cannot fail a page, cannot change
    /// <see cref="RecommendedAction"/>, cannot buy an image and does not appear in
    /// <see cref="ToString"/>, which is the line the ladder and the stored verdict read. It exists
    /// so that the next revision of the shot instruction has counted evidence rather than an
    /// impression from a printed proof — the supplier's finding was real, and a hard gate on a
    /// subjective single-frame judgement would pay for its false positives in image calls.
    /// </summary>
    public string? ShotNote { get; init; }

    /// <summary>
    /// What the reviewer thought about the child's apparent age — advisory, and only ever advisory
    /// (v1.4).
    ///
    /// CHILD_AGE was a blocking category until a pack died on it twice. The owner's ruling is that
    /// the photograph is the identity reference and nothing else: a parent may upload a picture
    /// from last year and buy the book for the age they typed, and a reviewer comparing the render
    /// to the photograph will call that a fault every time. So the observation is kept and the
    /// gate is gone.
    ///
    /// Like <see cref="ShotNote"/>, it is a property with a default rather than a constructor
    /// parameter, it stays out of <see cref="ToString"/> — the line the retry ladder reads — and
    /// nothing anywhere may branch on it.
    /// </summary>
    public string? AgeNote { get; init; }

    public const string Pass = "PASS";

    public const string ActionPass = "pass";
    public const string ActionRegenerateBase = "regenerate_base";
    public const string ActionRecompositeBeki = "recomposite_beki";
    public const string ActionHumanReview = "human_review";

    public bool Passed => string.Equals(Status, Pass, StringComparison.Ordinal);

    /// <summary>The verdict as one line, for the log and for the spread's stored record.</summary>
    public override string ToString() =>
        $"{Status} ({RecommendedAction})"
        + (FailedChecks.Count == 0 ? string.Empty : $": {string.Join(", ", FailedChecks)}");
}

/// <summary>Either a verdict, or the reasons the answer was not one.</summary>
public sealed record CompositeQaParseResult(
    bool IsValid, CompositeQaVerdict? Verdict, IReadOnlyList<string> Problems)
{
    public string Summary => string.Join("; ", Problems);
}

/// <summary>
/// The minimal visual QA contract: what the reviewer is asked, and what counts as an answer.
///
/// Deliberately not <see cref="Prompts.BekiImageQaPrompt"/>. That reviewer judges an image the
/// model drew, Beki included, against a character lock written from a photograph — most of which is
/// meaningless here, because Beki was not drawn and her fidelity is settled by a SHA-256 rather than
/// by an opinion. Asked the old questions about a composite, a reviewer refuses pages for Beki's
/// anatomy, which is the approved artwork's anatomy. So this is a second, smaller contract with
/// nine named categories and an explicit instruction not to grade beauty.
///
/// It also ignores the legacy <c>QaReviewEnabled</c> switch entirely. That flag turned review off
/// for the current production pipeline because the retries it caused were paying twice for the same
/// picture; this pipeline's review is what decides between regenerating a base and re-compositing a
/// pose, and neither of those is a second image bill by default.
///
/// The system instruction is transcribed from <c>contracts/BEKI_Minimal_Visual_QA_v1.md</c>; the
/// response is validated against <c>minimal_visual_qa_v1.schema.json</c>, which unlike the Markdown
/// is shipped into the published output and so is read from the file rather than copied.
/// </summary>
public static class CompositeMinimalQa
{
    /// <summary>
    /// v1.1: the reviewer is shown the child appearance anchor on every spread after the first, and
    /// CHILD_IDENTITY names the four attributes to compare against it.
    ///
    /// The amendment exists because the drifting book passed. Shown only the composite and the
    /// photograph, this reviewer judged each page against the photograph on its own — which is
    /// precisely what a drifting book does too — so eight independent readings of one child all came
    /// back PASS while the child changed. A comparison needs two pictures of the same thing.
    /// </summary>
    /// <summary>
    /// v1.2 hands the reviewer the book's identity spec and asks it to check the attributes by
    /// name — including the eye colour, which is the one the owner watched go wrong "almost
    /// always, especially on the cover".
    ///
    /// A reviewer comparing two pictures can only report what it thinks it sees; a reviewer told
    /// "the eyes must read as green" is answering a question with a right answer. The rest of the
    /// spec is there for the same reason: eyebrows, glasses and face shape were drifting precisely
    /// because nothing named them.
    /// </summary>
    /// <summary>
    /// v1.3 states the deterministic shot this page was asked for and accepts one optional,
    /// advisory <c>shot_note</c> back when the rendered composition clearly contradicts it.
    ///
    /// No new failed-check category, no effect on the verdict, no retry. The supplier's audit found
    /// wide and close spreads rendering as the same medium composition and nothing in the pipeline
    /// measured it; a hard gate on a subjective judgement made from one frame would spend a paid
    /// image call on every false positive. So the reviewer records, and the next revision decides.
    /// </summary>
    /// <summary>
    /// v1.5 is that next revision, and the evidence arrived as a production rejection rather than
    /// as counted notes: the audited book's shots were "only partially followed" and its key
    /// object appeared before its discovery and after being left behind, with every page passing
    /// review. Two categories join the taxonomy. SHOT_COMPLIANCE fails a page whose framing
    /// clearly contradicts the stated shot — wrong camera distance, a required full figure not
    /// fully visible, the main story subject cropped by the canvas edge. PROP_STATE fails a page
    /// that contradicts the plan's stated object states, which v2.2 scenarios now carry and this
    /// prompt now quotes. The advisory <c>shot_note</c> survives for borderline impressions; the
    /// clear contradiction is a check.
    /// </summary>
    /// <summary>
    /// v1.6 broadens GENERATED_TEXT to the panel that carries no text: a live book shipped a
    /// page with a translucent rectangle painted into the scene — the model's rendering of the
    /// "integration zone" it had been told to leave — and no category could name it, so eight
    /// reviews passed it. Text furniture without the text is still furniture; the category whose
    /// job is "things that are not scene" now says so. No schema change: the category name is
    /// unchanged, only its definition grew.
    /// </summary>
    public const string Version = "minimal-visual-qa-v1.6";

    /// <summary>
    /// The supplied file stays the authority. v1.1 and v1.2 changed only what the reviewer is
    /// shown and what CHILD_IDENTITY means, neither of which is in the schema. v1.3 added exactly
    /// one **optional** property, <c>shot_note</c>. v1.5 adds the two category names
    /// <c>PROP_STATE</c> and <c>SHOT_COMPLIANCE</c> to the <c>failed_checks</c> enum — additive,
    /// so every answer valid under v1.4 is still valid.
    /// </summary>
    public const string SchemaFileName = "minimal_visual_qa_v1.schema.json";

    private static readonly Lazy<JsonSchema> Schema = new(LoadSchema);

    /// <summary>
    /// The reviewer's whole prompt: the contract's system instruction, then the six inputs it is
    /// told to judge against.
    ///
    /// The inputs are appended rather than interpolated into the instruction so that the
    /// instruction stays byte-identical to the contract — the one thing a supplier revision has to
    /// be able to land cleanly on.
    /// </summary>
    /// <param name="anchorAttached">
    /// True on every spread after the first, where the accepted spread-1 base is attached as the
    /// child appearance anchor. The line is written only when the picture is actually sent, for the
    /// same reason the image prompt never names a reference it does not carry: a reviewer told to
    /// compare against a picture it cannot see answers about the one it can.
    /// </param>
    /// <param name="identity">
    /// The book's identity spec, written into the ask so the reviewer checks named attributes
    /// rather than an impression. Null only for a caller that has none, which the composite path
    /// no longer has.
    /// </param>
    /// <param name="shotInstruction">
    /// The deterministic shot this page was asked for, verbatim from the config's rhythm — v1.3.
    ///
    /// Stated so the advisory <c>shot_note</c> has something to be about: a reviewer that is not
    /// told what was asked for can only report what it sees, which is not a comparison. Empty or
    /// null omits both the line and the invitation to write a note, so a caller with no rhythm
    /// entry (the cover) is not asking about a shot nobody specified.
    /// </param>
    public static string Prompt(
        string childWorldScene,
        string bekiAction,
        string childOutfit,
        IReadOnlyList<string> recurringElements,
        string textSide,
        bool anchorAttached = false,
        ChildIdentitySpec? identity = null,
        string? shotInstruction = null,
        IReadOnlyList<string>? propStates = null)
    {
        var elements = recurringElements is { Count: > 0 }
            ? string.Join("; ", recurringElements)
            : "none required on this page";

        var anchor = anchorAttached
            ? "\nChild appearance anchor: the accepted first spread of this same book is attached. "
              + "This page's child must be the same stylized child — same face shape, hair, "
              + "eyebrows, eye colour, skin tone, glasses and outfit. It is not a pose, "
              + "composition, or background reference."
            : string.Empty;

        // The spec, and then the one attribute stated as a question with a right answer. The eye
        // colour is checked by name because that is the one the owner watched go wrong on almost
        // every book, and "does this look like the same child" is not a test a reviewer can fail
        // an eye colour on.
        var spec = identity is null
            ? string.Empty
            : $"\nChild identity spec for this book: {CompositeChildIdentity.SpecText(identity)}"
              + $"\nThe child's eyes must read as {identity.EyeColor} in this illustration. If they "
              + "do not, that alone is a CHILD_IDENTITY failure."
              + GlassesRule(identity);

        // v1.5: the clear contradiction is SHOT_COMPLIANCE, a real check; the note stays for the
        // borderline impression a check should not be failed on.
        var shot = string.IsNullOrWhiteSpace(shotInstruction)
            ? string.Empty
            : $"\nShot this page was asked for: {shotInstruction.Trim()} If the rendered "
              + "composition clearly contradicts that shot type — wrong camera distance, a "
              + "required full figure not fully visible, the main story subject cropped by the "
              + "canvas edge — fail SHOT_COMPLIANCE with recommended_action regenerate_base. For "
              + "a borderline impression, put one short sentence in shot_note instead; the note "
              + "is advisory and changes nothing.";

        // v1.5: the plan's own object states, quoted so PROP_STATE is a judgement against a
        // stated fact rather than an inference from one frame.
        var props = propStates is { Count: > 0 }
            ? "\nStory object states this page: " + string.Join("; ", propStates)
              + ". A picture that contradicts one of these states is a PROP_STATE failure with "
              + "recommended_action regenerate_base."
            : string.Empty;

        return $"""
            {SystemInstruction}

            ---

            THIS PAGE

            Child/world scene: {childWorldScene.Trim()}
            Beki action: {bekiAction.Trim()}
            Required base outfit: {childOutfit.Trim()}
            Relevant recurring elements: {elements}{props}{shot}
            Reserved text side: {textSide.ToUpperInvariant()} — the {textSide.ToLowerInvariant()} third of the spread carries printed story text and must stay clear of faces, hands, characters, foreground objects and key action.
            Central exclusion zone: a narrow vertical strip at the exact centre of the spread; continuous environment may cross it, but no face, hand, character or story-critical detail may.{spec}{anchor}
            """;
    }

    /// <summary>
    /// The plan's prop states as short reviewer-facing sentences, one per element that has a
    /// state worth judging. AMBIENT and ABSENT are omitted: an ambient companion is already
    /// covered by CAST_ERROR, and "not in this picture" is not a fact a single frame can
    /// contradict without also being NOT_FOUND or NO_LONGER_CARRIED.
    /// </summary>
    public static IReadOnlyList<string> PropStateLines(IReadOnlyList<VisualScenarioProp>? props)
    {
        if (props is null or { Count: 0 })
        {
            return [];
        }

        var lines = new List<string>();

        foreach (var prop in props)
        {
            var element = prop.Element?.Trim();
            if (string.IsNullOrWhiteSpace(element))
            {
                continue;
            }

            var line = prop.State?.Trim() switch
            {
                VisualScenarioPropStates.NotFound =>
                    $"{element} — not yet discovered in the story and must not appear",
                VisualScenarioPropStates.Found =>
                    $"{element} — the child discovers it on this very page",
                VisualScenarioPropStates.Carried =>
                    $"{element} — the child is holding or carrying it",
                VisualScenarioPropStates.Placed =>
                    $"{element} — the child is placing it where it now belongs",
                VisualScenarioPropStates.NoLongerCarried =>
                    $"{element} — left behind earlier in the story and must not appear",
                _ => null,
            };

            if (line is not null)
            {
                lines.Add(line);
            }
        }

        return lines;
    }

    /// <summary>
    /// The same reviewer, asked about a cover.
    ///
    /// The system instruction and the schema are the spread's, unchanged — the nine categories are
    /// the nine categories, and a second contract for one picture would be a second thing to keep
    /// in step. What differs is the page description: a cover carries no printed story text and has
    /// no centre to keep clear, so the two criteria that describe those are not stated for it, and
    /// the reviewer is told plainly that this is the cover.
    ///
    /// What is stated, and is the reason this exists at all, is the identity check. The cover is the
    /// picture the owner watched lose the eye colour on almost every book, and it is the one page
    /// that until now was never reviewed against the child's own spec or against the rest of the
    /// book.
    /// </summary>
    public static string CoverPrompt(
        string coverScene, string childOutfit, ChildIdentitySpec identity, bool anchorAttached = true)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var anchor = anchorAttached
            ? "\nChild appearance anchor: the accepted first spread of this same book is attached. "
              + "The child on this cover must be the same stylized child — same face shape, hair, "
              + "eyebrows, eye colour, skin tone, glasses and outfit. It is not a pose, "
              + "composition, or background reference."
            : string.Empty;

        return $"""
            {SystemInstruction}

            ---

            THIS PAGE

            This is the book's COVER, not a story spread. It carries no printed story text, so no side of it is reserved and there is no central exclusion zone to keep clear. Do not fail it for TEXT_SAFE_AREA or FOLD_SAFETY.

            Cover scene: {coverScene.Trim()}
            Required base outfit: {childOutfit.Trim()}
            Child identity spec for this book: {CompositeChildIdentity.SpecText(identity)}
            The child's eyes must read as {identity.EyeColor} on this cover. If they do not, that alone is a CHILD_IDENTITY failure.{GlassesRule(identity)}{anchor}
            """;
    }

    private static string GlassesRule(ChildIdentitySpec identity) =>
        string.Equals(identity.Glasses, CompositeChildIdentity.NoGlasses, StringComparison.OrdinalIgnoreCase)
            ? " This child wears no glasses; glasses appearing here are a CHILD_IDENTITY failure."
            : $" This child wears glasses ({identity.Glasses}); glasses missing here are a "
              + "CHILD_IDENTITY failure.";

    /// <summary>
    /// Reads one reviewer answer, forgiving about the wrapper and strict about the content.
    ///
    /// The wrapper is forgiven because a model in prose mode fences its JSON and that is not a
    /// failed review; the content is not, because the contract's deterministic validation list is
    /// the difference between a verdict and a sentence containing the word PASS. A PASS carrying
    /// failed checks, an invented category name, an extra key: all rejected, all with the reason
    /// named, because the one permitted parse retry is only useful if it can be told what was wrong.
    /// </summary>
    public static CompositeQaParseResult Parse(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return Invalid("the reviewer returned no text.");
        }

        var json = ModelJsonSanitizer.ExtractJsonObject(answer);
        if (string.IsNullOrWhiteSpace(json))
        {
            return Invalid("the reviewer's answer contains no JSON object.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return Invalid($"the reviewer's answer is not valid JSON: {ex.Message}");
        }

        using (document)
        {
            // CHILD_AGE comes out before the schema sees the answer, and a page whose only
            // objection it was becomes a pass. See DemoteAge.
            var (demoted, ageNote) = DemoteAge(document.RootElement);
            using var _ = demoted;

            var results = Schema.Value.Evaluate(
                demoted.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });

            if (!results.IsValid)
            {
                var details = (results.Details ?? [])
                    .Where(detail => !detail.IsValid && detail.Errors is { Count: > 0 })
                    .SelectMany(detail => detail.Errors!.Select(error =>
                        $"{Location(detail.InstanceLocation.ToString())} failed '{error.Key}': {error.Value}"))
                    .ToList();

                return new CompositeQaParseResult(
                    false,
                    null,
                    details.Count > 0
                        ? details
                        : [$"the reviewer's answer does not satisfy {SchemaFileName}."]);
            }

            var root = demoted.RootElement;

            return new CompositeQaParseResult(
                true,
                new CompositeQaVerdict(
                    // Proved strings by the schema a few lines above — read through the same
                    // guard as everything else so that no path out of Parse can throw.
                    Text(root, "status"),
                    Strings(root, "failed_checks"),
                    Text(root, "recommended_action"),
                    Strings(root, "notes"))
                {
                    // Absent and empty are the same answer — "the shot is fine, or I could not
                    // tell" — and both must read as no note at all, because a whitespace string
                    // would otherwise be counted and reported as an observation.
                    ShotNote = root.TryGetProperty("shot_note", out var note)
                               && note.ValueKind == JsonValueKind.String
                               && !string.IsNullOrWhiteSpace(note.GetString())
                        ? note.GetString()!.Trim()
                        : null,
                    AgeNote = ageNote,
                },
                []);
        }
    }

    /// <summary>
    /// Takes CHILD_AGE out of the answer before anything is decided by it, and says what it said.
    ///
    /// The owner's decision, on 2026-08-30, after a pack died on it twice: *"we must agree on
    /// entered age, name, eye color etc — but the image is the reference. It might be an older image
    /// and [the parent] wants the book for the child's younger age, so it must not be a blocker."*
    /// Pack 7fc8faf4 refused spread 1 as `FAIL (regenerate_base): CHILD_AGE`, bought a second
    /// picture, was refused for the same thing again, and stopped — a book lost to a disagreement
    /// between a photograph and a number the parent typed, when the number is the one that is right.
    ///
    /// Done here, before the schema, rather than after the verdict is built, because the two would
    /// otherwise disagree about what a valid answer is: the contract's failed-check list no longer
    /// contains CHILD_AGE, so an answer naming it would be rejected outright and spend the parse
    /// retry — turning an advisory into a harder blocker than it was. Stripping first means a
    /// reviewer that still names it is understood rather than argued with.
    ///
    /// A FAIL whose only objection was the age becomes the PASS it should always have been. A FAIL
    /// that also names something blocking keeps its status, its action and its other checks, and
    /// loses only the age.
    /// </summary>
    /// <returns>
    /// The document the schema and the verdict are read from, and the age remark to record — the
    /// reviewer's own <c>age_note</c> when it wrote one, and otherwise a short line saying the age
    /// was raised as a check and demoted, so the record is not silent about what was seen.
    /// </returns>
    private static (JsonDocument Demoted, string? AgeNote) DemoteAge(JsonElement root)
    {
        var reviewerNote = root.TryGetProperty("age_note", out var stated)
                           && stated.ValueKind == JsonValueKind.String
                           && !string.IsNullOrWhiteSpace(stated.GetString())
            ? stated.GetString()!.Trim()
            : null;

        var items = root.TryGetProperty("failed_checks", out var checks)
                    && checks.ValueKind == JsonValueKind.Array
            ? checks.EnumerateArray().ToList()
            : [];

        /*
          An answer whose failed_checks are not all strings is left exactly as it arrived.

          This runs before validation — it has to, so that a reviewer still naming CHILD_AGE is
          understood rather than rejected against an enum that no longer lists it — which means it
          is the first thing to touch a document nothing has checked yet. A number in that array
          used to throw straight out of here and fail a paid book on the spot, where the schema
          would merely have refused the answer and spent the parse retry. So a malformed array is
          not demoted, not rewritten and not read: it is handed on to the schema, which is the part
          that knows how to say no.
        */
        if (items.Any(item => item.ValueKind != JsonValueKind.String))
        {
            return (JsonDocument.Parse(root.GetRawText()), reviewerNote);
        }

        var failed = items.Select(item => item.GetString() ?? string.Empty).ToList();

        var raisedAge = failed.Any(check => string.Equals(check, AdvisoryAgeCheck, StringComparison.Ordinal));

        if (!raisedAge)
        {
            // Nothing to demote. The document is handed straight through, cloned only because the
            // caller owns the lifetime of what it evaluates.
            return (JsonDocument.Parse(root.GetRawText()), reviewerNote);
        }

        var kept = failed
            .Where(check => !string.Equals(check, AdvisoryAgeCheck, StringComparison.Ordinal))
            .ToList();

        var rewritten = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var property in root.EnumerateObject())
        {
            rewritten[property.Name] = property.Name switch
            {
                "failed_checks" => kept,
                // A page objected to only for the child's age is a page with nothing wrong with it.
                "status" when kept.Count == 0 => CompositeQaVerdict.Pass,
                "recommended_action" when kept.Count == 0 => CompositeQaVerdict.ActionPass,
                _ => JsonSerializer.Deserialize<JsonElement>(property.Value.GetRawText()),
            };
        }

        return (
            JsonDocument.Parse(JsonSerializer.Serialize(rewritten)),
            reviewerNote
            ?? "The reviewer raised CHILD_AGE; recorded as advisory and not treated as a failure.");
    }

    /// <summary>
    /// The one check that is collected rather than enforced (v1.4).
    ///
    /// Not a member of the contract's failed-check list any more — the schema's enum does not
    /// contain it — but named here because the reviewer may still return it, and this is what
    /// recognises it in order to take it out.
    /// </summary>
    public const string AdvisoryAgeCheck = "CHILD_AGE";

    private static string Location(string location) =>
        location.Length == 0 ? "(root)" : location;

    /// <summary>
    /// The string members of an array property, and only the string members.
    ///
    /// Belt to the schema's braces: this runs after validation, so every element here has already
    /// been proved a string — but it is one <c>GetString()</c> on an unchecked element away from
    /// throwing <see cref="InvalidOperationException"/> out of a parse that is supposed to return a
    /// verdict or a reason, and a throw here fails a paid book where an invalid parse would have
    /// spent the retry. Reading only what is actually a string costs nothing and cannot throw.
    /// </summary>
    private static IReadOnlyList<string> Strings(JsonElement root, string property) =>
        root.TryGetProperty(property, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? string.Empty)
                .ToList()
            : [];

    private static string Text(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static CompositeQaParseResult Invalid(string problem) => new(false, null, [problem]);

    private static JsonSchema LoadSchema()
    {
        var path = CompositeAssets.ContractPath(SchemaFileName);

        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"The minimal visual QA schema '{path}' is not in the published output. A reviewer "
                + "answer is checked against the supplied file and never against a copy in code.");
        }

        return JsonSchema.FromText(File.ReadAllText(path));
    }

    /// <summary>The contract's exact system instruction, verbatim.</summary>
    public const string SystemInstruction =
        """
        You are the Minimal Visual QA reviewer for BEKI personalized children's books.

        Review only critical, parent-visible failures. Do not score beauty, creativity, minor stylistic variation, tiny background artifacts, or subjective preferences. Do not request a retry merely to improve an already usable image.

        Use the original child photo only to judge whether the illustrated child remains recognizably the same child. Do not require photorealism.

        The photograph says WHO the child is. It does not say how old the child is in this book: the age, the name and the eye colour are the parent's entered values, and the book is drawn to those. A photograph may have been taken a year or two ago, and a parent may deliberately be buying the book for a younger age. Never fail an illustration because the child looks older or younger than the photograph, or than the stated age.

        When a child appearance anchor is supplied, use it only to judge whether this page's child is the same stylized child as the rest of the book. It is not a composition, pose, or background reference.

        Check exactly these categories:

        1. CHILD_IDENTITY - The illustrated child is not recognizably the supplied child; or the child's eyes do not read as the stated eye colour; or the child has materially different hair colour/style, eyebrows, face shape, skin tone, or outfit details from the child appearance anchor; or glasses are present when the spec says none, absent when the spec describes them, or a materially different style of frames.
        2. OUTFIT_CONTINUITY - The required base outfit is missing or materially changed.
        3. MAIN_SCENE_BEAT - The one required visible story event is missing, contradicted, or replaced by a different event.
        4. CAST_ERROR - The child or a required supporting character is missing, duplicated, or replaced; or an unrequested prominent character appears.
        5. GENERATED_TEXT - Readable text, pseudo-text, logo, label, sign, watermark, or QR appears in the illustration; or an artificial blank, white, translucent, or semi-transparent panel or rectangle is painted into the scene, with or without anything on it.
        6. TEXT_SAFE_AREA - A face, hand, character, foreground object, or key action blocks the reserved text side.
        7. FOLD_SAFETY - A face, hand, character, or story-critical detail crosses or touches the central exclusion zone.
        8. BEKI_INTEGRATION - Beki is duplicated, clipped, hidden, materially obstructs the main action, or is visibly pasted into an unsuitable hard-edged/foreground area.
        9. PROP_STATE - The page description states where each recurring story object stands; the picture contradicts one of those states - the object appears although it is not yet discovered or was left behind, or it is missing although the child is stated to be discovering, holding, or placing it here.
        10. SHOT_COMPLIANCE - The rendered framing clearly contradicts the shot stated for this page: the camera distance is wrong, a required full figure is not fully visible, or the main story subject is cropped by the canvas edge.

        Do not fail Beki for artistic anatomy or exact asset identity; those are enforced by the approved PNG hash. Do not fail for small differences in background detail. Do not rewrite the prompt.

        Return valid JSON only. Use PASS when no critical category fails. Use FAIL when at least one critical category fails.

        Choose one recommended_action:
        - pass: no critical failure;
        - regenerate_base: failure originates in the child/world generation;
        - recomposite_beki: base image is usable and only deterministic Beki placement is wrong;
        - human_review: the failure source is ambiguous or a second attempt has already failed.

        Return exactly this structure and no additional keys:

        {
          "status": "PASS",
          "failed_checks": [],
          "recommended_action": "pass",
          "notes": [],
          "shot_note": "",
          "age_note": ""
        }

        Each failed_checks item, when present, must be one of:
        CHILD_IDENTITY, OUTFIT_CONTINUITY, MAIN_SCENE_BEAT, CAST_ERROR, GENERATED_TEXT, TEXT_SAFE_AREA, FOLD_SAFETY, BEKI_INTEGRATION, PROP_STATE, SHOT_COMPLIANCE.

        age_note is optional, advisory, and never a failure. Fill it with one short sentence only when the illustrated child reads as a clearly different age from the stated one. Leave it out, or empty, otherwise. An age_note is not a failed check, does not appear in failed_checks, does not change status or recommended_action, and never causes a retry. The age the parent entered is the age the book is drawn to, whatever the photograph shows.

        shot_note is optional, advisory, and never a failure on its own. A CLEAR contradiction of the stated shot is the SHOT_COMPLIANCE check above. Use shot_note only for a borderline impression the check should not be failed on - the framing is defensible but drifts toward a medium shot, say. Leave it out, or empty, when the shot is right. A shot_note is not a failed check, does not appear in failed_checks, does not change status or recommended_action, and never causes a retry.

        Keep notes short, concrete, and visible in the supplied composite. Do not include sensitive descriptions of the child's source photo.
        """;
}
