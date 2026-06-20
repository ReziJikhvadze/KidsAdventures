using AdventurePacks.Api.DTOs.Auth;

namespace AdventurePacks.Api.Services.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);

    /// <summary>One-step auth: signs in if the email already exists, otherwise creates the account (with reCAPTCHA).</summary>
    Task<AuthResponse> ContinueAsync(RegisterRequest request, CancellationToken cancellationToken);

    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<AuthResponse> LoginWithGoogleAsync(GoogleLoginRequest request, CancellationToken cancellationToken);
    Task<bool> ConfirmEmailAsync(string token, CancellationToken cancellationToken);

    /// <summary>Lightweight check used by the email-first UI to decide between "sign in" and "create account".</summary>
    Task<EmailStatusResponse> GetEmailStatusAsync(string email, CancellationToken cancellationToken);
}
