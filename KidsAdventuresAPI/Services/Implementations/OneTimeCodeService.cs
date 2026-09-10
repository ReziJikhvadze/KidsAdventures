using System.Security.Cryptography;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.DTOs.Auth;
using AdventurePacks.Api.Infrastructure;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Implementations;

/// <inheritdoc />
public sealed class OneTimeCodeService(
    IAuthChallengeRepository challengeRepository,
    IOptions<PasswordlessAuthOptions> passwordlessOptions,
    IOptions<JwtOptions> jwtOptions,
    ILogger<OneTimeCodeService> logger) : IOneTimeCodeService
{
    private readonly PasswordlessAuthOptions _options = passwordlessOptions.Value;

    private readonly byte[] _signingKey = Encoding.UTF8.GetBytes(
        string.IsNullOrWhiteSpace(passwordlessOptions.Value.SigningKey)
            ? jwtOptions.Value.SecretKey
            : passwordlessOptions.Value.SigningKey!);

    public async Task EnforceThrottlesAsync(
        AuthChallengePurpose purpose,
        string destination,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var latest = await challengeRepository.GetLatestAsync(purpose, destination, cancellationToken);
        if (latest is not null)
        {
            var readyAt = latest.CreatedAt.AddSeconds(_options.ResendCooldownSeconds);
            if (readyAt > now)
            {
                var wait = (int)Math.Ceiling((readyAt - now).TotalSeconds);
                throw new TooManyRequestsException(
                    $"ხელახლა გაგზავნა შესაძლებელია {wait} წამში.", wait);
            }
        }

        var hourAgo = now.AddHours(-1);

        var perDestination = await challengeRepository.CountByDestinationSinceAsync(
            purpose, destination, hourAgo, cancellationToken);
        if (perDestination >= _options.MaxRequestsPerDestinationPerHour)
        {
            throw new TooManyRequestsException(
                "ძალიან ბევრი მოთხოვნაა. სცადეთ ერთ საათში.", 3600);
        }

        if (!string.IsNullOrWhiteSpace(ipAddress))
        {
            var perIp = await challengeRepository.CountByIpSinceAsync(ipAddress, hourAgo, cancellationToken);
            if (perIp >= _options.MaxRequestsPerIpPerHour)
            {
                logger.LogWarning("One-time code request throttled for IP {Ip} ({Count} in the last hour).", ipAddress, perIp);
                throw new TooManyRequestsException(
                    "ძალიან ბევრი მოთხოვნაა. სცადეთ ერთ საათში.", 3600);
            }
        }
    }

    public async Task<AuthChallenge> IssueAsync(
        AuthChallengePurpose purpose,
        string destination,
        string secret,
        Guid? userId,
        string? ipAddress,
        TimeSpan lifetime,
        CancellationToken cancellationToken)
    {
        // Retiring the previous challenges is what makes "resend" mean *replace*: an
        // older code left alive doubles the guessing surface for no benefit.
        await challengeRepository.InvalidatePendingAsync(purpose, destination, cancellationToken);

        var now = DateTime.UtcNow;
        var challenge = new AuthChallenge
        {
            Id = Guid.NewGuid(),
            Purpose = purpose,
            Destination = destination,
            UserId = userId,
            AttemptCount = 0,
            MaxAttempts = _options.MaxVerifyAttempts,
            ExpiresAt = now.Add(lifetime),
            IpAddress = Truncate(ipAddress, 64),
            CreatedAt = now
        };
        challenge.SecretHash = ComputeSecretHash(challenge.Id, purpose, destination, secret);

        await challengeRepository.InsertAsync(challenge, cancellationToken);
        return challenge;
    }

    public async Task<AuthChallenge> RedeemCodeAsync(
        AuthChallengePurpose purpose,
        string destination,
        string code,
        CancellationToken cancellationToken)
    {
        var digits = new string((code ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

        var challenge = await challengeRepository.GetLatestPendingAsync(purpose, destination, cancellationToken)
                        ?? throw new UnauthorizedAccessException(
                            "კოდს ვადა გაუვიდა. მოითხოვეთ ახალი კოდი.");

        if (!SecretMatches(challenge, digits))
        {
            var attempts = await challengeRepository.RecordFailedAttemptAsync(challenge.Id, cancellationToken);
            var remaining = Math.Max(0, challenge.MaxAttempts - attempts);
            throw new UnauthorizedAccessException(remaining > 0
                ? $"კოდი არასწორია. დარჩა {remaining} მცდელობა."
                : "კოდი არასწორია და მცდელობები ამოიწურა. მოითხოვეთ ახალი კოდი.");
        }

        if (!await challengeRepository.TryConsumeAsync(challenge.Id, cancellationToken))
        {
            throw new UnauthorizedAccessException("ეს კოდი უკვე გამოყენებულია. მოითხოვეთ ახალი კოდი.");
        }

        return challenge;
    }

    public bool SecretMatches(AuthChallenge challenge, string secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return false;
        }

        var candidate = ComputeSecretHash(challenge.Id, challenge.Purpose, challenge.Destination, secret);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(candidate),
            Encoding.UTF8.GetBytes(challenge.SecretHash));
    }

    public string CreateNumericCode(int length)
    {
        var digits = Math.Clamp(length, 4, 9);
        var exclusiveMax = (int)Math.Pow(10, digits);
        // GetInt32 is rejection-sampled, so there is no modulo bias to worry about.
        return RandomNumberGenerator.GetInt32(0, exclusiveMax).ToString(new string('0', digits));
    }

    public AuthChallengeResponse BuildResponse(
        string maskedDestination,
        AuthChallenge challenge,
        bool deliveryLive,
        string secret,
        string? url = null)
    {
        var expiresIn = (int)Math.Max(1, (challenge.ExpiresAt - DateTime.UtcNow).TotalSeconds);

        // The echo is a development affordance only, and a live sender always wins the
        // argument: if the message really went out, there is no reason to leak it back.
        var exposeSecret = _options.ExposeSecretsInResponse && !deliveryLive;

        return new AuthChallengeResponse
        {
            Destination = maskedDestination,
            ExpiresInSeconds = expiresIn,
            ResendAfterSeconds = _options.ResendCooldownSeconds,
            DeliveryLive = deliveryLive,
            OtpLength = _options.OtpLength,
            DevSecret = exposeSecret ? secret : null,
            DevUrl = exposeSecret ? url : null
        };
    }

    /// <summary>
    /// The challenge id is part of the input for two reasons: it salts a four-digit code so
    /// identical codes never produce identical hashes, and it keeps the unique index on
    /// SecretHash from rejecting a legitimate second challenge.
    /// </summary>
    private string ComputeSecretHash(Guid challengeId, AuthChallengePurpose purpose, string destination, string secret)
    {
        var payload = Encoding.UTF8.GetBytes($"{challengeId:N}|{purpose}|{destination}|{secret}");
        return Convert.ToBase64String(HMACSHA256.HashData(_signingKey, payload));
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];
}
