using AdventurePacks.Api.DTOs.Admin;

namespace AdventurePacks.Api.Repositories.Interfaces;

/// <summary>
/// The overview's counts, in one round trip.
///
/// Its own repository rather than more methods on <see cref="IAdminReportingRepository"/>, because
/// this one reads nothing per-row: it returns eleven numbers over four tables, and the thing that
/// makes it correct is that they are all taken at the same instant against the same boundaries.
/// Split across four calls it would be a panel whose figures quietly disagree with each other.
///
/// Like every admin read, it deliberately omits the per-user ownership predicate, so callers must
/// be behind <see cref="AuthorizationPolicies.Admin"/>.
/// </summary>
public interface IAdminOverviewRepository
{
    /// <summary>
    /// The counts, with the three boundaries the caller decides.
    /// </summary>
    /// <param name="dayStartUtc">Midnight UTC. "Today" on the console's own label.</param>
    /// <param name="monthStartUtc">The first of the month, UTC.</param>
    /// <param name="staleCutoffUtc">
    /// The moment before which a still-generating book counts as stuck — the stale sweep's own
    /// silence limit, passed in rather than recomputed here so the tile and the sweep agree about
    /// what "silent for too long" means.
    /// </param>
    Task<AdminOverviewCounts> GetCountsAsync(
        DateTime dayStartUtc,
        DateTime monthStartUtc,
        DateTime staleCutoffUtc,
        CancellationToken cancellationToken);
}
