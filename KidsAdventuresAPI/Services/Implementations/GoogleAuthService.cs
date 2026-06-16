using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Services.Interfaces;
using Google.Apis.Auth;

namespace AdventurePacks.Api.Services.Implementations;

public sealed class GoogleAuthService(IOptions<GoogleAuthOptions> options) : IGoogleAuthService
{
    private readonly GoogleAuthOptions _options = options.Value;

    public async Task<GoogleTokenPayload> ValidateIdTokenAsync(string idToken, CancellationToken cancellationToken)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.ClientId))
        {
            throw new InvalidOperationException("Google sign-in is not configured.");
        }

        var payload = await GoogleJsonWebSignature.ValidateAsync(
            idToken,
            new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = [_options.ClientId],
            });

        cancellationToken.ThrowIfCancellationRequested();

        return new GoogleTokenPayload
        {
            Email = payload.Email ?? string.Empty,
            EmailVerified = payload.EmailVerified,
            Subject = payload.Subject,
        };
    }
}
