using System.Collections.Concurrent;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Implementations;

/// <summary>
/// Best-effort, in-memory anti-abuse backstop for the free no-login preview. The "1 per browser" UX is enforced
/// on the client; this caps total free generations per IP so the OpenAI bill can't be run up by bots.
/// </summary>
public sealed class GuestRateLimiter : IGuestRateLimiter
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);
    private const int MaxPerWindow = 5;

    private readonly ConcurrentDictionary<string, List<DateTime>> _hits = new();

    public bool TryAcquire(string clientKey)
    {
        if (string.IsNullOrWhiteSpace(clientKey))
        {
            clientKey = "unknown";
        }

        var now = DateTime.UtcNow;
        var list = _hits.GetOrAdd(clientKey, _ => []);

        lock (list)
        {
            list.RemoveAll(t => now - t > Window);
            if (list.Count >= MaxPerWindow)
            {
                return false;
            }

            list.Add(now);
            return true;
        }
    }
}
