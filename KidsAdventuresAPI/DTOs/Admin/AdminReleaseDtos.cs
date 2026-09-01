namespace AdventurePacks.Api.DTOs.Admin;

/// <summary>
/// The release policy as the console shows it: one row per check, with the severity actually in
/// force and whether that severity is a stored decision or the shipped default.
///
/// The board is the policy service's own default list, laid over with whatever anybody has stored.
/// It is built that way rather than from the table alone because the table holds only overrides,
/// and a settings screen rendered from overrides opens empty on a fresh install and tells an
/// operator there is nothing here to configure.
/// </summary>
/// <param name="HumanReviewRequired">
/// The dedicated switch: <c>human_review</c> at <c>blocker</c> means a person must sign off the
/// rendered contact sheet before a book reaches its parent, <c>flag</c> means the step is skipped
/// and recorded as waived. Surfaced separately because it is the one setting that changes what an
/// operator has to do every day, and buried in a table of twenty gate names it would be a policy
/// nobody knows is on.
/// </param>
public sealed record AdminReleasePolicyResponse(
    bool HumanReviewRequired,
    IReadOnlyList<AdminReleaseCheckSetting> Checks);

/// <summary>
/// One check's severity, keyed as amendment B2 requires by check AND deliverable class: the same
/// render validation is a blocker on the press files a printer will bill for and a flag on the
/// PDF a parent reads tonight.
/// </summary>
/// <param name="DeliverableClass">
/// <c>all</c> is the wildcard row — a check whose severity does not vary by artifact.
/// </param>
/// <param name="Severity"><c>blocker</c> or <c>flag</c>.</param>
/// <param name="IsDefault">
/// True while nobody has stored a decision about this row, so the console can say "as shipped"
/// instead of naming an operator who never made one.
/// </param>
public sealed record AdminReleaseCheckSetting(
    string CheckId,
    string DeliverableClass,
    string Severity,
    bool IsDefault,
    string? UpdatedBy,
    DateTimeOffset? UpdatedAtUtc);

/// <summary>Body of <c>PUT /api/admin/release-policy</c>.</summary>
public sealed class AdminSetReleasePolicyRequest
{
    /// <summary>One of the policy's check ids. <c>human_review</c> is the review switch.</summary>
    public string? CheckId { get; set; }

    /// <summary>
    /// <c>all</c>, <c>press</c>, <c>digital</c>, <c>shared</c> or <c>package</c>. Omitted means
    /// <c>all</c>, which is what a check with a single severity wants.
    /// </summary>
    public string? DeliverableClass { get; set; }

    /// <summary><c>blocker</c> or <c>flag</c>.</summary>
    public string? Severity { get; set; }
}

/// <summary>
/// What a policy change did, not merely that it was recorded (amendment B7).
///
/// Loosening a check is a promise to the parents whose finished books are sitting withheld under
/// the old rule, so the change re-evaluates them and publishes what unlocks.
/// <paramref name="PublishedPacks"/> is how many books came out of that, which is the number that
/// tells an operator whether their click reached anybody.
/// </summary>
public sealed record AdminReleasePolicyUpdateResponse(
    AdminReleaseCheckSetting Setting,
    int PublishedPacks,
    bool HumanReviewRequired);

/// <summary>
/// One alarm: something the pipeline waived rather than died on, waiting for somebody to look.
///
/// <paramref name="EvidenceBlob"/> is a storage key and is returned as a key, not a link: the
/// console reaches evidence through the handback package on the order's own page, which is the
/// same rule the release-gates projection follows.
/// </summary>
/// <param name="LastSeenUtc">
/// Updated rather than duplicated when the same incident recurs (amendment B4), so a book redrawn
/// four times is one row with a recent timestamp instead of four rows saying the same thing.
/// </param>
public sealed record AdminAlarmRow(
    Guid Id,
    Guid PackId,
    Guid? OrderId,
    Guid UserId,
    string CheckId,
    string Severity,
    string Detail,
    string? EvidenceBlob,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastSeenUtc,
    string? ReviewedBy,
    DateTimeOffset? ReviewedAtUtc,
    string? Resolution);

/// <summary>
/// The alarm list, with the open count alongside it.
///
/// <paramref name="OpenCount"/> is counted independently of the returned page: the header badge
/// must say how many there are, not how many fitted in one request.
/// </summary>
public sealed record AdminAlarmListResponse(int OpenCount, IReadOnlyList<AdminAlarmRow> Items);

/// <summary>Body of <c>POST /api/admin/alarms/{id}/review</c>.</summary>
public sealed class AdminReviewAlarmRequest
{
    /// <summary>
    /// One of <c>acknowledged</c>, <c>fixed</c>, <c>wont_fix</c>, <c>false_alarm</c> — the four
    /// words the store's CHECK constraint accepts. Anything else is normalised to
    /// <c>acknowledged</c> rather than refused: "somebody looked at it" is the weakest true
    /// statement available, and it beats a constraint violation on the way out of a console click.
    /// </summary>
    public string? Resolution { get; set; }
}
