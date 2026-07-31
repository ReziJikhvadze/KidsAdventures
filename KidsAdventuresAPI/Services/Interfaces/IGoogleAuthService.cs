namespace AdventurePacks.Api.Services.Interfaces;

public interface IGoogleAuthService
{
    Task<GoogleTokenPayload> ValidateCredentialAsync(
        string? idToken,
        string? accessToken,
        CancellationToken cancellationToken);
}

public sealed class GoogleTokenPayload
{
    public string Email { get; init; } = string.Empty;

    public bool EmailVerified { get; init; }

    public string Subject { get; init; } = string.Empty;
}
