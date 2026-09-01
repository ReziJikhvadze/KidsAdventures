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

    /// <summary>One book's alarms, reviewed ones included — the order page shows the history.</summary>
    Task<IReadOnlyList<BekiAlarm>> ListForPackAsync(Guid packId, CancellationToken cancellationToken);

    Task<bool> ReviewAsync(
        Guid alarmId, string reviewedBy, string resolution, CancellationToken cancellationToken);

    Task<int> CountOpenAsync(CancellationToken cancellationToken);
}
