using System.Security.Cryptography;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Story;

/// <summary>
/// One incident, on its way into the alarms table.
/// </summary>
/// <param name="EvidenceKey">
/// What makes two raisings the same incident — amendment B4's deduplication. The SHA-256 of the
/// evidence blob's name when there is one, or an attempt discriminator (the spread and how many
/// tries it took) when there is not. It is required rather than optional because the alarms that
/// repeat most are precisely the ones raised on every re-evaluation of one book, and a null key
/// would let those be the ones that duplicate.
/// </param>
public sealed record BekiAlarmRaise(
    Guid PackId,
    Guid? OrderId,
    Guid UserId,
    string CheckId,
    string Severity,
    string Detail,
    string? EvidenceBlob,
    string EvidenceKey);

/// <summary>
/// A stored alarm.
/// </summary>
/// <param name="LastSeenUtc">
/// When this incident was last raised. It moves; <see cref="CreatedAtUtc"/> does not. The pair is
/// what tells "this happened once in March" from "this has happened on every evaluation since".
/// </param>
/// <param name="ReviewedBy">
/// Kept even after a reopen. A reviewed alarm that is raised again clears its resolution and its
/// review timestamp — it is open again, and pretending otherwise would hide a recurrence — but the
/// name of whoever looked last stays, because "somebody had already been here" is the useful half of
/// that history.
/// </param>
public sealed record BekiAlarm(
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
    string? Resolution)
{
    public bool IsOpen => ReviewedAtUtc is null;
}

public interface IBekiAlarmService
{
    /// <summary>
    /// Records one waived incident. Never throws: an alarm is a record of something that already
    /// happened, and a book must not fail because the recording of a waiver did.
    /// </summary>
    Task RaiseAsync(BekiAlarmRaise raise, CancellationToken ct);

    Task<IReadOnlyList<BekiAlarm>> ListOpenAsync(int limit, CancellationToken ct);

    /// <summary>
    /// The most recent alarms, reviewed or not — the console's "show the closed ones too".
    ///
    /// A separate method rather than a flag on <see cref="ListOpenAsync"/> because the two lists
    /// answer different questions and only one of them is the work queue. This one exists so that
    /// "has this happened before" can be answered without opening the book it happened to.
    /// </summary>
    Task<IReadOnlyList<BekiAlarm>> ListRecentAsync(int limit, CancellationToken ct);

    Task<IReadOnlyList<BekiAlarm>> ListForPackAsync(Guid packId, CancellationToken ct);

    /// <summary>One alarm by id, reviewed or not. Null when there is no such row.</summary>
    Task<BekiAlarm?> GetAsync(Guid alarmId, CancellationToken ct);

    /// <summary>Marks one alarm reviewed. False when there is no such alarm.</summary>
    Task<bool> ReviewAsync(Guid alarmId, string reviewedBy, string resolution, CancellationToken ct);

    Task<int> CountOpenAsync(CancellationToken ct);
}

