using AdventurePacks.Api.DTOs.Auth;
using AdventurePacks.Api.Infrastructure;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Implementations;

public sealed class AuthService(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwtTokenService) : IAuthService
{
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var existing = await userRepository.GetByEmailAsync(email, cancellationToken);
        if (existing is not null)
        {
            throw new InvalidOperationException("Email is already registered.");
        }

        PasswordValidator.ValidateOrThrow(request.Password);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = passwordHasher.Hash(request.Password),
            SubscriptionType = SubscriptionType.Free,
            CreatedAt = DateTime.UtcNow
        };

        await userRepository.CreateAsync(user, cancellationToken);
        return jwtTokenService.CreateToken(user);
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

        return jwtTokenService.CreateToken(user);
    }
}
