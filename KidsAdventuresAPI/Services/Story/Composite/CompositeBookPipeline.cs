using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story.Composite.Poses;
using SixLabors.ImageSharp;

namespace AdventurePacks.Api.Services.Story.Composite;

/// <summary>
/// A book that stopped, with the word it stopped on.
///
/// The code is the whole value of the type. Everything downstream of a failed book — the admin
/// notification, the support answer, the decision whether a retry could possibly help — turns on
/// which of the eight agreed failures happened, and a bare exception message makes that a matter
/// of reading English prose in a log.
/// </summary>
public sealed class CompositePipelineException(string failureCode, string message, Exception? inner = null)
    : InvalidOperationException(message, inner)
{
    /// <summary>One of <see cref="CompositeFailureCodes"/>.</summary>
    public string FailureCode { get; } = failureCode;

    /// <summary>Null for a book-level failure; the page for a per-spread one.</summary>
    public int? Page { get; init; }

    /// <summary>
    /// The picture the reviewer refused and the record of why, for the failures where "marked for
    /// human review" is the outcome.
    ///
    /// It rides on the exception because the exception is what reaches the fulfilment job, and the
    /// fulfilment job is the only part of this system that can write a blob. Until it does, a book
    /// that stopped at spread seven left a pack directory containing spreads one to six and nothing
    /// else: no picture to look at, no verdict to read, and a support answer that can only repeat
    /// the failure code back. The reviewable thing was generated, paid for, judged and discarded.
    ///
    /// Null on every failure that has no page to show — a refused input, a scenario that would not
    /// validate, an identity spec that could not be read.
    /// </summary>
    public CompositeFailureEvidence? Evidence { get; init; }
}

/// <summary>
/// What a page that stopped leaves behind: the picture that was refused, and a short document
/// saying what each attempt was and what was said about it.
/// </summary>
/// <param name="Page">
/// The spread number, for the blob names the fulfilment job builds. Zero for the cover wrap, which
/// has no spread number and whose evidence therefore lands under spread zero — a page number no
/// book has, which is the point.
/// </param>
/// <param name="CompositePng">
/// The picture as it was refused. Usually the last composite the reviewer saw — the whole page,
/// Beki included, exactly as judged.
///
/// A BASE image for the two deterministic gates audit-2 restored (P0-05's centre fold, P0-03's
/// cover construction bands), because those refuse a picture before Beki is ever pasted onto it:
/// what a human needs to look at there is the artwork the model generated, and there is no
/// composite to show.
/// </param>
/// <param name="QaJson">
/// The attempt record: every verdict in order, what was generated or moved between them, and the
/// prompt versions in force. It is written for a person opening two files in a blob browser, so it
/// is indented and it names things the way the logs do.
/// </param>
public sealed record CompositeFailureEvidence(int Page, byte[] CompositePng, string QaJson);

/// <summary>
/// A quality refusal the release policy told this pipeline to ship anyway.
///
/// Amendment B3's whitelist has four members and they have one thing in common: the artwork exists.
/// A centre-fold reading, a cover band, a reviewer's opinion, an answer that would not parse — in
/// every one of them there is an intact composite drawn from approved bytes, and what failed is a
/// judgement about it. The owner's ruling is that a judgement does not kill a paid book; it becomes
/// something an operator looks at later.
///
/// So the page is accepted, its QA record keeps saying exactly what the reviewer said, and this
/// carries the evidence out to the fulfilment job — the only part of the system that can write a
/// blob. Nothing else is waivable: a provider exception, bytes that will not decode, a compositor
/// that refused, an exact-Beki hash mismatch, the asset lock, a composition receipt, the wrap hash,
/// the identity spec and the scenario paths all stay terminal, because there is nothing to ship.
/// </summary>
/// <param name="CheckId">One of <c>BekiReleaseChecks</c>' five names.</param>
/// <param name="Page">The spread, or 0 for the cover wrap — the same numbering the evidence uses.</param>
/// <param name="EvidencePng">The picture that was refused: a composite, or a base for the two
/// deterministic gates, which refuse before Beki is ever pasted on.</param>
public sealed record CompositePolicyWaiver(
    string CheckId, int Page, string Detail, byte[] EvidencePng, string EvidenceJson);

/// <summary>
/// The four normalized inputs plus the job they belong to, handed to the illustrator by a caller
/// that actually knows them.
///
/// It exists because <see cref="IBekiBookGenerator.IllustrateAsync"/> receives a plan and a
/// photograph and nothing else: no age, no gender, no theme. The legacy path never needed them —
/// everything it draws from is inside the plan — and the composite path cannot do without them,
/// because the age band and the theme decide the Visual Scenario input and the theme decides which
/// approved world reference every image is generated against. So the fulfilment job, which holds
/// the run and the pack, supplies them; a caller that does not have them (the preview cover) simply
/// passes nothing and stays on the legacy path.
/// </summary>
public sealed record CompositeBookContext
{
    /// <summary>The pack id. Logged against every AI call, per the observability contract.</summary>
    public required Guid JobId { get; init; }

    /// <summary>The purchase as it was stored, before any mapping.</summary>
    public required BookGenerationInput Input { get; init; }

    /// <summary>
    /// What an earlier attempt at this book left in storage, already fetched.
    ///
    /// It rides on the context for the same reason the four inputs do: only the fulfilment job can
    /// read a blob, and the illustrator it passes through has no storage dependency and is not
    /// about to grow one for a path that is off by default. Empty means a first attempt.
    ///
    /// It also supersedes <c>IllustrateAsync</c>'s own <c>existingSpreads</c> on this path — one
    /// resume state rather than two, because the composited pages, their bases and the scenario
    /// they were drawn against have to be adopted together or not at all.
    /// </summary>
    public CompositeResumeState Resume { get; init; } = CompositeResumeState.Empty;

    /// <summary>
    /// Where to persist the validated scenario, called before the first image call.
    /// See <see cref="CompositeBookRequest.OnScenario"/> for why the timing is the point.
    /// </summary>
    public Func<string, Task>? OnScenario { get; init; }

    /// <summary>
    /// Whether an earlier attempt at this pack already redrew and reviewed the cover.
    ///
    /// The fulfilment job knows this from its own manifest and the illustrator cannot: the cover
    /// redraw is an improvement a book gets once, and a resumed attempt that drew some of the
    /// spreads must not buy it a second time — nor overwrite the reviewed picture with a fresh one
    /// that would have to be reviewed again from scratch.
    /// </summary>
    public bool CoverAlreadyRedrawn { get; init; }

    /// <summary>
    /// Where to persist the derived child identity spec, called before the first image call and
    /// for the same reason the scenario's callback is.
    ///
    /// It rides on the context rather than being written here because the spec is private data
    /// about a real child: it belongs in the pack's own storage, beside the photograph it was read
    /// from, and this pipeline has no storage dependency on purpose.
    /// </summary>
    public Func<string, Task>? OnIdentitySpec { get; init; }

    /// <summary>
    /// The release policy this book is being drawn under, read ONCE by the fulfilment job and
    /// carried — amendment B4.
    ///
    /// It rides on the context for the same reason the resume state does: only the caller that owns
    /// the job can read a policy table, and this pipeline has no repository dependency. A snapshot
    /// rather than a service is the load-bearing half — a pipeline that consulted a cache per spread
    /// could ship page three and refuse page seven under two different policies, and the book's own
    /// records would then describe a decision nobody made.
    ///
    /// Null means every whitelisted check blocks, which is what this pipeline did before the policy
    /// existed. That is the honest default for a caller that has no policy to state rather than one
    /// that has chosen leniency: nothing here quietly softens because an argument was omitted.
    /// </summary>
    public BekiReleasePolicySnapshot? ReleasePolicy { get; init; }

    /// <summary>
    /// Where a waived quality refusal goes, awaited before the page continues.
    ///
    /// Awaited, and before, for the reason the scenario callback is: the fulfilment job writes the
    /// evidence blob and raises the alarm inside it, and a book that shipped a refused page without
    /// the record of it having been refused would be exactly the silence this policy is meant to
    /// replace with an alarm.
    /// </summary>
    public Func<CompositePolicyWaiver, Task>? OnPolicyWaiver { get; init; }

    /// <summary>
    /// Called once, the moment the book has a child appearance anchor — the accepted first
    /// spread's base, drawn by this run or adopted from an earlier one — and awaited.
    ///
    /// It exists so the cover wrap can be drawn BESIDE spreads two to eight rather than after
    /// them. The wrap needs exactly three things every later spread also needs: the scenario, the
    /// identity lock and the anchor. All three are settled before the second spread starts, and a
    /// wrap that waited for the eighth spread was one more image call's worth of wall clock on a
    /// job a parent is watching. The fulfilment job starts the wrap inside this callback and awaits
    /// it after the run; see <see cref="CompositeAnchorAccepted"/>.
    /// </summary>
    public Func<CompositeAnchorAccepted, Task>? OnAnchorAccepted { get; init; }
}

/// <summary>
/// What a run knows the moment its child appearance anchor is settled: enough to draw the cover
/// wrap to the same child as the pages, and nothing a cover does not need.
///
/// The scenario is the validated object rather than its JSON because the caller's next move is the
/// wrap call, which takes the object; handing back the JSON would have it validated twice for no
/// reason. The identity is the same spec every spread is drawn to, and the anchor is the same bytes
/// spreads 2-8 attach — which is the whole of owner's rule 2 (2026-09-01): the cover is a
/// reproduction of a child this book has already drawn.
/// </summary>
public sealed record CompositeAnchorAccepted(
    VisualScenarioV2 Scenario,
    ChildIdentitySpec Identity,
    byte[] AnchorBasePng);

/// <summary>
/// One generate-and-review cycle, measured.
///
/// Its own record rather than the generator's <see cref="BekiImageAttempt"/> so the pipeline owes
/// nothing to the shape of a result type it does not produce; the generator maps one to the other
/// at the seam, which is the only place both are in scope.
/// </summary>
/// <param name="GenerationMs">Zero when this cycle re-composited rather than redrawing.</param>
/// <param name="ReviewMs">How long the minimal visual QA call took, including its parse retry.</param>
/// <param name="Verdict">The reviewer's verdict as one line — the thing telemetry is read for.</param>
/// <param name="Accepted">Whether this cycle's page is the one that shipped.</param>
public sealed record CompositeAttempt(long GenerationMs, long ReviewMs, string Verdict, bool Accepted)
{
    /// <summary>
    /// Where Beki stood for this cycle.
    ///
    /// Carried because the retry that moves her is only auditable if the rows say where she was
    /// each time. A page refused twice for FOLD_SAFETY reads as a pipeline that tried nothing
    /// unless the two rows show two different anchors — which is exactly the failure this record
    /// gained the field for.
    /// </summary>
    public BekiCompositeAnchor? Anchor { get; init; }
}

/// <summary>What one spread came out as, and every receipt it produced on the way.</summary>
public sealed record CompositeSpreadResult
{
    public required int Page { get; init; }

    /// <summary>The child/world image, before Beki. Kept: it is what a re-composite starts from.</summary>
    public required byte[] BasePng { get; init; }

    /// <summary>The page: base plus the approved Beki PNG, pasted.</summary>
    public required byte[] CompositePng { get; init; }

    public required BekiCompositionManifest Manifest { get; init; }

    /// <summary>The prompt the base was generated from, stored the way the legacy path stores its own.</summary>
    public required string Prompt { get; init; }

    public required string PoseId { get; init; }

    /// <summary>LEFT or RIGHT, from the config's rhythm.</summary>
    public required string TextSide { get; init; }

    /// <summary>The reviewer's verdict as one line, or the reason there is not one.</summary>
    public required string Verdict { get; init; }

    /// <summary>How many base images were paid for. 1 unless QA asked for a regeneration.</summary>
    public required int BaseAttempts { get; init; }

    /// <summary>
    /// One row per generate-and-review cycle this page cost, in order.
    ///
    /// Not derivable from <see cref="BaseAttempts"/>, which is why it is carried rather than
    /// reconstructed: a page re-composited after a placement failure was reviewed twice and
    /// generated once, and the telemetry the fulfilment job writes is read to answer "what did the
    /// second attempt object to", which only the rows can answer.
    /// </summary>
    public IReadOnlyList<CompositeAttempt> Attempts { get; init; } = [];

    /// <summary>True when this page was adopted whole from an earlier attempt at the same book.</summary>
    public bool Adopted { get; init; }

    /// <summary>
    /// True when no registry keyword matched this page's Beki sentence and the neutral hover was
    /// used instead. Carried out of the pipeline rather than only logged: a book quietly composited
    /// from eight fallbacks is a scenario-prompt problem, and it is only visible if it is counted.
    /// </summary>
    public bool PoseFallback { get; init; }

    /// <summary>
    /// The reviewer's advisory remark that this page's composition contradicts the shot it was asked
    /// for, or null — which is the usual answer.
    ///
    /// It rides on the accepted page rather than only on the attempt rows because it is a note about
    /// the picture that actually shipped. Nothing branches on it anywhere: <see cref="Verdict"/> is
    /// what the retry ladder read, and this was never part of that.
    /// </summary>
    public string? ShotNote { get; init; }

    /// <summary>
    /// What the reviewer thought about the child's apparent age on this page — advisory, and only
    /// ever advisory. Nothing in the retry ladder reads it; it is here to be counted.
    /// </summary>
    public string? AgeNote { get; init; }

    /// <summary>
    /// The accepted verdict as the document that gets stored — see <see cref="CompositeSpreadQa"/>.
    ///
    /// <see cref="Verdict"/> is the same answer as one line, and it stays: it is what the logs and
    /// the telemetry rows are written in. This is the whole reviewer answer, structured, because
    /// the release gates the audit demands have to read failed_checks and recommended_action
    /// rather than parse a sentence.
    ///
    /// Null on an adopted page, which this run did not review.
    /// </summary>
    public string? QaJson { get; init; }
}

/// <summary>
/// The artifacts a finished composite book has to persist: the scenario the whole book was planned
/// from, and one composition receipt per page.
///
/// Carried out of the pipeline rather than written by it. The pipeline has no storage dependency on
/// purpose — it is run in tests with no blob account and no container — and the fulfilment job
/// already owns every naming decision about where a pack's files live.
/// </summary>
public sealed record CompositeBookArtifacts
{
    /// <summary>The validated Visual Scenario, exactly as the model returned it.</summary>
    public required string ScenarioJson { get; init; }

    public required IReadOnlyList<CompositeSpreadArtifact> Spreads { get; init; }

    /// <summary>
    /// The eight attributes this book's child was drawn to — the same value as
    /// <see cref="CompositeBookResult.Identity"/>, carried on the artifacts so the caller that
    /// draws the cover has it.
    ///
    /// Owner's rule 2 (2026-09-01): "characters must be consistent on cover and spreads". The cover
    /// wrap is generated by the fulfilment job AFTER this book comes back, and until now the only
    /// thing it could hand the cover prompt was a photograph — so the wrap was a ninth independent
    /// stylization while spreads 2-8 were reproductions of an agreed drawing. The identity travels
    /// with the book because the picture that has to match it is drawn by somebody else.
    ///
    /// Adopted on a resume, exactly as the spreads are: a run that redrew nothing still hands back
    /// the spec its predecessor derived, which is what makes a rebuilt cover the same child as the
    /// pages it is bound around.
    /// </summary>
    public required ChildIdentitySpec Identity { get; init; }

    /// <summary>
    /// The accepted first spread's base — the child appearance anchor — for the same caller and the
    /// same reason.
    ///
    /// Null is a real answer and not a failure: a press rebuild whose stored base images are gone
    /// has no anchor to give, and a cover drawn from the lock and the photograph alone is exactly
    /// the condition spread one is drawn in. It is weaker, and it is honest; inventing one would
    /// mean anchoring the cover to a page nobody accepted.
    /// </summary>
    public byte[]? Anchor { get; init; }

    /// <summary>
    /// The book-level quality record: pose fallbacks, Georgian flags, shot advisories — see
    /// <see cref="CompositeBookReview"/>.
    ///
    /// An artifact rather than a log line only, because every one of these is a thing somebody has
    /// to go and read *after* a book ships, and scrollback is not where that happens. Null on a
    /// path that produced no review, which is none of them today.
    /// </summary>
    public string? ReviewJson { get; init; }

    /// <summary>
    /// The same record, typed, for the caller that projects a few numbers out of it rather than
    /// storing the document whole.
    ///
    /// Both, rather than one and a parse. The fulfilment job stores <see cref="ReviewJson"/>
    /// byte-for-byte as the pack's own artifact, and separately puts the counts — and only the
    /// counts — into its telemetry; re-parsing a string it was just handed, to read two integers
    /// out of it, is the kind of seam that eventually disagrees with itself.
    /// </summary>
    public CompositeBookReview? Review { get; init; }
}

/// <summary>
/// What one spread's stored QA record turned out to say, once it has been read back and found to
/// be one this deployment still understands.
///
/// Deliberately three fields out of a document with a dozen. Everything a resumed run needs to
/// know is "there is a readable verdict for this page, written by the reviewer contract now in
/// force"; the notes, the failed checks and the advisory remarks are for the human who opens the
/// blob, and re-deriving decisions from them here would be a second reviewer with no model behind
/// it.
/// </summary>
public sealed record CompositeSpreadQaRecord(
    int Page, string Status, string RecommendedAction, string QaPromptVersion);

/// <summary>
/// The per-spread QA document: written on the success path, and read back when a later attempt
/// wants to adopt the page it belongs to.
///
/// It exists because of audit-2 P0-09, whose evidence is one sentence long: "PACKAGE_CONTENTS.json
/// lists all eight qa/spread-XX-qa.json files as missing", on a package that nonetheless contained
/// final press and customer PDFs. The verdicts were real — every page was reviewed, and a refused
/// one wrote its record on the way out — but the accepted ones were held in memory, used to decide
/// whether to ship, and dropped. A book's QA either survives the book or it was never evidence.
/// </summary>
public static class CompositeSpreadQa
{
    /// <summary>
    /// One accepted page's verdict, as the document that gets stored beside it.
    ///
    /// The reviewer's own four fields, whole, plus the two advisory remarks and the provenance a
    /// reader needs to judge them: which reviewer contract asked the questions, which image prompt
    /// drew the picture, how many pictures were bought and how many readings it took. No model
    /// call — this is the verdict the ladder already had in its hand, written down instead of
    /// discarded.
    ///
    /// Nothing about the child is in it, for the reason the failure evidence gives: the scene, the
    /// outfit and the identity attributes stay out, and the picture beside it is the thing to look
    /// at.
    /// </summary>
    public static string Write(
        int page,
        string poseId,
        string textSide,
        int baseAttempts,
        int reviewAttempts,
        CompositeQaVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        return JsonSerializer.Serialize(
            new
            {
                page,
                qa_prompt_version = CompositeMinimalQa.Version,
                image_prompt_version = CompositeIllustrationPrompt.Version,
                pose_id = poseId,
                text_side = textSide,
                base_attempts = baseAttempts,
                review_attempts = reviewAttempts,
                status = verdict.Status,
                recommended_action = verdict.RecommendedAction,
                failed_checks = verdict.FailedChecks,
                notes = verdict.Notes,
                // Advisory, and carried as themselves rather than folded into failed_checks: A9
                // is explicit that a borderline shot impression and an age remark are for the
                // human gate, and a record that promoted them here would be the weakening it
                // forbids arriving through the back door.
                shot_note = verdict.ShotNote,
                age_note = verdict.AgeNote,
                verdict = verdict.ToString(),
            },
            CompositeJson.Readable);
    }

    /// <summary>
    /// One page's record when NOBODY reviewed it — owner's rule 5, 2026-09-01: "we don't need
    /// additional reviews for images".
    ///
    /// The page still gets a document, and the document still tells the truth. Nothing here says
    /// PASS: a status that claimed a verdict would invent a review that was deliberately not
    /// bought, and the release gates read exactly this field to decide what the supplier is told.
    /// What it says instead is the decision — which check was consulted, what it was set to, and
    /// that no model was asked — beside the provenance a reader needs: the reviewer contract that
    /// was in force and not run, and the image prompt that drew the picture.
    ///
    /// <c>review_attempts</c> is 0 and that is the point of writing it: it is the number that makes
    /// "this book cost eight vision calls" and "this book cost none" different sentences in the
    /// telemetry rather than the same one.
    ///
    /// The deterministic checks are NOT affected by the switch and did all run — the fold gate, the
    /// base checks, the composite receipt — which is why this page is accepted rather than merely
    /// unexamined, and why the list of them is named here rather than left to be assumed.
    /// </summary>
    public static string WriteSkipped(
        int page,
        string poseId,
        string textSide,
        int baseAttempts,
        string severity)
    {
        return JsonSerializer.Serialize(
            new
            {
                page,
                qa_prompt_version = CompositeMinimalQa.Version,
                image_prompt_version = CompositeIllustrationPrompt.Version,
                pose_id = poseId,
                text_side = textSide,
                base_attempts = baseAttempts,
                review_attempts = 0,
                status = CompositeBookPipeline.ReviewSkippedStatus,
                // No action, because nobody recommended one. The ladder that reads this field is
                // not entered on this path at all.
                recommended_action = "none",
                failed_checks = Array.Empty<string>(),
                notes = new[]
                {
                    "The per-spread visual review was skipped by release policy; no vision model "
                    + "looked at this page. The deterministic checks below did run and passed.",
                },
                release_policy_version = BekiReleasePolicySnapshot.Version,
                release_policy_check = BekiReleaseChecks.ImageReview,
                release_policy_severity = severity,
                deterministic_checks = new[]
                {
                    "centre_fold", "base_image_checks", "composite_checks", "exact_beki_receipt",
                },
                verdict = CompositeBookPipeline.ReviewSkippedStatus
                          + " (no model review; the deterministic checks passed)",
            },
            CompositeJson.Readable);
    }

