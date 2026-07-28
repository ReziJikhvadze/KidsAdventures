namespace AdventurePacks.Api.Domain.Entities;

/// <summary>
/// A pending magic link or SMS code. The secret itself never lands in the
/// database — only a keyed hash of it — so a dump of this table cannot be
/// replayed into account access.
/// </summary>
public sealed class AuthChallenge
{
    public Guid Id { get; set; }
    public AuthChallengePurpose Purpose { get; set; }

    /// <summary>Lower-cased email address, or an E.164 phone number.</summary>
    public string Destination { get; set; } = string.Empty;

    public string SecretHash { get; set; } = string.Empty;

    /// <summary>Null when the destination has no account yet; one is created on verify.</summary>
    public Guid? UserId { get; set; }

    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 5;
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsExpired(DateTime utcNow) => ExpiresAt <= utcNow;
    public bool IsExhausted => AttemptCount >= MaxAttempts;
    public bool IsPending(DateTime utcNow) => ConsumedAt is null && !IsExpired(utcNow) && !IsExhausted;
}
