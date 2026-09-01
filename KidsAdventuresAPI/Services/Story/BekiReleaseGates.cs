using System.Text.Json;
using System.Text.Json.Serialization;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story.Composite;

namespace AdventurePacks.Api.Services.Story;

/// <summary>
/// One hard gate's answer, with the blobs a reviewer can open to check it.
/// </summary>
/// <param name="Status">
/// <c>PASS</c>, <c>FAIL</c>, <c>NEEDS_HUMAN</c>, <c>REVIEW_SKIPPED_BY_POLICY</c> or
/// <c>UNKNOWN</c> — and only the first releases.
/// <c>UNKNOWN</c> is the one that took an audit to earn its place: the rejected package's evidence
/// was absent, and absence was being read as silence rather than as a refusal.
/// <c>REVIEW_SKIPPED_BY_POLICY</c> is the newest and says the least: the check was not run, so
/// there is nothing here that is either evidence or a refusal — see
/// <see cref="BekiReleaseGates.ReviewSkipped"/>.
/// </param>
/// <param name="Class">
/// <c>shared</c>, <c>press</c>, <c>digital</c> or <c>package</c> — amendment A5's governance split.
/// It is what decides which deliverable a failure withholds: a press gate never holds the parent's
/// download hostage, and no gate at all holds back the in-app reader.
/// </param>
public sealed record BekiGateResult(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("class")] string Class,
    [property: JsonPropertyName("detail")] string Detail,
    [property: JsonPropertyName("evidence")] IReadOnlyList<string> Evidence)
{
    /// <summary>
    /// What the release policy did about this gate's failure, when it did anything —
    /// <c>WAIVED_BY_POLICY</c> and nothing else.
    ///
    /// Amendment B2 is explicit about the shape: a waiver is a SEPARATE field beside the status,
    /// never a replacement for it. A gate that says PASS because an operator decided a failure was
    /// acceptable is a gate that lies to the supplier, to the next audit and to whoever reads
    /// release-gates.json in six months. So <see cref="Status"/> stays FAIL, or NEEDS_HUMAN, or
    /// UNKNOWN — exactly what the stored evidence supports — and this says what was done about it.
    ///
    /// Null on a passing gate and on a failing gate that withheld. See
    /// <see cref="BekiReleaseGateReport.PolicyWaivers"/> for the per-class detail: this field is the
    /// summary a person reading one gate entry needs, and the list below is what the publish
    /// decisions actually read.
    /// </summary>
    [property: JsonPropertyName("disposition")]
    public string? Disposition { get; init; }
}

/// <summary>
/// One failing check the policy let through, for one deliverable.
///
/// Per (check, class) rather than per check, because amendment B2's whole point is that the same
/// RENDER_VALIDATION failure is a blocker about the printer's file and a flag about the reading
/// copy. One entry here is one publication decision that went the other way, and one alarm.
/// </summary>
public sealed record BekiGateWaiver(
    [property: JsonPropertyName("check_id")] string CheckId,
    [property: JsonPropertyName("class")] string DeliverableClass,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("detail")] string Detail);

/// <summary>
/// What render validation said about ONE stored final, and which deliverable that final is.
///
/// The slice the two publish decisions read, and the answer to a coupling a review found.
/// <c>RENDER_VALIDATION</c> and <c>QR</c> are classed press because the supplier wrote them for the
/// printer's files — but the evidence they aggregate includes the customer's own PDF, so a reading
/// copy whose fonts did not resolve, or whose credits QR scanned to nothing, failed a PRESS gate and
/// was published to the parent anyway. The gate ids stay exactly as the acceptance document names
/// them, sixteen of them, once each; what is split is the evidence underneath, by the artifact it
/// came off.
/// </summary>
/// <param name="Class">
/// <c>press</c> for the two printer files, <c>digital</c> for the reading copy — amendment A5's
/// classes, applied to the artifact rather than to the gate id.
/// </param>
/// <param name="Render">PASS, FAIL, or UNKNOWN for a stored final nothing rendered back.</param>
/// <param name="Qr">
/// PASS, FAIL, or UNKNOWN. An artifact with no credits page was asked nothing and answers PASS —
/// asserting a QR on a press cover would be asserting a defect.
/// </param>
public sealed record BekiArtifactEvidence(
    [property: JsonPropertyName("artifact")] string Artifact,
    [property: JsonPropertyName("class")] string Class,
    [property: JsonPropertyName("stored")] bool Stored,
    [property: JsonPropertyName("render")] string Render,
    [property: JsonPropertyName("qr")] string Qr,
    [property: JsonPropertyName("report")] string Report);

/// <summary>
/// The release verdict for one book: every gate the supplier's acceptance document names, what this
/// deployment's stored artifacts say about each, and therefore which files may be handed over.
/// </summary>
public sealed record BekiReleaseGateReport
{
    [JsonPropertyName("schema")]
    public string Schema { get; init; } = BekiReleaseGates.Schema;

    /// <summary>The gates document these ids and thresholds were read from.</summary>
    [JsonPropertyName("contract")]
    public string Contract { get; init; } = BekiReleaseGates.AcceptanceGatesFile;

    [JsonPropertyName("verdict")]
    public required string Verdict { get; init; }

    [JsonPropertyName("evaluated_at_utc")]
    public required DateTimeOffset EvaluatedAtUtc { get; init; }

    [JsonPropertyName("gates")]
    public required IReadOnlyList<BekiGateResult> Gates { get; init; }

    /// <summary>
    /// The contact sheet the human gate is about — amendment A2: the approval records the SHA-256 of
    /// the pixels it approved, so "the reviewer looked at the book" names a specific rendering rather
    /// than a good intention. Null when render validation produced no sheet, which is itself a
    /// failing gate.
    /// </summary>
    [JsonPropertyName("contact_sheet_sha256")]
    public string? ContactSheetSha256 { get; init; }

    /// <summary>Whether a person still has to sign this book off before anything may ship.</summary>
    [JsonPropertyName("awaiting_human_review")]
    public required bool AwaitingHumanReview { get; init; }

    [JsonPropertyName("failing_gates")]
    public required IReadOnlyList<string> FailingGates { get; init; }

    /// <summary>
    /// Render and QR evidence per stored final, so the two publish decisions can consult the slice
    /// that is about the file they are deciding on rather than the aggregate of all three.
    ///
    /// Additive to the document and empty on a report written before it existed, which is what the
    /// fallback in <see cref="MayPublish"/> is for: with no slice to read, the gate's own status
    /// governs — strictly, in both directions.
    /// </summary>
    [JsonPropertyName("artifact_evidence")]
    public IReadOnlyList<BekiArtifactEvidence> ArtifactEvidence { get; init; } = [];

    /// <summary>
    /// Every failing check the release policy let through, and for which deliverable —
    /// amendment B4's alarm list, and amendment B2's per-class record.
    ///
    /// Stored in the document rather than recomputed, so that the two property families below stay
    /// pure functions of what is written down. A report read back by an admin endpoint months later
    /// answers the same way it answered when it was written, under the policy that was in force
    /// then, which is what makes "why was this published?" a question with an answer.
    ///
    /// Empty on a report written before the policy existed, which is the correct reading of one:
    /// nothing was waived, so the raw and the parent answers coincide, exactly as they did.
    /// </summary>
    [JsonPropertyName("policy_waivers")]
    public IReadOnlyList<BekiGateWaiver> PolicyWaivers { get; init; } = [];

    [JsonIgnore]
    public bool IsReleasable => Verdict == BekiReleaseGates.Releasable;

    /// <summary>
    /// THE SUPPLIER'S ANSWER for the reading copy: policy-blind, and the one the handback speaks in.
    ///
    /// Amendment B1's truth split. The parent's publication and the supplier's release used to be
    /// one boolean, so making a book shippable to a family under a flagged policy would also have
    /// made it shippable to the printer — and BekiPackageExport would have filed a failing PDF at
    /// the root of the handback zip as a deliverable. These two properties are what the package and
    /// the RELEASE_STATUS verdict read, and no policy touches them.
    /// </summary>
    [JsonIgnore]
    public bool SupplierCustomerPdfReleasable => MayPublish(BekiReleaseGates.DigitalClass, false);

    /// <summary>The same, raw, for the printer's two files.</summary>
    [JsonIgnore]
    public bool SupplierPressReleasable => MayPublish(BekiReleaseGates.PressClass, false);

    /// <summary>
    /// Whether the parent's download may be published — amendment A5's file-level rule as the
    /// release policy amends it: the customer PDF withholds on a failing shared, digital or human
    /// gate that the policy calls a BLOCKER, and press trouble never touches it.
    ///
    /// A gate the policy flagged is recorded in <see cref="PolicyWaivers"/>, alarmed, and does not
    /// withhold. It is never recorded as a pass.
    /// </summary>
    [JsonIgnore]
    public bool CustomerPdfMayPublish => MayPublish(BekiReleaseGates.DigitalClass, true);

    /// <summary>The same for the printer's two files: shared plus press, human review included.</summary>
    [JsonIgnore]
    public bool PressFilesMayPublish => MayPublish(BekiReleaseGates.PressClass, true);

    /// <summary>Whether the policy waived this check for this deliverable when the book was judged.</summary>
    public bool IsWaived(string checkId, string deliverableClass) =>
        PolicyWaivers.Any(waiver =>
            string.Equals(waiver.CheckId, checkId, StringComparison.Ordinal)
            && string.Equals(waiver.DeliverableClass, deliverableClass, StringComparison.Ordinal));

