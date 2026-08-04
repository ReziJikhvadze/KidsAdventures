using System.Collections.Concurrent;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Implementations;

/// <summary>
/// Best-effort, in-memory anti-abuse backstop for the free no-login preview.
///
/// Visitors may generate as many previews as they like — there is no per-browser limit. This ceiling
/// exists only because the endpoint is anonymous and every call costs a story plus an illustration, so
/// an unbounded one is a way to spend real money on a script. It is set well above what a person
/// exploring the product would ever reach, and shared IPs (an office, a school, mobile CGNAT) are the
/// reason it is not tighter.
/// </summary>
public sealed class GuestRateLimiter : IGuestRateLimiter
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);
    private const int MaxPerWindow = 40;

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