    /// <summary>
    /// A stored QA document, when it is one this deployment can still stand behind. Null otherwise.
    ///
    /// Version-guarded exactly the way the stored identity spec is, and for the same reason: a
    /// verdict written by an older reviewer contract answered a different set of questions. The QA
    /// prompt has been revised six times — v1.5 alone added SHOT_COMPLIANCE and PROP_STATE as
    /// failing categories — so "this page passed" under v1.2 is not the same claim as "this page
    /// passed" under v1.6, and a resumed book that carried the old claim forward would be shipping
    /// a page nothing current ever looked at.
    ///
    /// Never throws. Absent, unreadable and out-of-date all mean the same thing to every caller.
    /// </summary>
    public static CompositeSpreadQaRecord? TryReadStored(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("qa_prompt_version", out var version)
                || version.ValueKind != JsonValueKind.String
                || !string.Equals(version.GetString(), CompositeMinimalQa.Version, StringComparison.Ordinal))
            {
                return null;
            }

            if (!root.TryGetProperty("status", out var status)
                || status.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(status.GetString()))
            {
                return null;
            }

            var page = root.TryGetProperty("page", out var pageValue)
                       && pageValue.TryGetInt32(out var pageNumber)
                ? pageNumber
                : 0;

            var action = root.TryGetProperty("recommended_action", out var recommended)
                         && recommended.ValueKind == JsonValueKind.String
                ? recommended.GetString()!
                : string.Empty;

            return new CompositeSpreadQaRecord(
                page, status.GetString()!, action, version.GetString()!);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>One page's composition manifest, ready to store beside the image it describes.</summary>
/// <param name="BasePng">
/// The child/world image before Beki was pasted onto it, which has to be stored and not only used.
///
/// It is the continuity reference: the picture a later spread reusing the same creature is shown
/// and told to match. A run that resumed with only the composited pages would have to either
/// forgo continuity on every redrawn spread — letting a recurring character be redesigned halfway
/// through a book — or hand the image model a page with Beki already on it, which is a picture of
/// Beki and the one image this pipeline promises never to send. So the base is an artifact in its
/// own right.
/// </param>
public sealed record CompositeSpreadArtifact(
    int SpreadNumber, string PoseId, string ManifestJson, string OutputSha256, byte[] BasePng)
{
    /// <summary>
    /// The accepted verdict for this page, as the document to store — audit-2 P0-09, D7.
    ///
    /// Null for exactly two cases, and they are not the same case. An adopted page (see
    /// <see cref="Adopted"/>) was judged by the attempt that drew it and its record is already in
    /// storage, so this attempt has nothing to write and the fulfilment job re-reads what is
    /// there. A drawn page with a null here would be a page that shipped with no verdict at all,
    /// which is the state the audit found and the state the release gates exist to refuse.
    /// </summary>
    public string? QaJson { get; init; }

    /// <summary>
    /// Whether this page was adopted whole from an earlier attempt rather than drawn by this run.
    ///
    /// Adopted pages used to be filtered out of the artifact list entirely — the run had nothing
    /// new to say about them, so it said nothing — and amendment A4 is what that cost: the pages
    /// vanished from the record the release gates read, so a resumed book's QA coverage was
    /// whatever this attempt happened to redraw. They are in the list now, flagged, carrying no
    /// receipt of their own.
    ///
    /// Which makes the flag a contract with the fulfilment layer rather than a label: an artifact
    /// with this set has an empty <see cref="ManifestJson"/> and an empty
    /// <see cref="OutputSha256"/> — this run composited nothing, so there is no receipt to write —
    /// and storing it as though it were one would put an empty composition entry where the earlier
    /// attempt's real one belongs.
    /// </summary>
    public bool Adopted { get; init; }
}

/// <summary>
/// The press cover wrap and its paperwork: the generated 512:245 base (stored for the audit
/// package), the exact-Beki composite that gets typeset and press-prepared, the composition
/// manifest recording the pose, its hash and the locked front-panel anchor, and the resolved
/// prompt the base was generated from.
/// </summary>
public sealed record CompositeCoverWrap(
    byte[] BasePng, byte[] CompositePng, string ManifestJson, string PoseId, string Prompt);

/// <summary>
/// What an earlier attempt at this same book left behind, and what this attempt may therefore
/// adopt instead of paying for again.
/// </summary>
/// <param name="ScenarioJson">
/// The Visual Scenario that attempt planned, if it got that far.
///
/// Adopting it is not an optimisation. The scenario fixes the child's outfit and the recurring
/// elements for the whole book, and a resumed run that planned a fresh one would dress the child
/// differently on the spreads it redraws from the spreads it adopts — a book where the child
/// changes clothes at page four, assembled entirely from pages that each passed review.
/// </param>
/// <param name="Spreads">Composited pages already accepted and stored, by page number.</param>
/// <param name="BaseImages">
/// The pre-composite base of each of those pages, by page number, so continuity survives a resume.
/// Sparse is normal — a page stored before base images were kept has none.
/// </param>
public sealed record CompositeResumeState(
    string? ScenarioJson,
    IReadOnlyDictionary<int, byte[]> Spreads,
    IReadOnlyDictionary<int, byte[]> BaseImages)
{
    /// <summary>
    /// The child identity spec that attempt derived, as it was stored.
    ///
    /// Adopting it matters for the same reason adopting the scenario does. The four attributes are
    /// written into every image prompt, so a resumed run that derived a second spec — from the same
    /// photograph, by the same model, and quite possibly with "wavy" where the first said "curly" —
    /// would redraw its missing spreads to a different description of the same child than the ones
    /// it is adopting. Every page would still pass its own review.
    ///
    /// Null, unreadable, or written by a different derivation prompt version all mean the same
    /// thing: derive a new one. See <see cref="CompositeChildIdentity.TryReadStored"/>.
    /// </summary>
    public string? IdentitySpecJson { get; init; }

    /// <summary>
    /// The base image of the accepted first spread — the child appearance anchor every later
    /// spread is drawn and reviewed against.
    ///
    /// It is not a second copy of anything: it is <see cref="BaseImages"/>' entry for spread one,
    /// named separately because its job is different. As a continuity reference a base teaches a
    /// later page about a creature; as the anchor it teaches every later page what this child looks
    /// like once drawn. A resumed run that adopted spread one but cannot produce this has no anchor,
    /// and the honest answer there is to redraw spread one rather than to draw seven pages of a
    /// child nothing pins down.
    /// </summary>
    public byte[]? AnchorBasePng { get; init; }

    /// <summary>
    /// The book-level review an earlier attempt stored, as it was written.
    ///
    /// Adopted for two fields only, and see <see cref="CompositeBookReview.MergedWith"/> for why
    /// those two: a shot advisory belongs to the attempt that reviewed the page, and an adopted page
    /// was reviewed by somebody else. Without it, a resume that adopts seven pages writes a review
    /// saying the book has no shot trouble, and the fulfilment job overwrites the earlier attempt's
    /// document with that — silence where there were observations, on pages nobody will look at
    /// again.
    ///
    /// Null is normal and harmless: a first attempt, a book that failed before the review existed,
    /// or a document that could no longer be read. It means there is nothing to complete this
    /// attempt's own reading with.
    /// </summary>
    public string? ReviewJson { get; init; }

    /// <summary>
    /// The per-page QA verdict each stored spread was accepted on, by page number, exactly as it
    /// was written — D7, amendment A4.
    ///
    /// Carried so that a resumed book cannot lose its QA record. An adopted page's evidence is the
    /// document the attempt that drew it wrote; this attempt neither reviewed the page nor can
    /// invent a verdict for it, so the only two honest outcomes are "the record is there and the
    /// page may be adopted" and "the record is gone and the page is not what it claimed to be".
    ///
    /// What makes it a version guard rather than a presence check is
    /// <see cref="CompositeSpreadQa.TryReadStored"/>: a verdict written by an older reviewer
    /// contract answered older questions, and is treated as no verdict.
    ///
    /// Empty is not the same as "page N is missing", and the difference decides what happens.
    /// Empty means the caller does not supply QA at all — a book stored before this campaign, or a
    /// caller that predates it — and the run adopts as it always did, saying so in its warnings so
    /// that the release gates, not the pipeline, answer for the gap. A non-empty map missing page
    /// N means this caller does supply QA and page N's is gone, which is evidence about the page:
    /// it is redrawn rather than adopted.
    /// </summary>
    public IReadOnlyDictionary<int, string> SpreadQaJson { get; init; } =
        new Dictionary<int, string>();

    public static readonly CompositeResumeState Empty = new(
        null,
        new Dictionary<int, byte[]>(),
        new Dictionary<int, byte[]>());
}

/// <summary>
/// One run of the pipeline, as one object.
///
/// A record rather than nine positional parameters, because three of them are optional, two are
/// callbacks and the difference between "no scenario yet" and "a scenario to adopt" is the whole
/// resume story. A call site that has to count commas is a call site that will one day pass the
/// composite where the base belongs.
/// </summary>
public sealed record CompositeBookRequest
{
    public required CompositeBookContext Context { get; init; }

    /// <summary>The plan the parent previewed. Null asks the composite planner for a new one.</summary>
    public MasterStory? ExistingPlan { get; init; }

    public required byte[] ChildPhoto { get; init; }

    public required string ChildPhotoContentType { get; init; }

    public CompositeResumeState Resume { get; init; } = CompositeResumeState.Empty;

    /// <summary>
    /// Called with the validated scenario before the first image call, and awaited.
    ///
    /// Before, and awaited, for one reason: a job that dies during spread three has to come back to
    /// the scenario those three pages were drawn against. Persisting it with the finished book
    /// would mean the only attempt that stores it is the attempt that did not need it.
    /// </summary>
    public Func<string, Task>? OnScenario { get; init; }

    /// <summary>
    /// Called with the derived identity spec before the first image call, and awaited — the
    /// scenario callback's timing, for the scenario callback's reason.
    /// </summary>
    public Func<string, Task>? OnIdentitySpec { get; init; }

    /// <summary>
    /// Called once per finished page, in page order, one at a time — the same contract the legacy
    /// generator's callback has, and for the same reason: a parent is watching a spinner for
    /// several minutes.
    ///
    /// "In page order, one at a time" is a promise this pipeline keeps on the callback's behalf and
    /// not a description of how the pages are drawn. Spreads two to eight are drawn concurrently;
    /// the callback still sees two, then three, then four, with the previous call finished before
    /// the next begins. That is not politeness — the fulfilment job's callback mutates a dictionary
    /// and a counter and rewrites one manifest blob, none of which survives being run twice at
    /// once, and a manifest written out of order describes a book with holes in it.
    ///
    /// What it no longer promises is that nothing else is happening: page five may be generating
    /// while page two's picture is being uploaded.
    /// </summary>
    public Func<CompositeSpreadResult, Task>? OnSpread { get; init; }

    /// <summary>
    /// Called once the anchor is settled and before any other spread is drawn, and awaited — the
    /// request's own copy of <see cref="CompositeBookContext.OnAnchorAccepted"/>, which is the
    /// fallback when a caller sets neither here.
    /// </summary>
    public Func<CompositeAnchorAccepted, Task>? OnAnchorAccepted { get; init; }
}

/// <summary>Everything one run of the pipeline produced.</summary>
public sealed record CompositeBookResult
{
    public required MasterStory Plan { get; init; }

    public required StoryBoundaryOutput Boundary { get; init; }

    public required VisualScenarioV2 Scenario { get; init; }

    public required IReadOnlyList<CompositeSpreadResult> Spreads { get; init; }

    public required CompositeBookArtifacts Artifacts { get; init; }

    /// <summary>
    /// The eight attributes this book's child was drawn to, for the one picture this pipeline does
    /// not draw.
    ///
    /// The cover is the picture a parent judges the book by and the one the owner watched lose the
    /// eye colour "almost always". It is composed by the legacy upright-cover builder, which knows
    /// nothing about any of this — so the spec comes out here rather than staying private, and the
    /// caller that owns cover composition writes it into the lock.
    /// </summary>
    public required ChildIdentitySpec Identity { get; init; }

    /// <summary>
    /// The accepted first spread's base: the same picture spreads 2-8 were matched against, handed
    /// out so the cover can be matched against it too.
    ///
    /// Present on a fully-adopted resume as well, restored from what the earlier attempt stored —
    /// which is why it is not on its own a licence to redraw anything. See
    /// <see cref="SpreadsDrawnThisRun"/>.
    /// </summary>
    public byte[]? Anchor { get; init; }

    /// <summary>
    /// How many spreads this particular run actually drew, as opposed to adopted.
    ///
    /// It exists because <see cref="Anchor"/> could not answer the question the cover needs asked.
    /// A resume that adopts all eight pages still hands back an anchor — the stored one — and a
    /// caller reading only that would conclude a fresh book had just been drawn, redraw the cover
    /// against it, and upload a second cover over the reviewed one an earlier attempt had already
    /// stored. Zero here means this run changed no artwork, so there is nothing for a cover to be
    /// brought back into agreement with.
    /// </summary>
    public int SpreadsDrawnThisRun { get; init; }