    /// <summary>
    /// One deliverable class's answer: its own gates, the shared ones, and — for the two gates whose
    /// evidence spans artifacts — only the slice belonging to this class.
    ///
    /// The slice is amendment A5's correction. Reading RENDER_VALIDATION's aggregate status here
    /// would do one of two wrong things: leave the customer PDF unguarded by it (which is what
    /// happened, because the gate is classed press), or let a press cover's refusal withhold a
    /// family's download.
    ///
    /// <paramref name="applyPolicy"/> is amendment B1's. False is the supplier's reading and asks
    /// only what the evidence says; true is the parent's and additionally skips whatever the policy
    /// waived. The two run over the same gates and the same slice, which is the point — they can
    /// only ever differ by a waiver, and every waiver is written down.
    /// </summary>
    private bool MayPublish(string deliverableClass, bool applyPolicy)
    {
        var slice = ArtifactEvidence
            .Where(artifact => artifact.Class == deliverableClass)
            .ToList();

        bool Waived(string checkId) => applyPolicy && IsWaived(checkId, deliverableClass);

        foreach (var gate in Gates)
        {
            if (gate.Status == BekiReleaseGates.Pass)
            {
                continue;
            }

            /*
              The human gate, which is a status rather than a gate id.

              NEEDS_HUMAN can be produced by any gate — today only VISUAL_QA does — and it is what
              AwaitingHumanReview is computed from. Handled here rather than by an early return on
              that flag so that the waiver applies to it the way it applies to everything else: when
              `human_review` is a flag, a book waiting on a signature publishes and alarms, and the
              report still says AwaitingHumanReview so the console can offer the signature anyway.
            */
            if (gate.Status == BekiReleaseGates.NeedsHuman
                && applyPolicy
                && IsWaived(BekiReleaseChecks.HumanReview, deliverableClass))
            {
                continue;
            }

            if (BekiReleaseGates.PerArtifactGates.Contains(gate.Id))
            {
                /*
                  These two are answered by the slice below when there is one. When there is not —
                  a report written before the slice existed, read back by the admin endpoint — the
                  aggregate answers instead, whatever class the gate is filed under: "we cannot tell
                  which file failed" must not be read as "not this one".
                */
                if (slice.Count == 0 && !Waived(gate.Id))
                {
                    return false;
                }

                continue;
            }

            if (gate.Class != BekiReleaseGates.SharedClass && gate.Class != deliverableClass)
            {
                continue;
            }

            if (!Waived(gate.Id))
            {
                return false;
            }
        }

        return slice.All(artifact =>
            (artifact.Render == BekiReleaseGates.Pass || Waived("RENDER_VALIDATION"))
            && (artifact.Qr == BekiReleaseGates.Pass || Waived("QR")));
    }

    public string ToJson() => JsonSerializer.Serialize(this, BekiReleaseGates.Json);

    /// <summary>
    /// A stored report, or null when there is not one to be had. Never throws: the admin endpoint's
    /// answer to an unreadable verdict is "there is no verdict", not a 500.
    /// </summary>
    public static BekiReleaseGateReport? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<BekiReleaseGateReport>(json, BekiReleaseGates.Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// The release gates, as an evaluator rather than as a document nobody reads.
///
/// Audit P0-09 is the finding this class answers, and its evidence is two sentences long: the only
/// gate in the whole pipeline was the composition-receipt check, `TryUpdateStatusAsync(Completed)`
/// followed it unconditionally, and `BEKI_Acceptance_Gates_v1.json` — sixteen hard gates with
/// `release_policy: all_hard_gates_must_pass` — had zero C# references. A book was declared finished
/// because nothing had thrown.
///
/// **Everything is read back out of storage.** Not one input to this evaluation comes from the
/// fulfilment job's memory, and that is the design rather than an inconvenience: amendment A5 makes
/// the admin approval endpoint re-run this evaluation hours or days later, against a process that
/// never drew the book, and a gate that could only be answered by the run that produced the artifact
/// would be a gate that cannot be re-answered. So the rule here is simple and total — if it is not
/// stored, it did not happen, and the gate that wanted it says <c>UNKNOWN</c> rather than passing.
///
/// **What the gates withhold, and what they never withhold.** The parent's in-app reader is the
/// spread PNGs and is served the moment they exist; no gate touches it, because a paid book must not
/// be held hostage to a printer's colour profile. What is gated is every deliverable FILE: the
/// customer PDF publishes only when the shared, digital and human gates pass; the two press PDFs
/// only when the shared and press gates do; and the handback package is marked RELEASABLE only when
/// all sixteen pass together.
/// </summary>
public sealed class BekiReleaseGates(IBlobStorageService blobStorage)
{
    public const string Schema = "beki-release-gates-v1";

    public const string AcceptanceGatesFile = "BEKI_Acceptance_Gates_v1.json";

    public const string Releasable = "RELEASABLE";

    public const string NotReleasable = "NOT_RELEASABLE";

    public const string Pass = "PASS";

    public const string Fail = "FAIL";

    public const string NeedsHuman = "NEEDS_HUMAN";

    public const string Unknown = "UNKNOWN";

    /// <summary>
    /// A gate whose evidence says the check was deliberately not run — today only VISUAL_QA, from
    /// the pipeline's own <see cref="CompositeBookPipeline.ReviewSkippedStatus"/> records.
    ///
    /// The same word in both places on purpose: the gate is quoting the stored document rather than
    /// summarising it, so "why does the handback say this?" is answered by opening one of the eight
    /// files it names. Like every non-PASS status it keeps the verdict at NOT_RELEASABLE for the
    /// supplier; unlike FAIL it asserts nothing about the pictures, and unlike NEEDS_HUMAN it puts
    /// nobody in a queue.
    /// </summary>
    public const string ReviewSkipped = CompositeBookPipeline.ReviewSkippedStatus;

    public const string SharedClass = "shared";

    public const string PressClass = "press";

    public const string DigitalClass = "digital";

    /// <summary>
    /// <c>HANDBACK_COMPLETENESS</c>'s class: it gates the package's own verdict and nothing else.
    /// A missing provenance file is a reason not to call a handback complete; it is not a reason to
    /// withhold a book a family paid for.
    /// </summary>
    public const string PackageClass = "package";

    /// <summary>
    /// The two gates whose evidence is gathered per stored artifact rather than per book, and whose
    /// withholding therefore follows the artifact's class instead of the gate's.
    ///
    /// Both are classed <c>press</c> in <see cref="GateClasses"/> and stay that way — the sixteen
    /// ids and their governance are the supplier's document, not ours to renumber. What changed is
    /// that the publish decisions read
    /// <see cref="BekiReleaseGateReport.ArtifactEvidence"/> for these two, so the reading copy's own
    /// render and QR failures withhold the reading copy.
    /// </summary>
    public static readonly IReadOnlySet<string> PerArtifactGates =
        new HashSet<string>(StringComparer.Ordinal) { "RENDER_VALIDATION", "QR" };

    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Which class each gate belongs to. The ids themselves are read from the supplier's document at
    /// evaluation time — a gate this table does not know about is still evaluated, as
    /// <c>UNKNOWN</c>/shared, because an id we cannot answer must not be an id we quietly drop.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> GateClasses =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ASSET_LOCK"] = SharedClass,
            ["EXACT_BEKI"] = SharedClass,
            ["SINGLE_COVER_MASTER"] = SharedClass,
            ["COVER_CONTINUITY"] = SharedClass,
            ["INTERIOR_CONTINUITY"] = SharedClass,
            ["TEXT_LAYER"] = SharedClass,
            ["FONT_INTEGRITY"] = SharedClass,
            ["VISUAL_QA"] = SharedClass,
            ["PRESS_GEOMETRY"] = PressClass,
            ["PRESS_COLOR"] = PressClass,
            ["PRESS_RESOLUTION"] = PressClass,
            ["TEXT_COLOR_INTEGRITY"] = PressClass,
            ["RENDER_VALIDATION"] = PressClass,
            ["QR"] = PressClass,
            ["DIGITAL_GEOMETRY"] = DigitalClass,
            ["HANDBACK_COMPLETENESS"] = PackageClass,
        };

    /// <summary>
    /// Evaluates every gate against what this pack has in storage, right now.
    /// </summary>
    /// <param name="policy">
    /// The release policy this evaluation is judged under, taken once by the caller and passed down
    /// — amendment B4. Nothing about the SUPPLIER's answer depends on this argument; see
    /// <see cref="BekiReleaseGateReport.SupplierPressReleasable"/>.
    ///
    /// REQUIRED, and it used to be optional with a defaults fallback. That fallback closed a review
    /// finding by hiding it: the admin approval endpoint called this with no policy at all, so a
    /// deployment where an operator had made a check STRICTER re-judged the book under the shipped
    /// defaults at approval time and overwrote the stored verdict with a laxer one — and a
    /// deployment where they had made one KINDER got the opposite. An argument that silently
    /// substitutes a policy nobody chose is worse than a compile error, so this is a compile error
    /// now; a caller with genuinely no table to read says
    /// <see cref="BekiReleasePolicySnapshot.Defaults"/> or <see cref="BekiReleasePolicySnapshot.Strict"/>
    /// out loud. (Review finding 1.)
    /// </param>
    /// <param name="baseDirectory">Test override for locating the acceptance-gates document.</param>
    public async Task<BekiReleaseGateReport> EvaluateAsync(
        Guid userId,
        Guid packId,
        CancellationToken cancellationToken,
        BekiReleasePolicySnapshot policy,
        string? baseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var ids = ReadGateIds(baseDirectory ?? AppContext.BaseDirectory);
        var evidence = await GatherAsync(userId, packId, cancellationToken);
        var inForce = policy;

        var results = ids
            .Select(id => Evaluate(id, evidence))
            .ToList();

        var (waivers, waivedGateIds) = Waivers(results, inForce);

        // The waived gates carry their disposition, so one gate entry says both what the evidence
        // showed and what was done about it. The status is untouched — B2's rule.
        results = results
            .Select(gate => waivedGateIds.Contains(gate.Id)
                ? gate with { Disposition = WaivedByPolicy }
                : gate)
            .ToList();

        var awaiting = results.Any(gate => gate.Status == NeedsHuman);
        var failing = results
            .Where(gate => gate.Status != Pass)
            .Select(gate => gate.Id)
            .ToList();

        return new BekiReleaseGateReport
        {
            // The handback's verdict, computed from the raw results exactly as it always was.
            // A waiver is a decision about a family's book; it is not an opinion the supplier asked
            // for, and a RELEASABLE stamped over a failing gate would be a false statement in a
            // document written to be checked.
            Verdict = failing.Count == 0 ? Releasable : NotReleasable,
            EvaluatedAtUtc = DateTimeOffset.UtcNow,
            Gates = results,
            ArtifactEvidence = evidence.Artifacts,
            ContactSheetSha256 = evidence.ContactSheetSha256,
            AwaitingHumanReview = awaiting,
            FailingGates = failing,
            PolicyWaivers = waivers,
        };
    }

    /// <summary>The one word a waived gate's disposition carries.</summary>
    public const string WaivedByPolicy = "WAIVED_BY_POLICY";

    /// <summary>
    /// Which failing gates the policy lets through, and for which deliverable.
    ///
    /// Asked per deliverable class rather than per gate, because that is the key the policy is
    /// stored under and because the answer genuinely differs: a render report that refused the press
    /// cover is a blocker about the press files and a flag about the reading copy, and one severity
    /// per gate could not express it.
    ///
    /// The human gate is asked under its own name — <c>human_review</c> — rather than under the id
    /// of whichever gate produced NEEDS_HUMAN. That is what makes "human review is skipped by
    /// default" one switch in the admin table instead of a rule hidden inside VISUAL_QA.
    /// </summary>
    private static (IReadOnlyList<BekiGateWaiver> Waivers, IReadOnlySet<string> WaivedGateIds) Waivers(
        IReadOnlyList<BekiGateResult> results, BekiReleasePolicySnapshot policy)
    {
        var waivers = new List<BekiGateWaiver>();
        var gateIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var gate in results.Where(gate => gate.Status != Pass))
        {
            // A gate that stopped on NEEDS_HUMAN is governed by the human-review switch rather than
            // by its own id: "human review is skipped by default" is one decision in the admin
            // table, not a rule buried inside whichever gate happened to raise the flag.
            var checkId = gate.Status == NeedsHuman ? BekiReleaseChecks.HumanReview : gate.Id;

            foreach (var deliverable in new[] { PressClass, DigitalClass, PackageClass })
            {
                if (!Concerns(gate, deliverable) || !policy.IsFlagged(checkId, deliverable))
                {
                    continue;
                }

                waivers.Add(new BekiGateWaiver(checkId, deliverable, gate.Status, gate.Detail));
                gateIds.Add(gate.Id);
            }
        }

        return (waivers, gateIds);

        // Whether a failing gate is one of this deliverable's problems at all. The per-artifact two
        // concern both printed and digital files; a package gate concerns only the handback.
        static bool Concerns(BekiGateResult gate, string deliverable) =>
            PerArtifactGates.Contains(gate.Id)
                ? deliverable is PressClass or DigitalClass
                : gate.Class == deliverable
                  || (gate.Class == SharedClass && deliverable is PressClass or DigitalClass);
    }

