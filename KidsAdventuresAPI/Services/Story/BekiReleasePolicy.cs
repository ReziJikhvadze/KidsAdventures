using AdventurePacks.Api.Repositories.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AdventurePacks.Api.Services.Story;

/// <summary>
/// The names of the checks that are not release gates — the pipeline's own quality refusals, plus
/// the human gate.
///
/// The sixteen release-gate ids are the supplier's and are read from their document; these five are
/// ours, and they are the whole of amendment B3's policy-eligible whitelist. Everything else the
/// pipeline can stop on — a provider exception, bytes that will not decode, a compositor that
/// refused, an exact-Beki hash mismatch, the asset lock, a composition receipt, the wrap hash, the
/// identity spec, a scenario that would not resume — stays terminal, because those protect an
/// invariant rather than a taste. A book that fails one of them has nothing to ship.
/// </summary>
public static class BekiReleaseChecks
{
    /// <summary>
    /// The blocking centre-fold measurement, on the page's second base image. Waivable: a fold the
    /// measurement dislikes is still a picture, and the alternative is a paid book that dies for a
    /// reading a person may well disagree with.
    /// </summary>
    public const string CentreFold = "centre_fold";

    /// <summary>The cover wrap's construction-band measurement, second attempt.</summary>
    public const string CoverBands = "cover_bands";

    /// <summary>
    /// The visual reviewer's verdict-based refusal of an intact composite — the exhausted retry
    /// ladder included. The picture exists and was composited from approved bytes; what failed is an
    /// opinion about it.
    /// </summary>
    public const string ImageQa = "image_qa";

    /// <summary>
    /// Two unreadable answers from the reviewer. Waivable for the same reason and with one extra
    /// obligation: the evidence this path never attached is attached now, whichever way the policy
    /// goes, because "we could not read the review" is only useful next to the page it is about.
    /// </summary>
    public const string QaUnreadable = "qa_unreadable";

    /// <summary>
    /// Whether a person has to sign a book off before anything ships. <c>blocker</c> is the old
    /// behaviour — VISUAL_QA's human half withholds until the contact sheet is approved;
    /// <c>flag</c> is the owner's default, which is that review is skipped and recorded.
    /// </summary>
    public const string HumanReview = "human_review";

    /// <summary>Every check this deployment mints, for the admin table and the equivalence test.</summary>
    public static readonly IReadOnlyList<string> Pipeline =
        [CentreFold, CoverBands, ImageQa, QaUnreadable, HumanReview];
}

/// <summary>The two words a check can be set to, and the wildcard class.</summary>
public static class BekiReleaseSeverity
{
    /// <summary>Stops the thing it is about: the book, or the deliverable.</summary>
    public const string Blocker = "blocker";

    /// <summary>Ships it and raises an alarm. The owner's default for everything but the press files.</summary>
    public const string Flag = "flag";

    /// <summary>
    /// The class that means "whatever deliverable this is about" — the row almost every check uses.
    /// A per-class row wins over it; see <see cref="BekiReleasePolicySnapshot.SeverityOf"/>.
    /// </summary>
    public const string AllClasses = "all";
}

/// <summary>
/// One row of the policy table: what a named check does, for one deliverable class.
/// </summary>
/// <param name="DeliverableClass">
/// <c>all</c>, or one of the gate classes (<c>shared</c>, <c>press</c>, <c>digital</c>,
/// <c>package</c>). Amendment B2: RENDER_VALIDATION and QR aggregate evidence from artifacts that
/// belong to different deliverables, so their severity has to be sayable per class — blocker about
/// the printer's files, flag about the reading copy — and one row per check could not say it.
/// </param>
public sealed record BekiReleaseCheckSetting(
    string CheckId,
    string DeliverableClass,
    string Severity,
    string? UpdatedBy,
    DateTimeOffset? UpdatedAtUtc);

