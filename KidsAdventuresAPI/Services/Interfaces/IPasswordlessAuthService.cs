using AdventurePacks.Api.DTOs.Auth;

namespace AdventurePacks.Api.Services.Interfaces;

public interface IPasswordlessAuthService
{
    PasswordlessConfigResponse GetConfig();

    Task<AuthChallengeResponse> RequestMagicLinkAsync(
        MagicLinkRequest request,
        string? ipAddress,
        CancellationToken cancellationToken);

    Task<AuthResponse> VerifyMagicLinkAsync(VerifyMagicLinkRequest request, CancellationToken cancellationToken);

    Task<AuthChallengeResponse> RequestPhoneCodeAsync(
        PhoneCodeRequest request,
        string? ipAddress,
        CancellationToken cancellationToken);

    Task<AuthResponse> VerifyPhoneCodeAsync(VerifyPhoneCodeRequest request, CancellationToken cancellationToken);
}
