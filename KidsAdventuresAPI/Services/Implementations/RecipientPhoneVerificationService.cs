using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.DTOs.Auth;
using AdventurePacks.Api.DTOs.Orders;
using AdventurePacks.Api.Infrastructure;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Implementations;

/// <inheritdoc />
public sealed class RecipientPhoneVerificationService(
    IOneTimeCodeService oneTimeCodes,
    IVerifiedRecipientPhoneRepository verifiedPhoneRepository,
    IUserRepository userRepository,
    ISmsSender smsSender,
    IOptions<PasswordlessAuthOptions> passwordlessOptions,
    ILogger<RecipientPhoneVerificationService> logger) : IRecipientPhoneVerificationService
{
    private readonly PasswordlessAuthOptions _options = passwordlessOptions.Value;

    public async Task<RecipientPhoneStatusResponse> GetStatusAsync(
        Guid userId, string phoneNumber, CancellationToken cancellationToken)
    {
        var phone = GeorgianPhoneNumber.NormalizeOrThrow(phoneNumber);
        return Status(phone, await IsVerifiedAsync(userId, phone, cancellationToken));
    }

    public async Task<AuthChallengeResponse> RequestCodeAsync(
        Guid userId, string phoneNumber, string? ipAddress, CancellationToken cancellationToken)
    {
        var phone = GeorgianPhoneNumber.NormalizeOrThrow(phoneNumber);

        // Nothing is sent to a number that is already proved. Not a saving so much as a
        // correctness point: a recipient who gets a code for a book they were told about last
        // week has no idea what it is for, and the panel would be asking a question it already
        // has the answer to.
        if (await IsVerifiedAsync(userId, phone, cancellationToken))
        {
            throw new InvalidOperationException("ეს ნომერი უკვე დადასტურებულია.");
        }

        await oneTimeCodes.EnforceThrottlesAsync(
            AuthChallengePurpose.RecipientPhone, Destination(userId, phone), ipAddress, cancellationToken);

        var code = oneTimeCodes.CreateNumericCode(_options.OtpLength);
        var challenge = await oneTimeCodes.IssueAsync(
            AuthChallengePurpose.RecipientPhone,
            Destination(userId, phone),
            code,
            userId,
            ipAddress,
            TimeSpan.FromMinutes(_options.OtpLifetimeMinutes),
            cancellationToken);

        // One line, and one that says what it is for. Georgian is UCS-2 to a gateway, so an SMS
        // turns into a second charged segment after 70 characters; this is about 35. Naming the
        // delivery matters more than the length here: the handset receiving it may not be the
        // parent's, and "code" alone reads as somebody trying to get into an account.
        var message = $"მიწოდების დადასტურების კოდი : {code}";
        await smsSender.SendAsync(phone, message, cancellationToken);

        logger.LogInformation(
            "Recipient phone code issued for {Phone} via {Provider} (challenge {ChallengeId}).",
            GeorgianPhoneNumber.Mask(phone), smsSender.ProviderName, challenge.Id);

        return oneTimeCodes.BuildResponse(
            GeorgianPhoneNumber.Mask(phone),
            challenge,
            deliveryLive: smsSender.IsLive,
            secret: code);
    }

    public async Task<RecipientPhoneStatusResponse> VerifyAsync(
        Guid userId, string phoneNumber, string code, CancellationToken cancellationToken)
    {
        var phone = GeorgianPhoneNumber.NormalizeOrThrow(phoneNumber);

        if (await IsVerifiedAsync(userId, phone, cancellationToken))
        {
            return Status(phone, verified: true);
        }

        await oneTimeCodes.RedeemCodeAsync(
            AuthChallengePurpose.RecipientPhone, Destination(userId, phone), code, cancellationToken);

        await verifiedPhoneRepository.MarkVerifiedAsync(userId, phone, cancellationToken);

        logger.LogInformation(
            "Recipient phone {Phone} verified by {UserId}.", GeorgianPhoneNumber.Mask(phone), userId);

        return Status(phone, verified: true);
    }

    public async Task EnsureVerifiedAsync(Guid userId, string? phoneNumber, CancellationToken cancellationToken)
    {
        var phone = GeorgianPhoneNumber.NormalizeOrThrow(phoneNumber);

        if (!await IsVerifiedAsync(userId, phone, cancellationToken))
        {
            throw new InvalidOperationException(
                "მიმღების ნომერი დადასტურებული არ არის. გამოგზავნეთ კოდი და შეიყვანეთ იგი.");
        }
    }

    /// <summary>
    /// The number a code is issued against is scoped to the parent asking for it.
    ///
    /// The challenge table is keyed by destination, and a bare phone number would mean two
    /// parents posting to the same grandmother in the same minute cancel each other's codes -
    /// issuing retires the pending ones for that destination, which is right for a resend and
    /// wrong for a stranger. Scoping also puts the resend cooldown and the hourly cap where they
    /// belong: on this parent's attempts at this number rather than on the number itself, which
    /// anybody could otherwise lock by asking about it six times.
    /// </summary>
    private static string Destination(Guid userId, string phone) => $"{userId:N}:{phone}";

    private async Task<bool> IsVerifiedAsync(Guid userId, string phone, CancellationToken cancellationToken)
    {
        if (await verifiedPhoneRepository.IsVerifiedAsync(userId, phone, cancellationToken))
        {
            return true;
        }

        // Their own number, already confirmed by a sign-in code, counts as proved. Asking a
        // parent to read back four digits on the handset they signed in with minutes ago is the
        // shop failing to remember something it watched happen.
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);

        return user is { PhoneConfirmed: true }
               && GeorgianPhoneNumber.TryNormalize(user.PhoneNumber, out var own)
               && string.Equals(own, phone, StringComparison.Ordinal);
    }

    private RecipientPhoneStatusResponse Status(string phone, bool verified) => new()
    {
        PhoneNumber = GeorgianPhoneNumber.Mask(phone),
        Verified = verified,
        OtpLength = _options.OtpLength,
        ResendCooldownSeconds = _options.ResendCooldownSeconds
    };
}