/// <summary>
/// The policy as one immutable reading, taken once and passed down.
///
/// Amendment B4 is why this is a value rather than a service call: a fulfilment job that consulted a
/// cached service at each decision could apply one policy to spread three and a different one to
/// spread seven, because an admin flipped a switch in between. A book is judged by the policy that
/// was in force when its evaluation began, whichever that was, and the snapshot is what makes that
/// sentence true rather than aspirational.
/// </summary>
public sealed class BekiReleasePolicySnapshot
{
    private readonly Dictionary<(string CheckId, string Class), string> _severities;

    public BekiReleasePolicySnapshot(IEnumerable<BekiReleaseCheckSetting> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Settings = settings.ToList();

        _severities = new Dictionary<(string, string), string>();

        foreach (var setting in Settings)
        {
            // Last row wins rather than throwing: the table's primary key already forbids
            // duplicates, and a snapshot that threw would take the whole fulfilment job down over a
            // row somebody hand-inserted.
            _severities[(Key(setting.CheckId), Key(setting.DeliverableClass))] =
                Normalize(setting.Severity);
        }
    }

    /// <summary>Every row this snapshot was built from, in the order it was read.</summary>
    public IReadOnlyList<BekiReleaseCheckSetting> Settings { get; }

    /// <summary>
    /// What a check does for one deliverable, resolved in the order the table is keyed: the row for
    /// this exact class, then the <c>all</c> row, then the code default.
    ///
    /// The code default is not a fallback nobody thought about — <see cref="Defaults"/> is the same
    /// list migration 035 seeds, and the two are required to agree. What it is for is the database
    /// that has not been migrated yet, the check a later campaign mints without a row, and the row
    /// an operator deleted: all three answer with the shipping behaviour rather than with silence.
    /// </summary>
    public string SeverityOf(string checkId, string deliverableClass = BekiReleaseSeverity.AllClasses)
    {
        var id = Key(checkId);
        var cls = Key(deliverableClass);

        if (_severities.TryGetValue((id, cls), out var exact))
        {
            return exact;
        }

        if (cls != BekiReleaseSeverity.AllClasses
            && _severities.TryGetValue((id, BekiReleaseSeverity.AllClasses), out var wildcard))
        {
            return wildcard;
        }

        return DefaultSeverityOf(id, cls);
    }

    /// <summary>Whether this check waives rather than blocks, for this deliverable.</summary>
    public bool IsFlagged(string checkId, string deliverableClass = BekiReleaseSeverity.AllClasses) =>
        SeverityOf(checkId, deliverableClass) == BekiReleaseSeverity.Flag;

    /// <summary>
    /// Whether a person still has to sign books off. False under the owner's default, which is that
    /// the human gate is recorded and skipped.
    /// </summary>
    public bool HumanReviewRequired =>
        SeverityOf(BekiReleaseChecks.HumanReview) == BekiReleaseSeverity.Blocker;

    /*
      The two gate lists are declared HERE, above the snapshots that read them.

      Static field initializers run in textual order, so a list declared below Defaults is null while
      Defaults is being built — which is a NullReferenceException inside a type initializer, the
      least legible failure C# has. Keeping the data above the things made from it is the whole fix.
    */

    /// <summary>
    /// The shared, digital and package gates: flagged by default, because a book whose artwork is in
    /// hand is a book the family paid for.
    /// </summary>
    private static readonly IReadOnlyList<string> FlaggedGateDefaults =
    [
        "ASSET_LOCK", "EXACT_BEKI", "SINGLE_COVER_MASTER", "COVER_CONTINUITY",
        "INTERIOR_CONTINUITY", "TEXT_LAYER", "FONT_INTEGRITY", "VISUAL_QA",
        "DIGITAL_GEOMETRY", "HANDBACK_COMPLETENESS",
    ];