    /// <summary>
    /// What this book is worth flagging for a person, none of which failed it: how often the pose
    /// table fell back, what the Georgian check-list found in the printed copy, and which pages the
    /// reviewer thought were shot wrongly.
    /// </summary>
    public required CompositeBookReview Review { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public interface ICompositeBookPipeline
{
    /// <summary>
    /// Input to eight composited spreads: normalize, story, boundary, Visual Scenario, then per
    /// page an image, a pose, a composite and a review.
    /// </summary>
    Task<CompositeBookResult> RunAsync(CompositeBookRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// The continuous cover base, which this campaign cannot draw.
    ///
    /// Present, and failing loudly, rather than absent: the cover contract requires seven regions
    /// from the printer-approved dieline and says in as many words that a missing geometry stops
    /// the job with <see cref="CompositeFailureCodes.LayoutFailed"/> and never substitutes the
    /// interior bleed. A method that quietly returned the legacy cover would be that substitution
    /// with extra steps.
    /// </summary>
    Task<byte[]> DrawCoverAsync(
        CompositeBookContext context,
        VisualScenarioV2 scenario,
        byte[] childPhoto,
        string childPhotoContentType,
        CancellationToken cancellationToken);

    /// <summary>
    /// The press cover: one continuous 512 × 245 mm hardcover wrap generated against the Locked
    /// Print Specification's dieline (<see cref="BekiCoverDieline"/>), with the exact approved
    /// Beki pose composited onto the front board and the receipt to prove it. One paid image
    /// call — two when the first one paints the dieline into the art (audit-2 P0-03) — and the
    /// caller typesets the title and press-prepares the result.
    /// </summary>
    /// <param name="identity">
    /// The book's own child identity spec, written into the cover prompt's CHILD IDENTITY LOCK by
    /// the same builder every spread uses. Required — owner's rule 2, 2026-09-01: "characters must
    /// be consistent on cover and spreads", enforced by giving both pictures the same instruction
    /// rather than by looking at them afterwards.
    /// </param>
    /// <param name="childAnchor">
    /// The accepted first spread's base, when the caller has it. Attached as the leading reference
    /// exactly as spreads 2-8 attach it, which is what makes the cover a reproduction of a child
    /// this book has already drawn. Null is honest, and is precisely spread one's condition.
    /// </param>
    Task<CompositeCoverWrap> DrawCoverWrapAsync(
        CompositeBookContext context,
        VisualScenarioV2 scenario,
        byte[] childPhoto,
        string childPhotoContentType,
        ChildIdentitySpec identity,
        byte[]? childAnchor,
        CancellationToken cancellationToken);
}

/// <summary>
/// The composite pipeline, end to end (handoff §6, steps 0 through 7).
///
/// One class rather than a stage-per-service arrangement, because the stages are not independently
/// useful: there is exactly one order they run in, each one's output is the next one's only input,
/// and a scenario without a boundary or a composite without a scenario is not a thing anybody
/// wants. What that buys is that the whole sequence — including which failure code stops it where —
/// is readable top to bottom in one file.
///
/// Two rules shape everything below.
///
/// Beki is never generated. The image model receives the child's photograph and the approved world
/// reference and is told, in the prompt's hard constraints, not to draw her or anything like her;
/// she arrives afterwards as an exact PNG at coordinates the config decides. Every place this class
/// assembles a reference list is a place that rule could be broken by adding one entry, so the
/// reference lists are short and built in one method.
///
/// Nothing is retried more than the contract allows. One Visual Scenario retry, one identity
/// derivation retry, one base regeneration, one re-composite, one QA parse retry, and then the book
/// stops with a code. The legacy pipeline learned this the expensive way — a refused spread redrawn
/// twice changed no outcome and doubled the bill — and the counts here are the supplier's own
/// numbers from <c>pipeline_config_v1.json</c>.
///
/// A retry that cannot change anything is not one of them. The re-composite used to hand the
/// reviewer the identical picture, which cost a book its seventh spread and taught the rule the
/// ladder in <see cref="DrawSpreadAsync"/> now follows: every rung must produce a different page,
/// and a rung with nothing left to change is skipped rather than spent.
/// </summary>
public sealed class CompositeBookPipeline(
    IStoryModelClient storyClient,
    IOpenAiService openAi,
    IMasterStoryService masterStory,
    IOptions<BekiOptions> bekiOptions,
    IOptions<BekiPrintLayoutOptions> printLayoutOptions,
    ILogger<CompositeBookPipeline> logger) : ICompositeBookPipeline
{
    /// <summary>
    /// The shape asked of the image provider.
    ///
    /// The same value the legacy Beki path uses, and for the same reason: the providers offer three
    /// or four fixed shapes and none of them is 15:7, so the widest landscape on offer is the one
    /// that survives normalization with the least thrown away. <see
    /// cref="CompositeDeterministicChecks"/> is what actually enforces that the render can become a
    /// printed spread.
    /// </summary>
    public const string SpreadImageSize = BekiBookGenerator.SpreadImageSize;

    /// <summary>
    /// The page whose accepted base becomes the child appearance anchor for the rest of the book.
    ///
    /// One rather than "whichever page is drawn first", and named here rather than assumed in two
    /// files. The scenario is validated as exactly eight spreads numbered 1 to 8 in order, so the
    /// first page of the book is the first page drawn on a fresh run; the constant exists so that
    /// the fulfilment job — which has to hand a resumed run the right stored base image — is
    /// reading the same answer this pipeline is, rather than its own copy of the number 1.
    /// </summary>
    public const int AnchorSpreadNumber = 1;

    private readonly BekiOptions _options = bekiOptions.Value;

    /// <summary>
    /// The registry, the config and the nine pose PNGs, loaded on first use and not before.
    ///
    /// Lazy because this service is registered unconditionally and injected into the illustrator,
    /// so it is constructed for every book in production — including every book drawn by the legacy
    /// path, on a deployment where the composite assets may not even be present. Loading 16 MB of
    /// artwork and verifying nine hashes to then not use any of it would be a tax on the path this
    /// flag exists to leave alone.
    /// </summary>
    private readonly Lazy<BekiCompositeEngine> _engine = new(() => BekiCompositeEngine.Create());

    public async Task<CompositeBookResult> RunAsync(
        CompositeBookRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var context = request.Context;
        var childPhoto = request.ChildPhoto;
        var childPhotoContentType = request.ChildPhotoContentType;
        var resume = request.Resume ?? CompositeResumeState.Empty;

        var warnings = new List<string>();

        // ---- Step 0: validate and normalize, before anything is paid for --------------------
        var normalized = InputNormalization.Normalize(context.Input, childPhoto);
        if (!normalized.IsValid)
        {
            throw new CompositePipelineException(
                CompositeFailureCodes.InvalidBookInput,
                $"The book input cannot be used: {string.Join(" ", normalized.Problems)}");
        }

        var input = normalized.Story!;
        var theme = CompositeThemeReferences.For(input.ThemeId);

        logger.LogInformation(
            "Composite pipeline {JobId}: age band {AgeBand}, gender {Gender}, theme {ThemeId} "
            + "({ThemeName}), reference {ThemeFile}.",
            context.JobId, input.AgeBand, input.ChildGender, input.ThemeId, theme.OfficialName,
            theme.FileName);

        // ---- Step 1: the story ---------------------------------------------------------------
        var plan = request.ExistingPlan ?? await WriteStoryAsync(context, input, cancellationToken);

        if (request.ExistingPlan is not null)
        {
            // Adopted, never rewritten. The parent read this story and bought it; the composite
            // prompt would write a different one, and a book that is not the book somebody chose
            // is a worse outcome than a book written by the older prompt. Everything the older
            // prompt puts in that this pipeline must not carry — English copy, the appearance
            // paragraph, the eye colour — is dropped by the boundary below rather than trusted.
            logger.LogInformation(
                "Composite pipeline {JobId}: adopting the story the parent previewed; no new "
                + "planning call.", context.JobId);

            var adoption = await AdoptedStoryNamesTheChildAsync(
                context, input, plan, cancellationToken);

            plan = adoption.Plan;

            /*
              A replaced story takes the artwork with it — review finding 3.

              The name-fidelity replan writes a DIFFERENT book: new title, new beats, new prose on
              every page. Everything a resumed run holds was planned from the story that has just
              been thrown away — the Visual Scenario was written from its boundary, and every stored
              spread was drawn from that scenario's pages. Leaving `resume` alone meant the run then
              adopted all of it: the new text laid over the old pictures, page by page, each picture
              illustrating a beat that is no longer in the book. Every stored page still passes its
              own review, because nothing reviews a picture against the sentence beside it.

              So the scenario and the pages go, following the same invalidation idiom the resume
              machinery uses everywhere else (a stored spec from a superseded prompt version, a
              missing appearance anchor): drop the artwork, keep what is still true of this child,
              and redraw the whole book under one plan. The anchor goes with the pages for the reason
              it always does — it is the first page of the discarded book.

              What survives is the identity spec, which is derived from the photograph and knows
              nothing about the story, and the earlier attempt's review, which records what that
              attempt spent rather than what it drew.

              It costs eight images. The alternative costs a book whose pictures are about a
              different story, which is the outcome the replan was performed to avoid.
            */
            if (adoption.Replaced
                && (resume.Spreads.Count > 0 || resume.ScenarioJson is { Length: > 0 }))
            {
                logger.LogWarning(
                    "Composite pipeline {JobId}: the previewed story was replaced over the child's "
                    + "name, so the {Spreads} stored spread(s) and the stored Visual Scenario were "
                    + "discarded — they belong to the story this run just stopped printing. The "
                    + "book is redrawn from the new plan.",
                    context.JobId, resume.Spreads.Count);

                warnings.Add(
                    $"{resume.Spreads.Count} spread(s) and the Visual Scenario from an earlier "
                    + "attempt were discarded: the previewed story misspelled the child's name and "
                    + "was rewritten, and artwork planned for the old story would illustrate a book "
                    + "this one no longer tells.");

                resume = resume with
                {
                    ScenarioJson = null,
                    Spreads = new Dictionary<int, byte[]>(),
                    BaseImages = new Dictionary<int, byte[]>(),
                    AnchorBasePng = null,
                };
            }
        }

        var boundaryResult = StoryBoundary.From(plan);
        if (!boundaryResult.IsValid)
        {
            throw new CompositePipelineException(
                CompositeFailureCodes.StoryFailed,
                $"The story cannot be mapped to the boundary: {string.Join(" ", boundaryResult.Problems)}");
        }

        var boundary = boundaryResult.Boundary!;

        /*
          The deterministic Georgian read, on the copy that will actually be printed.

          Here rather than after the pictures because this is where the prose is settled: the plan is
          either the one the parent previewed or the one this run just wrote, and nothing downstream
          edits a Georgian word. Flags, never repairs — see CompositeGeorgianCheck for why the fix
          belongs to the polish pass and why a silent correction would hide the miss.

          It cannot fail a book. A misspelling is a thing to tell somebody about, not a reason to
          refuse an order that is otherwise a finished book.
        */
        var georgianFlags = CompositeGeorgianCheck.Inspect(plan);

        /*
          A check-list that could not be fully loaded is reported, not swallowed and not fatal.

          "No flags" and "no rules ran" look identical on a finished book, and only one of them
          means the Georgian was read. So a broken rule lands in the log and on the book's own
          record, while the rules that did compile still run — an advisory check may not be the
          reason a paid book fails, least of all over its own configuration.
        */
        foreach (var problem in CompositeGeorgianCheck.RuleProblems)
        {
            logger.LogWarning(
                "Composite pipeline {JobId}: georgian_checklist_problem — {Problem} This book was "
                + "checked by the remaining rules only.", context.JobId, problem);

            warnings.Add(
                $"Georgian check-list ({CompositeGeorgianCheck.ChecklistVersion}): {problem} This "
                + "book was checked by the remaining rules only.");
        }

        foreach (var flag in georgianFlags)
        {
            logger.LogWarning(
                "Composite pipeline {JobId}: georgian_text_flag rule={Rule} location={Location} "
                + "found=\"{Found}\" expected={Expected}. Flagged for human review; nothing was "
                + "rewritten.",
                context.JobId, flag.RuleId, flag.Location, flag.Found, flag.Expected);

            warnings.Add(
                $"Georgian check-list ({CompositeGeorgianCheck.ChecklistVersion}): {flag}. This book "
                + "is flagged for human reading; no text was changed.");
        }

        // ---- Step 2: the Visual Scenario ------------------------------------------------------
        //
        // Adopted when a previous attempt at this book already planned one, and planned afresh
        // otherwise. Adopting is the correctness case, not the cheap one: the scenario fixes the
        // outfit and the recurring elements for all nine pictures, so a resumed run that planned a
        // second scenario would redraw its missing spreads against a different outfit from the ones
        // it is adopting — and every page would still pass its own review.
        var adoptedScenario = AdoptScenario(context, resume, warnings);

        if (adoptedScenario is null)
        {
            /*
              A replan and adopted artwork cannot both stand.

              The scenario is what every page was drawn against — the outfit, the recurring
              elements — so a book planned twice is a book drawn to two different specifications.
              Keeping the pages and planning a new scenario produces the exact failure the whole
              resume path exists to avoid, and it produces it silently: eight images that each
              passed their own review, a scenario record that describes none of them, and a child
              who changes clothes partway through.

              Which way it resolves depends on what has already been paid for.

              With pages already drawn and stored, redrawing them is money spent twice on artwork
              somebody may have already looked at, and the cause is not a book fault — it is a
              scenario this deployment can no longer read or no longer accepts, which is an
              operational question. So the job stops, names the stored scenario, and a person
              decides whether to clear it and redraw or to fix what made it unreadable.

              With nothing drawn there is nothing to lose and no decision to make: the bases are
              dropped along with the pages, because a base image belongs to the scenario its page
              was planned under, and the run plans freely.
            */
            if (resume.Spreads.Count > 0)
            {
                logger.LogError(
                    "Composite pipeline {JobId}: {Adopted} spread(s) are already stored but their "
                    + "Visual Scenario cannot be used, so this book would be finished against a "
                    + "scenario those pages were never drawn from. Stopping for a human.",
                    context.JobId, resume.Spreads.Count);

                throw new CompositePipelineException(
                    CompositeFailureCodes.VisualScenarioFailed,
                    $"{resume.Spreads.Count} spread(s) from an earlier attempt are stored, but the "
                    + "Visual Scenario they were drawn against is missing or no longer valid. "
                    + "Planning a new one would finish the book to a different specification. "
                    + "Clear the stored spreads to redraw the book, or restore the scenario.");
            }

            resume = CompositeResumeState.Empty;
        }

        var planned = adoptedScenario is { } already
            ? (already.Scenario, already.Json, PoseAudit: (CompositePoseAudit?)null, RetrySpent: false)
            : await PlanVisualScenarioAsync(context, input, theme, boundary, cancellationToken);

        var (scenario, scenarioJson) = (planned.Scenario, planned.Json);

        /*
          An adopted scenario is audited too, and it is worth saying why rather than skipping it.

          The pose count is a fact about the book that ships, not about the call that planned it. A
          resumed run adopts the scenario an earlier attempt wrote — possibly under the previous
          keyword table, possibly before the verb steering existed — and the finished book still has
          however many neutral hovers in it. What the resumed run does NOT do is spend a retry: the
          scenario is not being asked for again, the pages are already drawn against it, and
          replanning it is the one thing the whole resume path exists to prevent.
        */
        var poseAudit = planned.PoseAudit ?? CompositePoseVocabulary.Audit(_engine.Value.Registry, scenario);

        // Persisted before the first image call, and awaited, so that the attempt which dies on
        // spread three is not the attempt that never wrote down what it was drawing.
        if (request.OnScenario is not null)
        {
            await request.OnScenario(scenarioJson);
        }

        // ---- Step 2b: the child identity spec --------------------------------------------------
        //
        // Once per book, before any picture is bought, and required. See DeriveIdentityAsync.
        var storedIdentity = CompositeChildIdentity.TryReadStored(resume.IdentitySpecJson);

        /*
          Stored artwork with no spec to go with it is adopted as nothing at all.

          The four attributes are written into every image prompt, so pages drawn under one spec and
          pages drawn under another are pages of two different children — and a run that adopted the
          first while deriving the second would produce exactly that, from the same photograph, with
          every page passing its own review. There is no reading of a missing spec that makes the
          two halves match: the derivation is a model call over a photograph, so a second one is a
          second opinion, not a recovery of the first.

          A spec is missing here for one of three reasons and they all end the same way: an earlier
          attempt stored none, the blob is gone, or it was written by a derivation prompt this
          deployment no longer uses. So the artwork goes and the book is redrawn under one spec,
          which is the same answer a prompt-version change already gets from the resume contract.

          The scenario is untouched. Nothing is wrong with it — the outfit and the recurring elements
          it fixes are still the ones this book was sold as — and eight pages redrawn against it is
          a whole book rather than two halves.
        */
        if (resume.Spreads.Count > 0 && storedIdentity is null)
        {
            logger.LogWarning(
                "Composite pipeline {JobId}: {Stored} stored spread(s) have no usable child "
                + "identity spec, so the pages this attempt redrew could not be drawn to the same "
                + "child as the pages it adopted. Redrawing the whole book.",
                context.JobId, resume.Spreads.Count);

            warnings.Add(
                $"{resume.Spreads.Count} spread(s) from an earlier attempt were discarded: this "
                + "book's child identity spec is missing or was derived by a different prompt "
                + "version, and finishing the book without it would draw two different children "
                + "into one book.");

            resume = resume with
            {
                Spreads = new Dictionary<int, byte[]>(),
                BaseImages = new Dictionary<int, byte[]>(),
                // The anchor goes with them: it is the first page of the discarded book, and
                // matching seven fresh spreads to it would put the drift back one level down.
                AnchorBasePng = null,
            };
        }

        var identity = storedIdentity
                       ?? await DeriveIdentityAsync(context, request, childPhoto, cancellationToken);

        if (storedIdentity is not null)
        {
            logger.LogInformation(
                "Composite pipeline {JobId}: identity_spec_adopted promptVersion={PromptVersion}; "
                + "no new identity call.",
                context.JobId, CompositeChildIdentity.Version);
        }

        // ---- Steps 3-7: the anchor spread, then the rest ---------------------------------------
        var visualLock = scenario.VisualLock!;
        var continuity = new CompositeContinuity();
        var pages = scenario.Spreads!;

        // Validated as exactly eight, numbered 1 to 8 in order, so the first entry is spread one —
        // the page that produces the anchor and therefore the page that cannot be drawn beside any
        // other.
        const int anchorPage = AnchorSpreadNumber;

        var adopted = new Dictionary<int, CompositeSpreadResult>();
        var toDraw = new List<VisualScenarioSpread>(pages.Count);

        /*
          The anchor an earlier attempt left behind, decided before a single page is adopted.

          Named explicitly by the caller, or simply spread one's stored base — the same bytes
          either way. The named field exists so the fulfilment job can say which image is the
          anchor rather than leaving this class to know the page number twice; the fallback keeps
          a caller that supplies only the bases correct.
        */
        var anchor = resume.AnchorBasePng is { Length: > 0 } named
            ? named
            : resume.BaseImages.GetValueOrDefault(anchorPage);

        /*
          Stored artwork with no anchor to go with it is adopted as nothing at all.

          Redrawing only the missing anchor was the wrong repair, and it was wrong in the way this
          whole amendment exists to prevent. The stored pages were drawn against an anchor this
          attempt cannot see; a fresh spread one is a fresh stylization of the same child; so the
          pages this run redraws would be matched to the new one and the pages it adopts would keep
          the old — one book, two children, every page passing its own review. The same holds when
          spread one itself is missing while later pages are stored.

          So the whole book is redrawn under one anchor. It costs eight images, which is the price
          of the only outcome that is a book rather than two halves of two books; and it is the
          same answer a prompt-version change already gets from the resume contract.
        */
        if (resume.Spreads.Count > 0 && anchor is not { Length: > 0 })
        {
            logger.LogWarning(
                "Composite pipeline {JobId}: {Stored} stored spread(s) have no child appearance "
                + "anchor — spread {AnchorPage}'s base image is missing — so they were drawn "
                + "against a stylization this attempt cannot match. Redrawing the whole book.",
                context.JobId, resume.Spreads.Count, anchorPage);

            warnings.Add(
                $"{resume.Spreads.Count} spread(s) from an earlier attempt were discarded: spread "
                + $"{anchorPage}'s base image, which is the child appearance anchor for the whole "
                + "book, is missing, and finishing the book without it would mix two stylizations "
                + "of the same child.");

            resume = resume with
            {
                Spreads = new Dictionary<int, byte[]>(),
                BaseImages = new Dictionary<int, byte[]>(),
            };
        }

        /*
          Whether this caller keeps per-page QA records at all — D7, amendment A4.

          The distinction is the version guard, and it is drawn once rather than per page so that
          a resumed book gets one answer about itself. A caller that supplies no QA for any page
          is a caller from before the records existed; refusing to adopt on those grounds would
          redraw every book in flight at the moment this shipped, at eight paid image calls each,
          to establish something the release gates already refuse to guess about. A caller that
          supplies QA for some pages and not others is telling us something else entirely: those
          pages' evidence is gone.
        */
        var qaTracked = resume.SpreadQaJson.Count > 0;

        if (resume.Spreads.Count > 0 && !qaTracked)
        {
            logger.LogWarning(
                "Composite pipeline {JobId}: {Stored} stored spread(s) are being adopted without "
                + "per-page QA records — this book was stored before the records existed, or by a "
                + "caller that does not keep them. Their artifacts are flagged adopted with no "
                + "verdict, and the release gates decide what that is worth.",
                context.JobId, resume.Spreads.Count);

            warnings.Add(
                $"{resume.Spreads.Count} spread(s) adopted from an earlier attempt carry no stored "
                + "QA verdict, so this book's own record covers only the pages this attempt drew.");
        }

        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!resume.Spreads.TryGetValue(page.Page, out var alreadyDrawn)
                || alreadyDrawn.Length == 0)
            {
                toDraw.Add(page);
                continue;
            }

            /*
              A stored page whose verdict is gone is not a stored page.

              The audit's P0-09 finding was a package that shipped eight spreads and zero QA
              documents, and the correction it demands is that a missing mandatory QA artifact
              stops assembly. A resumed run that adopted a page whose record had been lost would
              be manufacturing exactly that package one page at a time — the picture is there, the
              evidence is not, and nothing downstream can tell the difference between a verdict
              that was lost and a verdict that was never asked for.

              So the page is redrawn, which is the resume path's existing answer to every piece of
              missing provenance: an unreadable scenario, an absent identity spec and a missing
              anchor all cost artwork rather than being worked around. This one costs the least of
              the four — one page, not the book — because a verdict belongs to a page and to
              nothing else.
            */
            if (qaTracked
                && CompositeSpreadQa.TryReadStored(
                    resume.SpreadQaJson.GetValueOrDefault(page.Page)) is null)
            {
                logger.LogWarning(
                    "Composite pipeline {JobId}: spread {Page} is stored but has no readable QA "
                    + "verdict from the reviewer contract now in force ({Version}), so it is being "
                    + "redrawn rather than adopted on a record nobody can produce.",
                    context.JobId, page.Page, CompositeMinimalQa.Version);

                warnings.Add(
                    $"Spread {page.Page} was redrawn rather than adopted: the QA verdict stored "
                    + "for it is missing or was written by a different reviewer prompt version.");

                toDraw.Add(page);
                continue;
            }

            /*
              An adopted page still teaches the pages after it.

              Skipping it entirely was the bug: spread two introduces the story's creature, a
              resumed run adopts spread two and redraws spread three, and spread three arrives
              with no continuity reference and redesigns the creature — in the middle of a book
              where the reader can see both pages at once.

              The reference restored here is the BASE image, never the composited one. The
              composite has the approved Beki pasted onto it, and the continuity instruction
              tells the model to copy the named elements from the attached picture: hand it a
              composite and the thing it is being shown is Beki.
            */
            var elements = CompositeIllustrationPrompt.ElementsFor(
                visualLock.RecurringElements, page.ChildWorldScene, page.Props).Required;

            if (resume.BaseImages.TryGetValue(page.Page, out var storedBase)
                && storedBase.Length > 0)
            {
                continuity.Remember(elements, storedBase);
            }
            else if (elements.Count > 0)
            {
                warnings.Add(
                    $"Spread {page.Page} was adopted without its base image, so the recurring "
                    + "elements it introduced cannot be a continuity reference for later spreads.");
            }

            adopted[page.Page] = AdoptedSpread(page.Page, alreadyDrawn);

            if (page.Page == anchorPage)
            {
                logger.LogInformation(
                    "Composite pipeline {JobId}: adopting spread {Page}'s stored base as the child "
                    + "appearance anchor for the rest of the book.", context.JobId, page.Page);
            }
        }

        var (drawn, bookAnchor) = await DrawSpreadsAsync(
            context, input, theme, scenario, toDraw, anchorPage, anchor, identity, continuity,
            childPhoto, childPhotoContentType, request.OnSpread,
            // The request's own hook when a caller set one, the context's otherwise — the same
            // fallback the identity callback uses, and for the same reason: the illustrator
            // between the fulfilment job and this class builds the request from the context.
            request.OnAnchorAccepted ?? context.OnAnchorAccepted,
            cancellationToken);

        var spreads = pages
            .Select(page => adopted.TryGetValue(page.Page, out var already)
                ? already
                : drawn[page.Page])
            .ToList();

        // Collected here rather than as each page lands, because the pages no longer land in order:
        // gathering them from the finished book keeps the warning list the same list it always was.
        foreach (var spread in spreads.Where(spread => spread.PoseFallback))
        {
            warnings.Add(
                $"Spread {spread.Page}: no pose keyword matched the scenario's Beki action, so "
                + "the neutral hover was composited.");
        }

        /*
          The book-level record: the three findings that are true of a book rather than of a page.

          Built from the scenario's audit rather than from the drawn pages, deliberately. A resumed
          run adopts pages it did not draw and knows nothing about how their poses were chosen; the
          scenario is what every page — adopted or drawn — was composited from, so replaying it is
          the only count that describes the finished book. The shot advisories come from the pages,
          because only a page that was actually reviewed has one.
        */
        var review = new CompositeBookReview
        {
            PoseRegistryVersion = _engine.Value.Registry.RegistryVersion,
            PoseKeywordRevision = _engine.Value.Registry.KeywordRevision,
            ScenarioPromptVersion = CompositeVisualScenarioPrompt.Version,
            PoseSelectionFallbacks = poseAudit.FallbackCount,
            PoseFallbackPages = poseAudit.FallbackPages,
            DistinctPoses = poseAudit.DistinctPoses,
            PoseVocabularyRetrySpent = planned.RetrySpent,
            PoseFallbackBudgetExceeded = poseAudit.ExceedsFallbackBudget,
            GeorgianFlags = georgianFlags,
            GeorgianChecklistVersion = CompositeGeorgianCheck.ChecklistVersion,
            GeorgianChecklistProblems = CompositeGeorgianCheck.RuleProblems,
            ShotAdvisories = spreads
                .Where(spread => spread.ShotNote is { Length: > 0 })
                .Select(spread => new CompositeShotAdvisory(
                    spread.Page, CompositeSpreadRhythm.ShotFor(spread.Page), spread.ShotNote!))
                .ToList(),
            AgeAdvisories = spreads
                .Where(spread => spread.AgeNote is { Length: > 0 })
                .Select(spread => new CompositeAgeAdvisory(
                    spread.Page, input.ChildAge, spread.AgeNote!))
                .ToList(),
        }
            /*
              …completed with what an earlier attempt observed about the pages this one adopted.

              An adopted page was reviewed by the run that drew it, and its shot advisory lives in
              that run's review and nowhere else. Rebuilding from what this attempt can see would
              write a book with no shot trouble in it and hand that to the fulfilment job, which
              overwrites the stored document — losing observations about pages nobody is going to
              look at again. The retry flag rides along for the same reason: a resumed run adopts
              the scenario and never re-asks, so it cannot know the retry was spent.
            */
            .MergedWith(
                CompositeBookReview.TryRead(resume.ReviewJson),
                adopted.Keys.ToHashSet());

        // One line, whole, in the same key=value idiom as every other observability line here. The
        // fallback count is the number R13 exists to drive down and the one to watch across books.
        logger.LogInformation(
            "Composite book review {JobId}: {Summary} registry={Registry} keywords={Keywords} "
            + "scenarioPrompt={ScenarioPrompt} needsHumanReading={NeedsReading}",
            context.JobId, review.Summary, review.PoseRegistryVersion, review.PoseKeywordRevision,
            review.ScenarioPromptVersion, review.NeedsHumanReading);

        return new CompositeBookResult
        {
            Plan = plan,
            Boundary = boundary,
            Scenario = scenario,
            Spreads = spreads,
            Identity = identity,
            Anchor = bookAnchor,
            SpreadsDrawnThisRun = drawn.Count,
            Review = review,
            Warnings = warnings,
            Artifacts = new CompositeBookArtifacts
            {
                ScenarioJson = scenarioJson,
                ReviewJson = review.ToJson(),
                Review = review,
                // The two the cover is drawn from — see CompositeBookArtifacts.Identity. The same
                // values the result carries; they are on the artifacts because the artifacts are
                // what the fulfilment job holds when it goes and draws the wrap.
                Identity = identity,
                Anchor = bookAnchor,
                /*
                  Every page of the book, adopted ones included — amendment A4.

                  The filter that used to stand here (`.Where(spread => !spread.Adopted)`) read as
                  obviously right: an adopted page produced no receipt this run could write, so
                  there was nothing to put in the list. What it actually did was decide what the
                  book's own record contains, and a resumed book's record then covered whatever
                  this attempt happened to redraw — six pages of evidence for an eight-page book,
                  with the gap indistinguishable from a book that only had six pages.

                  So an adopted page is in the list, flagged, carrying nothing: no manifest, no
                  output hash, no base, no QA. What it carries is the assertion that page N exists
                  and belongs to an earlier attempt, which is the one thing the release gates
                  cannot work out for themselves — and it is the fulfilment layer that goes and
                  reads that attempt's stored qa/spread-NN-qa.json, because it is the layer with a
                  storage account. See CompositeSpreadArtifact.Adopted for what an empty manifest
                  obliges that layer to do.
                */
                Spreads = spreads
                    .Select(spread => spread.Adopted
                        ? new CompositeSpreadArtifact(
                            spread.Page, string.Empty, string.Empty, string.Empty, [])
                        {
                            Adopted = true,
                            QaJson = null,
                        }
                        : new CompositeSpreadArtifact(
                            spread.Page,
                            spread.PoseId,
                            spread.Manifest.ToJson(),
                            spread.Manifest.Output.Sha256,
                            spread.BasePng)
                        {
                            QaJson = spread.QaJson,
                        })
                    .ToList()
            }
        };
    }

    public Task<byte[]> DrawCoverAsync(
        CompositeBookContext context,
        VisualScenarioV2 scenario,
        byte[] childPhoto,
        string childPhotoContentType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(scenario);

        var geometry = CompositeCoverGeometryResolver.TryResolve(printLayoutOptions.Value);

        if (geometry is null)
        {
            // Stated as a failure, not logged as a warning and worked around. The alternatives are
            // both worse than a stopped job: a cover generated to interior geometry puts the child
            // across the spine, and a book delivered without a cover is a book nobody can sell.
            logger.LogError(
                "Composite pipeline {JobId}: no printer-approved cover geometry is configured, so "
                + "the continuous cover base cannot be generated. The interior sheet's geometry is "
                + "not a substitute and is not being used.", context.JobId);

            throw new CompositePipelineException(
                CompositeFailureCodes.LayoutFailed,
                "The composite cover needs the active printer-approved cover geometry — back "
                + "panel, spine, hinge, front panel, title-safe, child/action, Beki integration and "
                + "wrap — and this deployment has none configured. The interior bleed must never be "
                + "substituted for it.");
        }

        /*
          There is deliberately nothing after that throw any more.

          What stood here was an unreachable builder, written out so that "which seven values does
          this need?" was answerable by reading it. Rule 2 (2026-09-01) added an eighth that this
          method genuinely cannot supply: the cover prompt now requires the book's child identity
          spec, and a reader-facing cover drawn before any spread exists has neither a derived spec
          nor an anchor to hand it. Keeping the sketch would have meant inventing one of the two
          inputs the whole rule is about, in code nobody runs, which is a worse guide than a
          sentence.

          The live cover is DrawCoverWrapAsync below — same prompt, same lock, same anchor — and it
          is what this method should be made to look like on the day the reader-facing flow gets a
          dieline of its own.
        */
        throw new UnreachableException(
            "The composite reader-facing cover has no geometry and the refusal above is total.");
    }

    /// <summary>
    /// <inheritdoc cref="ICompositeBookPipeline.DrawCoverWrapAsync"/>
    ///
    /// Beside <see cref="DrawCoverAsync"/> rather than replacing it, because the two answer
    /// different questions. That method serves the reader-facing cover flow, whose geometry
    /// resolver still refuses — a wrap squeezed onto the app's cover leaf would be the wrong
    /// picture. This one exists for the press package alone: the Locked Print Specification
    /// supplied the dieline the resolver was refusing to invent, as code
    /// (<see cref="BekiCoverDieline"/>), and the press cover is generated, cropped and
    /// Beki-composited against it.
    /// </summary>
    public async Task<CompositeCoverWrap> DrawCoverWrapAsync(
        CompositeBookContext context,
        VisualScenarioV2 scenario,
        byte[] childPhoto,
        string childPhotoContentType,
        ChildIdentitySpec identity,
        byte[]? childAnchor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(identity);

        var cover = scenario.Cover!;
        var input = InputNormalization.Normalize(context.Input, childPhoto).Story!;
        var theme = CompositeThemeReferences.For(input.ThemeId);

        /*
          The cover is drawn to the same child as the pages — owner's rule 2, 2026-09-01:
          "characters must be consistent on cover and spreads".

          It is enforced here, at the input, and not by a review. The observed defect was a book
          whose cover hero and spread hero were plainly different characters, and no reviewer could
          have caught it: each picture was good, and each was judged alone. What was actually wrong
          is that this call carried neither of the two things every spread carries — the CHILD
          IDENTITY LOCK, and the accepted first spread as the appearance anchor — so the cover was a
          ninth independent stylization of a photograph while spreads 2-8 were reproductions of one
          agreed drawing.

          Both now travel with the book (CompositeBookArtifacts.Identity/Anchor) and both are
          written by the builders the spreads use, numbered against the references this request
          actually attaches. An anchor may still be null on a press rebuild whose stored bases are
          gone; that is exactly spread one's condition — lock plus photograph — and it is honest
          rather than invented.
        */
        var anchored = childAnchor is { Length: > 0 };

        var prompt = CompositeIllustrationPrompt.ForCover(new CompositeCoverPromptInput
        {
            Geometry = BekiCoverDieline.Geometry,
            ChildAge = input.ChildAge,
            Theme = theme,
            FrontChildWorldScene = cover.FrontChildWorldScene!,
            BackEnvironment = cover.BackEnvironment!,
            ChildOutfit = scenario.VisualLock!.ChildOutfit!,
            RecurringElements = CompositeIllustrationPrompt.RelevantRecurringElements(
                scenario.VisualLock.RecurringElements, cover.FrontChildWorldScene),
            IdentitySpec = identity,
            AnchorAttached = anchored,
        });

        logger.LogInformation(
            "Composite pipeline {JobId} cover wrap: drawing to the book's own identity lock "
            + "({Version}); appearance anchor {Anchor}.",
            context.JobId, CompositeIllustrationPrompt.CoverVersion,
            anchored ? "attached" : "not available — the lock and the photograph carry it");

        var (raw, _) = await GenerateBaseImageAsync(
            context, page: null, prompt,
            References(childPhoto, childPhotoContentType, theme, childAnchor, continuityImage: null),
            cancellationToken);

        // To the wrap's own shape — 512:245 — not the interior's 15:7.
        var basePng = SpreadArtCrop.CropToRatio(raw, BekiCoverDieline.AspectRatio);

        /*
          The construction bands, measured — audit-2 P0-03, amendment A2.

          The old reasoning here was that the wrap's centre is the spine, which the prompt keeps
          low-information on purpose, and that a hinge-to-board tonal read would be judging
          bookbinding as if it were a story spread. So the wrap was measured by nothing at all.

          The supplier's audit is what that argument cost. The cover prompt names the centre
          construction as percentage regions — "from 47% to 53% of the canvas width" — the model
          painted the regions it was told about, and the shipped wrap carried "strong vertical
          tonal jumps at approximately x=1236 and x=1291 px", which on a 2528-wide, 512 mm cover
          are 250.5 mm and 261.5 mm: the exact spine boundaries, drawn as artwork. Hinge and spine
          geometry may guide placement; it may not be rendered.

          So the argument is answered rather than repeated. The reading is taken at the four
          dieline lines rather than at the middle, and its strips are sized to fit inside an 8 mm
          hinge, so what it judges is whether a boundary was PAINTED — not whether the two boards
          of a hardcover differ. A wrap is a continuous panorama by contract; four full-height
          discontinuities landing exactly on the four lines the prompt names is not a coincidence
          any measurement should keep quiet about.

          The wrap gets exactly one regeneration, which it never had before — the spread pattern,
          applied to the page that costs the same as a spread and is the first thing a parent sees.
        */
        var bands = CompositeSeamRepair.MeasureConstructionBands(basePng);

        if (bands.Exceeded)
        {
            logger.LogWarning(
                "Composite pipeline {JobId} cover wrap: the generated base has a full-height "
                + "discontinuity on {Count} of {Total} dieline boundaries — {Offending}. Spending "
                + "the one base regeneration.",
                context.JobId, bands.Offending.Count, bands.Bands.Count,
                string.Join("; ", bands.Offending));

            logger.LogWarning(
                "Composite pipeline {JobId} cover wrap: buying a new base image — the centre "
                + "construction is painted into the artwork.", context.JobId);

            var (retry, _) = await GenerateBaseImageAsync(
                context, page: null, prompt,
                // The same references as the first attempt, anchor included: the regeneration is
                // buying a different painting of the same child, not a different child.
                References(
                    childPhoto, childPhotoContentType, theme,
                    childAnchor, continuityImage: null),
                cancellationToken);

            basePng = SpreadArtCrop.CropToRatio(retry, BekiCoverDieline.AspectRatio);

            var second = CompositeSeamRepair.MeasureConstructionBands(basePng);

            if (second.Exceeded)
            {
                /*
                  The same policy question the fold asks, about the one page a parent sees first.

                  Flagged, the wrap ships with its bands measured and recorded: what the reading is
                  about — hinge and spine geometry rendered as artwork rather than merely guiding
                  placement — is a printing complaint, and the family's copy is a screen. Blocked, it
                  is audit-2 P0-03 exactly as written. Either way the press files are governed
                  separately by their own blocker-severity gates, so a wrap this measurement dislikes
                  never reaches a printer by accident.
                */
                if (Waivable(context, BekiReleaseChecks.CoverBands))
                {
                    await WaiveAsync(
                        context, BekiReleaseChecks.CoverBands, 0,
                        "both generated cover bases carry a full-height discontinuity on the dieline "
                        + $"boundaries {string.Join(", ", second.Offending.Select(band => band.Boundary.Name))}",
                        basePng, CoverBandEvidenceJson(bands, second));
                }
                else
                {
                    logger.LogError(
                        "Composite pipeline {JobId} cover wrap: the regenerated base still paints "
                        + "the centre construction — {Offending}. Stopping the book; the refused "
                        + "wrap and the numbers are stored as evidence.",
                        context.JobId, string.Join("; ", second.Offending));

                    throw new CompositePipelineException(
                        CompositeFailureCodes.ImageGenerationFailed,
                        "The cover wrap paints its own construction: both generated bases carry a "
                        + "full-height discontinuity on the dieline boundaries "
                        + $"{string.Join(", ", second.Offending.Select(band => band.Boundary.Name))}. "
                        + "Hinge and spine geometry may guide placement but may not be rendered as "
                        + "artwork, and no layout, upscale or colour conversion removes a band that "
                        + "is in the source.")
                    {
                        // No spread number: the cover is not a page of the book, and zero is the
                        // page number no book has. The evidence blob lands under spread zero, which
                        // is where somebody looking for a refused cover will find it.
                        Page = 0,
                        Evidence = new CompositeFailureEvidence(
                            0, basePng, CoverBandEvidenceJson(bands, second)),
                    };
                }
            }
            else
            {
                logger.LogInformation(
                    "Composite pipeline {JobId} cover wrap: the regenerated base reads as one "
                    + "continuous panorama across all {Total} dieline boundaries.",
                    context.JobId, second.Bands.Count);
            }
        }

        // The pose from the scenario's own cover sentence, composited at the locked front-panel
        // anchor — the exact-PNG discipline of every story page, applied to the one page that
        // never had it.
        var selection = BekiPoseSelector.Select(_engine.Value.Registry, cover.BekiAction!);

        if (selection.Fallback)
        {
            logger.LogWarning(
                "Composite pipeline {JobId} cover wrap: no pose keyword matched \"{Action}\"; "
                + "using {PoseId}.", context.JobId, cover.BekiAction, selection.PoseId);
        }

        var composite = _engine.Value.Composite(
            basePng,
            "cover-wrap-base.png",
            selection.PoseId,
            BekiCoverDieline.FrontBekiAnchor,
            "cover-wrap-composite.png");

        return new CompositeCoverWrap(
            basePng, composite.Png, composite.Manifest.ToJson(), selection.PoseId, prompt);
    }

    /// <summary>
    /// The stored scenario, when there is one and it still holds.
    ///
    /// Re-validated rather than trusted, and that is not defensiveness about our own storage. The
    /// scenario is checked against the supplied schema and the contract's semantic rules, and both
    /// of those are documents the illustration supplier revises: a scenario written under last
    /// month's rules can be a scenario this month's pipeline must not draw from. When it no longer
    /// validates the honest answer is a new one — a redrawn book against current rules beats a book
    /// half-drawn under each.
    ///
    /// Null means "plan one", which is also what an attempt that never got that far leaves behind.
    /// </summary>
    private (VisualScenarioV2 Scenario, string Json)? AdoptScenario(
        CompositeBookContext context, CompositeResumeState resume, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(resume.ScenarioJson))
        {
            return null;
        }

        var validation = VisualScenarioValidator.Validate(resume.ScenarioJson);

        if (!validation.IsValid)
        {
            logger.LogWarning(
                "Composite pipeline {JobId}: the stored Visual Scenario no longer validates, so a "
                + "new one is being planned — {Problems}", context.JobId, validation.Summary);

            warnings.Add(
                "The stored Visual Scenario no longer validates and was replanned; spreads adopted "
                + "from the earlier attempt were drawn against the old one.");

            return null;
        }

        logger.LogInformation(
            "Composite pipeline {JobId}: adopting the Visual Scenario an earlier attempt planned; "
            + "no new scenario call.", context.JobId);

        return (validation.Scenario!, resume.ScenarioJson!);
    }

