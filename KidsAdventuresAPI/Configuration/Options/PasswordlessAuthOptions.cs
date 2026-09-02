namespace AdventurePacks.Api.Configuration.Options;

public sealed class PasswordlessAuthOptions
{
    public const string SectionName = "PasswordlessAuth";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Four digits, which is what the SMS says and what the panel lays itself out for — it reads
    /// this number back off the config endpoint rather than hard-coding a box count.
    ///
    /// Only the phone flow uses this; e-mail sign-in is a link or a password, never a code.
    /// </summary>
    public int OtpLength { get; set; } = 4;

    public int OtpLifetimeMinutes { get; set; } = 10;

    /// <summary>Longer than the OTP: mail delivery and a distracted parent both take time.</summary>
    public int MagicLinkLifetimeMinutes { get; set; } = 20;

    /// <summary>Wrong-code guesses allowed before the challenge is burned.</summary>
    public int MaxVerifyAttempts { get; set; } = 5;

    /// <summary>Matches the countdown the sign-in panel shows under "resend".</summary>
    public int ResendCooldownSeconds { get; set; } = 45;

    public int MaxRequestsPerDestinationPerHour { get; set; } = 6;

    public int MaxRequestsPerIpPerHour { get; set; } = 30;

    /// <summary>
    /// Returns the code or link token in the API response so the flow is testable without a
    /// gateway or a mailbox. Ignored whenever a live SMS sender is configured, and it must
    /// stay false in production.
    /// </summary>
    public bool ExposeSecretsInResponse { get; set; }

    /// <summary>
    /// Key for the HMAC that protects stored secrets. Falls back to <c>Jwt:SecretKey</c>, which
    /// is already required to be present and secret; set this separately to rotate the two
    /// independently.
    /// </summary>
    public string? SigningKey { get; set; }

    /// <summary>Frontend route that trades a magic-link token for a session.</summary>
    public string MagicLinkPath { get; set; } = "/auth/magic";
}