    /// <summary>
    /// The gate ids, in the supplier's own order, read from the supplier's own file.
    ///
    /// Read rather than transcribed for the reason every other consumer of this document reads it:
    /// a gate added to the contract must appear in the next verdict without a C# edit, and a
    /// verdict that silently answered fifteen of sixteen gates is exactly the shape of the finding
    /// this class exists for. A document that cannot be read is not a reason to pass anything —
    /// the evaluation refuses instead.
    /// </summary>
    public static IReadOnlyList<string> ReadGateIds(string baseDirectory)
    {
        var path = Path.Combine(
            baseDirectory, "Assets", "BekiComposite", "contracts", AcceptanceGatesFile);

        if (!File.Exists(path))
        {
            throw new BekiLayoutException(
                CompositeFailureCodes.LayoutFailed,
                $"the acceptance gates document is missing at '{path}'; the release verdict is "
                + "read from the supplier's file and is never guessed.");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));

        if (!document.RootElement.TryGetProperty("hard_gates", out var gates)
            || gates.ValueKind != JsonValueKind.Array)
        {
            throw new BekiLayoutException(
                CompositeFailureCodes.LayoutFailed,
                $"'{AcceptanceGatesFile}' states no hard_gates array.");
        }

        return gates
            .EnumerateArray()
            .Select(gate => gate.TryGetProperty("id", out var id) ? id.GetString() : null)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToList();
    }

    // ==============================================================================================
    // Per-gate evaluation
    // ==============================================================================================

    private static BekiGateResult Evaluate(string id, StoredEvidence stored) => id switch
    {
        "ASSET_LOCK" => stored.AssetLock switch
        {
            null => Missing(id, "no asset-lock manifest is stored, so nothing proves this book was "
                                + "built from approved bytes.", [stored.AssetLockName]),
            var manifest when manifest.Assets.Count == 0 =>
                Failed(id, "the asset-lock manifest names no assets.", [stored.AssetLockName]),
            var manifest when manifest.Assets.Any(a => a.ApprovalStatus != "approved") =>
                Failed(id, "one or more locked assets are not approved.", [stored.AssetLockName]),
            var manifest => Passed(
                id, $"{manifest.Assets.Count} assets resolved by role and SHA-256.",
                [stored.AssetLockName]),
        },

        "EXACT_BEKI" => stored.SpreadsWithoutReceipt.Count > 0
            ? Failed(
                id,
                "no exact-Beki composition receipt for spread(s) "
                + $"{string.Join(", ", stored.SpreadsWithoutReceipt)}.",
                [stored.ManifestName])
            : !stored.CoverCompositionPresent
                ? Failed(id, "the cover wrap carries no composition receipt.", [stored.CoverCompositionName])
                : Passed(
                    id,
                    $"{BookFormat.SpreadCount} spread receipts and the cover receipt name an "
                    + "approved pose and its output hash.",
                    [stored.ManifestName, stored.CoverCompositionName]),

        "SINGLE_COVER_MASTER" => !stored.WrapCompositePresent
            ? Missing(
                id, "the canonical cover wrap composite is not stored, so press and digital covers "
                    + "cannot be shown to share a master.", [stored.WrapCompositeName])
            : !stored.CoverIsWrapMaster
                ? Failed(
                    id,
                    "the fulfilment manifest's cover record does not name the wrap master "
                    + $"('{stored.CoverPromptVersion ?? "(none)"}').",
                    [stored.ManifestName])
                : !stored.CoverFrontPresent
                    ? Failed(
                        id, "the reader's cover is not the wrap's front-board crop.",
                        [stored.CoverFrontName])
                    : Passed(
                        id,
                        "press cover, customer front/back and the reader image all derive from the "
                        + "one stored wrap composite.",
                        [stored.WrapCompositeName, stored.CoverFrontName, stored.ManifestName]),

        "COVER_CONTINUITY" => stored.CoverCompositionPresent && stored.WrapBasePresent
            ? Passed(
                id,
                "the wrap base passed the dieline band measurement before it was composited; a "
                + "discontinuity at a hinge or spine line refuses the wrap and fails the book.",
                [stored.WrapBaseName, stored.CoverCompositionName])
            : Missing(
                id, "the wrap base and its receipt are not both stored, so the band measurement "
                    + "cannot be evidenced.", [stored.WrapBaseName, stored.CoverCompositionName]),

        "INTERIOR_CONTINUITY" => stored.ReviewPresent && stored.SpreadsWithoutReceipt.Count == 0
            ? Passed(
                id,
                "every spread passed the blocking centre-field measurement; a book that exceeded it "
                + "twice never reaches this evaluation.",
                [stored.ReviewName])
            : Missing(
                id, "the book-level review is not stored, so the centre-field verdicts cannot be "
                    + "evidenced.", [stored.ReviewName]),

        /*
          TEXT_LAYER asks one question and has only ever asked one: is the book's copy vector type
          that layout can account for, page by page?

          It is worth saying plainly, because the wording used to promise more than the check did.
          The pass message named "copy-sized washes" beside the type, and no clause of this gate ever
          looked at a wash — LayoutPagesWithoutTypography is the whole of the evidence, and the
          geometry of any cream box was the fixed-page QA record's business. Owner ruling 2026-09-01,
          the third and final on the question, has removed the wash from the book entirely: copy is
          outlined type drawn straight on the artwork. So the requirement is stated as what it is —
          vector typography recorded for every page that has text — and the gate itself is unchanged,
          because it was already right.
        */
        "TEXT_LAYER" => stored.LayoutReceiptDocuments.Count == 0
            ? Missing(
                id, "no post-layout receipts are stored, so no typography claim can be evidenced "
                    + "(amendment A4).", stored.LayoutReceiptNames)
            : stored.LayoutPagesWithoutTypography.Count > 0
                ? Failed(
                    id,
                    "layout receipts record no typography for page(s) "
                    + $"{string.Join(", ", stored.LayoutPagesWithoutTypography)}.",
                    stored.LayoutReceiptNames)
                : Passed(
                    id,
                    $"{stored.LayoutReceiptDocuments.Count} layout receipt document(s) record vector "
                    + "typography for every page that carries text.",
                    stored.LayoutReceiptNames),

        "FONT_INTEGRITY" => !stored.FontsLocked
            ? Missing(
                id, "the asset lock does not carry the licensed font hashes.", [stored.AssetLockName])
            : stored.FontsEmbedded switch
            {
                false => Failed(
                    id, "a renderer reported a font that is not embedded.", stored.RenderReportNames),
                null => Missing(
                    id, "no render validation is stored, so embedding was never read back off the "
                        + "stored file.", stored.RenderReportNames),
                true => Passed(
                    id, "Noto and Ottia are hash-locked and embedded in the stored artifacts.",
                    [stored.AssetLockName]),
            },

        // A spread's record is READ, not counted — amendment B1, and it is asked FIRST. A stored
        // refusal is the strongest thing this gate can be told, and a book whose spread three says
        // FAIL must not be graded by whether spread seven's document happens to be missing.
        "VISUAL_QA" => stored.SpreadsFailingQa.Count > 0
            ? Failed(
                id,
                "the stored QA record for spread(s) "
                + $"{string.Join(", ", stored.SpreadsFailingQa)} records a refusal.",
                stored.SpreadQaNames)
            : stored.SpreadsWithoutQa.Count > 0
            ? Missing(
                id,
                $"no final QA record for spread(s) {string.Join(", ", stored.SpreadsWithoutQa)}.",
                stored.SpreadQaNames)
            // A fixed page's record is READ, not counted. It was counted, and a record that said
            // FAIL — a placeholder endpaper, a wash over the fold — was evidence that the page had
            // been looked at rather than evidence that it was right.
            : stored.FixedPagesFailingQa.Count > 0
                ? Failed(
                    id,
                    "the machine QA record for fixed page(s) "
                    + $"{string.Join(", ", stored.FixedPagesFailingQa)} records a failure.",
                    stored.FixedQaNames)
                : stored.FixedPagesWithoutQa.Count > 0
                ? Missing(
                    id,
                    "no readable, current machine QA record for fixed page(s) "
                    + $"{string.Join(", ", stored.FixedPagesWithoutQa)}.",
                    stored.FixedQaNames)
                /*
                  An outstanding human-review requirement is asked BEFORE the skip, and the order is
                  the whole of review finding 4.

                  It used to be the other way round, and the consequence was a book that escaped the
                  one gate a machine cannot close. A book with skipped pages AND a Georgian or pose
                  advisory — or a stored NEEDS_HUMAN verdict — matched the skip clause first, was
                  graded REVIEW_SKIPPED_BY_POLICY, and never produced a NEEDS_HUMAN status anywhere.
                  AwaitingHumanReview is computed from that status, so it came back false: the console
                  offered nobody the signature, and an operator who had deliberately set human_review
                  to blocker had their blocker silently bypassed by an unrelated switch.

                  The two statements do not compete. "Nobody was asked to look at the artwork" and
                  "somebody must read this book" are both true of that book, and only one of them puts
                  a person in front of it — so that one is the status, and the skip is written into the
                  detail and the evidence rather than lost. A skip may only be the answer when there is
                  no human-review requirement left outstanding.
                */
                : (stored.NeedsHumanReading || stored.SpreadsNeedingHumanQa.Count > 0)
                  && !stored.HumanApprovalIsCurrent
                    ? NeedsAHuman(
                        id,
                        (stored.HumanApprovalPresent
                            ? "the stored approval covers a different contact sheet than the one "
                              + "render validation produced; a stale approval is refused."
                            : stored.SpreadsNeedingHumanQa.Count > 0
                                ? "spread(s) "
                                  + string.Join(", ", stored.SpreadsNeedingHumanQa)
                                  + " have no readable reviewer verdict; the rendered contact sheet "
                                  + "needs a person to look at those pages."
                                : "the book carries an unresolved human-review flag (shot or age "
                                  + "advisory); the rendered contact sheet needs a reviewer's signature.")
                        + (stored.SpreadsWithReviewSkipped.Count > 0
                            ? " No visual review was performed on spread(s) "
                              + string.Join(", ", stored.SpreadsWithReviewSkipped)
                              + " either: the stored record for each says "
                              + $"{CompositeBookPipeline.ReviewSkippedStatus} (release policy check "
                              + "'image_review'). The reader is looking at pages no model judged."
                            : string.Empty),
                        [
                            stored.ReviewName,
                            stored.HumanApprovalName,
                            // The skipped pages' own records travel with it, so the person the gate
                            // just summoned can open the documents that say nobody looked.
                            .. (stored.SpreadsWithReviewSkipped.Count > 0
                                ? stored.SpreadQaNames
                                : Array.Empty<string>()),
                        ])
                /*
                  Nobody reviewed these pages, and the supplier is told exactly that.

                  Owner's rule 5, 2026-09-01: "we don't need additional reviews for images". That is
                  a decision about what this deployment buys, and it is a legitimate one — but it is
                  not a claim about the artwork, and this gate exists to make claims about artwork.
                  So the answer is its own word rather than a PASS: RELEASABLE would tell a supplier
                  reading the handback that eight pages were visually checked when none were, which
                  is the precise species of lie amendment B1's truth split was built to prevent.

                  It is also not a FAIL and not NEEDS_HUMAN. Nothing refused these pages, and the
                  ruling is that nobody has to look at them — a status meaning "somebody must" would
                  put a queue in front of a decision that was taken to remove one. Which is exactly
                  why it may only be said of a book that has no such queue outstanding, above.

                  The family's copy is unaffected, and by the ordinary route rather than a special
                  one: VISUAL_QA is a shared gate that the policy flags by default, so the waiver
                  step below records this and publishes, exactly as it does for any other non-PASS
                  shared gate. An operator who sets VISUAL_QA to blocker gets the other behaviour,
                  and an operator who sets image_review to blocker never gets here at all.
                */
                    : stored.SpreadsWithReviewSkipped.Count > 0
                    ? new BekiGateResult(
                        id,
                        ReviewSkipped,
                        GateClasses.GetValueOrDefault(id, SharedClass),
                        "no visual review was performed on spread(s) "
                        + string.Join(", ", stored.SpreadsWithReviewSkipped)
                        + $": the stored record for each says {CompositeBookPipeline.ReviewSkippedStatus} "
                        + "(release policy check 'image_review'). Every page has a record and the "
                        + "deterministic checks passed; no model judged the artwork.",
                        stored.SpreadQaNames)
                    : Passed(
                        id,
                        stored.NeedsHumanReading
                            ? $"every page has a final QA record, and {stored.HumanApprover} signed "
                              + "off the rendered contact sheet."
                            : "every story and fixed page has a final QA record and nothing is "
                              + "flagged for a human.",
                        stored.SpreadQaNames.Concat(stored.FixedQaNames).ToList()),

        "PRESS_GEOMETRY" or "PRESS_COLOR" or "PRESS_RESOLUTION" or "TEXT_COLOR_INTEGRITY" =>
            EvaluatePress(id, stored),

        /*
          Every stored final, one at a time — the review's finding, which was that this gate asked
          whether ANY render report was releasable.

          Three finals can be in storage and the gate would pass on two: a press cover whose own
          validation failed, or which was never rendered back at all, was covered by the interior
          and the reading copy agreeing. So the question is asked per artifact and answered per
          artifact, and a stored file nobody rendered is UNKNOWN rather than absent from the count.
        */
        "RENDER_VALIDATION" => stored.StoredFinalsWithoutReport.Count > 0
            ? Missing(
                id,
                "stored final(s) "
                + string.Join(", ", stored.StoredFinalsWithoutReport)
                + " carry no render report, so nothing has been read back off the bytes that "
                + "shipped (amendment A8).",
                stored.RenderReportNames)
            : stored.StoredFinals.Count == 0
                ? Missing(
                    id, "no final artifact is stored, so neither Ghostscript nor Poppler has "
                        + "anything to have spoken about.", stored.RenderReportNames)
                : stored.RenderReports.Any(report => !report.Releasable)
                    ? Failed(
                        id,
                        "render validation refused "
                        + string.Join(
                            ", ",
                            stored.RenderReports.Where(r => !r.Releasable).Select(r => r.Artifact))
                        + ".",
                        stored.RenderReportNames)
                    : Passed(
                        id,
                        $"all {stored.StoredFinals.Count} stored final(s) rendered cleanly under "
                        + "both Ghostscript and Poppler.",
                        stored.RenderReportNames),

        // Asked of each artifact that carries a credits page, and answered by the strictest of
        // them: one clean scan does not cover for a refusal on another file.
        "QR" => stored.QrStatus switch
        {
            null => Missing(
                id, "no rendered artifact was scanned for the credits QR.", stored.RenderReportNames),
            "ok" => Passed(
                id, "exactly one QR was decoded off the rendered credits page and resolves to the "
                    + "locked destination.", stored.RenderReportNames),
            var status => Failed(id, $"the QR scan came back '{status}'.", stored.RenderReportNames),
        },

        // A stored report that declares its own refusal is a refusal, not a presence. The retry
        // case is the one this closes: the digital preparation fails, the unprepared PDF is stored
        // under the same name, and the report a previous attempt wrote is overwritten with a
        // withholding record — which this reads rather than counts.
        "DIGITAL_GEOMETRY" => stored.DigitalReportWithheld
            ? Failed(
                id,
                "the stored digital preflight report records a refusal, so the file under the "
                + "customer PDF's name has not passed its own geometry check.",
                [stored.DigitalReportName])
            : stored.DigitalReportPresent
                ? Passed(
                    id, "the customer PDF passed its own preflight: 14 trim-size pages, CropBox "
                        + "present, no press structures, sRGB rasters, /Lang ka-GE, linearized.",
                    [stored.DigitalReportName])
                : Missing(
                    id, "the customer PDF was withheld or never preflighted.",
                    [stored.DigitalReportName]),

        "HANDBACK_COMPLETENESS" => stored.HandbackGaps.Count > 0
            ? Missing(
                id,
                "the package would ship without " + string.Join(", ", stored.HandbackGaps) + ".",
                stored.HandbackGaps)
            : Passed(
                id,
                "normalized story, scenario, asset lock, composition receipts, QA evidence, "
                + "provenance and the final PDFs are all stored.",
                [stored.ManifestName]),

        // A gate the supplier's document names and this deployment cannot answer. Never a pass:
        // an unanswerable gate is the exact state audit P0-09 found being read as success.
        _ => new BekiGateResult(
            id, Unknown, GateClasses.GetValueOrDefault(id, SharedClass),
            "this deployment has no evaluator for this gate.", []),
    };

    /// <summary>
    /// The four press gates, which share one piece of evidence: the preflight refuses rather than
    /// degrades, so a stored preflight report IS the pass, and the withholding record written when
    /// it refused is what names which gate did the refusing.
    /// </summary>
    private static BekiGateResult EvaluatePress(string id, StoredEvidence stored)
    {
        if (stored.PressFailedGates.Contains(id, StringComparer.Ordinal))
        {
            return Failed(id, stored.PressWithheldReason ?? "the press stage refused.", [stored.PressStatusName]);
        }

        /*
          A preflight report that declares its own refusal, which is what a retried pack leaves
          behind when a stage that succeeded last time fails this time.

          Checked before presence, because it IS present — that is the whole difficulty. The stored
          document is a withholding record written over the previous attempt's success, and reading
          it as "a report exists, so preparation happened" is how bytes nobody preflighted acquire
          a preflight.
        */
        if (stored.InteriorPreflightWithheld || stored.CoverPreflightWithheld)
        {
            var refused = new List<string>();
            if (stored.InteriorPreflightWithheld) refused.Add("interior");
            if (stored.CoverPreflightWithheld) refused.Add("cover");

            return Failed(
                id,
                $"the stored press {string.Join(" and ", refused)} preflight report(s) record a "
                + "refusal rather than a preparation"
                + (stored.PressWithheldReason is { Length: > 0 } why ? $" — {why}" : "."),
                [stored.InteriorPreflightName, stored.CoverPreflightName, stored.PressStatusName]);
        }

        if (!stored.InteriorPreflightPresent || !stored.CoverPreflightPresent)
        {
            var absent = new List<string>();
            if (!stored.InteriorPreflightPresent) absent.Add("interior");
            if (!stored.CoverPreflightPresent) absent.Add("cover");

            return Missing(
                id,
                $"no preflight report for the press {string.Join(" or ", absent)}"
                + (stored.PressWithheldReason is { Length: > 0 } why ? $" — {why}" : "."),
                [stored.InteriorPreflightName, stored.CoverPreflightName, stored.PressStatusName]);
        }

        return Passed(
            id,
            "both press files came out of print preparation, which refuses rather than reports.",
            [stored.InteriorPreflightName, stored.CoverPreflightName]);
    }

    private static BekiGateResult Passed(string id, string detail, IReadOnlyList<string> evidence) =>
        new(id, Pass, GateClasses.GetValueOrDefault(id, SharedClass), detail, evidence);

    private static BekiGateResult Failed(string id, string detail, IReadOnlyList<string> evidence) =>
        new(id, Fail, GateClasses.GetValueOrDefault(id, SharedClass), detail, evidence);

    private static BekiGateResult Missing(string id, string detail, IReadOnlyList<string> evidence) =>
        new(id, Unknown, GateClasses.GetValueOrDefault(id, SharedClass), detail, evidence);

    private static BekiGateResult NeedsAHuman(string id, string detail, IReadOnlyList<string> evidence) =>
        new(id, NeedsHuman, GateClasses.GetValueOrDefault(id, SharedClass), detail, evidence);

    // ==============================================================================================
    // Reading the evidence back out of storage
    // ==============================================================================================

    private async Task<StoredEvidence> GatherAsync(
        Guid userId, Guid packId, CancellationToken cancellationToken)
    {
        var evidence = new StoredEvidence
        {
            AssetLockName = BekiPackBlobs.AssetLockName(userId, packId),
            ManifestName = BekiPackBlobs.ManifestName(userId, packId),
            ReviewName = BekiPackBlobs.CompositeReviewName(userId, packId),
            HumanApprovalName = BekiPackBlobs.HumanApprovalName(userId, packId),
            WrapCompositeName = BekiPackBlobs.CoverWrapCompositeName(userId, packId),
            WrapBaseName = BekiPackBlobs.CoverWrapBaseName(userId, packId),
            CoverCompositionName = BekiPackBlobs.CoverCompositionName(userId, packId),
            CoverFrontName = BekiPackBlobs.CoverFrontName(userId, packId),
            InteriorPreflightName = BekiPackBlobs.InteriorPreflightName(userId, packId),
            CoverPreflightName = BekiPackBlobs.CoverPreflightName(userId, packId),
            PressStatusName = BekiPackBlobs.PressStatusName(userId, packId),
            DigitalReportName = BekiPackBlobs.DigitalReportName(userId, packId),
        };

        evidence.AssetLock = ParseAssetLock(
            await TryReadTextAsync(evidence.AssetLockName, cancellationToken));

        var manifest = ParseManifest(
            await TryReadTextAsync(evidence.ManifestName, cancellationToken));

        var receipted = (manifest?.Compositions ?? [])
            .Where(entry => entry.PoseId is { Length: > 0 } && entry.OutputSha256 is { Length: > 0 })
            .Select(entry => entry.SpreadNumber)
            .ToHashSet();

        evidence.SpreadsWithoutReceipt = Enumerable.Range(1, BookFormat.SpreadCount)
            .Where(spread => !receipted.Contains(spread))
            .ToList();

        evidence.CoverPromptVersion = manifest?.Cover?.PromptVersion;
        evidence.CoverIsWrapMaster = manifest?.Cover?.IsWrapMaster == true;

        evidence.WrapCompositePresent = await ExistsAsync(evidence.WrapCompositeName, cancellationToken);
        evidence.WrapBasePresent = await ExistsAsync(evidence.WrapBaseName, cancellationToken);
        evidence.CoverCompositionPresent = await ExistsAsync(evidence.CoverCompositionName, cancellationToken);
        evidence.CoverFrontPresent = await ExistsAsync(evidence.CoverFrontName, cancellationToken);
        evidence.ReviewPresent = await ExistsAsync(evidence.ReviewName, cancellationToken);

        /*
          Per-spread QA, and now its VERDICT rather than its existence — amendment B1.

          The record was read for its version and then counted, so a stored document whose own status
          said FAIL satisfied this gate by being present. That was survivable while the only way a
          page got a QA record was by passing; the release policy ends that, because a spread the
          reviewer refused now ships with its refusal written down, and a gate that counted documents
          would grade that book PASS. A stored FAIL can never produce a PASS.

          Three answers, exactly as the fixed pages have had since the audit: a current PASS is
          evidence, a current refusal names the page, and anything else — absent, unreadable, or
          written under a superseded reviewer contract — is no record at all.
        */
        var spreadQaNames = new List<string>();
        var spreadsWithoutQa = new List<int>();
        var spreadsFailingQa = new List<int>();
        var spreadsNeedingHuman = new List<int>();
        var spreadsReviewSkipped = new List<int>();
        for (var spread = 1; spread <= BookFormat.SpreadCount; spread++)
        {
            var name = BekiPackBlobs.SpreadQaName(userId, packId, spread);
            var json = await TryReadTextAsync(name, cancellationToken);
            var record = CompositeSpreadQa.TryReadStored(json);

            if (record is null)
            {
                spreadsWithoutQa.Add(spread);
                continue;
            }

            spreadQaNames.Add(name);

            if (string.Equals(record.Status, CompositeQaVerdict.Pass, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // NEEDS_HUMAN is what the pipeline writes when the reviewer's answer could not be read
            // at all and the policy shipped the page anyway. It is a weaker statement than FAIL —
            // nobody said the picture was wrong — and it routes to the human gate rather than to a
            // failure, which is the distinction the whole NEEDS_HUMAN status exists to make.
            if (string.Equals(record.Status, NeedsHuman, StringComparison.OrdinalIgnoreCase))
            {
                spreadsNeedingHuman.Add(spread);
            }
            /*
              And REVIEW_SKIPPED_BY_POLICY is what it writes when nobody was asked — owner's rule 5,
              2026-09-01: "we don't need additional reviews for images".

              Read here, at the same seam as the other three, because this gate's whole discipline is
              that the stored document decides. The alternative — asking the policy what it is set to
              now — would grade a book by a switch that may have been flipped since it was drawn, and
              the record in front of us already says what actually happened.
            */
            else if (string.Equals(
                         record.Status,
                         CompositeBookPipeline.ReviewSkippedStatus,
                         StringComparison.OrdinalIgnoreCase))
            {
                spreadsReviewSkipped.Add(spread);
            }
            else
            {
                spreadsFailingQa.Add(spread);
            }
        }

        evidence.SpreadQaNames = spreadQaNames;
        evidence.SpreadsWithoutQa = spreadsWithoutQa;
        evidence.SpreadsFailingQa = spreadsFailingQa;
        evidence.SpreadsNeedingHumanQa = spreadsNeedingHuman;
        evidence.SpreadsWithReviewSkipped = spreadsReviewSkipped;

        /*
          The fixed pages, read the way the spreads above are read.

          They were counted by existence, and the document under the name was never opened — so a
          record whose own status said FAIL (an endpaper that placed nothing the lock proved, a wash
          inside the fold clearance) satisfied the gate by being there. Three answers now: a current
          PASS is evidence, a current FAIL is a failure that names the page, and anything else —
          absent, unparseable, or written under a superseded QA contract — is no record at all.
        */
        var fixedQaNames = new List<string>();
        var fixedWithout = new List<string>();
        var fixedFailing = new List<string>();
        foreach (var role in BekiFixedPageQa.Roles)
        {
            var name = BekiPackBlobs.FixedPageQaName(userId, packId, role);

            switch (BekiFixedPageQa.ReadStoredStatus(
                await TryReadTextAsync(name, cancellationToken)))
            {
                case BekiFixedPageQa.Pass:
                    fixedQaNames.Add(name);
                    break;
                case BekiFixedPageQa.Fail:
                    fixedQaNames.Add(name);
                    fixedFailing.Add(role);
                    break;
                default:
                    fixedWithout.Add(role);
                    break;
            }
        }

        evidence.FixedQaNames = fixedQaNames;
        evidence.FixedPagesWithoutQa = fixedWithout;
        evidence.FixedPagesFailingQa = fixedFailing;

        // The layout receipts, one whole-document file per composed artifact.
        var layoutNames = new List<string>();
        var pagesWithoutType = new List<string>();
        foreach (var mode in BekiPackBlobs.LayoutModes)
        {
            var name = BekiPackBlobs.LayoutReceiptName(userId, packId, mode);
            var json = await TryReadTextAsync(name, cancellationToken);
            if (json is not { Length: > 0 })
            {
                continue;
            }

            layoutNames.Add(name);
            evidence.LayoutReceiptDocuments.Add(mode);
            pagesWithoutType.AddRange(PagesWithoutTypography(mode, json));
        }

        evidence.LayoutReceiptNames = layoutNames;
        evidence.LayoutPagesWithoutTypography = pagesWithoutType;

        /*
          The three preparation reports, opened rather than counted.

          Presence is not a verdict when a retry can leave a previous attempt's document under the
          name: the stages write "verdict": "PASS" when they prepare a file, and a withholding
          record written over a stale success says otherwise in the same field. See
          ReportDeclaresRefusal.
        */
        var interiorPreflight = await TryReadTextAsync(evidence.InteriorPreflightName, cancellationToken);
        var coverPreflight = await TryReadTextAsync(evidence.CoverPreflightName, cancellationToken);
        var digitalReport = await TryReadTextAsync(evidence.DigitalReportName, cancellationToken);

        evidence.InteriorPreflightWithheld = ReportDeclaresRefusal(interiorPreflight);
        evidence.CoverPreflightWithheld = ReportDeclaresRefusal(coverPreflight);
        evidence.DigitalReportWithheld = ReportDeclaresRefusal(digitalReport);

        evidence.InteriorPreflightPresent =
            interiorPreflight is { Length: > 0 } && !evidence.InteriorPreflightWithheld;
        evidence.CoverPreflightPresent =
            coverPreflight is { Length: > 0 } && !evidence.CoverPreflightWithheld;
        evidence.DigitalReportPresent =
            digitalReport is { Length: > 0 } && !evidence.DigitalReportWithheld;

        ReadPressStatus(
            await TryReadTextAsync(evidence.PressStatusName, cancellationToken), evidence);

        /*
          Which finals are actually in storage, and what was read back off each.

          The enumeration is the correction. RENDER_VALIDATION used to ask whether any stored report
          was releasable, so a press cover with no report of its own — or with a refusal — was
          carried by the other two artifacts passing. A file that is in storage is a file that ships,
          and every one of them owes this gate a report of its own.
        */
        var renderNames = new List<string>();
        var storedFinals = new List<string>();
        var finalsWithoutReport = new List<string>();
        var artifacts = new List<BekiArtifactEvidence>();

        foreach (var artifact in BekiPackBlobs.RenderedArtifacts)
        {
            var name = BekiPackBlobs.RenderReportName(userId, packId, artifact);
            var json = await TryReadTextAsync(name, cancellationToken);
            var isStored = await ExistsAsync(
                BekiPackBlobs.FinalPdfName(userId, packId, artifact), cancellationToken);

            if (isStored)
            {
                storedFinals.Add(artifact);
            }

            if (json is not { Length: > 0 })
            {
                if (isStored)
                {
                    finalsWithoutReport.Add(artifact);

                    artifacts.Add(new BekiArtifactEvidence(
                        artifact, BekiPackBlobs.RenderArtifactClass(artifact), true,
                        Unknown, Unknown, name));
                }

                continue;
            }

            renderNames.Add(name);
            var read = ReadRenderReport(artifact, json, evidence);

            artifacts.Add(new BekiArtifactEvidence(
                artifact,
                BekiPackBlobs.RenderArtifactClass(artifact),
                isStored,
                read.Releasable ? Pass : Fail,
                read.QrStatus switch
                {
                    // No credits page on this artifact: it was asked nothing, and nothing is the
                    // correct answer rather than a silent failure.
                    null => Pass,
                    "ok" => Pass,
                    _ => Fail,
                },
                name));
        }

        evidence.RenderReportNames = renderNames;
        evidence.StoredFinals = storedFinals;
        evidence.StoredFinalsWithoutReport = finalsWithoutReport;
        evidence.Artifacts = artifacts;

        evidence.FontsLocked = evidence.AssetLock is { } locked
            && locked.Assets.Any(asset => asset.Role.Contains("noto", StringComparison.OrdinalIgnoreCase))
            && locked.Assets.Any(asset => asset.Role.Contains("ottia", StringComparison.OrdinalIgnoreCase));

        ReadHumanReview(
            await TryReadTextAsync(evidence.ReviewName, cancellationToken),
            await TryReadTextAsync(evidence.HumanApprovalName, cancellationToken),
            evidence);

        evidence.HandbackGaps = HandbackGaps(userId, packId, evidence, manifest);

        return evidence;
    }

    /// <summary>
    /// What the handback would be missing if it were assembled now — the completeness gate's whole
    /// content, named as blob paths so a reader can go and look.
    /// </summary>
    private static IReadOnlyList<string> HandbackGaps(
        Guid userId, Guid packId, StoredEvidence evidence, BekiFulfillmentManifest? manifest)
    {
        var gaps = new List<string>();

        if (evidence.AssetLock is null) gaps.Add(evidence.AssetLockName);
        if (manifest is null) gaps.Add(evidence.ManifestName);
        if (manifest?.ScenarioUrl is not { Length: > 0 }) gaps.Add(BekiPackBlobs.ScenarioName(userId, packId));
        if (manifest?.StoryUrl is not { Length: > 0 }) gaps.Add(BekiPackBlobs.StoryName(userId, packId));
        if (!evidence.ReviewPresent) gaps.Add(evidence.ReviewName);
        if (evidence.LayoutReceiptDocuments.Count == 0) gaps.Add(BekiPackBlobs.LayoutReceiptName(userId, packId, "reading"));
        if (evidence.SpreadsWithoutQa.Count > 0) gaps.Add(BekiPackBlobs.SpreadQaName(userId, packId, evidence.SpreadsWithoutQa[0]));
        if (evidence.RenderReports.Count == 0) gaps.Add(BekiPackBlobs.RenderReportName(userId, packId, BekiPackBlobs.InteriorRenderArtifact));

        // Build provenance is not on this list because it is not a blob: the export assembles it at
        // download time from the assembly's own informational version, the preflight reports and
        // whatever telemetry has landed. A gate that demanded a provenance file would be demanding
        // a file this design deliberately does not store.
        return gaps;
    }

    private static IEnumerable<string> PagesWithoutTypography(string mode, string json)
    {
        // A layout receipt without typography for a page that carries copy is TEXT_LAYER's blind
        // spot in reverse: the document exists, and evidences nothing.
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("pages", out var pages)
                || pages.ValueKind != JsonValueKind.Array)
            {
                return [$"{mode} (no pages)"];
            }

            return pages
                .EnumerateArray()
                .Where(page =>
                    page.TryGetProperty("text_lines", out var lines)
                    && lines.ValueKind == JsonValueKind.Array
                    && lines.GetArrayLength() > 0
                    && (!page.TryGetProperty("typography", out var type)
                        || type.ValueKind != JsonValueKind.Array
                        || type.GetArrayLength() == 0))
                .Select(page => $"{mode} p{(page.TryGetProperty("page", out var n) ? n : default)}")
                .ToList();
        }
        catch (JsonException)
        {
            return [$"{mode} (unreadable)"];
        }
    }

    private static void ReadPressStatus(string? json, StoredEvidence evidence)
    {
        if (json is not { Length: > 0 })
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.TryGetProperty("failed_gates", out var gates) && gates.ValueKind == JsonValueKind.Array)
            {
                evidence.PressFailedGates = gates
                    .EnumerateArray()
                    .Select(gate => gate.GetString())
                    .Where(gate => !string.IsNullOrWhiteSpace(gate))
                    .Select(gate => gate!)
                    .ToList();
            }

            if (root.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String)
            {
                evidence.PressWithheldReason = reason.GetString();
            }
        }
        catch (JsonException)
        {
            // An unreadable withholding record leaves the preflight-presence rule to answer.
        }
    }

    private static RenderReading ReadRenderReport(string artifact, string json, StoredEvidence evidence)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var verdict = root.TryGetProperty("verdict", out var v) ? v.GetString() : null;
            var releasable = string.Equals(verdict, "RELEASABLE", StringComparison.Ordinal);
            string? artifactQr = null;
            evidence.RenderReports.Add(new RenderSummary(artifact, releasable));

            // Only an artifact that was actually asked for a QR says anything about the QR gate. A
            // press cover has no credits page, and its scan reports "ok" for having been asked
            // nothing — reading that as evidence would let the gate pass on a book whose credits
            // page was never scanned at all.
            if (root.TryGetProperty("qr", out var qr)
                && qr.ValueKind == JsonValueKind.Object
                && qr.TryGetProperty("page", out var qrPage)
                && qrPage.ValueKind == JsonValueKind.Number
                && qr.TryGetProperty("status", out var qrStatus)
                && qrStatus.ValueKind == JsonValueKind.String
                && qrStatus.GetString() is { Length: > 0 } status)
            {
                // The strictest answer wins: a book with one clean scan and one refusal has a
                // refusal. Kept per artifact as well, because the withholding follows the file.
                artifactQr = status;
                evidence.QrStatus = evidence.QrStatus is null or "ok" ? status : evidence.QrStatus;
            }

            if (artifact == BekiPackBlobs.DigitalRenderArtifact
                && root.TryGetProperty("contact_sheet", out var contactSheet)
                && contactSheet.ValueKind == JsonValueKind.Object
                && contactSheet.TryGetProperty("sha256", out var sheet)
                && sheet.ValueKind == JsonValueKind.String
                && sheet.GetString() is { Length: > 0 } sha)
            {
                // The customer's fourteen pages are what a human reviews — cover included, which is
                // what makes the identity and age review of amendment A7 possible at all.
                evidence.ContactSheetSha256 = sha;
            }

            /*
              Embedding is read back through the renderers rather than asserted: `pdffonts` is one of
              the three runs whose failure makes a validation NOT_RELEASABLE, so a releasable
              artifact is one whose faces a second implementation could resolve.

              ANY releasable artifact answers it, not every one — and that is a deliberate line
              between the classes. FONT_INTEGRITY is shared, so making it depend on every artifact
              would let a press cover that failed its own render drag the parent's download down with
              it, which is the coupling amendment A5 exists to remove. A book where no stored
              artifact renders at all still fails, as it should.
            */
            evidence.FontsEmbedded = evidence.FontsEmbedded == true || releasable;

            return new RenderReading(releasable, artifactQr);
        }
        catch (JsonException)
        {
            evidence.RenderReports.Add(new RenderSummary(artifact, false));
            evidence.FontsEmbedded ??= false;

            // A report nobody can parse evidences nothing about the file it names.
            return new RenderReading(false, "unreadable");
        }
    }

    /// <summary>
    /// Whether a stored preparation report is a refusal rather than a preparation.
    ///
    /// The preparation stages write <c>"verdict": "PASS"</c> when they produce a file, and
    /// <see cref="BekiWithheldReport"/> writes anything else when a retry's failure overwrites an
    /// earlier attempt's success. A document that states no verdict at all is left to the presence
    /// rule — that is the shape every report written before this campaign has. Unparseable is a
    /// refusal: a report that cannot be read is not a report that passed.
    /// </summary>
    private static bool ReportDeclaresRefusal(string? json)
    {
        if (json is not { Length: > 0 })
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            return document.RootElement.TryGetProperty("verdict", out var verdict)
                && verdict.ValueKind == JsonValueKind.String
                && !string.Equals(verdict.GetString(), "PASS", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private static void ReadHumanReview(string? reviewJson, string? approvalJson, StoredEvidence evidence)
    {
        if (reviewJson is { Length: > 0 })
        {
            try
            {
                using var document = JsonDocument.Parse(reviewJson);
                evidence.NeedsHumanReading =
                    document.RootElement.TryGetProperty("needs_human_reading", out var flag)
                    && flag.ValueKind == JsonValueKind.True;
            }
            catch (JsonException)
            {
                // A review nobody can parse is a review nobody has read: treat it as flagged, which
                // routes the book to a person rather than past one.
                evidence.NeedsHumanReading = true;
            }
        }

        var approval = BekiHumanApproval.TryParse(approvalJson);
        evidence.HumanApprovalPresent = approval is not null;
        evidence.HumanApprover = approval?.ApprovedBy ?? "a reviewer";

        // Current means: it approves the contact sheet that render validation actually produced.
        // Amendment A2 — approval of a stale sheet is not approval of this book.
        evidence.HumanApprovalIsCurrent = approval is not null
            && evidence.ContactSheetSha256 is { Length: > 0 } current
            && string.Equals(approval.ContactSheetSha256, current, StringComparison.OrdinalIgnoreCase);
    }

    private static BekiAssetLockManifest? ParseAssetLock(string? json)
    {
        if (json is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<BekiAssetLockManifest>(
                json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static BekiFulfillmentManifest? ParseManifest(string? json)
    {
        if (json is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<BekiFulfillmentManifest>(
                json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<bool> ExistsAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            return await blobStorage.ExistsAsync(name, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    private async Task<string?> TryReadTextAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            if (!await blobStorage.ExistsAsync(name, cancellationToken))
            {
                return null;
            }

            await using var stream = await blobStorage.DownloadAsync(name, cancellationToken);
            using var reader = new StreamReader(stream);
            var text = await reader.ReadToEndAsync(cancellationToken);

            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private sealed record RenderSummary(string Artifact, bool Releasable);

    /// <summary>One artifact's render report, as the per-artifact slice needs it.</summary>
    private sealed record RenderReading(bool Releasable, string? QrStatus);

    /// <summary>Everything storage had to say about this book, in one bag the gates read from.</summary>
    private sealed class StoredEvidence
    {
        public required string AssetLockName { get; init; }

        public required string ManifestName { get; init; }

        public required string ReviewName { get; init; }

        public required string HumanApprovalName { get; init; }

        public required string WrapCompositeName { get; init; }

        public required string WrapBaseName { get; init; }

        public required string CoverCompositionName { get; init; }

        public required string CoverFrontName { get; init; }

        public required string InteriorPreflightName { get; init; }

        public required string CoverPreflightName { get; init; }

        public required string PressStatusName { get; init; }

        public required string DigitalReportName { get; init; }

        public BekiAssetLockManifest? AssetLock { get; set; }

        public IReadOnlyList<int> SpreadsWithoutReceipt { get; set; } = [];

        public string? CoverPromptVersion { get; set; }

        public bool CoverIsWrapMaster { get; set; }

        public bool WrapCompositePresent { get; set; }

        public bool WrapBasePresent { get; set; }

        public bool CoverCompositionPresent { get; set; }

        public bool CoverFrontPresent { get; set; }

        public bool ReviewPresent { get; set; }

        public IReadOnlyList<string> SpreadQaNames { get; set; } = [];

        public IReadOnlyList<int> SpreadsWithoutQa { get; set; } = [];

        /// <summary>Spreads whose stored record says the reviewer refused the page.</summary>
        public IReadOnlyList<int> SpreadsFailingQa { get; set; } = [];

        /// <summary>Spreads whose stored record says there was no readable verdict to record.</summary>
        public IReadOnlyList<int> SpreadsNeedingHumanQa { get; set; } = [];

        /// <summary>
        /// Spreads whose stored record says no model was asked about them at all — the release
        /// policy's <c>image_review</c> switch, as the pipeline wrote it down.
        ///
        /// Its own bucket rather than either of the two above, because it is neither. Nothing
        /// refused these pages, so they are not failures; and nobody is waiting to look at them,
        /// so they are not the human queue. What they are is unjudged, deliberately, and the
        /// gate's job is to say so out loud.
        /// </summary>
        public IReadOnlyList<int> SpreadsWithReviewSkipped { get; set; } = [];

        public IReadOnlyList<string> FixedQaNames { get; set; } = [];

        public IReadOnlyList<string> FixedPagesWithoutQa { get; set; } = [];

        public IReadOnlyList<string> FixedPagesFailingQa { get; set; } = [];

        public IReadOnlyList<string> LayoutReceiptNames { get; set; } = [];

        public List<string> LayoutReceiptDocuments { get; } = [];

        public IReadOnlyList<string> LayoutPagesWithoutTypography { get; set; } = [];

        public bool InteriorPreflightPresent { get; set; }

        public bool CoverPreflightPresent { get; set; }

        public bool DigitalReportPresent { get; set; }

        public bool InteriorPreflightWithheld { get; set; }

        public bool CoverPreflightWithheld { get; set; }

        public bool DigitalReportWithheld { get; set; }

        public IReadOnlyList<string> PressFailedGates { get; set; } = [];

        public string? PressWithheldReason { get; set; }

        public IReadOnlyList<string> RenderReportNames { get; set; } = [];

        /// <summary>The finals actually in storage, which is what RENDER_VALIDATION enumerates.</summary>
        public IReadOnlyList<string> StoredFinals { get; set; } = [];

        public IReadOnlyList<string> StoredFinalsWithoutReport { get; set; } = [];

        public IReadOnlyList<BekiArtifactEvidence> Artifacts { get; set; } = [];

        public List<RenderSummary> RenderReports { get; } = [];

        public string? QrStatus { get; set; }

        public string? ContactSheetSha256 { get; set; }

        public bool FontsLocked { get; set; }

        public bool? FontsEmbedded { get; set; }

        public bool NeedsHumanReading { get; set; }

        public bool HumanApprovalPresent { get; set; }

        public bool HumanApprovalIsCurrent { get; set; }

        public string HumanApprover { get; set; } = "a reviewer";

        public IReadOnlyList<string> HandbackGaps { get; set; } = [];
    }
}

/// <summary>
/// The QA record for a page nobody generated: the cover boards, the two endpaper spreads, the
/// intro and the credits.
///
/// D7's other half. The eight story spreads are judged by a model that looked at them; these six
/// pages have no model verdict to write down and were therefore evidenced by nothing at all — which
/// is how a placeholder endpaper once reached a printed book. What can be said about them is
/// mechanical and is exactly what the audit asks for: the approved assets they placed hashed to the
/// files the asset lock proved, the layout receipt for the page exists, the page that set copy
/// recorded typography for it, and any cream wash a pre-ruling receipt still carries is inside the
/// fold and trim clearances the wash rule stated. So the record is machine-generated from the layout
/// receipt and the asset-lock manifest, with no model call and no judgement.
/// </summary>
public static class BekiFixedPageQa
{
    public const string Version = "beki-fixed-page-qa-v1";

    public const string Pass = "PASS";

    public const string Fail = "FAIL";

    /// <summary>
    /// A stored fixed-page record's verdict: <c>PASS</c>, <c>FAIL</c>, or null for a document that
    /// is absent, unreadable, or written under a QA contract this deployment no longer stands
    /// behind.
    ///
    /// Version-checked for the reason the spread records are: a document that answered different
    /// questions is not this book's evidence, and the release gates treat "not this contract" the
    /// same as "not there" rather than as a pass.
    /// </summary>
    public static string? ReadStoredStatus(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!root.TryGetProperty("qa_prompt_version", out var version)
                || version.ValueKind != JsonValueKind.String
                || !string.Equals(version.GetString(), Version, StringComparison.Ordinal))
            {
                return null;
            }

            if (!root.TryGetProperty("status", out var status)
                || status.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return string.Equals(status.GetString(), Pass, StringComparison.OrdinalIgnoreCase)
                ? Pass
                : Fail;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The six pages this record covers, named as the composer's layout receipts name them, so a
    /// reader can put the QA file and the receipt side by side without a translation table.
    /// </summary>
    public static readonly IReadOnlyList<string> Roles =
        ["cover-front", "endpaper-front", "intro", "credits", "endpaper-rear", "cover-back"];

    /// <summary>
    /// The minimum a wash must keep clear of the centre fold and of any trim edge, in millimetres.
    /// The audit's P1-04 wording — "outside the fold safety area and trim safety margins" — as a
    /// number the receipt can be measured against.
    /// </summary>
    public const double MinimumClearanceMm = 5d;

    /// <summary>
    /// One page's record, or null when the composed document carries no such page — a legacy proof
    /// export has no <c>cover-back</c>, and inventing a verdict for a page that does not exist is
    /// how a QA set becomes decoration.
    /// </summary>
    /// <param name="receipts">The layout receipts for the document this page belongs to.</param>
    /// <param name="lockedAssetHashes">Every SHA-256 the asset lock proved, for the placement check.</param>
    public static string? Write(
        string role,
        BekiLayoutReceipts receipts,
        IReadOnlySet<string> lockedAssetHashes)
    {
        ArgumentNullException.ThrowIfNull(receipts);
        ArgumentNullException.ThrowIfNull(lockedAssetHashes);

        var page = receipts.Pages.FirstOrDefault(candidate => candidate.Role == role);
        if (page is null)
        {
            return null;
        }

        var failed = new List<string>();

        /*
          The art on a story-derived page (the cover boards are crops of the generated wrap) is not
          in the lock and cannot be: it is this book's own artwork. What is checked is the fixed
          furniture — the endpaper pattern, the intro background, the composited Beki marks — which
          is precisely what a placeholder would replace.

          Compared SOURCE to lock, not final raster to lock, which is the review's finding. The
          reading copy's endpaper is the approved pattern downsampled for a screen and the intro is
          a background with a pose composited onto it; neither embedded raster can hash to a locked
          file, so the check as written failed every approved page it was pointed at and would have
          held a correct book. The receipt now carries the provenance separately, and this reads
          that.
        */
        var placementIsFixedArt = role is "endpaper-front" or "endpaper-rear" or "intro" or "credits";
        var sources = page.SourceSha256 ?? [];
        var unlocked = placementIsFixedArt
            ? sources.Where(sha => !lockedAssetHashes.Contains(sha)).ToList()
            : [];

        if (placementIsFixedArt && page.ImageSha256.Count == 0)
        {
            failed.Add("ASSET_PLACEMENT: the page placed no raster at all.");
        }

        if (placementIsFixedArt && sources.Count == 0)
        {
            failed.Add(
                "ASSET_PLACEMENT: the layout receipt names no approved source for the raster(s) "
                + "this page placed, so their provenance cannot be checked against the lock.");
        }

        if (unlocked.Count > 0)
        {
            failed.Add(
                $"ASSET_PLACEMENT: {unlocked.Count} placed raster(s) derive from source bytes the "
                + "asset lock did not prove.");
        }

        /*
          The wash clauses, kept and now vacuous.

          Owner ruling 2026-09-01 — the third and final — took the cream box out of the book, so a
          receipt written by today's composer carries no wash and this block does not run. It stays
          because Write is also how an operator re-derives a QA record from a receipt stored before
          the ruling, and those receipts do carry one: the clearances a wash had to keep are still
          the right question to ask of a book that has one.
        */
        if (page.Wash is { } wash)
        {
            if (wash.FoldClearanceMm < MinimumClearanceMm)
            {
                failed.Add(
                    $"WASH_GEOMETRY: the wash comes within {wash.FoldClearanceMm:0.0} mm of the "
                    + $"centre fold ({MinimumClearanceMm:0.0} mm is the minimum).");
            }

            if (wash.TrimClearanceMm < MinimumClearanceMm)
            {
                failed.Add(
                    $"WASH_GEOMETRY: the wash comes within {wash.TrimClearanceMm:0.0} mm of a trim "
                    + $"edge ({MinimumClearanceMm:0.0} mm is the minimum).");
            }
        }

        if (page.Typography.Count == 0 && page.TextLines.Count > 0)
        {
            failed.Add("TEXT_LAYER: the page set copy and recorded no typography for it.");
        }

        return JsonSerializer.Serialize(
            new
            {
                role,
                page = page.Page,
                qa_prompt_version = Version,
                mode = receipts.Mode,
                status = failed.Count == 0 ? Pass : Fail,
                recommended_action = failed.Count == 0 ? "ship" : "fix_layout",
                machine_generated = true,
                checks = new
                {
                    asset_hashes_verified = unlocked.Count == 0,
                    layout_receipt_present = true,
                    wash_within_rules = failed.All(f => !f.StartsWith("WASH_GEOMETRY", StringComparison.Ordinal)),
                },
                failed_checks = failed,
                image_sha256 = page.ImageSha256,
                // Both hashes, side by side: what the page carries, and what it came from. They
                // differ on every derived page, which is the whole content of this correction.
                source_sha256 = sources,
                wash = page.Wash,
                typography = page.Typography,
            },
            BekiReleaseGates.Json);
    }
}

/// <summary>
/// A person's signature on one rendered contact sheet — the "explicit signed resolution" the
/// VISUAL_QA gate asks for when a book carries a shot or age advisory.
/// </summary>
/// <param name="ContactSheetSha256">
/// The pixels that were looked at. Amendment A2 makes this the whole point of the artifact: an
/// approval that does not name a rendering is an approval of nothing in particular, and a book
/// re-rendered after approval is a book nobody has approved.
/// </param>
public sealed record BekiHumanApproval(
    [property: JsonPropertyName("approved_by")] string ApprovedBy,
    [property: JsonPropertyName("approved_at_utc")] DateTimeOffset ApprovedAtUtc,
    [property: JsonPropertyName("contact_sheet_sha256")] string ContactSheetSha256,
    [property: JsonPropertyName("note")] string? Note)
{
    public string ToJson() => JsonSerializer.Serialize(this, BekiReleaseGates.Json);

    public static BekiHumanApproval? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var approval = JsonSerializer.Deserialize<BekiHumanApproval>(json, BekiReleaseGates.Json);

            return approval is { ContactSheetSha256.Length: > 0, ApprovedBy.Length: > 0 }
                ? approval
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
