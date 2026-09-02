using AdventurePacks.Api.Services.Story;

namespace AdventurePacks.Api.Repositories.Interfaces;

/// <summary>What one raising actually did to the table — the log line's whole content.</summary>
public enum BekiAlarmRaiseOutcome
{
    /// <summary>A new incident.</summary>
    Inserted,

    /// <summary>An open alarm that has happened again; only its last-seen time moved.</summary>
    Touched,

    /// <summary>An alarm somebody had reviewed, happening again. Its resolution is cleared.</summary>
    Reopened,
}

public interface IBekiAlarmRepository
{
    /// <summary>
    /// Records one incident, deduplicated on <c>(PackId, CheckId, EvidenceKey)</c> — amendment B4.
    /// </summary>
    Task<BekiAlarmRaiseOutcome> RaiseAsync(BekiAlarmRaise raise, CancellationToken cancellationToken);

    /// <summary>The open alarms, most recently seen first.</summary>
    Task<IReadOnlyList<BekiAlarm>> ListOpenAsync(int limit, CancellationToken cancellationToken);

    /// <summary>
    /// The most recent alarms, reviewed or not.
    ///
    /// The console's "show the reviewed ones too" toggle, and it exists because "has this happened
    /// before" is a question the open list cannot answer: an incident somebody closed last week is
    /// exactly the row that makes this week's identical one worth escalating. Capped like the open
    /// list — a table that has been running for a year is not a page.
    /// </summary>
    Task<IReadOnlyList<BekiAlarm>> ListRecentAsync(int limit, CancellationToken cancellationToken);

    /// <summary>
    /// One alarm by id, reviewed or not. Null when there is no such row.
    ///
    /// What the evidence route reads before it streams anything: the blob name lives on the row,
    /// and an id nobody recognises has to be a 404 rather than a guess at a storage key.
    /// </summary>
    Task<BekiAlarm?> GetAsync(Guid alarmId, CancellationToken cancellationToken);

    /// <summary>One book's alarms, reviewed ones included — the order page shows the history.</summary>
    Task<IReadOnlyList<BekiAlarm>> ListForPackAsync(Guid packId, CancellationToken cancellationToken);

    Task<bool> ReviewAsync(
        Guid alarmId, string reviewedBy, string resolution, CancellationToken cancellationToken);

    Task<int> CountOpenAsync(CancellationToken cancellationToken);
}
