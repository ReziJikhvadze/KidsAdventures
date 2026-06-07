using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.DTOs.Auth;
using AdventurePacks.Api.Infrastructure;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Implementations;

public sealed class AuthService(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwtTokenService,
    ISubscriptionService subscriptionService,
    IEmailService emailService,
    IOptions<EmailOptions> emailOptions,
    ILogger<AuthService> logger) : IAuthService
{
    private readonly EmailOptions _emailOptions = emailOptions.Value;

    public async Task<RegisterResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var existing = await userRepository.GetByEmailAsync(email, cancellationToken);
        if (existing is not null)
        {
            throw new InvalidOperationException("Email is already registered.");
        }

        PasswordValidator.ValidateOrThrow(request.Password);

        var confirmationToken = Guid.NewGuid().ToString("N");
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = passwordHasher.Hash(request.Password),
            SubscriptionType = SubscriptionType.Free,
            WelcomeStoryRemaining = 1,
            EmailConfirmed = false,
            EmailConfirmationToken = confirmationToken,
            EmailConfirmationExpiresAt = DateTime.UtcNow.AddDays(2),
            CreatedAt = DateTime.UtcNow
        };

        await userRepository.CreateAsync(user, cancellationToken);

        var confirmUrl =
            $"{_emailOptions.BaseUrl.TrimEnd('/')}/confirm-email?token={Uri.EscapeDataString(confirmationToken)}";

        try
        {
            await emailService.SendAccountActivationAsync(email, confirmUrl, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Account created for {Email} but confirmation email failed. Confirm URL: {ConfirmUrl}", email, confirmUrl);
            return new RegisterResponse
            {
                Email = email,
                Message = "Account created, but we could not send the confirmation email. Ask the site admin to fix Email:SmtpPassword (Gmail App Password required)."
            };
        }

        return new RegisterResponse
        {
            Email = email,
            Message = "We sent a confirmation link to your email. Please confirm your account before signing in."
        };
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await userRepository.GetByEmailAsync(email, cancellationToken)
                   ?? throw new UnauthorizedAccessException("Invalid credentials.");

        if (!passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedAccessException("Invalid credentials.");
        }

        if (!user.EmailConfirmed)
        {
            throw new UnauthorizedAccessException("Please confirm your email before signing in. Check your inbox for the activation link.");
        }

        var response = jwtTokenService.CreateToken(user);
        var balance = await subscriptionService.GetAccountBalanceAsync(user.Id, cancellationToken);
        response.BookCredits = balance.BookCredits;
        response.StoriesUsedThisMonth = balance.StoriesUsedThisMonth;
        response.StoriesAllowedThisMonth = balance.StoriesAllowedThisMonth;
        response.StoriesRemainingThisMonth = balance.StoriesRemainingThisMonth;
        response.WelcomeStoryRemaining = balance.WelcomeStoryRemaining;
        return response;
    }

    public async Task<bool> ConfirmEmailAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var user = await userRepository.GetByConfirmationTokenAsync(token.Trim(), cancellationToken);
        if (user is null)
        {
            return false;
        }

        if (user.EmailConfirmed)
        {
            return true;
        }

        if (user.EmailConfirmationExpiresAt is { } expires && expires < DateTime.UtcNow)
        {
            return false;
        }

        return await userRepository.ConfirmEmailAsync(user.Id, cancellationToken);
    }
}