    /// <summary>
    /// The printer's four, which keep their blockers. A press PDF is somebody else's press time: a
    /// bad one is a reprint and an invoice, not a disappointment a support message can answer.
    /// </summary>
    private static readonly IReadOnlyList<string> BlockingGateDefaults =
        ["PRESS_GEOMETRY", "PRESS_COLOR", "PRESS_RESOLUTION", "TEXT_COLOR_INTEGRITY"];

    /// <summary>
    /// The owner's ruling, as the policy a deployment has before anybody touches it — and the
    /// answer for any check with no row.
    ///
    /// It must stay identical to the seed block at the bottom of migration 035. A disagreement would
    /// make a deployment's behaviour depend on whether that script had run, which is the least
    /// debuggable difference a system can have.
    /// </summary>
    public static BekiReleasePolicySnapshot Defaults { get; } = new(DefaultSettings());

    /// <summary>
    /// Every check a blocker: the behaviour this system had before the policy existed.
    ///
    /// Kept because it is the honest thing to hand a caller that must not soften anything — and
    /// because a test that is about the gates rather than about the policy should be able to say so
    /// in one word.
    /// </summary>
    public static BekiReleasePolicySnapshot Strict { get; } = new(
        DefaultSettings()
            .Select(setting => setting with { Severity = BekiReleaseSeverity.Blocker })
            .ToList());

    private static IReadOnlyList<BekiReleaseCheckSetting> DefaultSettings()
    {
        var rows = new List<BekiReleaseCheckSetting>();

        void Add(string checkId, string cls, string severity) =>
            rows.Add(new BekiReleaseCheckSetting(checkId, cls, severity, "default", null));

        foreach (var check in BekiReleaseChecks.Pipeline)
        {
            Add(check, BekiReleaseSeverity.AllClasses, BekiReleaseSeverity.Flag);
        }

        foreach (var gate in FlaggedGateDefaults)
        {
            Add(gate, BekiReleaseSeverity.AllClasses, BekiReleaseSeverity.Flag);
        }

        foreach (var gate in BlockingGateDefaults)
        {
            Add(gate, BekiReleaseSeverity.AllClasses, BekiReleaseSeverity.Blocker);
        }

        foreach (var gate in BekiReleaseGates.PerArtifactGates.OrderBy(id => id, StringComparer.Ordinal))
        {
            Add(gate, BekiReleaseGates.PressClass, BekiReleaseSeverity.Blocker);
            Add(gate, BekiReleaseGates.DigitalClass, BekiReleaseSeverity.Flag);
        }

        return rows;
    }

    /// <summary>
    /// The answer for a check with no row, and the reason the comparisons here are all
    /// case-insensitive: <see cref="Key"/> has already lower-cased the id, and the gate ids these
    /// lists hold are the supplier's own SHOUTED ones. An ordinal comparison would silently match
    /// nothing and quietly flag the four press gates, which is the one place in this file where
    /// leniency is wrong.
    /// </summary>
    private static string DefaultSeverityOf(string checkId, string cls)
    {
        if (BekiReleaseGates.PerArtifactGates.Contains(checkId.ToUpperInvariant()))
        {
            return cls == BekiReleaseGates.PressClass
                ? BekiReleaseSeverity.Blocker
                : BekiReleaseSeverity.Flag;
        }

        if (BlockingGateDefaults.Contains(checkId, StringComparer.OrdinalIgnoreCase))
        {
            return BekiReleaseSeverity.Blocker;
        }

        /*
          Flag for everything else, including a check nothing here has heard of.

          The direction of the unknown-check default is a product decision rather than a safety one.
          A check minted by a later campaign will be one of ours — the whitelist's shape says so —
          and answering "blocker" for it would let a new quality measurement start killing paid books
          the moment it is written, before anybody has decided it should. It ships and it alarms.
        */
        return BekiReleaseSeverity.Flag;
    }

    private static string Key(string? value) =>
        (value ?? BekiReleaseSeverity.AllClasses).Trim().ToLowerInvariant() is { Length: > 0 } text
            ? text
            : BekiReleaseSeverity.AllClasses;

