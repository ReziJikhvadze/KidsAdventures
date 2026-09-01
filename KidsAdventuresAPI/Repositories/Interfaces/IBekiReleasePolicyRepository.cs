using AdventurePacks.Api.Services.Story;

namespace AdventurePacks.Api.Repositories.Interfaces;

/// <summary>
/// The policy table, as two reads and a write.
///
/// Deliberately small, and deliberately its own interface rather than a corner of a bigger one: the
/// only caller is <see cref="IBekiReleasePolicyService"/>, whose whole job is to put a snapshot and
/// a cache in front of these three calls. A test double for the service needs none of this; a test
/// double for this needs none of the service.
/// </summary>
public interface IBekiReleasePolicyRepository
{
    /// <summary>
    /// Every stored row. Rows that have never been set are absent rather than defaulted — the code
    /// defaults are <see cref="BekiReleasePolicySnapshot"/>'s to apply, so that "no row" stays
    /// distinguishable from "a row that happens to say what the default says".
    /// </summary>
    Task<IReadOnlyList<BekiReleaseCheckSetting>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Sets one (check, class) pair, inserting the row when there is not one. Records who, because a
    /// severity nobody can be asked about is a decision with no owner.
    /// </summary>
    Task SetAsync(
        string checkId,
        string deliverableClass,
        string severity,
        string updatedBy,
        CancellationToken cancellationToken);
}
