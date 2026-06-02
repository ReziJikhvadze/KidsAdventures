using AdventurePacks.Api.DTOs.Auth;

namespace AdventurePacks.Api.Services.Interfaces;

public interface IJwtTokenService
{
    AuthResponse CreateToken(User user);
}