    private static string Normalize(string? severity) =>
        string.Equals(severity?.Trim(), BekiReleaseSeverity.Blocker, StringComparison.OrdinalIgnoreCase)
            ? BekiReleaseSeverity.Blocker
            : BekiReleaseSeverity.Flag;
}

public interface IBekiReleasePolicyService
{
    /// <summary>
    /// One reading of the whole policy, for a job or an evaluation to carry. Cached briefly; see the
    /// implementation for why the cache is never what a fulfilment job reads twice.
    /// </summary>
    Task<BekiReleasePolicySnapshot> SnapshotAsync(CancellationToken ct);

    /// <summary>Every row, for the admin table — including the ones nobody has ever set.</summary>
    Task<IReadOnlyList<BekiReleaseCheckSetting>> ListAsync(CancellationToken ct);

    /// <summary>
    /// Sets one check, and acts on it: a check moved to <c>flag</c> unlocks books that are sitting
    /// withheld for it, and this is what goes and publishes them (amendment B7).
    /// </summary>
    /// <returns>
    /// How many withheld books the change published — ONE scan's answer, which is why it is returned
    /// rather than left for the caller to go and measure. This used to start a reconciliation in the
    /// background while the controller awaited a second one of its own: two scans over the same set,
    /// racing, and the number the operator was shown was whatever the caller's copy managed to
    /// publish before the background copy took the rest. (Review finding 3.)
    /// </returns>
    Task<int> SetAsync(string checkId, string deliverableClass, string severity, string updatedBy, CancellationToken ct);
}

