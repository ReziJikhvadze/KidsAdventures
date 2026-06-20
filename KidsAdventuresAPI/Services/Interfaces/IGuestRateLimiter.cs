namespace AdventurePacks.Api.Services.Interfaces;

public interface IGuestRateLimiter
{
    /// <summary>Returns true if this client may run another free guest preview right now.</summary>
    bool TryAcquire(string clientKey);
}