    // -----------------------------------------------------------------------------------------
    // Step 1 — story
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// The adopted story, held to the same rule about the child's name as one this pipeline wrote —
    /// and replanned rather than refused when it fails.
    ///
    /// An adopted plan is the older, more dangerous case. It was written by whatever prompt was in
    /// force when the parent previewed it, which is by definition a version whose validation was
    /// weaker than today's: the observed defect of 2026-09-01 shipped through exactly this seam,
    /// because the preview's ladder did not yet look at the name and the fulfilment job adopted the
    /// story without re-reading it.
    ///
    /// Adoption is otherwise absolute — the parent read this story and bought it, and rewriting it
    /// would deliver a book nobody chose. The name is the one thing that outranks that. A story
    /// about a child called ვეკო that says ველო is not the story the parent chose either; it is the
    /// story they chose with their child's name spelled wrong, and the honest repair is a new one.
    ///
    /// So a blocker replans, once, and only the story: this runs before the visual scenario and
    /// before the first image call, so what it spends is text and never artwork. A flag keeps the
    /// adopted story and raises the alarm — an operator who has set this check to <c>flag</c> has
    /// said that a name they can see is wrong is not worth a rewrite, and this is not the place to
    /// argue with them.
    /// </summary>
    /// <returns>
    /// The story to draw, and whether it REPLACED the one the parent previewed. The second half is
    /// not bookkeeping: everything a resumed run holds was planned from the story that came in, so a
    /// caller that is handed a different one has stale artwork on its hands and has to be told
    /// (review finding 3). False covers both the adopted cases — the name was right, or the check is
    /// a flag and the previewed story ships as it is.
    /// </returns>
    private async Task<(MasterStory Plan, bool Replaced)> AdoptedStoryNamesTheChildAsync(
        CompositeBookContext context,
        NormalizedBookInput input,
        MasterStory plan,
        CancellationToken cancellationToken)
    {
        var problems = GeorgianNameFidelity.Inspect(plan, input.ChildName);

        if (problems.Count == 0)
        {
            return (plan, false);
        }

        var detail = NameFidelityDetail(problems, input.ChildName);

        if (Waivable(context, BekiReleaseChecks.NameFidelity))
        {
            await WaiveAsync(
                context, BekiReleaseChecks.NameFidelity, problems[0].Spread,
                $"the previewed story was adopted with the child's name misspelled — {detail}",
                [], NameFidelityEvidenceJson(problems, input.ChildName));

            return (plan, false);
        }

        logger.LogWarning(
            "Composite pipeline {JobId}: the previewed story misspells the child's name — {Detail}. "
            + "Writing a new story rather than printing it; no image has been drawn, so this costs "
            + "text and not artwork.", context.JobId, detail);

        return (await WriteStoryAsync(context, input, cancellationToken), true);
    }

    /// <summary>
    /// The story, and the one thing about it this pipeline checks before a single image is bought:
    /// that the book spells the child's name the way the parent typed it.
    ///
    /// **The observed defect, 2026-09-01.** A live run for a child called ვეკო came back titled
    /// „ველო და მოციმციმე ტყე“ — one Georgian letter, კ for ლ, in the child's own name, in the
    /// title. Nothing validated it. The title is canonical: it goes onto the cover, into the pack
    /// row and into the PDF's metadata, so a family paying to have their child in a book would have
    /// opened it to somebody else's name.
    ///
    /// This path had no plan validation at all. The preview's planner has had a
    /// one-retry-with-problems ladder since the composite prompt existed, and the fulfilment job's
    /// own planning call — the one that runs when there is no previewed story to adopt — went
    /// straight from the model to the boundary. So the ladder is here now, holding one rule: the
    /// name. Not the whole plan validator, deliberately — the spread count and the cast are already
    /// answered by <see cref="StoryBoundary"/> immediately below, and widening this seam is a
    /// separate decision about what a fulfilment run is allowed to reject.
    ///
    /// One retry, then the release policy, and see <see cref="SettleNameFidelityAsync"/> for what
    /// each answer means.
    /// </summary>
    private async Task<MasterStory> WriteStoryAsync(
        CompositeBookContext context, NormalizedBookInput input, CancellationToken cancellationToken)
    {
        var storyInput = CompositeStoryInput.From(input);

        var plan = await PlanStoryAsync(context, storyInput, [], attempt: 0, cancellationToken);

        var problems = GeorgianNameFidelity.Inspect(plan, input.ChildName);

        if (problems.Count == 0)
        {
            return plan;
        }

        logger.LogWarning(
            "Composite pipeline {JobId}: the story the planner wrote does not spell "
            + "\"{ChildName}\" the way the parent typed it — {Problems}. Asking again, once, with "
            + "the correction; nothing has been drawn yet, so this costs one text call.",
            context.JobId, input.ChildName,
            string.Join(" | ", problems.Select(problem => problem.ToString())));

        plan = await PlanStoryAsync(
            context, storyInput,
            problems.Select(problem => problem.ToString()).ToList(),
            attempt: 1, cancellationToken);

        var stillWrong = GeorgianNameFidelity.Inspect(plan, input.ChildName);

        return stillWrong.Count == 0
            ? plan
            : await SettleNameFidelityAsync(context, plan, stillWrong, input.ChildName);
    }

    /// <summary>
    /// One composite planning call, timed and logged in the observability contract's own shape.
    /// </summary>
    /// <param name="problems">
    /// The previous attempt's problems, numbered onto the composite prompt by the story service.
    /// Empty for a first attempt.
    /// </param>
    private async Task<MasterStory> PlanStoryAsync(
        CompositeBookContext context,
        CompositeStoryInput input,
        IReadOnlyList<string> problems,
        int attempt,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();

        try
        {
            var result = await masterStory.WriteCompositePlanAsync(input, problems, cancellationToken);

            started.Stop();
            LogModelCall(
                context, "story", result.Model, MasterStoryPromptComposite.Version,
                started.ElapsedMilliseconds, retryCount: attempt, validation: "accepted");

            return result.Story;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            started.Stop();
            LogModelCall(
                context, "story", masterStory.ModelName, MasterStoryPromptComposite.Version,
                started.ElapsedMilliseconds, retryCount: attempt, validation: "failed");

            throw new CompositePipelineException(
                CompositeFailureCodes.StoryFailed, "The composite story call failed.", ex);
        }
    }

    /// <summary>
    /// What happens to a book whose story still misspells the child after its corrective retry.
    ///
    /// Blocker — the shipped default, and the reason <c>name_fidelity</c> is the one check of ours
    /// that does not default to shipping — stops the run with a coded failure that names the word
    /// that was written. Nothing has been drawn at this point, so what is refused is a text call and
    /// not artwork, and the parent's order fails before any of it is spent.
    ///
    /// Flag ships the story and raises the alarm, with the offending word and its page as the
    /// evidence. There is no picture: this is a refusal about prose, which is why the evidence PNG
    /// is empty and the fulfilment job's recorder skips the upload rather than storing a zero-byte
    /// image somebody would later open.
    /// </summary>
    private async Task<MasterStory> SettleNameFidelityAsync(
        CompositeBookContext context,
        MasterStory plan,
        IReadOnlyList<NameFidelityProblem> problems,
        string childName)
    {
        var detail = NameFidelityDetail(problems, childName);

        if (Waivable(context, BekiReleaseChecks.NameFidelity))
        {
            await WaiveAsync(
                context, BekiReleaseChecks.NameFidelity, problems[0].Spread, detail,
                [], NameFidelityEvidenceJson(problems, childName));

            return plan;
        }

        logger.LogError(
            "Composite pipeline {JobId}: both attempts at the story misspelled the child's name — "
            + "{Detail}. Stopping the book before anything is drawn.", context.JobId, detail);

        throw new CompositePipelineException(
            CompositeFailureCodes.StoryFailed,
            $"The story does not spell the child's name: {detail}. The name is an input, not "
            + "something the story may choose, and a book whose title carries a misspelling of it "
            + "is the first thing the family would see.");
    }

