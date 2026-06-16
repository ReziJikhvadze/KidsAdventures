namespace AdventurePacks.Api.Services.Interfaces;

public interface IGoogleAuthService
{
    Task<GoogleTokenPayload> ValidateIdTokenAsync(string idToken, CancellationToken cancellationToken);
}

public sealed class GoogleTokenPayload
{
    public string Email { get; init; } = string.Empty;

    public bool EmailVerified { get; init; }

    public string Subject { get; init; } = string.Empty;
}