/// <summary>
/// The policy store, and the one thing setting a policy does beyond storing it.
///
/// The cache is thirty seconds and it exists for the ad-hoc reads — the admin page, the download
/// endpoint asking why a book is held. It is deliberately NOT what a fulfilment job or a gate
/// evaluation reads more than once: those take a snapshot at the start and carry it, which is
/// amendment B4's rule. A cache that were consulted mid-job would produce books judged by two
/// policies, and the resulting release-gates.json would describe a decision nobody made.
/// </summary>
/// <param name="scopes">
/// Where the reconciliation is resolved from, and it is a scope factory rather than the service
/// itself for two reasons that both had to be answered.
///
/// The first is a cycle: the reconciliation reads the policy to re-judge a withheld book, so a
/// constructor that took it directly would be a container that cannot build either of them. The
/// second is lifetime, and it is the one that would have bitten in production: the reconciliation
/// this triggers outlives the admin request that triggered it, so resolving it from the request's
/// own scope would hand a background task a repository whose connection is about to be disposed.
/// </param>
public sealed class BekiReleasePolicyService(
    IBekiReleasePolicyRepository repository,
    IServiceScopeFactory scopes,
    ILogger<BekiReleasePolicyService> logger,
    TimeProvider? timeProvider = null) : IBekiReleasePolicyService
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private BekiReleasePolicySnapshot? _cached;
    private DateTimeOffset _cachedAtUtc = DateTimeOffset.MinValue;

    public async Task<BekiReleasePolicySnapshot> SnapshotAsync(CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow();

        if (_cached is { } fresh && now - _cachedAtUtc < CacheLifetime)
        {
            return fresh;
        }

        await _gate.WaitAsync(ct);

        try
        {
            if (_cached is { } stillFresh && _timeProvider.GetUtcNow() - _cachedAtUtc < CacheLifetime)
            {
                return stillFresh;
            }

            var snapshot = new BekiReleasePolicySnapshot(await repository.ListAsync(ct));

            _cached = snapshot;
            _cachedAtUtc = _timeProvider.GetUtcNow();

            return snapshot;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            /*
              A policy table that cannot be read answers with the shipping defaults rather than
              stopping a book.

              The alternative was considered and refused: refusing to evaluate would mean a database
              hiccup withholds every deliverable in flight, which is the exact class of fault — "the
              machinery blocks the paying parent" — this whole campaign exists to remove. The
              defaults are the owner's ruling; falling back to them is falling back to the intended
              behaviour, not to an absence of one.
            */
            logger.LogError(
                ex, "The Beki release policy could not be read; using the shipped defaults for this "
                    + "evaluation. An operator's overrides are NOT in force until this recovers.");

            return BekiReleasePolicySnapshot.Defaults;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<IReadOnlyList<BekiReleaseCheckSetting>> ListAsync(CancellationToken ct) =>
        repository.ListAsync(ct);

    public async Task<int> SetAsync(
        string checkId, string deliverableClass, string severity, string updatedBy, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checkId);

        var normalizedSeverity =
            string.Equals(severity?.Trim(), BekiReleaseSeverity.Blocker, StringComparison.OrdinalIgnoreCase)
                ? BekiReleaseSeverity.Blocker
                : string.Equals(severity?.Trim(), BekiReleaseSeverity.Flag, StringComparison.OrdinalIgnoreCase)
                    ? BekiReleaseSeverity.Flag
                    : throw new ArgumentOutOfRangeException(
                        nameof(severity), severity, "A check is either a blocker or a flag.");

        var normalizedClass = string.IsNullOrWhiteSpace(deliverableClass)
            ? BekiReleaseSeverity.AllClasses
            : deliverableClass.Trim().ToLowerInvariant();

        await repository.SetAsync(
            checkId.Trim(), normalizedClass, normalizedSeverity, updatedBy, ct);

        // The cache is dropped rather than refreshed: the next reader takes the reading, and a
        // refresh here would be a second round trip on a path a person is waiting on.
        _cached = null;

        logger.LogInformation(
            "Beki release policy: {CheckId} ({Class}) is now a {Severity}, set by {UpdatedBy}.",
            checkId, normalizedClass, normalizedSeverity, updatedBy);

        /*
          And the change acts — amendment B7.

          A policy that only took effect on the next book would leave every already-withheld book
          withheld, which is precisely the state an operator flipping the switch is trying to end.
          So the withheld set is re-evaluated under the new policy and whatever unlocks is published.

          ONE scan, awaited, and its count returned. It used to be fire-and-forget on the grounds
          that a console request must not wait on a scan — but the console request waited anyway,
          because the controller ran a second reconciliation of its own so that it would have a
          number to show. Two scans over the same set, concurrently: every write in both is
          compare-and-set so nothing was published twice, but whichever one reached a book first took
          it out of the other's view, and the figure the operator read was a fraction of what their
          click had actually released. (Review finding 3.)

          Note what it deliberately does NOT do: flag → blocker never revokes a file that has already
          been published. Publication is a promise to a family, and there is no version of "we have
          taken your book back" that is better than an alarm.
        */
        try
        {
            /*
              Its own scope, and the reason survives the change from fire-and-forget.

              The reconciliation reads the policy, so this service cannot hold one without making a
              cycle the container could not build; and it is resolved per call rather than kept,
              because a scan that took minutes must not be pinning a repository connection between
              policy changes.
            */
            using var scope = scopes.CreateScope();

            var republished = await scope.ServiceProvider
                .GetRequiredService<IBekiReleaseReconciliation>()
                .ReconcileWithheldAsync(ct);

            if (republished > 0)
            {
                logger.LogWarning(
                    "Beki release policy change published {Count} book(s) that had been withheld.",
                    republished);
            }

            return republished;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            /*
              The policy row is already written, and it stays written.

              A reconciliation that could not run is a set of books that stay withheld until the next
              pass — a disappointment. Rolling the setting back because the follow-up scan failed
              would be a worse answer: the operator's decision is recorded, and the next policy
              change or the next scan acts on it.
            */
            logger.LogError(
                ex, "The reconciliation triggered by a release-policy change did not finish. The "
                    + "setting is stored; the withheld books will be re-judged by the next pass.");

            return 0;
        }
    }
}
