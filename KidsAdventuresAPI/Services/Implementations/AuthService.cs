using AdventurePacks.Api.Domain;
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
    IGoogleAuthService googleAuthService,
    IRecaptchaVerifier recaptchaVerifier,
    IWelcomeGiftService welcomeGiftService) : IAuthService
{
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        if (!await recaptchaVerifier.VerifyAsync(request.RecaptchaToken, cancellationToken))
        {
            throw new InvalidOperationException("reCAPTCHA verification failed. Please try again.");
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var existing = await userRepository.GetByEmailAsync(email, cancellationToken);
        if (existing is not null)
        {
            throw new InvalidOperationException("Email is already registered.");
        }

        PasswordValidator.ValidateOrThrow(request.Password);

        // No email verification: accounts are active immediately and the user is signed in right away.
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            Email = email,
            PasswordHash = passwordHasher.Hash(request.Password),
            SubscriptionType = SubscriptionType.Free,
            WelcomeStoryRemaining = await ResolveWelcomeGiftAsync(userId, request, cancellationToken),
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow
        };

        await userRepository.CreateAsync(user, cancellationToken);

        return await BuildAuthResponseAsync(user, cancellationToken);
    }

    public async Task<AuthResponse> ContinueAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var existing = await userRepository.GetByEmailAsync(email, cancellationToken);

        // Returning account → just sign in. No reCAPTCHA needed (we are not creating anything).
        if (existing is not null)
        {
            if (existing.PasswordHash == OAuthProviders.GooglePasswordPlaceholder)
            {
                throw new UnauthorizedAccessException("This account uses Google sign-in. Please continue with Google.");
            }

            if (!passwordHasher.Verify(request.Password, existing.PasswordHash))
            {
                throw new UnauthorizedAccessException("That password doesn't match this email. Try again.");
            }

            return await BuildAuthResponseAsync(existing, cancellationToken);
        }

        // New account → verify reCAPTCHA, then create and sign in immediately (no email confirmation).
        if (!await recaptchaVerifier.VerifyAsync(request.RecaptchaToken, cancellationToken))
        {
            throw new InvalidOperationException("reCAPTCHA verification failed. Please try again.");
        }

        PasswordValidator.ValidateOrThrow(request.Password);

        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            Email = email,
            PasswordHash = passwordHasher.Hash(request.Password),
            SubscriptionType = SubscriptionType.Free,
            WelcomeStoryRemaining = await ResolveWelcomeGiftAsync(userId, request, cancellationToken),
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow
        };

        await userRepository.CreateAsync(user, cancellationToken);

        return await BuildAuthResponseAsync(user, cancellationToken);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await userRepository.GetByEmailAsync(email, cancellationToken)
                   ?? throw new UnauthorizedAccessException("Invalid credentials.");

        if (user.PasswordHash == OAuthProviders.GooglePasswordPlaceholder)
        {
            throw new UnauthorizedAccessException("This account uses Google sign-in. Please continue with Google.");
        }

        if (!passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedAccessException("Invalid credentials.");
        }

        return await BuildAuthResponseAsync(user, cancellationToken);
    }

    public async Task<AuthResponse> LoginWithGoogleAsync(GoogleLoginRequest request, CancellationToken cancellationToken)
    {
        var googleUser = await googleAuthService.ValidateIdTokenAsync(request.IdToken, cancellationToken);
        var email = googleUser.Email.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(email) || !googleUser.EmailVerified)
        {
            throw new UnauthorizedAccessException("Google account email is not verified.");
        }

        var user = await userRepository.GetByEmailAsync(email, cancellationToken);
        if (user is null)
        {
            var userId = Guid.NewGuid();
            user = new User
            {
                Id = userId,
                Email = email,
                PasswordHash = OAuthProviders.GooglePasswordPlaceholder,
                SubscriptionType = SubscriptionType.Free,
                WelcomeStoryRemaining = await welcomeGiftService.GetWelcomeStoryRemainingAsync(
                    new WelcomeGiftContext
                    {
                        UsedGuestPreview = request.UsedGuestPreview,
                        GuestPreviewId = request.GuestPreviewId,
                        StoryId = request.StoryId
                    },
                    userId,
                    cancellationToken),
                EmailConfirmed = true,
                CreatedAt = DateTime.UtcNow,
            };

            await userRepository.CreateAsync(user, cancellationToken);
        }
        else if (!user.EmailConfirmed)
        {
            await userRepository.ConfirmEmailAsync(user.Id, cancellationToken);
            user.EmailConfirmed = true;
        }

        return await BuildAuthResponseAsync(user, cancellationToken);
    }

    private Task<int> ResolveWelcomeGiftAsync(Guid userId, RegisterRequest request, CancellationToken cancellationToken) =>
        welcomeGiftService.GetWelcomeStoryRemainingAsync(
            new WelcomeGiftContext
            {
                UsedGuestPreview = request.UsedGuestPreview,
                GuestPreviewId = request.GuestPreviewId,
                StoryId = request.StoryId
            },
            userId,
            cancellationToken);

    private async Task<AuthResponse> BuildAuthResponseAsync(User user, CancellationToken cancellationToken)
    {
        var response = jwtTokenService.CreateToken(user);
        var balance = await subscriptionService.GetAccountBalanceAsync(user.Id, cancellationToken);
        response.BookCredits = balance.BookCredits;
        response.StoriesUsedThisMonth = balance.StoriesUsedThisMonth;
        response.StoriesAllowedThisMonth = balance.StoriesAllowedThisMonth;
        response.StoriesRemainingThisMonth = balance.StoriesRemainingThisMonth;
        response.WelcomeStoryRemaining = balance.WelcomeStoryRemaining;
        return response;
    }

    public async Task<EmailStatusResponse> GetEmailStatusAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new EmailStatusResponse { Exists = false, IsGoogleAccount = false };
        }

        var user = await userRepository.GetByEmailAsync(normalized, cancellationToken);
        return new EmailStatusResponse
        {
            Exists = user is not null,
            IsGoogleAccount = user is not null && user.PasswordHash == OAuthProviders.GooglePasswordPlaceholder
        };
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
