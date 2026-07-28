using System.Security.Cryptography;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.DTOs.Auth;
using AdventurePacks.Api.Infrastructure;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Implementations;

/// <summary>
/// Sign-in without a password: an emailed one-time link, or a six-digit code sent to a
/// Georgian mobile.
///
/// Two properties drive the whole design. First, the secret is never stored — only an
/// HMAC of it, keyed with a server-side secret and salted by the challenge id, so a
/// database dump cannot be brute-forced back into a six-digit code. Second, requesting a
/// challenge tells the caller nothing about whether the account exists; the account is
/// created on successful verification, which removes the enumeration oracle that a
/// "no such user" response would hand out.
/// </summary>
public sealed class PasswordlessAuthService(
    IAuthChallengeRepository challengeRepository,
    IUserRepository userRepository,
    IAuthSessionFactory sessionFactory,
    IWelcomeGiftService welcomeGiftService,
    IEmailService emailService,
    ISmsSender smsSender,
    IOptions<PasswordlessAuthOptions> passwordlessOptions,
    IOptions<EmailOptions> emailOptions,
    IOptions<JwtOptions> jwtOptions,
    ILogger<PasswordlessAuthService> logger) : IPasswordlessAuthService
{
    private readonly PasswordlessAuthOptions _options = passwordlessOptions.Value;
    private readonly EmailOptions _email = emailOptions.Value;

    private readonly byte[] _signingKey = Encoding.UTF8.GetBytes(
        string.IsNullOrWhiteSpace(passwordlessOptions.Value.SigningKey)
            ? jwtOptions.Value.SecretKey
            : passwordlessOptions.Value.SigningKey!);

    /// <summary>
    /// An SMTP password is what separates "we sent it" from "we logged it". Without one the
    /// magic link would silently go nowhere, so the flow reports itself as non-live and the
    /// dev echo takes over.
    /// </summary>
    private bool EmailDeliveryLive => _email.Enabled && !string.IsNullOrWhiteSpace(_email.SmtpPassword);

    public PasswordlessConfigResponse GetConfig() => new()
    {
        MagicLinkEnabled = _options.Enabled,
        PhoneEnabled = _options.Enabled,
        SmsDeliveryLive = smsSender.IsLive,
        MagicLinkDeliveryLive = EmailDeliveryLive,
        OtpLength = _options.OtpLength,
        ResendCooldownSeconds = _options.ResendCooldownSeconds
    };

    public async Task<AuthChallengeResponse> RequestMagicLinkAsync(
        MagicLinkRequest request,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();

        var email = NormalizeEmail(request.Email);
        await EnforceThrottlesAsync(AuthChallengePurpose.MagicLink, email, ipAddress, cancellationToken);

        var existing = await userRepository.GetByEmailAsync(email, cancellationToken);
        var secret = CreateUrlSafeSecret();
        var challenge = await IssueChallengeAsync(
            AuthChallengePurpose.MagicLink,
            email,
            secret,
            existing?.Id,
            ipAddress,
            TimeSpan.FromMinutes(_options.MagicLinkLifetimeMinutes),
            cancellationToken);

        var token = $"{challenge.Id:N}.{secret}";
        var url = BuildMagicLinkUrl(token, request.ReturnPath);

        if (EmailDeliveryLive)
        {
            await emailService.SendMagicLinkAsync(email, url, _options.MagicLinkLifetimeMinutes, cancellationToken);
        }
        else
        {
            logger.LogInformation("[dev-mail] magic link for {Email}: {Url}", MaskEmail(email), url);
        }

        logger.LogInformation("Magic link issued for {Email} (challenge {ChallengeId}).", MaskEmail(email), challenge.Id);

        return BuildChallengeResponse(MaskEmail(email), challenge, deliveryLive: EmailDeliveryLive, secret: token);
    }

    public async Task<AuthResponse> VerifyMagicLinkAsync(
        VerifyMagicLinkRequest request,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();

        var (challengeId, secret) = ParseMagicLinkToken(request.Token);
        var challenge = await challengeRepository.GetByIdAsync(challengeId, cancellationToken);

        if (challenge is null || challenge.Purpose != AuthChallengePurpose.MagicLink)
        {
            throw new UnauthorizedAccessException(LinkInvalidMessage);
        }

        if (!challenge.IsPending(DateTime.UtcNow))
        {
            throw new UnauthorizedAccessException(LinkInvalidMessage);
        }

        if (!SecretMatches(challenge, secret))
        {
            await challengeRepository.RecordFailedAttemptAsync(challenge.Id, cancellationToken);
            throw new UnauthorizedAccessException(LinkInvalidMessage);
        }

        if (!await challengeRepository.TryConsumeAsync(challenge.Id, cancellationToken))
        {
            throw new UnauthorizedAccessException(LinkInvalidMessage);
        }

        var user = await ResolveOrCreateByEmailAsync(
            challenge.Destination,
            new WelcomeGiftContext
            {
                GuestPreviewId = request.GuestPreviewId,
                StoryId = request.StoryId
            },
            cancellationToken);

        return await sessionFactory.CreateAsync(user, cancellationToken);
    }

    public async Task<AuthChallengeResponse> RequestPhoneCodeAsync(
        PhoneCodeRequest request,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();

        var phone = GeorgianPhoneNumber.NormalizeOrThrow(request.PhoneNumber);
        await EnforceThrottlesAsync(AuthChallengePurpose.PhoneOtp, phone, ipAddress, cancellationToken);

        var existing = await userRepository.GetByPhoneNumberAsync(phone, cancellationToken);
        var code = CreateNumericCode(_options.OtpLength);
        var challenge = await IssueChallengeAsync(
            AuthChallengePurpose.PhoneOtp,
            phone,
            code,
            existing?.Id,
            ipAddress,
            TimeSpan.FromMinutes(_options.OtpLifetimeMinutes),
            cancellationToken);

        var message = $"Adventrya: თქვენი კოდია {code}. მოქმედებს {_options.OtpLifetimeMinutes} წუთი.";
        await smsSender.SendAsync(phone, message, cancellationToken);

        logger.LogInformation(
            "Phone code issued for {Phone} via {Provider} (challenge {ChallengeId}).",
            GeorgianPhoneNumber.Mask(phone), smsSender.ProviderName, challenge.Id);

        return BuildChallengeResponse(
            GeorgianPhoneNumber.Mask(phone),
            challenge,
            deliveryLive: smsSender.IsLive,
            secret: code);
    }

    public async Task<AuthResponse> VerifyPhoneCodeAsync(
        VerifyPhoneCodeRequest request,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();

        var phone = GeorgianPhoneNumber.NormalizeOrThrow(request.PhoneNumber);
        var code = new string(request.Code.Where(char.IsAsciiDigit).ToArray());

        var challenge = await challengeRepository.GetLatestPendingAsync(
                            AuthChallengePurpose.PhoneOtp, phone, cancellationToken)
                        ?? throw new UnauthorizedAccessException(
                            "კოდს ვადა გაუვიდა. მოითხოვეთ ახალი კოდი.");

        if (!SecretMatches(challenge, code))
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

        var user = await ResolveOrCreateByPhoneAsync(
            phone,
            new WelcomeGiftContext
            {
                GuestPreviewId = request.GuestPreviewId,
                StoryId = request.StoryId
            },
            cancellationToken);

        return await sessionFactory.CreateAsync(user, cancellationToken);
    }

    // -- challenge plumbing -------------------------------------------------

    private const string LinkInvalidMessage = "ბმული არასწორია ან ვადაგასულია. მოითხოვეთ ახალი ბმული.";

    private async Task<AuthChallenge> IssueChallengeAsync(
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

    private async Task EnforceThrottlesAsync(
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
                logger.LogWarning("Passwordless request throttled for IP {Ip} ({Count} in the last hour).", ipAddress, perIp);
                throw new TooManyRequestsException(
                    "ძალიან ბევრი მოთხოვნაა. სცადეთ ერთ საათში.", 3600);
            }
        }
    }

    private AuthChallengeResponse BuildChallengeResponse(
        string maskedDestination,
        AuthChallenge challenge,
        bool deliveryLive,
        string secret)
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
            DevSecret = exposeSecret ? secret : null
        };
    }

    // -- account resolution -------------------------------------------------

    private async Task<User> ResolveOrCreateByEmailAsync(
        string email,
        WelcomeGiftContext giftContext,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByEmailAsync(email, cancellationToken);
        if (user is not null)
        {
            if (!user.EmailConfirmed)
            {
                // Clicking a link sent to this address is proof of ownership, which is
                // exactly what the confirmation email was asking for.
                await userRepository.ConfirmEmailAsync(user.Id, cancellationToken);
                user.EmailConfirmed = true;
            }

            return user;
        }

        var userId = Guid.NewGuid();
        var created = new User
        {
            Id = userId,
            Email = email,
            PasswordHash = null,
            PreferredLanguage = "ka",
            DisplayName = DisplayNameFromEmail(email),
            EmailConfirmed = true,
            SubscriptionType = SubscriptionType.Free,
            WelcomeStoryRemaining = await welcomeGiftService.GetWelcomeStoryRemainingAsync(
                giftContext, userId, cancellationToken),
            CreatedAt = DateTime.UtcNow
        };

        await userRepository.CreateAsync(created, cancellationToken);
        logger.LogInformation("Created account {UserId} from a magic link.", userId);
        return created;
    }

    private async Task<User> ResolveOrCreateByPhoneAsync(
        string phone,
        WelcomeGiftContext giftContext,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByPhoneNumberAsync(phone, cancellationToken);
        if (user is not null)
        {
            return user;
        }

        var userId = Guid.NewGuid();
        var created = new User
        {
            Id = userId,
            Email = string.Empty,
            PasswordHash = null,
            PhoneNumber = phone,
            PhoneConfirmed = true,
            PreferredLanguage = "ka",
            EmailConfirmed = false,
            SubscriptionType = SubscriptionType.Free,
            WelcomeStoryRemaining = await welcomeGiftService.GetWelcomeStoryRemainingAsync(
                giftContext, userId, cancellationToken),
            CreatedAt = DateTime.UtcNow
        };

        await userRepository.CreateAsync(created, cancellationToken);
        logger.LogInformation("Created account {UserId} from a phone code.", userId);
        return created;
    }

    // -- secrets ------------------------------------------------------------

    private string ComputeSecretHash(Guid challengeId, AuthChallengePurpose purpose, string destination, string secret)
    {
        // The challenge id is part of the input for two reasons: it salts a six-digit code
        // so identical codes never produce identical hashes, and it keeps the unique index
        // on SecretHash from rejecting a legitimate second challenge.
        var payload = Encoding.UTF8.GetBytes($"{challengeId:N}|{purpose}|{destination}|{secret}");
        return Convert.ToBase64String(HMACSHA256.HashData(_signingKey, payload));
    }

    private bool SecretMatches(AuthChallenge challenge, string secret)
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

    private static string CreateUrlSafeSecret() =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string CreateNumericCode(int length)
    {
        var digits = Math.Clamp(length, 4, 9);
        var exclusiveMax = (int)Math.Pow(10, digits);
        // GetInt32 is rejection-sampled, so there is no modulo bias to worry about.
        return RandomNumberGenerator.GetInt32(0, exclusiveMax).ToString(new string('0', digits));
    }

    private static (Guid ChallengeId, string Secret) ParseMagicLinkToken(string token)
    {
        var separator = token.IndexOf('.');
        if (separator <= 0 || separator == token.Length - 1)
        {
            throw new UnauthorizedAccessException(LinkInvalidMessage);
        }

        if (!Guid.TryParseExact(token[..separator], "N", out var challengeId))
        {
            throw new UnauthorizedAccessException(LinkInvalidMessage);
        }

        return (challengeId, token[(separator + 1)..]);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // -- helpers ------------------------------------------------------------

    private void EnsureEnabled()
    {
        if (!_options.Enabled)
        {
            throw new InvalidOperationException("პაროლის გარეშე შესვლა ამჟამად მიუწვდომელია.");
        }
    }

    private string BuildMagicLinkUrl(string token, string? returnPath)
    {
        var baseUrl = _email.BaseUrl.TrimEnd('/');
        var path = _options.MagicLinkPath.StartsWith('/') ? _options.MagicLinkPath : "/" + _options.MagicLinkPath;
        var url = $"{baseUrl}{path}?token={Uri.EscapeDataString(token)}";

        var safeReturn = SanitizeReturnPath(returnPath);
        return safeReturn is null ? url : $"{url}&next={Uri.EscapeDataString(safeReturn)}";
    }

    /// <summary>
    /// Only same-origin relative paths survive. "//evil.example" is a protocol-relative URL,
    /// so rejecting a leading double slash is what stops the email from becoming an open redirect.
    /// </summary>
    private static string? SanitizeReturnPath(string? returnPath)
    {
        if (string.IsNullOrWhiteSpace(returnPath))
        {
            return null;
        }

        var trimmed = returnPath.Trim();
        if (!trimmed.StartsWith('/') || trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return null;
        }

        return trimmed.Any(char.IsControl) ? null : trimmed;
    }

    private static string NormalizeEmail(string email)
    {
        var normalized = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0 || !normalized.Contains('@'))
        {
            throw new InvalidOperationException("ელფოსტის მისამართი არასწორია.");
        }

        return normalized;
    }

    private static string DisplayNameFromEmail(string email)
    {
        var at = email.IndexOf('@');
        return at > 0 ? email[..at] : email;
    }

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0)
        {
            return "***";
        }

        var local = email[..at];
        var visible = local.Length <= 2 ? local[..1] : local[..2];
        return $"{visible}{new string('*', Math.Max(1, local.Length - visible.Length))}{email[at..]}";
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];
}
