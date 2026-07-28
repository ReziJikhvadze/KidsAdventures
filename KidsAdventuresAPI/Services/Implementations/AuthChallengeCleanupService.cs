using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Implementations;

public sealed class AuthChallengeCleanupService(
    IAuthChallengeRepository challengeRepository,
    ILogger<AuthChallengeCleanupService> logger) : IAuthChallengeCleanupService
{
    /// <summary>
    /// Rows are kept a day past expiry so an abuse investigation still has the trail,
    /// which the throttle counters alone would not give us.
    /// </summary>
    private static readonly TimeSpan RetentionAfterExpiry = TimeSpan.FromDays(1);

    public async Task PurgeExpiredAsync()
    {
        var cutoff = DateTime.UtcNow - RetentionAfterExpiry;
        var deleted = await challengeRepository.DeleteExpiredAsync(cutoff, CancellationToken.None);
        if (deleted > 0)
        {
            logger.LogInformation("Purged {Count} expired auth challenges.", deleted);
        }
    }
}
