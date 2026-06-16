using AdventurePacks.Api.DTOs.Auth;

namespace AdventurePacks.Api.Services.Interfaces;

public interface IAuthService
{
    Task<RegisterResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<AuthResponse> LoginWithGoogleAsync(GoogleLoginRequest request, CancellationToken cancellationToken);
    Task<bool> ConfirmEmailAsync(string token, CancellationToken cancellationToken);
}
