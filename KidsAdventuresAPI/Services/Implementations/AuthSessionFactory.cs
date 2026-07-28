using AdventurePacks.Api.DTOs.Auth;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Implementations;

public sealed class AuthSessionFactory(
    IJwtTokenService jwtTokenService,
    IUserRepository userRepository) : IAuthSessionFactory
{
    public async Task<AuthResponse> CreateAsync(User user, CancellationToken cancellationToken)
    {
        var response = jwtTokenService.CreateToken(user);

        // The caller may hand us a user object assembled during sign-up, before the
        // welcome gift was written. Re-reading keeps the session honest about what the
        // parent is actually owed.
        var stored = await userRepository.GetByIdAsync(user.Id, cancellationToken);
        if (stored is not null)
        {
            response.WelcomeStoryRemaining = stored.WelcomeStoryRemaining;
        }

        return response;
    }
}