    /// <summary>One line for a log, an alarm and a failure message: the words, and where.</summary>
    private static string NameFidelityDetail(
        IReadOnlyList<NameFidelityProblem> problems, string childName)
    {
        var written = problems
            .Where(problem => problem.Kind == NameFidelityProblem.NearMiss)
            .Select(problem => $"„{problem.Found}“ in the {problem.Location}")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return written.Count > 0
            ? $"the child is „{childName}“ and the story wrote {string.Join(", ", written)}"
            : $"the child is „{childName}“ and the story never names them";
    }

    /// <summary>
    /// The document that goes into the pack beside a waived name problem.
    ///
    /// Every problem, not just the first: a name written wrongly in the title is usually written
    /// wrongly on pages too, and an operator deciding whether to let a book go needs to know whether
    /// this is one word or the whole book.
    /// </summary>
    private static string NameFidelityEvidenceJson(
        IReadOnlyList<NameFidelityProblem> problems, string childName) =>
        JsonSerializer.Serialize(
            new
            {
                check = BekiReleaseChecks.NameFidelity,
                failure_code = CompositeFailureCodes.StoryFailed,
                story_prompt_version = MasterStoryPromptComposite.Version,
                expected_name = childName,
                problems = problems.Select(problem => new
                {
                    kind = problem.Kind,
                    location = problem.Location,
                    spread = problem.Spread,
                    found = problem.Found,
                }).ToList(),
            },
            CompositeJson.Readable);

    // -----------------------------------------------------------------------------------------
    // Step 2 — Visual Scenario v2
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// One call, one validation, one retry, then stop — the contract's own sequence.
    ///
    /// Both attempts go through the same validator, and the retry is sent the validator's short
    /// error list appended to the original ask. What it is not sent is a rewritten prompt: the
    /// second attempt has to be the same scenario without the fault, and a model given a different
    /// instruction returns a different book's pictures.
    ///
    /// v2.1 adds one more thing a scenario can be rejected for, and it is deliberately inside the
    /// same budget rather than beside it. After both validation layers pass, the pose registry is
    /// replayed over the eight Beki sentences — no model call, the same selector the pages use — and
    /// a book that would be composited from more than
    /// <see cref="CompositePoseVocabulary.MaxFallbacksPerBook"/> neutral hovers is treated as a
    /// semantic miss and spends the one retry it already had. It never buys a second one, and it
    /// never fails a book: a scenario that is still repetitive after its retry is drawn anyway, and
    /// the count is recorded. The fallback is an approved pose; six of them in eight pages is a
    /// quality signal, not a defect worth discarding a paid plan over.
    /// </summary>
    private async Task<(VisualScenarioV2 Scenario, string Json, CompositePoseAudit? PoseAudit, bool RetrySpent)>
        PlanVisualScenarioAsync(
        CompositeBookContext context,
        NormalizedBookInput input,
        CompositeThemeReference theme,
        StoryBoundaryOutput boundary,
        CancellationToken cancellationToken)
    {
        var inputJson = CompositeVisualScenarioPrompt.InputJson(input, theme, boundary);
        var model = VisualScenarioModel;

        VisualScenarioValidationResult? previous = null;

        // Whether the one retry was spent on the pose vocabulary specifically, rather than on a
        // schema or semantic fault. The record wants to distinguish them: "the planner was asked
        // again about its verbs" and "the planner returned invalid JSON" are different stories about
        // the same retry.
        var poseRetrySpent = false;

        for (var attempt = 0; attempt <= 1; attempt++)
        {
            var user = attempt == 0
                ? CompositeVisualScenarioPrompt.User(inputJson)
                : CompositeVisualScenarioPrompt.RetryUser(inputJson, previous!.Problems);

            var started = Stopwatch.StartNew();
            string? answer = null;
            Exception? failure = null;

            try
            {
                // JsonElement rather than the typed model: the validator needs the response as the
                // model actually wrote it, both to evaluate the supplied schema against it and to
                // store it. Deserializing to VisualScenarioV2 here would throw away the raw text on
                // exactly the responses that need explaining.
                var result = await storyClient.CompleteAsync<JsonElement>(
                    model,
                    // The contract's instruction plus v2.1's verb-family block — see
                    // CompositeVisualScenarioPrompt.SystemInstruction for why they are two members.
                    CompositeVisualScenarioPrompt.SystemInstruction,
                    user,
                    CompositeVisualScenarioPrompt.SchemaName,
                    CompositeVisualScenarioPrompt.ResponseSchema(),
                    cancellationToken);

                answer = result.Value.GetRawText();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A transport failure and an unparseable answer arrive here the same way, and both
                // are worth the one retry — the alternative is a book lost to a dropped connection.
                failure = ex;
            }

            started.Stop();

            var validation = failure is null
                ? VisualScenarioValidator.Validate(answer)
                : new VisualScenarioValidationResult
                {
                    IsValid = false,
                    Problems =
                    [
                        new VisualScenarioProblem(
                            VisualScenarioProblemCodes.MalformedJson,
                            $"the scenario call failed: {failure.Message}")
                    ]
                };

            /*
              The pose audit, run only on an answer that already passed both validation layers.

              Deterministic and free: it replays the registry over the eight Beki sentences with the
              selector the pages themselves use. Doing it here rather than in the validator keeps the
              two documents apart — the validator is the supplied schema plus the contract MD's own
              rules, and this is a fact about a different file with its own revisions.
            */
            CompositePoseAudit? audit = null;

            if (validation.IsValid)
            {
                audit = CompositePoseVocabulary.Audit(_engine.Value.Registry, validation.Scenario!);

                // Only on the first attempt. The retry is one retry; a second rejection here would
                // be the pipeline paying twice to be told the same thing, which is the exact rule
                // the whole retry budget exists to hold.
                if (audit.ExceedsFallbackBudget && attempt == 0)
                {
                    logger.LogWarning(
                        "Composite pipeline {JobId}: the Visual Scenario is valid but maps "
                        + "{Fallbacks} of {Spreads} spreads to the fallback pose (pages {Pages}); "
                        + "spending the one corrective retry on the Beki action vocabulary.",
                        context.JobId, audit.FallbackCount, audit.Choices.Count,
                        string.Join(", ", audit.FallbackPages));

                    validation = validation with
                    {
                        IsValid = false,
                        Problems = [CompositePoseVocabulary.Problem(audit)],
                    };

                    poseRetrySpent = true;
                    audit = null;
                }
            }

            LogModelCall(
                context, "visual_scenario", model, CompositeVisualScenarioPrompt.Version,
                started.ElapsedMilliseconds, attempt,
                validation.IsValid ? "accepted" : validation.Summary);

            if (validation.IsValid)
            {
                if (audit!.ExceedsFallbackBudget)
                {
                    // The retry has been spent and the second answer is still repetitive. The book
                    // is drawn: the fallback is an approved pose, and refusing a paid plan over
                    // Beki's variety would trade a delivered book for a better one nobody gets.
                    logger.LogWarning(
                        "Composite pipeline {JobId}: the Visual Scenario still maps "
                        + "{Fallbacks} of {Spreads} spreads to the fallback pose after its one "
                        + "retry (pages {Pages}). Drawing the book and recording the count.",
                        context.JobId, audit.FallbackCount, audit.Choices.Count,
                        string.Join(", ", audit.FallbackPages));
                }

                return (validation.Scenario!, answer!, audit, poseRetrySpent);
            }

            previous = validation;

            logger.LogWarning(
                "Composite pipeline {JobId}: Visual Scenario attempt {Attempt} rejected — {Problems}",
                context.JobId, attempt + 1, validation.Summary);
        }

        throw new CompositePipelineException(
            CompositeFailureCodes.VisualScenarioFailed,
            "Two Visual Scenario attempts were both invalid: " + previous!.Summary);
    }

    /// <summary>
    /// Which model plans the scenario.
    ///
    /// Empty configuration means the story provider's own model, which is what the handoff asks
    /// for — the slot exists so the scenario *can* be moved, not so it must be. The fallback is
    /// <see cref="IMasterStoryService.ModelName"/> rather than a literal, because that is the name
    /// the OpenAI client would need and the one the Gemini client is entitled to ignore in favour
    /// of its own configured story model.
    /// </summary>
    private string VisualScenarioModel =>
        string.IsNullOrWhiteSpace(_options.VisualScenarioModel)
            ? masterStory.ModelName
            : _options.VisualScenarioModel.Trim();