/// <summary>
/// The alarms, which are what the owner asked for in place of blocks.
///
/// "Problems become admin alarms to check later, not blocks" is one sentence with two obligations in
/// it. The first is that the book ships, and that is the pipeline's and the gates' business. The
/// second is this one: that nothing is quietly waived. Every policy waiver — a spread the reviewer
/// refused and we shipped anyway, a gate that failed and did not withhold, a book the stale sweep
/// buried — leaves a row here, with the blob a person can open.
///
/// Deduplicated, because the alternative is an alarms list nobody reads. A book that is evaluated at
/// fulfilment, again by an admin opening its page, and twice more by a policy change would raise the
/// same four gate waivers each time; four hundred rows describing eight incidents is the same as no
/// rows at all.
///
/// Blockers page a person as well. Flags stay in the console: they are the normal state of a healthy
/// system under this policy, and an email per flag would train whoever receives it to filter the
/// address.
/// </summary>
public sealed class BekiAlarmService(
    IBekiAlarmRepository repository,
    IAdminNotifier adminNotifier,
    ILogger<BekiAlarmService> logger) : IBekiAlarmService
{
    public async Task RaiseAsync(BekiAlarmRaise raise, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(raise);

        try
        {
            var outcome = await repository.RaiseAsync(raise, ct);

            switch (outcome)
            {
                case BekiAlarmRaiseOutcome.Inserted:
                    logger.LogWarning(
                        "Beki alarm raised for pack {PackId}: {CheckId} ({Severity}) — {Detail}",
                        raise.PackId, raise.CheckId, raise.Severity, raise.Detail);
                    break;

                case BekiAlarmRaiseOutcome.Reopened:
                    logger.LogWarning(
                        "Beki alarm REOPENED for pack {PackId}: {CheckId} ({Severity}) — it had been "
                        + "reviewed and has happened again. {Detail}",
                        raise.PackId, raise.CheckId, raise.Severity, raise.Detail);
                    break;

                default:
                    logger.LogInformation(
                        "Beki alarm for pack {PackId} {CheckId} was raised again; only its "
                        + "last-seen time moved.", raise.PackId, raise.CheckId);
                    break;
            }

            /*
              A blocker also pages somebody, and only on the first sighting or a reopen.

              The condition is the whole design of this line. Paging on every raise would mean a book
              re-evaluated by an admin sends that admin an email about the thing they are looking at;
              paging never would mean a blocker-severity incident lives in a table nobody has a
              reason to open. First sighting and recurrence-after-review are the two moments where
              there is news.
            */
            if (raise.Severity == BekiReleaseSeverity.Blocker
                && outcome is BekiAlarmRaiseOutcome.Inserted or BekiAlarmRaiseOutcome.Reopened)
            {
                await adminNotifier.BookFailedAsync(
                    raise.PackId,
                    $"{raise.CheckId}: {raise.Detail}",
                    CancellationToken.None);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Swallowed on purpose, and loudly. Every caller of this is in the middle of shipping a
            // book: a waiver that could not be recorded is a gap in the record, and a book that
            // failed because of it would be the fault this campaign exists to remove, restored.
            logger.LogError(
                ex, "Beki alarm for pack {PackId} ({CheckId}) could not be recorded. The book is "
                    + "unaffected; the incident is not in the alarms table.",
                raise.PackId, raise.CheckId);
        }
    }

    public async Task<IReadOnlyList<BekiAlarm>> ListOpenAsync(int limit, CancellationToken ct) =>
        await repository.ListOpenAsync(Math.Clamp(limit, 1, 500), ct);

    public async Task<IReadOnlyList<BekiAlarm>> ListRecentAsync(int limit, CancellationToken ct) =>
        await repository.ListRecentAsync(Math.Clamp(limit, 1, 500), ct);

    public async Task<IReadOnlyList<BekiAlarm>> ListForPackAsync(Guid packId, CancellationToken ct) =>
        await repository.ListForPackAsync(packId, ct);

    public Task<BekiAlarm?> GetAsync(Guid alarmId, CancellationToken ct) =>
        repository.GetAsync(alarmId, ct);

    public async Task<bool> ReviewAsync(
        Guid alarmId, string reviewedBy, string resolution, CancellationToken ct)
    {
        var normalized = BekiAlarmResolutions.Normalize(resolution);

        var reviewed = await repository.ReviewAsync(alarmId, reviewedBy, normalized, ct);

        if (reviewed)
        {
            logger.LogInformation(
                "Beki alarm {AlarmId} reviewed by {ReviewedBy}: {Resolution}.",
                alarmId, reviewedBy, normalized);
        }

        return reviewed;
    }

    public Task<int> CountOpenAsync(CancellationToken ct) => repository.CountOpenAsync(ct);
}

/// <summary>
/// The four words an alarm can be closed with — the same four the migration's CHECK constraint
/// names, because a resolution the database refuses is a review that silently did nothing.
/// </summary>
public static class BekiAlarmResolutions
{
    public const string Acknowledged = "acknowledged";

    public const string Fixed = "fixed";

    public const string WontFix = "wont_fix";

    public const string FalseAlarm = "false_alarm";

    public static readonly IReadOnlyList<string> All = [Acknowledged, Fixed, WontFix, FalseAlarm];

    /// <summary>
    /// One of the four, defaulting to <c>acknowledged</c>. A resolution nobody recognises means
    /// "somebody looked at it", which is the weakest true statement available and better than a
    /// constraint violation on the way out of a console click.
    /// </summary>
    public static string Normalize(string? resolution) =>
        All.FirstOrDefault(known =>
            string.Equals(known, resolution?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? Acknowledged;
}

/// <summary>
/// How to name an incident so that two sightings of it are one row.
///
/// The evidence key is a per-book identity, not a global one: the unique index is on
/// <c>(PackId, CheckId, EvidenceKey)</c>, so the same key on two books is two alarms, which is what
/// it should be.
/// </summary>
public static class BekiAlarmEvidence
{
    /// <summary>
    /// The key for an incident with a stored artifact behind it: the hash of the blob's name.
    ///
    /// The NAME rather than the bytes, and that is deliberate. The bytes of a refused spread change
    /// on every attempt — a hash of them would make each retry a new alarm, which is precisely the
    /// duplication the key exists to prevent — while the name is stable for the incident and unique
    /// across books and pages.
    /// </summary>
    public static string ForBlob(string blobName) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(blobName)))
            .ToLowerInvariant()[..32];

    /// <summary>
    /// The key for an incident with no artifact: a short discriminator built from whatever names
    /// the occasion — a page number, a deliverable class, an attempt count.
    /// </summary>
    public static string ForAttempt(params object?[] parts) =>
        string.Join(
            ":",
            parts.Select(part => (part?.ToString() ?? "-").Trim().ToLowerInvariant()))
            is { Length: > 0 } key
            ? key.Length <= 128 ? key : key[..128]
            : "-";
}
