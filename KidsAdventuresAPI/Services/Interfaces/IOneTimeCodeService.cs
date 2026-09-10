using AdventurePacks.Api.DTOs.Auth;

namespace AdventurePacks.Api.Services.Interfaces;

/// <summary>
/// One-time secrets sent down a channel and redeemed once: the throttles, the storage, the
/// hashing and the counting, with no opinion about what redeeming one entitles anybody to.
///
/// It was all private to <see cref="Implementations.PasswordlessAuthService"/> until checkout
/// needed to send four digits to a recipient's handset. That is not a sign-in - proving the
/// number a parcel is going to says nothing about who is holding the account - but it is the
/// same secret, sent the same way, with the same guessing budget, the same replay window and the
/// same reasons for every one of them. The parts that must not diverge live here; what a redeemed
/// code means stays with the caller that asked for it.
/// </summary>
public interface IOneTimeCodeService
{
    /// <summary>
    /// The resend cooldown, the per-destination hourly cap and the per-IP hourly cap, in that
    /// order. Throws <see cref="Infrastructure.TooManyRequestsException"/> with the wait in
    /// seconds, so the caller can put a countdown under its own button.
    /// </summary>
    Task EnforceThrottlesAsync(
        AuthChallengePurpose purpose,
        string destination,
        string? ipAddress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stores a challenge for <paramref name="secret"/> and retires every earlier one for the same
    /// destination, so "resend" means replace rather than "now there are two codes that work".
    /// </summary>
    Task<AuthChallenge> IssueAsync(
        AuthChallengePurpose purpose,
        string destination,
        string secret,
        Guid? userId,
        string? ipAddress,
        TimeSpan lifetime,
        CancellationToken cancellationToken);

    /// <summary>
    /// Checks a code against the newest pending challenge for a destination and consumes it.
    ///
    /// Every failure throws <see cref="UnauthorizedAccessException"/> carrying the sentence a
    /// parent should read: no pending challenge, a wrong code (with the guesses left), or a code
    /// somebody already redeemed. A wrong guess is counted before it throws, which is what makes
    /// the budget a budget.
    /// </summary>
    Task<AuthChallenge> RedeemCodeAsync(
        AuthChallengePurpose purpose,
        string destination,
        string code,
        CancellationToken cancellationToken);

    /// <summary>Whether a secret matches a challenge, compared in fixed time.</summary>
    bool SecretMatches(AuthChallenge challenge, string secret);

    /// <summary>A cryptographically random decimal code, zero-padded to <paramref name="length"/>.</summary>
    string CreateNumericCode(int length);

    /// <summary>
    /// What the caller hands back to its client: how long the secret lasts, when a resend is
    /// allowed, and - only where nothing was really delivered and the configuration permits it -
    /// the secret itself, so the flow is walkable without a gateway.
    /// </summary>
    AuthChallengeResponse BuildResponse(
        string maskedDestination,
        AuthChallenge challenge,
        bool deliveryLive,
        string secret,
        string? url = null);
}
