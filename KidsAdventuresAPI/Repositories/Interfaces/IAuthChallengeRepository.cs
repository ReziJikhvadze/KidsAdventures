namespace AdventurePacks.Api.Repositories.Interfaces;

public interface IAuthChallengeRepository
{
    Task InsertAsync(AuthChallenge challenge, CancellationToken cancellationToken);

    Task<AuthChallenge?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Newest challenge for a destination regardless of state, used for the resend cooldown.</summary>
    Task<AuthChallenge?> GetLatestAsync(AuthChallengePurpose purpose, string destination, CancellationToken cancellationToken);

    /// <summary>Newest unconsumed, unexpired, non-exhausted challenge — the one a code is checked against.</summary>
    Task<AuthChallenge?> GetLatestPendingAsync(AuthChallengePurpose purpose, string destination, CancellationToken cancellationToken);

    Task<int> CountByDestinationSinceAsync(
        AuthChallengePurpose purpose,
        string destination,
        DateTime since,
        CancellationToken cancellationToken);

    Task<int> CountByIpSinceAsync(string ipAddress, DateTime since, CancellationToken cancellationToken);

    /// <summary>Marks the challenge used. Returns false when another request got there first.</summary>
    Task<bool> TryConsumeAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Increments the attempt counter and returns the new value.</summary>
    Task<int> RecordFailedAttemptAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Retires every outstanding challenge for a destination, so only the newest secret works.</summary>
    Task InvalidatePendingAsync(AuthChallengePurpose purpose, string destination, CancellationToken cancellationToken);

    Task<int> DeleteExpiredAsync(DateTime olderThanUtc, CancellationToken cancellationToken);
}
