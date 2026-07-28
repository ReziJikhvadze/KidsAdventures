using AdventurePacks.Api.DTOs.Auth;

namespace AdventurePacks.Api.Services.Interfaces;

/// <summary>
/// Turns a <see cref="User"/> into the signed-in payload the client stores. Every entry
/// point — password, Google, magic link, phone code — funnels through here so a session
/// looks identical no matter which door the parent came through.
/// </summary>
public interface IAuthSessionFactory
{
    Task<AuthResponse> CreateAsync(User user, CancellationToken cancellationToken);
}