    // -----------------------------------------------------------------------------------------
    // Step 2b — the child identity spec
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// The four attributes every page of this book draws the child to, read once from the
    /// photograph.
    ///
    /// Required, and terminal when it cannot be had. That is the amendment's whole point. Before
    /// it, identity rode on the attached photograph alone, which means the model interpreted the
    /// same picture afresh nine times; the completed book that proved it wrong had a visibly
    /// different child on every spread and had passed all eight of its own reviews. So there is no
    /// soft-degrade here: two unusable answers stop the book with
    /// <see cref="CompositeFailureCodes.IdentitySpecFailed"/>, before the first image is paid for.
    ///
    /// Called only when there is no spec to adopt — see the caller, which discards a resumed run's
    /// artwork rather than let a second derivation describe the same child differently from the
    /// pages it would keep. The parent's eye colour is applied here, once, for the same reason: an
    /// adopted spec is adopted exactly as stored.
    ///
    /// Nothing in here logs an attribute, or a digest of one. The event and the prompt version are
    /// the whole record; the values live in the pack's private storage with the photograph they
    /// were read from.
    /// </summary>
    /// <param name="childPhoto">
    /// The photograph itself, and no content type beside it: the reviewer door takes bytes, and
    /// the normalizer behind it decodes by sniffing rather than by what it is told — a JPEG and a
    /// PNG reach the model as the same normalized picture either way.
    /// </param>
    private async Task<ChildIdentitySpec> DeriveIdentityAsync(
        CompositeBookContext context,
        CompositeBookRequest request,
        byte[] childPhoto,
        CancellationToken cancellationToken)
    {
        var model = _options.VisualReviewerModel;
        ChildIdentityParseResult? previous = null;

        for (var attempt = 0; attempt <= 1; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ask = attempt == 0
                ? CompositeChildIdentity.Prompt
                : CompositeChildIdentity.RetryPrompt(previous!.Problems);

            var started = Stopwatch.StartNew();
            string answer;

            try
            {
                /*
                  The existing multimodal reviewer door, not a new one.

                  It is already exactly this call — images plus an instruction, in, one text answer
                  out, validated against a schema by the caller — and on the Gemini route it is
                  already the vision model the handoff names for this work. A second method on the
                  illustration client would have been the same request with a different name on it,
                  and one more surface for the router, the OpenAI implementation and every test
                  double to keep in step.

                  The photograph goes in the "under review" position because that is the picture
                  being read; the prompt says in its first line what the image is.
                */
                answer = await openAi.ReviewIllustrationAsync(
                    childPhoto, ask, [], cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                started.Stop();
                LogModelCall(
                    context, "identity_spec", model, CompositeChildIdentity.Version,
                    started.ElapsedMilliseconds, attempt, "failed");

                // A transport failure and an unreadable answer are the same thing here: no spec.
                // Both are worth the one retry, and the second of either stops the book.
                previous = new ChildIdentityParseResult(
                    false, null, ["the identity call did not complete."]);

                continue;
            }

            started.Stop();

            var parsed = CompositeChildIdentity.Parse(answer);

            // parsed.Summary is value-free by construction — see ChildIdentityParseResult — which
            // is what makes it safe to put in the validation field of a log line.
            LogModelCall(
                context, "identity_spec", model, CompositeChildIdentity.Version,
                started.ElapsedMilliseconds, attempt, parsed.IsValid ? "accepted" : parsed.Summary);

            if (!parsed.IsValid)
            {
                previous = parsed;

                logger.LogWarning(
                    "Composite pipeline {JobId}: identity spec attempt {Attempt} rejected — {Problems}",
                    context.JobId, attempt + 1, parsed.Summary);

                continue;
            }

            var spec = CompositeChildIdentity.WithParentEyeColor(
                parsed.Spec!, context.Input.LegacyEyeColor);

            // The event and the version, and one boolean that is about the order form rather than
            // about the child: whether a parent-supplied eye colour replaced the derived one. No
            // attribute value and no digest of one — see CompositeChildIdentity for why a hash of
            // four low-entropy attributes salted with a job id that is logged beside it is the
            // attributes with extra steps.
            logger.LogInformation(
                "Composite pipeline {JobId}: identity_spec_derived promptVersion={PromptVersion} "
                + "parentEyeColour={ParentSupplied}.",
                context.JobId, CompositeChildIdentity.Version,
                !string.IsNullOrWhiteSpace(context.Input.LegacyEyeColor));

            // The request's own callback when a caller set one, and the context's otherwise. The
            // fallback is what lets the fulfilment job persist the spec without the illustrator in
            // between having to learn about it: that class builds this request from the context and
            // copies the callbacks it knows about, and this campaign did not touch it.
            var persist = request.OnIdentitySpec ?? context.OnIdentitySpec;

            if (persist is not null)
            {
                await persist(CompositeChildIdentity.ToStoredJson(spec));
            }

            return spec;
        }

        throw new CompositePipelineException(
            CompositeFailureCodes.IdentitySpecFailed,
            "The child's identity attributes could not be read from the photograph in two "
            + "attempts, and this book may not be drawn without them: " + previous!.Summary);
    }

    // -----------------------------------------------------------------------------------------
    // Steps 3-7 — the pages
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Draws every page this run owes, spread one alone and the rest together.
    ///
    /// Spread one is alone because it is the anchor: it is the picture the other seven are told to
    /// match the child against, so nothing else can start until it has been drawn and accepted.
    /// After that the remaining pages have no dependency on each other that a shared continuity
    /// reference does not already satisfy, and drawing them one at a time cost the first real book
    /// 651 seconds of mostly waiting.
    ///
    /// Three rules make the concurrency safe rather than merely fast.
    ///
    /// Delivery stays serialized and in spread order. The callback belongs to the fulfilment job,
    /// where it mutates a dictionary of stored URLs, advances a progress counter and rewrites one
    /// manifest blob; two of those at once corrupts the manifest and the third reports a book as
    /// further along than it is. <see cref="OrderedSpreadDelivery"/> is the whole of that promise.
    ///
    /// The first terminal failure stops the rest. A book fails as a book — one page's
    /// IMAGE_QA_FAILED is the run's failure — so the sibling token is cancelled the moment one page
    /// gives up, and the pages still in flight stop before their next paid call rather than
    /// finishing pictures for a book that is already over.
    ///
    /// The continuity reference is whatever was accepted when a page was scheduled. It is usually
    /// spread one, which is the accepted trade: a creature introduced mid-book may now be matched
    /// against the page that introduced it rather than against the page immediately before, and QA
    /// still checks it.
    /// </summary>
    private async Task<(IReadOnlyDictionary<int, CompositeSpreadResult> Drawn, byte[]? Anchor)>
        DrawSpreadsAsync(
        CompositeBookContext context,
        NormalizedBookInput input,
        CompositeThemeReference theme,
        VisualScenarioV2 scenario,
        IReadOnlyList<VisualScenarioSpread> toDraw,
        int anchorPage,
        byte[]? anchor,
        ChildIdentitySpec identity,
        CompositeContinuity continuity,
        byte[] childPhoto,
        string childPhotoContentType,
        Func<CompositeSpreadResult, Task>? onSpread,
        Func<CompositeAnchorAccepted, Task>? onAnchorAccepted,
        CancellationToken cancellationToken)
    {
        var drawn = new ConcurrentDictionary<int, CompositeSpreadResult>();

        if (toDraw.Count == 0)
        {
            // A fully adopted book still has an anchor — the stored one — and the caller waiting
            // to draw a cover against it is told so here, the same way as on every other run.
            await AnnounceAnchorAsync(anchor);

            return (drawn, anchor);
        }

        var delivery = new OrderedSpreadDelivery(
            toDraw.Select(page => page.Page).ToList(), onSpread);

        var remaining = toDraw;

        // The anchor page, when this run is the one drawing it: alone, first, and awaited.
        if (anchor is not { Length: > 0 })
        {
            var first = toDraw[0];

            if (first.Page != anchorPage)
            {
                // Unreachable: a page is either adopted with its base — which is what gives this
                // run an anchor — or it is in toDraw, and toDraw is in page order. Stated anyway,
                // because the alternative to a loud contradiction is seven spreads silently drawn
                // with no anchor at all.
                throw new CompositePipelineException(
                    CompositeFailureCodes.ImageGenerationFailed,
                    $"Spread {anchorPage} is neither adopted with its base image nor the first "
                    + "page to be drawn, so this run has no child appearance anchor.");
            }

            var anchorSpread = await DrawSpreadAsync(
                context, input, theme, scenario, first, continuity, childPhoto,
                childPhotoContentType, identity, anchor: null, cancellationToken);

            drawn[anchorSpread.Page] = anchorSpread;

            /*
              The base this page shipped with, which on a page that spent its one regeneration is
              the regenerated picture and never the refused draft.

              Under the release policy that base can now be one a reviewer refused, and it is still
              the anchor — deliberately. The anchor is what stops the child changing between pages,
              and a book whose first page was questioned is better off with seven pages that match it
              than with seven that match nothing: the drifting-child defect passed eight independent
              reviews precisely because each page was judged alone. The waiver is recorded, the page
              is alarmed, and a person looking at it is looking at a consistent book.

              Continuity is the other half and goes the other way — see DrawSpreadAsync, where a
              refused page is not remembered. That reference tells a later spread to REPRODUCE a
              particular creature, which is a stronger instruction than "this is the same child".
            */
            anchor = anchorSpread.BasePng;

            logger.LogInformation(
                "Composite pipeline {JobId}: spread {Page} accepted; its base is the child "
                + "appearance anchor for the remaining {Count} spread(s).",
                context.JobId, anchorSpread.Page, toDraw.Count - 1);

            /*
              The cover's cue, before the anchor page is even handed over.

              Everything the wrap needs is now settled — the scenario, the identity lock and this
              base — and the caller that draws it is told so before the anchor's own delivery
              (an upload and a manifest write) rather than after, because the wrap is a paid image
              call that takes as long as a spread does, and every second it starts earlier is a
              second off the job a parent is watching. It runs on the caller's own token, not the
              siblings': a failed spread cancels the siblings and throws, and it is the caller that
              decides what to do about a wrap it started.
            */
            await AnnounceAnchorAsync(anchor);

            await delivery.DeliverAsync(anchorSpread, cancellationToken);

            remaining = toDraw.Skip(1).ToList();
        }
        else
        {
            // The anchor was adopted from an earlier attempt: settled before this run drew
            // anything, so the cover may start beside the very first spread this run draws.
            await AnnounceAnchorAsync(anchor);
        }

        if (remaining.Count == 0)
        {
            return (drawn, anchor);
        }

        var concurrency = Math.Max(1, _options.SpreadConcurrency);
        var anchorImage = anchor!;

        logger.LogInformation(
            "Composite pipeline {JobId}: drawing {Count} spread(s) with at most {Concurrency} at "
            + "once.", context.JobId, remaining.Count, concurrency);

        // Linked, so the caller's own cancellation still stops everything, and cancellable by us,
        // so the first terminal failure stops everything too.
        using var siblings = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var slots = new SemaphoreSlim(concurrency, concurrency);

        Exception? terminal = null;

        var tasks = remaining.Select(DrawOneAsync).ToList();

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (Exception)
        {
            /*
              What the run failed of is the first page that gave up, not whichever exception
              Task.WhenAll happens to surface — and once the siblings are cancelled, most of what
              it surfaces is cancellation. So the real failure is captured at the moment it
              happens and rethrown here with its stack intact.
            */
            if (terminal is not null)
            {
                ExceptionDispatchInfo.Capture(terminal).Throw();
            }

            // No terminal failure means the caller cancelled us, which is not this run's to
            // reinterpret.
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }

        return (drawn, anchor);

        /*
          Once per run, and only with a real anchor. The hook is a promise that the three inputs
          the wrap needs exist; an announcement carrying nothing would be a promise the caller
          could not act on, and a run with no anchor at this point is a run that is about to draw
          one — it announces when it has.
        */
        async Task AnnounceAnchorAsync(byte[]? settled)
        {
            if (onAnchorAccepted is null || settled is not { Length: > 0 })
            {
                return;
            }

            await onAnchorAccepted(new CompositeAnchorAccepted(scenario, identity, settled));
        }

        async Task DrawOneAsync(VisualScenarioSpread page)
        {
            await slots.WaitAsync(siblings.Token).ConfigureAwait(false);

            CompositeSpreadResult spread;

            try
            {
                spread = await DrawSpreadAsync(
                    context, input, theme, scenario, page, continuity, childPhoto,
                    childPhotoContentType, identity, anchorImage, siblings.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Fail(ex);
                throw;
            }
            finally
            {
                slots.Release();
            }

            drawn[spread.Page] = spread;

            try
            {
                // Outside the slot on purpose: the callback uploads a picture and rewrites a
                // manifest, and holding a generation slot through somebody else's network calls
                // would make the concurrency limit a limit on uploads.
                await delivery.DeliverAsync(spread, siblings.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Fail(ex);
                throw;
            }
        }

        void Fail(Exception ex)
        {
            // First writer wins: the page that actually stopped the book is the one reported.
            Interlocked.CompareExchange(ref terminal, ex, null);

            logger.LogError(
                ex, "Composite pipeline {JobId}: a spread failed terminally; cancelling the "
                + "spreads still in flight.", context.JobId);

            siblings.Cancel();
        }
    }

    private async Task<CompositeSpreadResult> DrawSpreadAsync(
        CompositeBookContext context,
        NormalizedBookInput input,
        CompositeThemeReference theme,
        VisualScenarioV2 scenario,
        VisualScenarioSpread page,
        CompositeContinuity continuity,
        byte[] childPhoto,
        string childPhotoContentType,
        ChildIdentitySpec identity,
        byte[]? anchor,
        CancellationToken cancellationToken)
    {
        var textSide = CompositeSpreadRhythm.TextSideFor(page.Page);
        var visualLock = scenario.VisualLock!;

        // The prop-state contract decides what this page shows, forbids and matches on; a
        // scenario planned before v2.2 falls back to the fuzzy scene matching inside.
        var elementPlan = CompositeIllustrationPrompt.ElementsFor(
            visualLock.RecurringElements, page.ChildWorldScene, page.Props);
        var elements = elementPlan.Required;

        // Read once, when this page is scheduled: the most recently accepted base carrying one of
        // this page's recurring elements. With the pages drawn concurrently that is usually spread
        // one rather than the page immediately before, which is the trade the parallel campaign
        // accepted and QA's CAST_ERROR still checks.
        var reference = continuity.For(elements);

        var anchored = anchor is { Length: > 0 };

        var prompt = CompositeIllustrationPrompt.ForSpread(new CompositeSpreadPromptInput
        {
            Page = page.Page,
            ChildAge = input.ChildAge,
            Theme = theme,
            ChildWorldScene = page.ChildWorldScene!,
            ChildOutfit = visualLock.ChildOutfit!,
            RecurringElements = elementPlan.Annotated,
            ForbiddenElements = elementPlan.Forbidden,
            ContinuityElementNames = reference?.ElementNames ?? [],
            IdentitySpec = identity,
            AnchorAttached = anchored,
        });

        // Chosen from the scenario's Beki sentence and nothing else, before a single pixel exists.
        // The selection cannot depend on the picture, because the picture was drawn with a hole
        // shaped like this pose in it.
        var selection = BekiPoseSelector.Select(_engine.Value.Registry, page.BekiAction);

        if (selection.Fallback)
        {
            logger.LogWarning(
                "Composite pipeline {JobId} spread {Page}: no pose keyword matched \"{Action}\"; "
                + "pose_selection_fallback=true, using {PoseId}.",
                context.JobId, page.Page, page.BekiAction, selection.PoseId);
        }

        var (rawPng, generationMs) = await GenerateBaseImageAsync(
            context, page.Page, prompt,
            References(childPhoto, childPhotoContentType, theme, anchor, reference?.Image),
            cancellationToken);

        var basePng = NormalizeToSpread(context, page.Page, rawPng);

        var baseAttempts = 1;
        var recomposited = false;
        var regenerated = false;

        // Where Beki stands. Null means the deterministic default for this text side, which is what
        // every page starts from and what a regenerated base goes back to: the anchors are the
        // numbers the partners approved against a printed proof, and the adjusted one below is a
        // repair for a particular picture rather than a new default.
        BekiCompositeAnchor? placement = null;

        /*
          The centre-fold gate BLOCKS again — audit-2 P0-05, which reversed the telemetry-only
          ruling of 2026-08-31.

          The history is worth keeping, because both rulings were made from evidence. The
          measurement was calibrated on the veiled books the supplier rejected; the v1.5 prompt
          then changed what clean art measures like — a calm bright text side against a busy
          action side is the composition the prompt itself asks for — and on its first live
          outing the gate refused a page the evidence says was art, twice, and stopped a paid
          order. So it was demoted to a log line while the distributions were watched.

          Then the supplier audited a shipped book and rejected it: five of eight story spreads
          carried "an abnormal pixel jump exactly at x=1264/1265, the 50% fold coordinate", and
          the required correction is named in as many words — "add an automated centerline test".
          The book that proved the gate too eager and the book that proved it necessary are the
          same instrument read at two thresholds, and the audit settles which way the doubt goes.

          What makes the reversal affordable is that crossing the line is not a refusal any more.
          It buys the page's one base regeneration first — the same budget the reviewer spends,
          spent earlier and on arithmetic rather than on an opinion — and only a second picture
          that still measures as two halves stops the run. A false positive now costs one image
          call; the old single-tier gate's false positive cost a paid order.

          The severe tier no longer selects anything (blocking makes two tiers moot) and the
          numbers still go into the log, because they are the calibration data these thresholds
          will be re-judged from.
        */
        var centreField = CompositeSeamRepair.MeasureCentreField(basePng);

        if (centreField.Exceeded)
        {
            logger.LogWarning(
                "Composite pipeline {JobId} spread {Page}: centre-fold gate — edge {Edge:P1} at "
                + "column {EdgeColumn}, one-way field {Field:P1} at column {FieldColumn}{Severe} "
                + "(advisory limits {EdgeLimit:P0}/{FieldLimit:P0}). Spending the one base "
                + "regeneration.",
                context.JobId, page.Page,
                centreField.EdgeCoverage, centreField.EdgeColumn,
                centreField.FieldCoverage, centreField.FieldColumn,
                centreField.Severe ? " — SEVERE tier" : string.Empty,
                CompositeSeamRepair.EdgeCoverageLimit, CompositeSeamRepair.FieldCoverageLimit);

            (basePng, generationMs, placement) = await RegenerateBaseAsync(
                context, page, prompt,
                $"the base does not continue across the centre fold ({Reading(centreField)})",
                childPhoto, childPhotoContentType, theme, anchor, reference?.Image,
                cancellationToken);

            regenerated = true;
            baseAttempts++;

            // The refused picture is gone from here on: the reviewer judges the replacement, Beki
            // is composited onto it, and it is what a later spread may be anchored to — once it has
            // passed the same measurement, which is what the call below either grants or stops on.
            centreField = await MeasureRegeneratedBaseAsync(
                context, page, basePng, selection.PoseId, textSide, baseAttempts, centreField);
        }

        // One row per generate-and-review cycle, kept whether it passed or not. A page that shipped
        // on its second attempt is a page whose first verdict is the only record of what was wrong,
        // and that verdict is what the fulfilment job's telemetry is read for.
        var attempts = new List<CompositeAttempt>();

        /*
          Owner's rule 5, 2026-09-01: "we don't need additional reviews for images".

          Below this line is the visual reviewer and the ladder it drives — one vision call per
          spread, and up to one more picture bought on its say-so. Under the shipped policy none of
          that runs: the page is accepted on the checks that have already happened, which are the
          ones that measure rather than judge. The centre-fold gate ran above (and spent its
          regeneration if it had to), the base image was decoded and shape-checked on the way out of
          the provider, and the composite below carries the exact-Beki receipt that proves whose
          picture is pasted where. What is skipped is the opinion, not the arithmetic.

          The switch follows human_review exactly: `blocker` is the reviewed loop this pipeline has
          always run, `flag` is the owner's default. A run with NO policy at all reviews, because a
          caller that said nothing must get the behaviour that predates the policy.

          Two things deliberately do not move. Spread one's accepted base is still the child
          appearance anchor, which is what keeps the child the same across the book — a job the
          reviewer never did. And the identity spec and the photograph call are not reviews: they
          are how the book knows who it is about, and rule 5 is about not re-judging pictures.
        */
        var reviewed = context.ReleasePolicy?.ImageReviewRequired ?? true;

        if (!reviewed)
        {
            // Resolved once and used twice — the log line and the stored record say the same word,
            // because they are the same word.
            var severity = context.ReleasePolicy!.SeverityOf(BekiReleaseChecks.ImageReview);

            var unreviewed = Composite(context, page, basePng, selection.PoseId, textSide, placement);

            // A row, with a zero where the review time would be. The fulfilment job reads an empty
            // attempt list as "this page was adopted from an earlier run and cost nothing", so a
            // freshly drawn page with no rows would report a paid image call as free.
            attempts.Add(new CompositeAttempt(
                generationMs, 0, ReviewSkippedStatus, true)
            {
                Anchor = unreviewed.Manifest.BekiLayer.NormalizedAnchor is { } placedWithoutReview
                    ? new BekiCompositeAnchor(
                        placedWithoutReview.VisibleCenterX,
                        placedWithoutReview.VisibleCenterY,
                        placedWithoutReview.VisibleHeight)
                    : null,
            });

            logger.LogInformation(
                "Composite pipeline {JobId} spread {Page}: accepted without a visual review — "
                + "image_review is a {Severity} in the policy this job is running under. "
                + "review_attempts=0; the deterministic checks passed.",
                context.JobId, page.Page, severity);

            // Remembered like any other accepted page: nothing refused it, and the next spread
            // sharing a creature with it has no better picture to be shown.
            continuity.Remember(elements, basePng);

            return new CompositeSpreadResult
            {
                Page = page.Page,
                BasePng = basePng,
                CompositePng = unreviewed.Png,
                Manifest = unreviewed.Manifest,
                Prompt = prompt,
                PoseId = selection.PoseId,
                TextSide = textSide,
                Verdict = ReviewSkippedStatus + " (no model review; the deterministic checks passed)",
                BaseAttempts = baseAttempts,
                Attempts = attempts,
                PoseFallback = selection.Fallback,
                QaJson = CompositeSpreadQa.WriteSkipped(
                    page.Page, selection.PoseId, textSide, baseAttempts, severity),
            };
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var composite = Composite(context, page, basePng, selection.PoseId, textSide, placement);

            var review = await ReviewAsync(
                context, page, scenario, composite, textSide, childPhoto, childPhotoContentType,
                theme, elements, anchor, identity, cancellationToken);

            var verdict = review.Verdict;

            attempts.Add(new CompositeAttempt(
                generationMs, review.ReviewMs, verdict.ToString(), verdict.Passed)
            {
                Anchor = composite.Manifest.BekiLayer.NormalizedAnchor is { } placed
                    ? new BekiCompositeAnchor(
                        placed.VisibleCenterX, placed.VisibleCenterY, placed.VisibleHeight)
                    : null,
            });

            /*
              An unreadable review, shipped by policy — and it goes no further round this loop.

              The ladder's three rungs all act on a recommended_action, and there is not one here:
              nobody judged this picture. Letting it climb would buy a second base image to fix a
              complaint that was never made, which is the exact bill the parse retry exists to avoid
              paying. The page is accepted with the truthful NEEDS_HUMAN record ReviewAsync minted,
              and — like the refused page below — it does not become a continuity reference, because
              a picture nothing has approved is not a picture to tell the next spread to match.
            */
            if (review.Unreadable)
            {
                return new CompositeSpreadResult
                {
                    Page = page.Page,
                    BasePng = basePng,
                    CompositePng = composite.Png,
                    Manifest = composite.Manifest,
                    Prompt = prompt,
                    PoseId = selection.PoseId,
                    TextSide = textSide,
                    Verdict = verdict.ToString(),
                    BaseAttempts = baseAttempts,
                    Attempts = attempts,
                    PoseFallback = selection.Fallback,
                    QaJson = CompositeSpreadQa.Write(
                        page.Page, selection.PoseId, textSide, baseAttempts, attempts.Count, verdict),
                };
            }

            if (verdict.Passed)
            {
                // Only an accepted page becomes a continuity reference. An image the reviewer
                // refused is precisely the one a later spread must not be told to match.
                continuity.Remember(elements, basePng);

                if (verdict.AgeNote is { Length: > 0 } ageNote)
                {
                    /*
                      Logged and carried, and that is the whole of its effect.

                      CHILD_AGE was a blocking check until a pack died on it twice — refused, redrawn,
                      refused again for the same thing, stopped. The owner's ruling is that the
                      photograph says who the child is and the entered age says how old the book is
                      for: a picture from last year is a perfectly good identity reference for a book
                      about a four-year-old, and a reviewer comparing render to photograph will call
                      that a fault every time it is asked to. So the observation is collected and the
                      gate is gone.
                    */
                    logger.LogInformation(
                        "Composite pipeline {JobId} spread {Page}: age_note (advisory, no effect on "
                        + "the verdict) — entered age {Age}, reviewer says \"{Note}\".",
                        context.JobId, page.Page, input.ChildAge, ageNote);
                }

                if (verdict.ShotNote is { Length: > 0 } shotNote)
                {
                    // Logged as a warning and carried out, and that is the whole of its effect. The
                    // page passed; nothing below this line reads it. A hard gate on a subjective
                    // single-frame judgement would spend a paid image call on every false positive,
                    // and there is no evidence yet to price that — which is what this collects.
                    logger.LogWarning(
                        "Composite pipeline {JobId} spread {Page}: shot_note (advisory, no effect "
                        + "on the verdict) — asked for \"{Shot}\", reviewer says \"{Note}\".",
                        context.JobId, page.Page, CompositeSpreadRhythm.ShotFor(page.Page), shotNote);
                }

                return new CompositeSpreadResult
                {
                    Page = page.Page,
                    BasePng = basePng,
                    CompositePng = composite.Png,
                    Manifest = composite.Manifest,
                    Prompt = prompt,
                    PoseId = selection.PoseId,
                    TextSide = textSide,
                    Verdict = verdict.ToString(),
                    BaseAttempts = baseAttempts,
                    Attempts = attempts,
                    PoseFallback = selection.Fallback,
                    ShotNote = verdict.ShotNote,
                    AgeNote = verdict.AgeNote,
                    // The verdict that accepted this page, written down rather than dropped —
                    // audit-2 P0-09, whose finding was that every one of a shipped book's eight QA
                    // documents was missing because only refused pages ever wrote one. No second
                    // model call: this is the answer the loop above just read.
                    QaJson = CompositeSpreadQa.Write(
                        page.Page, selection.PoseId, textSide, baseAttempts, attempts.Count,
                        verdict),
                };
            }

            /*
              The ladder a refused page climbs, and it stops at three rungs.

              Which rung applies is the reviewer's own recommended_action, because it is the only
              reader that can tell a badly generated world from a well-generated one with Beki in
              the wrong part of it — and the two cost very differently: one is another paid image
              call, the other is arithmetic.
            */

            // Rung one: the world is wrong, and there is a second picture in the budget.
            if (verdict.RecommendedAction == CompositeQaVerdict.ActionRegenerateBase && !regenerated)
            {
                (basePng, generationMs, placement) = await RegenerateBaseAsync(
                    context, page, prompt, verdict.ToString(), childPhoto, childPhotoContentType,
                    theme, anchor, reference?.Image, cancellationToken);

                regenerated = true;
                baseAttempts++;

                // And the replacement is measured, which it was not. See MeasureRegeneratedBase:
                // a base bought here is a base the fold gate never saw, and the whole point of
                // P0-05's automated centerline test is that no picture reaches the book unmeasured.
                centreField = await MeasureRegeneratedBaseAsync(
                    context, page, basePng, selection.PoseId, textSide, baseAttempts, centreField);

                continue;
            }

            /*
              Rung two: the world is usable and Beki landed badly, so Beki moves.

              This rung used to do nothing at all, and a real book died on it. A spread refused for
              FOLD_SAFETY was re-composited from the same bytes, with the same pose, at the same
              configured anchor, by arithmetic that is deterministic by design — so the "second
              attempt" produced the first attempt's exact image, and the reviewer refused it again
              in the same words. The pack stopped at spread seven having bought two reviews of one
              picture, and the retry rule had been, in effect, a way of paying to fail twice.

              What it does now is what §14 asked for: adjust the deterministic anchor. Beki steps
              away from the middle of the sheet and is drawn slightly smaller, which is what
              FOLD_SAFETY and BEKI_INTEGRATION are complaining about; the step is bounded and
              clamped to the rectangle the deterministic checks already enforce. She is not
              redrawn, mirrored, rotated, warped or recoloured — none of those exist as code, and
              an anchor is three numbers.
            */
            if (verdict.RecommendedAction == CompositeQaVerdict.ActionRecompositeBeki && !recomposited)
            {
                recomposited = true;

                var layer = composite.Manifest.BekiLayer;

                var adjusted = new BekiCompositeAnchor(
                        layer.NormalizedAnchor.VisibleCenterX,
                        layer.NormalizedAnchor.VisibleCenterY,
                        layer.NormalizedAnchor.VisibleHeight)
                    .NudgedAwayFromCentre(
                        BekiCompositeConfig.ParseTextSide(textSide),
                        composite.Manifest.Canvas.WidthPx,
                        layer.RenderedSizePx.WidthPx);

                if (adjusted is not null)
                {
                    logger.LogWarning(
                        "Composite pipeline {JobId} spread {Page}: QA asked for a re-composite; "
                        + "moving Beki from {FromX},{FromY},{FromH} to {ToX},{ToY},{ToH} — {Verdict}",
                        context.JobId, page.Page,
                        layer.NormalizedAnchor.VisibleCenterX, layer.NormalizedAnchor.VisibleCenterY,
                        layer.NormalizedAnchor.VisibleHeight,
                        adjusted.VisibleCenterX, adjusted.VisibleCenterY, adjusted.VisibleHeight,
                        verdict);

                    placement = adjusted;

                    // Nothing was generated for this cycle, and the row says so: a zero here is the
                    // difference between "the second attempt was free" and "the second attempt was
                    // another image bill", which is the question the retry rules exist to answer.
                    generationMs = 0;

                    continue;
                }

                // Nowhere to move her to — she is wide enough that the canvas and the reserved
                // third leave no window. Falling through rather than re-compositing is the whole
                // lesson of the defect: an identical picture reviewed a second time is a paid call
                // whose answer is already known.
                logger.LogWarning(
                    "Composite pipeline {JobId} spread {Page}: QA asked for a re-composite, but "
                    + "Beki cannot be moved within the canvas and the reserved text third; not "
                    + "re-reviewing an identical picture.", context.JobId, page.Page);
            }

            /*
              Rung three: moving her did not fix it, and the base budget is still untouched.

              Reached only after a re-composite has been tried, which is what keeps the ladder
              bounded and keeps it honest — a first verdict of human_review still stops the book,
              because "the failure source is ambiguous" is not a thing another picture answers. But
              a placement the reviewer refused twice is evidence about the picture rather than about
              the placement: there was nowhere on that base to put Beki. The base is what changes.

              She goes back to the approved anchor for the new picture. The nudge was a repair for
              the old one, and carrying it forward would draw every later attempt further out for a
              reason that no longer exists.
            */
            if (recomposited && !regenerated)
            {
                (basePng, generationMs, placement) = await RegenerateBaseAsync(
                    context, page, prompt, verdict.ToString(), childPhoto, childPhotoContentType,
                    theme, anchor, reference?.Image, cancellationToken);

                regenerated = true;
                baseAttempts++;

                // Measured on the way in, exactly as rung one's replacement is.
                centreField = await MeasureRegeneratedBaseAsync(
                    context, page, basePng, selection.PoseId, textSide, baseAttempts, centreField);

                continue;
            }

            var exhausted = FailureEvidenceJson(
                page.Page, selection.PoseId, textSide, baseAttempts, attempts);

            /*
              The ladder is spent, and this is where amendment B3 changes what that means.

              Everything the reviewer refused is still true and still written down: the verdict below
              goes into this page's QA record verbatim, the release gates read it and grade the book
              NOT_RELEASABLE for the supplier, and an alarm carries the picture to somebody who can
              look at it. What no longer happens is that a family loses a book they paid for because
              a model disliked a picture twice — the composite exists, it was built from approved
              bytes, and what failed is an opinion about it.
            */
            if (Waivable(context, BekiReleaseChecks.ImageQa))
            {
                await WaiveAsync(
                    context, BekiReleaseChecks.ImageQa, page.Page,
                    $"the reviewer refused this page after {baseAttempts} base image(s) and "
                    + $"{attempts.Count} review(s) — {verdict}",
                    composite.Png, exhausted);

                // Deliberately NOT remembered as a continuity reference. A page the reviewer refused
                // is precisely the one a later spread must not be told to match — shipping it is a
                // decision about this page and must not become a decision about the next.
                return new CompositeSpreadResult
                {
                    Page = page.Page,
                    BasePng = basePng,
                    CompositePng = composite.Png,
                    Manifest = composite.Manifest,
                    Prompt = prompt,
                    PoseId = selection.PoseId,
                    TextSide = textSide,
                    Verdict = verdict.ToString(),
                    BaseAttempts = baseAttempts,
                    Attempts = attempts,
                    PoseFallback = selection.Fallback,
                    ShotNote = verdict.ShotNote,
                    AgeNote = verdict.AgeNote,
                    // The refusal, unaltered. A record that said PASS because the policy shipped the
                    // page would be the lie this whole split exists to prevent.
                    QaJson = CompositeSpreadQa.Write(
                        page.Page, selection.PoseId, textSide, baseAttempts, attempts.Count, verdict),
                };
            }

            logger.LogError(
                "Composite pipeline {JobId} spread {Page}: stopping for human review after "
                + "{BaseAttempts} base image(s) and {Reviews} review(s) — {Verdict}",
                context.JobId, page.Page, baseAttempts, attempts.Count, verdict);

            throw new CompositePipelineException(
                CompositeFailureCodes.ImageQaFailed,
                $"Spread {page.Page} failed the minimal visual QA and is marked for human review: {verdict}")
            {
                Page = page.Page,
                // The picture and the paperwork, so that "marked for human review" leaves a human
                // something to review.
                Evidence = new CompositeFailureEvidence(page.Page, composite.Png, exhausted),
            };
        }
    }

    /// <summary>
    /// The second picture, and the reset that goes with it.
    ///
    /// Extracted because two rungs of the ladder spend the same budget for different reasons — the
    /// reviewer asked for a new world, or moving Beki on the old one did not work — and the one
    /// thing that must be identical either way is what happens to the placement: it goes back to
    /// the approved anchor, because the nudge belonged to the picture being thrown away.
    /// </summary>
    private async Task<(byte[] BasePng, long GenerationMs, BekiCompositeAnchor? Placement)>
        RegenerateBaseAsync(
            CompositeBookContext context,
            VisualScenarioSpread page,
            string prompt,
            string reason,
            byte[] childPhoto,
            string childPhotoContentType,
            CompositeThemeReference theme,
            byte[]? anchor,
            byte[]? continuityImage,
            CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "Composite pipeline {JobId} spread {Page}: buying a new base image — {Reason}",
            context.JobId, page.Page, reason);

        var (rawPng, generationMs) = await GenerateBaseImageAsync(
            context, page.Page, prompt,
            References(childPhoto, childPhotoContentType, theme, anchor, continuityImage),
            cancellationToken);

        /*
          The centre-fold reading is taken by the caller rather than here, and every caller takes
          it — see MeasureRegeneratedBase.

          It used to say that measuring a QA-requested redraw would be "imposing the gate on a
          picture bought to fix a different complaint", and that reasoning had a hole in it a review
          found: a book whose first base passed the fold and whose reviewer then asked for a new
          world shipped the replacement without any fold measurement at all. The gate was avoidable
          by being asked for a redraw. It is not the caller's judgement whether the new picture is
          measured — only which reading it is compared against.
        */
        return (NormalizeToSpread(context, page.Page, rawPng), generationMs, null);
    }

    /// <summary>
    /// The blocking centre-fold measurement on a base that was just paid for, wherever it was
    /// bought — audit-2 P0-05, and the hole a review found in it.
    ///
    /// The gate at the top of the page measured the first base and the regeneration it spent
    /// itself, and stopped there. The two QA rungs buy the same one regeneration for a different
    /// reason — the reviewer wanted a new world, or moving Beki on the old one did not help — and
    /// their replacement went into the book unmeasured. So a spread whose first base was clean at
    /// the fold and whose second was painted in half shipped, and the "automated centerline test"
    /// the supplier asked for was, in that path, not run.
    ///
    /// The terminal behaviour is deliberately identical whichever rung bought the picture: the
    /// budget is spent either way, and there is no third image to try. The evidence carries both
    /// readings — the base this page started from and the one that failed — because the pair is
    /// what tells a prompt problem from an unlucky picture.
    /// </summary>
    private async Task<CentreFieldMeasurement> MeasureRegeneratedBaseAsync(
        CompositeBookContext context,
        VisualScenarioSpread page,
        byte[] basePng,
        string poseId,
        string textSide,
        int baseAttempts,
        CentreFieldMeasurement previous)
    {
        var measured = CompositeSeamRepair.MeasureCentreField(basePng);

        if (!measured.Exceeded)
        {
            logger.LogInformation(
                "Composite pipeline {JobId} spread {Page}: the regenerated base continues across "
                + "the centre fold — {Second}.",
                context.JobId, page.Page, Reading(measured));

            return measured;
        }

        /*
          Two readings past the limit, and the policy decides what that costs.

          The measurement's history is in the block at the top of the page and it is a history of
          being reversed twice, in both directions, on real evidence — which is itself the argument
          for making this an operator's switch rather than a constant. Flagged, the page keeps its
          artwork and the numbers go to somebody who can look at the picture and decide whether the
          instrument or the model was wrong; blocked, it does exactly what audit-2 P0-05 asked for.

          The BASE is the evidence either way, and the second one: Beki was never pasted onto it — a
          page that cannot pass this gate never reaches the compositor.
        */
        if (Waivable(context, BekiReleaseChecks.CentreFold))
        {
            await WaiveAsync(
                context, BekiReleaseChecks.CentreFold, page.Page,
                $"the regenerated base reads as two halves at the centre fold ({Reading(measured)}); "
                + $"the base it replaced read {Reading(previous)}",
                basePng,
                CentreFoldEvidenceJson(page.Page, poseId, textSide, baseAttempts, previous, measured));

            return measured;
        }

        logger.LogError(
            "Composite pipeline {JobId} spread {Page}: the regenerated base does not continue "
            + "across the centre fold — {Second} (the base it replaced read {First}). Stopping the "
            + "book; the refused picture and the numbers are stored as evidence.",
            context.JobId, page.Page, Reading(measured), Reading(previous));

        throw new CompositePipelineException(
            CompositeFailureCodes.ImageGenerationFailed,
            $"Spread {page.Page} carries a full-height discontinuity at its centre fold in the "
            + $"regenerated base ({Reading(measured)}); the base it replaced read "
            + $"{Reading(previous)}. The page's one regeneration is spent, and a fold painted into "
            + "the art cannot be repaired by layout, upscaling or colour conversion.")
        {
            Page = page.Page,
            /*
              The picture and the paperwork, exactly as the QA failure leaves them — same shape,
              same two blobs, so the fulfilment job's evidence path needs to know nothing about
              which gate refused a page.

              The BASE rather than a composite, and the second one rather than the first: the defect
              is in the generated artwork, and the picture worth looking at is the one that failed
              after a redraw had already been paid for. Beki was never pasted onto either — a page
              that cannot pass this gate never reaches the compositor.
            */
            Evidence = new CompositeFailureEvidence(
                page.Page,
                basePng,
                CentreFoldEvidenceJson(
                    page.Page, poseId, textSide, baseAttempts, previous, measured)),
        };
    }

    /// <summary>One centre-field reading as a phrase, for a log line and for a failure message.</summary>
    private static string Reading(CentreFieldMeasurement measured) =>
        $"edge {measured.EdgeCoverage:P1} at column {measured.EdgeColumn}, one-way field "
        + $"{measured.FieldCoverage:P1} at column {measured.FieldColumn}"
        + (measured.Severe ? ", SEVERE" : string.Empty);

    /// <summary>
    /// The document stored beside a base the centre-fold gate refused, in the same shape and the
    /// same two blobs as the QA failure's evidence (see <see cref="FailureEvidenceJson"/>).
    ///
    /// Both readings, before and after, because the pair is the whole argument: one measurement
    /// past the limit is a picture, and two independent generations past the limit at the same
    /// place is a prompt or a model painting a fold it was never asked for. The limits travel with
    /// the numbers so that a document read in six months can be judged against the thresholds that
    /// were in force when it was written rather than against whatever they became.
    /// </summary>
    private static string CentreFoldEvidenceJson(
        int page,
        string poseId,
        string textSide,
        int baseAttempts,
        CentreFieldMeasurement first,
        CentreFieldMeasurement second) =>
        JsonSerializer.Serialize(
            new
            {
                page,
                failure_code = CompositeFailureCodes.ImageGenerationFailed,
                audit_item = "P0-05",
                image_prompt_version = CompositeIllustrationPrompt.Version,
                pose_id = poseId,
                text_side = textSide,
                base_attempts = baseAttempts,
                gate = "centre_fold",
                limits = new
                {
                    edge_coverage = CompositeSeamRepair.EdgeCoverageLimit,
                    field_coverage = CompositeSeamRepair.FieldCoverageLimit,
                    severe_edge_coverage = CompositeSeamRepair.SevereEdgeCoverageLimit,
                    severe_field_coverage = CompositeSeamRepair.SevereFieldCoverageLimit,
                },
                readings = new[] { CentreFoldReading(1, first), CentreFoldReading(2, second) },
            },
            CompositeJson.Readable);

    private static object CentreFoldReading(int attempt, CentreFieldMeasurement measured) => new
    {
        attempt,
        edge_coverage = measured.EdgeCoverage,
        edge_column = measured.EdgeColumn,
        field_coverage = measured.FieldCoverage,
        field_column = measured.FieldColumn,
        exceeded = measured.Exceeded,
        severe = measured.Severe,
    };

    /// <summary>
    /// The document stored beside a cover wrap the construction-band gate refused.
    ///
    /// Per boundary rather than in aggregate, and every boundary rather than only the offending
    /// ones: what the audit's own evidence looked like was "x=1236 and x=1291" resolved to
    /// millimetres, and the two lines that stayed clean are as much a part of the argument as the
    /// two that did not — a wrap that is painted at every boundary is a prompt problem, and one
    /// painted at the spine alone is a different conversation with the model.
    /// </summary>
    private static string CoverBandEvidenceJson(
        CoverBandMeasurement first, CoverBandMeasurement second) =>
        JsonSerializer.Serialize(
            new
            {
                page = 0,
                failure_code = CompositeFailureCodes.ImageGenerationFailed,
                audit_item = "P0-03",
                image_prompt_version = CompositeIllustrationPrompt.Version,
                gate = "cover_construction_bands",
                canvas_width_mm = BekiCoverDieline.CanvasWidthMm,
                limits = new
                {
                    edge_coverage = CompositeSeamRepair.EdgeCoverageLimit,
                    field_coverage = CompositeSeamRepair.FieldCoverageLimit,
                    band_fraction = CompositeSeamRepair.CoverBandFraction,
                },
                attempts = new[] { CoverBandAttempt(1, first), CoverBandAttempt(2, second) },
            },
            CompositeJson.Readable);

    private static object CoverBandAttempt(int attempt, CoverBandMeasurement measured) => new
    {
        attempt,
        exceeded = measured.Exceeded,
        boundaries = measured.Bands.Select(band => new
        {
            name = band.Boundary.Name,
            millimetres_from_left = band.Boundary.MillimetresFromLeft,
            width_fraction = band.Boundary.WidthFraction,
            edge_coverage = band.Measurement.EdgeCoverage,
            edge_column = band.Measurement.EdgeColumn,
            field_coverage = band.Measurement.FieldCoverage,
            field_column = band.Measurement.FieldColumn,
            exceeded = band.Exceeded,
        }).ToList(),
    };

    /// <summary>
    /// The document that goes into the pack beside the refused picture.
    ///
    /// Written for the person who opens two files in a blob browser and has to decide what happened:
    /// every cycle in order, what each one cost, where Beki stood for it, and what the reviewer
    /// said. The anchors are the point of including the rows at all — a page refused twice for the
    /// same category reads as a pipeline that tried nothing until the two rows show two different
    /// placements.
    ///
    /// Nothing about the child is in it. The scene, the outfit and the identity attributes stay out:
    /// this is a record of a placement decision, and the picture beside it is the thing to look at.
    /// </summary>
    /// <summary>
    /// Whether the policy in force lets this named check ship the page instead of killing the book.
    ///
    /// One place, so that "which checks are waivable" is answered by the whitelist and by nothing
    /// else. A caller that passed no policy gets false for everything, which is this pipeline's
    /// behaviour before the policy existed.
    /// </summary>
    private static bool Waivable(CompositeBookContext context, string checkId) =>
        context.ReleasePolicy?.IsFlagged(checkId) == true;

    /// <summary>
    /// Ships a refusal, and says so — the whole of what "flag" means on this side of the system.
    ///
    /// The callback is awaited before the page continues so that the evidence and the alarm exist
    /// before the artwork does anything further; a waiver nobody recorded is the silence the owner's
    /// ruling replaced with an alarm, not the leniency it asked for.
    ///
    /// A callback that throws is not allowed to take the book down with it. That is not a swallow of
    /// convenience: the alternative is that a book with perfectly good artwork dies because the
    /// alarms table was unreachable, which is the exact fault class this campaign exists to remove.
    /// The log line is what remains, and it is a loud one.
    /// </summary>
    private async Task WaiveAsync(
        CompositeBookContext context,
        string checkId,
        int page,
        string detail,
        byte[] evidencePng,
        string evidenceJson)
    {
        logger.LogWarning(
            "Composite pipeline {JobId} {Where}: {CheckId} refused this page and the release policy "
            + "flags it, so the artwork ships and an alarm is raised — {Detail}",
            context.JobId, page == 0 ? "cover wrap" : $"spread {page}", checkId, detail);

        if (context.OnPolicyWaiver is not { } sink)
        {
            return;
        }

        try
        {
            await sink(new CompositePolicyWaiver(checkId, page, detail, evidencePng, evidenceJson));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex, "Composite pipeline {JobId}: the {CheckId} waiver on page {Page} could not be "
                    + "recorded. The book is unaffected; the incident is not in the alarms table.",
                context.JobId, checkId, page);
        }
    }

    private static string FailureEvidenceJson(
        int page,
        string poseId,
        string textSide,
        int baseAttempts,
        IReadOnlyList<CompositeAttempt> attempts) =>
        JsonSerializer.Serialize(
            new
            {
                page,
                failure_code = CompositeFailureCodes.ImageQaFailed,
                image_prompt_version = CompositeIllustrationPrompt.Version,
                qa_prompt_version = CompositeMinimalQa.Version,
                pose_id = poseId,
                text_side = textSide,
                base_attempts = baseAttempts,
                review_attempts = attempts.Count,
                attempts = attempts.Select((attempt, index) => new
                {
                    attempt = index + 1,
                    generation_ms = attempt.GenerationMs,
                    review_ms = attempt.ReviewMs,
                    accepted = attempt.Accepted,
                    verdict = attempt.Verdict,
                    beki_anchor = attempt.Anchor is null
                        ? null
                        : new
                        {
                            visible_center_x = attempt.Anchor.VisibleCenterX,
                            visible_center_y = attempt.Anchor.VisibleCenterY,
                            visible_height = attempt.Anchor.VisibleHeight,
                        },
                }).ToList(),
            },
            CompositeJson.Readable);

    /// <summary>
    /// One paid image call, checked deterministically before anything else happens to it.
    ///
    /// The checks are here rather than at the call site because their whole value is being between
    /// the provider and the compositor: a base that will not decode composites into an exception
    /// several frames away from the reason, and a base of the wrong shape produces a page that is
    /// only wrong once it has been cropped for print.
    /// </summary>
    /// <returns>The picture, and how long it took — the latter for this page's attempt record.</returns>
    private async Task<(byte[] Png, long GenerationMs)> GenerateBaseImageAsync(
        CompositeBookContext context,
        int? page,
        string prompt,
        StoryImageReference? references,
        CancellationToken cancellationToken)
    {
        // Checked here rather than only by the provider, and that is what makes fail-fast real: a
        // sibling spread that has already given up cancels this token, and the promise is that no
        // further image is PAID FOR after a book has terminally failed — not that a request is sent
        // and abandoned.
        cancellationToken.ThrowIfCancellationRequested();

        var started = Stopwatch.StartNew();
        byte[] image;

        try
        {
            // requireReferences, and it is not caution — it is the difference between this
            // pipeline working and appearing to. The child's likeness lives only in the attached
            // photograph (the composite plan has no appearance description to fall back on), the
            // world only in the approved theme reference, and a recurring creature only in the
            // continuity image. The OpenAI path's silent retreat from the edit route to
            // images/generations would return a picture of a different child in a generic world,
            // and this pipeline would then composite the approved Beki onto it, review it, store
            // it and print it. Better a stopped book with a named failure code.
            image = await openAi.GenerateStoryImageAsync(
                prompt, references, cancellationToken, SpreadImageSize, requireReferences: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            started.Stop();
            LogModelCall(
                context, "image_generation", _options.ImageModel, CompositeIllustrationPrompt.Version,
                started.ElapsedMilliseconds, retryCount: 0, validation: "failed", page: page);

            throw new CompositePipelineException(
                CompositeFailureCodes.ImageGenerationFailed,
                $"The image call for {(page is null ? "the cover" : $"spread {page}")} failed.", ex)
            {
                Page = page
            };
        }

        started.Stop();

        var problems = CompositeDeterministicChecks.BaseImageProblems(image);

        LogModelCall(
            context, "image_generation", _options.ImageModel, CompositeIllustrationPrompt.Version,
            started.ElapsedMilliseconds, retryCount: 0,
            validation: problems.Count == 0 ? "accepted" : string.Join("; ", problems), page: page);

        if (problems.Count > 0)
        {
            throw new CompositePipelineException(
                CompositeFailureCodes.ImageGenerationFailed,
                $"The generated base image for {(page is null ? "the cover" : $"spread {page}")} is "
                + $"not usable: {string.Join(" ", problems)}")
            {
                Page = page
            };
        }

        return (image, started.ElapsedMilliseconds);
    }

    /// <summary>
    /// Brings the provider's frame to the printed spread's shape, before anything else sees it.
    ///
    /// This is the step whose absence made every other number on the page wrong. The image models
    /// offer 3:2 and the printed spread is 15:7, so a base composited at 3:2 gets roughly a third
    /// of its height removed at layout time — and everything computed against the taller canvas
    /// travels with it. Beki's configured visible height of 0.333 became about 0.476 of the page
    /// actually printed, half again the size the partners approved; her anchor moved; the
    /// composition manifest recorded coordinates for a canvas that never existed as a page; and the
    /// reviewer passed or failed a picture with a band top and bottom that the reader would never
    /// see. Normalizing first is what makes the manifest, the verdict and the printed sheet three
    /// descriptions of one thing.
    ///
    /// A centred crop and nothing else, which is exactly what the handoff permits (§8: a tiny
    /// centred crop to normalize to 15:7 is allowed, stretching is forbidden) and exactly what
    /// <see cref="SpreadArtCrop.CropToRatio"/> already does for the reviewer's copy on the legacy
    /// path. Reused rather than reimplemented so the two cannot drift: the crop the composite
    /// pipeline bakes in and the crop the layout stage applies have to be the same arithmetic.
    ///
    /// The image prompt is written for this: it asks for a panorama "designed for a final 15:7
    /// crop" and for the important content to stay in the central horizontal band, so what the crop
    /// removes is sky and ground the scene was told it could lose.
    /// </summary>
    private byte[] NormalizeToSpread(CompositeBookContext context, int page, byte[] rawPng)
    {
        var before = Image.Identify(rawPng);

        var normalized = SpreadArtCrop.CropToRatio(
            rawPng, (float)CompositeDeterministicChecks.TargetAspect);

        var after = Image.Identify(normalized);

        logger.LogInformation(
            "Composite pipeline {JobId} spread {Page}: normalized {BeforeW}x{BeforeH} to "
            + "{AfterW}x{AfterH} for the 15:7 spread before compositing.",
            context.JobId, page, before.Width, before.Height, after.Width, after.Height);

        normalized = RepairSeam(context, page, normalized);

        var problems = CompositeDeterministicChecks.NormalizedSpreadProblems(normalized);
        if (problems.Count > 0)
        {
            // A crop that did not land on the sheet's shape is not something to composite onto and
            // then discover at layout time. It cannot normally happen — the crop is arithmetic —
            // which is precisely why it is worth saying out loud when it does.
            throw new CompositePipelineException(
                CompositeFailureCodes.ImageGenerationFailed,
                $"The base image for spread {page} could not be normalized to the printed spread: "
                + string.Join(" ", problems))
            {
                Page = page
            };
        }

        return normalized;
    }

    /// <summary>
    /// The centre-column gate: measure every picture before anything else sees it, and paint out a
    /// seam when one is there.
    ///
    /// Here rather than at the end because everything downstream inherits the bytes — the reviewer
    /// judges them, Beki is composited onto them, the anchor is one of them, and the printer gets
    /// one of them. A repair applied after any of that would be a different picture from the one
    /// that was approved.
    ///
    /// Deliberately silent when there is nothing to do, which is the common case: the v1.1 prompt
    /// amendment removed the cause, and this catches the faint residue that survived it.
    /// </summary>
    /// <param name="page">Null for the cover, which is measured by the same gate.</param>
    private byte[] RepairSeam(CompositeBookContext context, int? page, byte[] png)
    {
        var (repaired, before, after) = CompositeSeamRepair.Gate(png);

        if (!before.Exceeded)
        {
            if (before.DeclinedRepair)
            {
                // Measured, declined, and said out loud. The old silence here is how five veiled
                // spreads at 11-57× baseline reached a printed proof: the interpolator rightly
                // refused to smear a band that wide, and nothing recorded that it had seen one.
                // The centre-field gate now decides what happens to this picture; this line is so
                // the log shows both instruments read the same page.
                logger.LogWarning(
                    "Composite pipeline {JobId} {Page}: centre measured {Ratio:F1}x baseline "
                    + "({Centre:F2} against {Baseline:F2}) at {Offset:+0.0%;-0.0%;0.0%} from "
                    + "centre, but no narrow run to repair — a step or a band wider than "
                    + "{Max} columns. Deferring to the centre-field gate.",
                    context.JobId, page is null ? "cover" : $"spread {page}",
                    before.Ratio, before.Centre, before.Baseline, before.OffsetFraction,
                    CompositeSeamRepair.MaxRepairColumns);
            }

            return png;
        }

        logger.LogWarning(
            "Composite pipeline {JobId} {Page}: a centre seam measured {BeforeRatio:F1}x the "
            + "picture's baseline column change ({BeforeCentre:F2} against {Baseline:F2}) at "
            + "{Offset:+0.0%;-0.0%;0.0%} from centre; interpolated {Columns} column(s) from {First} "
            + "to {Last}, now {AfterRatio:F1}x.",
            context.JobId, page is null ? "cover" : $"spread {page}",
            before.Ratio, before.Centre, before.Baseline, before.OffsetFraction,
            before.ColumnCount, before.FirstColumn, before.LastColumn, after.Ratio);

        return repaired;
    }

    /// <summary>
    /// Paste the approved pose, then check the receipt it wrote.
    ///
    /// The deterministic post-checks read the manifest rather than the pixels, which is the whole
    /// design: the engine records where Beki went and what she hashed to, so "is Beki fully inside
    /// the canvas" is arithmetic somebody can repeat months later without the pipeline.
    /// </summary>
    /// <param name="placement">
    /// Null for the deterministic anchor this text side's config gives, which is what every first
    /// attempt uses; an adjusted anchor only on the one permitted placement retry. The engine has
    /// taken an override since it was written — §14 anticipated exactly this — so nothing about the
    /// composite itself changes here except the three numbers it is told to place her at.
    /// </param>
    private BekiCompositeResult Composite(
        CompositeBookContext context,
        VisualScenarioSpread page,
        byte[] basePng,
        string poseId,
        string textSide,
        BekiCompositeAnchor? placement = null)
    {
        var side = BekiCompositeConfig.ParseTextSide(textSide);

        BekiCompositeResult result;
        try
        {
            result = _engine.Value.CompositeStorySpread(
                basePng,
                $"spread-{page.Page:00}-base.png",
                poseId,
                side,
                $"spread-{page.Page:00}.png",
                placement);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new CompositePipelineException(
                CompositeFailureCodes.ImageGenerationFailed,
                $"Compositing Beki onto spread {page.Page} failed.", ex)
            {
                Page = page.Page
            };
        }

        var manifest = result.Manifest;
        var layer = manifest.BekiLayer;

        // The per-composite record the observability contract asks for, in one entry. Split across
        // several and the one that matters is always the one that scrolled away.
        logger.LogInformation(
            "Composite {JobId} spread {Page}: pose {PoseId} ({PoseFile}, sha256 {PoseSha}) "
            + "alphaBox={BoxX},{BoxY},{BoxW}x{BoxH} rendered={RenderW}x{RenderH} "
            + "placement={PlaceX},{PlaceY} anchor={AnchorX},{AnchorY},{AnchorH} opacity={Opacity} "
            + "resampler={Resampler} mirrored={Mirrored} rotated={Rotated} warped={Warped} "
            + "redrawn={Redrawn} output={OutputSha}",
            context.JobId, page.Page, layer.PoseId, layer.File, layer.Sha256,
            layer.SourceAlphaBbox.XPx, layer.SourceAlphaBbox.YPx,
            layer.SourceAlphaBbox.WidthPx, layer.SourceAlphaBbox.HeightPx,
            layer.RenderedSizePx.WidthPx, layer.RenderedSizePx.HeightPx,
            layer.PlacementPx.XPx, layer.PlacementPx.YPx,
            layer.NormalizedAnchor.VisibleCenterX, layer.NormalizedAnchor.VisibleCenterY,
            layer.NormalizedAnchor.VisibleHeight, layer.Opacity, manifest.Resampler,
            layer.Mirrored, layer.Rotated, layer.Warped, layer.Redrawn, manifest.Output.Sha256);

        var problems = CompositeDeterministicChecks.CompositeProblems(
            manifest, _engine.Value.Registry, side);

        if (problems.Count > 0)
        {
            throw new CompositePipelineException(
                CompositeFailureCodes.ImageGenerationFailed,
                $"The composite for spread {page.Page} failed its deterministic checks: "
                + string.Join(" ", problems))
            {
                Page = page.Page
            };
        }

        return result;
    }

    /// <summary>
    /// The multimodal review, with the contract's one parse retry.
    ///
    /// The retry re-asks rather than re-generating, which is the distinction the contract draws:
    /// an answer that will not parse says nothing about the picture, and paying for another
    /// picture to get a better sentence is the wrong bill. A second unparseable answer is
    /// <see cref="CompositeFailureCodes.ImageQaFailed"/> — there is no verdict, and shipping a page
    /// because the reviewer was incoherent is the same as not reviewing it.
    /// </summary>
    /// <returns>The verdict, the wall clock the whole review cost including a parse retry, and
    /// whether that verdict is a placeholder for an answer nobody could read.</returns>
    private async Task<CompositeReview> ReviewAsync(
        CompositeBookContext context,
        VisualScenarioSpread page,
        VisualScenarioV2 scenario,
        BekiCompositeResult composite,
        string textSide,
        byte[] childPhoto,
        string childPhotoContentType,
        CompositeThemeReference theme,
        IReadOnlyList<string> elements,
        byte[]? anchor,
        ChildIdentitySpec identity,
        CancellationToken cancellationToken)
    {
        var anchored = anchor is { Length: > 0 };

        var prompt = CompositeMinimalQa.Prompt(
            page.ChildWorldScene!,
            page.BekiAction!,
            scenario.VisualLock!.ChildOutfit!,
            elements,
            textSide,
            anchored,
            identity,
            // v1.3: the same sentence the image prompt opened its composition block with, so the
            // reviewer's shot judgement is a comparison rather than a description.
            CompositeSpreadRhythm.ShotFor(page.Page),
            // v1.5: the plan's own object states, so PROP_STATE is a check against a stated fact
            // — the lantern book's every page passed because no reviewer was ever told where the
            // lantern was supposed to be.
            CompositeMinimalQa.PropStateLines(page.Props));

        /*
          The child's photograph, and — after spread one — the child appearance anchor. Nothing else.

          The photograph answers "is this the same child"; the anchor answers "is this the same
          child as the rest of this book", which is the question the drifting book's eight PASSes
          were never asked. Both are about the child. The theme reference is still absent, because
          it would invite the reviewer to grade the world against a picture the scene was never
          meant to reproduce, and a Beki reference is still absent, because a SHA-256 settles that.
        */
        var references = new List<(byte[] Bytes, string ContentType, string Label)>
        {
            (childPhoto, childPhotoContentType, "Original child photograph"),
        };

        if (anchored)
        {
            references.Add((anchor!, "image/png", "Child appearance anchor (accepted spread 1)"));
        }

        CompositeQaParseResult? previous = null;

        // Across both attempts, because the attempt record measures what reviewing this page cost
        // and a parse retry is part of that cost even though it bought no new picture.
        var reviewClock = Stopwatch.StartNew();

        for (var attempt = 0; attempt <= 1; attempt++)
        {
            // A sibling spread that has terminally failed has already ended this book; reviewing
            // a page for it is one more paid call for nothing.
            cancellationToken.ThrowIfCancellationRequested();

            var ask = attempt == 0
                ? prompt
                : prompt
                  + "\n\nThe previous answer could not be read: "
                  + previous!.Summary
                  + " Return only the JSON object described above.";

            var started = Stopwatch.StartNew();
            string answer;

            try
            {
                answer = await openAi.ReviewIllustrationAsync(
                    composite.Png, ask, references, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                started.Stop();
                LogModelCall(
                    context, "visual_qa", _options.VisualReviewerModel, CompositeMinimalQa.Version,
                    started.ElapsedMilliseconds, attempt, "failed", page.Page);

                throw new CompositePipelineException(
                    CompositeFailureCodes.ImageQaFailed,
                    $"The minimal visual QA call for spread {page.Page} failed.", ex)
                {
                    Page = page.Page
                };
            }

            started.Stop();

            var parsed = CompositeMinimalQa.Parse(answer);

            LogModelCall(
                context, "visual_qa", _options.VisualReviewerModel, CompositeMinimalQa.Version,
                started.ElapsedMilliseconds, attempt,
                parsed.IsValid ? parsed.Verdict!.ToString() : parsed.Summary, page.Page);

            if (parsed.IsValid)
            {
                reviewClock.Stop();
                return new CompositeReview(parsed.Verdict!, reviewClock.ElapsedMilliseconds, false);
            }

            previous = parsed;

            logger.LogWarning(
                "Composite pipeline {JobId} spread {Page}: the QA answer did not parse — {Problems}",
                context.JobId, page.Page, parsed.Summary);
        }

        reviewClock.Stop();

        /*
          Two unreadable answers, and the evidence that was never attached.

          The audit's finding on this path was not only that it killed a book — it was that it killed
          one silently: the exception carried no Evidence, so the fulfilment job wrote no picture and
          no document, and "marked for human review" left a human nothing whatever to review. That is
          fixed for BOTH outcomes below, which is amendment B3's explicit instruction: the page and
          the reason it could not be judged are stored whether the policy ships the book or stops it.
        */
        var evidenceJson = UnreadableQaEvidenceJson(page.Page, previous!.Problems);

        if (Waivable(context, BekiReleaseChecks.QaUnreadable))
        {
            await WaiveAsync(
                context, BekiReleaseChecks.QaUnreadable, page.Page,
                $"no readable QA verdict after two attempts — {previous.Summary}",
                composite.Png, evidenceJson);

            /*
              NEEDS_HUMAN, which is a status this reviewer contract cannot produce and this pipeline
              can.

              It is the honest word for what happened: nobody said the picture was wrong, and nobody
              said it was right. Recording it as a FAIL would invent a refusal; recording it as a
              PASS would invent a review. The release gates read this status back and route the book
              to the human gate rather than to a failure, which is the distinction the status exists
              to make — and the ladder above does not run, because there is nothing here to act on.
            */
            return new CompositeReview(
                new CompositeQaVerdict(
                    UnreadableStatus,
                    ["QA_UNREADABLE"],
                    CompositeQaVerdict.ActionHumanReview,
                    [$"The reviewer's answer could not be read twice: {previous.Summary}"]),
                reviewClock.ElapsedMilliseconds,
                true);
        }

        throw new CompositePipelineException(
            CompositeFailureCodes.ImageQaFailed,
            $"Spread {page.Page} has no readable QA verdict after two attempts and is marked for "
            + $"human review: {previous.Summary}")
        {
            Page = page.Page,
            Evidence = new CompositeFailureEvidence(page.Page, composite.Png, evidenceJson),
        };
    }

    /// <summary>
    /// The status a page carries when the reviewer's answer could not be read — a word this
    /// pipeline mints and the release gates recognise.
    /// </summary>
    public const string UnreadableStatus = "NEEDS_HUMAN";

    /// <summary>
    /// The status a page carries when no model was asked about it at all, because the policy says
    /// not to — owner's rule 5, 2026-09-01: "we don't need additional reviews for images".
    ///
    /// A word of its own, and deliberately not one of the four the reviewer contract can produce.
    /// PASS would claim a verdict nobody gave. FAIL would invent a refusal. Even NEEDS_HUMAN, which
    /// this pipeline mints for the unreadable answer, would be wrong here in the one way that
    /// matters: it means "somebody has to look at this", and the owner's ruling is precisely that
    /// nobody has to. What happened is that the deterministic checks ran, the page passed them, and
    /// no opinion was bought — and that is a fact worth having its own name, in the record and in
    /// the release gate that reads it back.
    /// </summary>
    public const string ReviewSkippedStatus = "REVIEW_SKIPPED_BY_POLICY";

    /// <summary>One review, and whether its verdict is a real one.</summary>
    private sealed record CompositeReview(
        CompositeQaVerdict Verdict, long ReviewMs, bool Unreadable);

    /// <summary>
    /// The document stored beside a page whose review would not parse — the evidence this path used
    /// to omit entirely.
    /// </summary>
    private static string UnreadableQaEvidenceJson(int page, IReadOnlyList<string> problems) =>
        JsonSerializer.Serialize(
            new
            {
                page,
                failure_code = CompositeFailureCodes.ImageQaFailed,
                gate = BekiReleaseChecks.QaUnreadable,
                qa_prompt_version = CompositeMinimalQa.Version,
                image_prompt_version = CompositeIllustrationPrompt.Version,
                review_attempts = 2,
                status = UnreadableStatus,
                problems,
                note = "Two answers from the visual reviewer, neither of which could be read as a "
                       + "verdict. The picture beside this document was never judged — not refused.",
            },
            CompositeJson.Readable);

    /// <summary>
    /// The images the generation call carries, in the order the prompt numbers them, and the one it
    /// must never carry.
    ///
    /// Two on the first spread — the child's photograph as the identity reference and the approved
    /// world reference — then the child appearance anchor on every page after it, then the last
    /// accepted base containing this page's recurring element when there is one. Four at most,
    /// which is the template's own limit.
    ///
    /// The order is not cosmetic. The prompt calls them Image 1 to Image 4 by position, so a list
    /// assembled in a different order tells the model to take the child's stylization from a
    /// picture of a creature. It is also the weighting: the first reference is the one the image
    /// model leans on hardest, and that is the photograph, deliberately, on every page.
    ///
    /// No Beki, in any position, under any label — the config says <c>send_beki_reference: false</c>,
    /// and a list built anywhere else is a list this rule could be broken in. Note what the anchor
    /// is for the same reason: the accepted BASE of spread one, which is the page before Beki was
    /// pasted onto it.
    /// </summary>
    private static StoryImageReference? References(
        byte[] childPhoto,
        string childPhotoContentType,
        CompositeThemeReference theme,
        byte[]? childAnchor,
        byte[]? continuityImage)
    {
        var references = new List<(byte[] Bytes, string ContentType, string Label)>();

        // The anchor first, from v1.2, on every page that has one. The first reference is the one
        // the image model leans on hardest, and until now that was a photograph of a real child —
        // so every spread re-stylized it from scratch and the book's own answer, sitting third in
        // the list, was a hint. The picture that already shows the drawn child now leads.
        if (childAnchor is { Length: > 0 })
        {
            references.Add((childAnchor, "image/png", "Child appearance anchor"));
        }

        // And the photograph directly behind it, never dropped: the anchor is one stylization, and
        // a stylization is answerable to the child it was made from.
        references.Add((childPhoto, childPhotoContentType, "Child identity reference"));
        references.Add((theme.Bytes, "image/png", $"Approved {theme.OfficialName} world reference"));

        if (continuityImage is { Length: > 0 })
        {
            references.Add((continuityImage, "image/png", "Continuity reference"));
        }

        return BekiImageReferences.ToStoryImageReference(references);
    }

    private static CompositeSpreadResult AdoptedSpread(int page, byte[] image) => new()
    {
        Page = page,
        BasePng = [],
        CompositePng = image,
        // The manifest is not rebuilt for an adopted page. It was written, checked and stored the
        // run that drew it, and a fresh one built from bytes we did not composite would be a
        // receipt for work this run did not do.
        Manifest = AdoptedManifest,
        Prompt = string.Empty,
        PoseId = string.Empty,
        TextSide = CompositeSpreadRhythm.TextSideFor(page),
        Verdict = "Adopted from a previous attempt's accepted artwork.",
        BaseAttempts = 0,
        Adopted = true,
    };

    private static readonly BekiCompositionManifest AdoptedManifest = new()
    {
        Canvas = new BekiCompositionSize { WidthPx = 0, HeightPx = 0 },
        BaseImage = new BekiCompositionFile { File = string.Empty, Sha256 = string.Empty },
        BekiLayer = new BekiCompositionLayer
        {
            PoseId = string.Empty,
            File = string.Empty,
            Sha256 = string.Empty,
            SourceAlphaBbox = new BekiCompositionRect { XPx = 0, YPx = 0, WidthPx = 0, HeightPx = 0 },
            RenderedSizePx = new BekiCompositionSize { WidthPx = 0, HeightPx = 0 },
            PlacementPx = new BekiCompositionPoint { XPx = 0, YPx = 0 },
            NormalizedAnchor = new BekiCompositionAnchor
            {
                VisibleCenterX = 0, VisibleCenterY = 0, VisibleHeight = 0
            },
        },
        Output = new BekiCompositionFile { File = string.Empty, Sha256 = string.Empty },
    };

    /// <summary>
    /// One line per AI call, in the fields §8 asks for: the job, the stage, the model, the prompt
    /// version, the latency, the retry count and what validation made of the answer.
    ///
    /// What is not in it is as deliberate as what is: no API key, no signed URL, no photograph
    /// bytes, no child's name. A log line is the artifact most likely to be pasted into a chat
    /// window, and everything in this one is safe to paste.
    ///
    /// For the story and scenario stages the model is the id this pipeline actually passed. For the
    /// image and QA stages it is the configured slot instead, because those two go through the
    /// provider router, which picks the vendor and the model from its own options and logs the id it
    /// used; recording a guess here as though it were the real one would be worse than recording
    /// what was configured.
    /// </summary>
    private void LogModelCall(
        CompositeBookContext context,
        string stage,
        string model,
        string promptVersion,
        long latencyMs,
        int retryCount,
        string validation,
        int? page = null) =>
        logger.LogInformation(
            "Composite AI call {JobId}: stage={Stage} page={Page} model={Model} "
            + "promptVersion={PromptVersion} latencyMs={LatencyMs} retry={Retry} validation={Validation}",
            context.JobId, stage, page?.ToString() ?? "-", model, promptVersion, latencyMs,
            retryCount, validation);

    /// <summary>
    /// The continuity reference mechanism, reused rather than rebuilt (handoff §6 Step 4: "do not
    /// build a new extraction service for v0").
    ///
    /// It remembers the most recent accepted BASE image each recurring element appeared in — the
    /// base, never the composite, because the composite has Beki in it and the continuity
    /// instruction tells the model to copy only the named elements from that picture. Handing it a
    /// page with Beki on it is handing it a picture of Beki.
    /// </summary>
    private sealed class CompositeContinuity
    {
        private readonly Dictionary<string, byte[]> _byElement = new(StringComparer.Ordinal);

        /// <summary>
        /// Read while one page is being scheduled, written when another is accepted, and those two
        /// now happen at the same time. A plain dictionary torn by a concurrent write is not a
        /// wrong reference — it is a corrupted dictionary, on the path that decides what nine paid
        /// image calls are shown.
        /// </summary>
        private readonly object _gate = new();

        /// <summary>
        /// The reference for this page: the last accepted base containing any of the elements this
        /// page reuses, and the names it may be read for.
        ///
        /// One image, not several. The image call takes a list of references and the model weights
        /// the first most heavily; two continuity pictures is how a spread came back with the same
        /// creature drawn twice on the legacy path, and this pipeline's answer is to attach the one
        /// picture and name what may be taken from it.
        /// </summary>
        public (byte[] Image, IReadOnlyList<string> ElementNames)? For(IReadOnlyList<string> elements)
        {
            lock (_gate)
            {
                foreach (var element in elements)
                {
                    if (_byElement.TryGetValue(element, out var image))
                    {
                        return (image, [element]);
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Records the most recent accepted appearance, replacing whatever was there.
        ///
        /// It used to keep the first and never look again, which is the wrong end of the book. The
        /// contract asks for "the most recent approved image containing a recurring story character
        /// or object", and the reason is drift: each spread is drawn from the one before it, so by
        /// spread seven the creature has moved a little from where spread two left it, and matching
        /// spread seven against spread two asks the model to undo six pages of accumulated change in
        /// one step. Matching it against spread six asks for one page's worth.
        /// </summary>
        public void Remember(IReadOnlyList<string> elements, byte[] basePng)
        {
            if (basePng is not { Length: > 0 })
            {
                return;
            }

            lock (_gate)
            {
                foreach (var element in elements)
                {
                    _byElement[element] = basePng;
                }
            }
        }
    }

    /// <summary>
    /// The promise the fulfilment callback is written against: one at a time, in spread order,
    /// however the pages actually finish.
    ///
    /// It exists because the callback is not a notification. On the other side of it a job uploads
    /// a picture, stores a composition receipt, advances a percentage and rewrites the one manifest
    /// blob that decides what a resumed run may adopt — reading and rewriting a dictionary it also
    /// mutates. Two of those at once is a manifest missing whichever page lost the race, which is
    /// then a page redrawn by the next attempt and paid for twice; out of order, it is a book that
    /// reports page five as done while page three is still being drawn.
    ///
    /// Making the callback thread-safe was the alternative, and it is the wrong place for the fix:
    /// the fulfilment job's callback is written once and read by people reasoning about a book, and
    /// "pages arrive in order, one at a time" is a sentence they can hold. The concurrency this
    /// campaign added belongs to the pipeline that added it.
    ///
    /// Pages that were adopted from an earlier attempt are not in the order at all — they were
    /// delivered by the run that drew them, and their pictures are already stored.
    /// </summary>
    private sealed class OrderedSpreadDelivery(
        IReadOnlyList<int> order, Func<CompositeSpreadResult, Task>? callback)
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly Dictionary<int, CompositeSpreadResult> _ready = [];

        /// <summary>How far down <c>order</c> the callback has been taken.</summary>
        private int _next;

        /// <summary>
        /// Set when the callback itself throws, and read before anything else is handed over.
        ///
        /// The sibling cancellation that follows such a failure is a moment behind it — the
        /// callback throws, this gate is released, and only then does the catch upstream cancel —
        /// which is long enough for the next page, already queued here, to be delivered into a
        /// storage layer that has just failed. The latch closes that window without depending on
        /// how quickly cancellation arrives. Only ever touched inside the gate.
        /// </summary>
        private bool _abandoned;

        /// <summary>
        /// Hands over one finished page, and with it every page that was only waiting for this one.
        ///
        /// A spread that finishes early simply waits: page four completing before page three
        /// records itself and returns, and page three's own call delivers three and then four. So
        /// the callback runs on whichever thread completed the page that unblocked it, one call at
        /// a time, in the book's order.
        /// </summary>
        public async Task DeliverAsync(CompositeSpreadResult spread, CancellationToken cancellationToken)
        {
            if (callback is null)
            {
                return;
            }

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                if (_abandoned)
                {
                    return;
                }

                _ready[spread.Page] = spread;

                while (_next < order.Count && _ready.TryGetValue(order[_next], out var due))
                {
                    // A book that has already failed terminally does not go on being delivered:
                    // the pages after the failure will never exist, and the manifest should not
                    // claim otherwise.
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        await callback(due).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        _abandoned = true;
                        throw;
                    }

                    _next++;
                }
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
