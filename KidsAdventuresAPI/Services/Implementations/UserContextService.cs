using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Implementations;

public sealed class UserContextService(IHttpContextAccessor accessor) : IUserContextService
{
    public Guid GetUserId()
    {
        var value = accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(value, out var userId))
        {
            throw new UnauthorizedAccessException("User is not authenticated.");
        }

        return userId;
    }

    public string GetEmail()
    {
        return accessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email)
               ?? throw new UnauthorizedAccessException("User email claim is missing.");
    }
}
