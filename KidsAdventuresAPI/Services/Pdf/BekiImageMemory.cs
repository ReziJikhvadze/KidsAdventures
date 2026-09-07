using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Memory;

namespace AdventurePacks.Api.Services.Pdf;

/// <summary>
/// The one place this process tells ImageSharp how much memory it may keep.
///
/// ImageSharp's default allocator sizes its buffer pool from the machine it finds itself on and
/// then retains what it has allocated, on the reasonable assumption that a process decoding images
/// will decode more of them. This process is not that. It is a web application that, a few times a
/// day, walks ten thirteen-to-seventeen-megapixel canvases through a press stage in a burst and
/// then serves ordinary requests again for hours — and on a small App Service plan the pool it kept
/// from the burst is the difference between a resident set that fits in the plan's memory and one
/// that swaps. Swapping is not slower by a factor; the observed symptom was a stage that took over
/// an hour.
///
/// Called once at startup, before anything decodes an image, because
/// <see cref="SixLabors.ImageSharp.Configuration.Default"/> is process-wide state and a change to
/// it partway through a decode is not something ImageSharp promises anything about.
/// </summary>
public static class BekiImageMemory
{
    private static int _configured;

    /// <summary>
    /// Caps ImageSharp's pooled memory at <paramref name="maximumPoolSizeMegabytes"/>.
    /// </summary>
    /// <returns>
    /// Whether this call was the one that set it. False for a value of zero or less — which leaves
    /// ImageSharp's own default alone, the escape hatch for a deployment that would rather tune the
    /// runtime — and false for a second call, so a test host that builds the application twice does
    /// not swap the allocator out from under images the first one is still holding.
    /// </returns>
    public static bool Configure(int maximumPoolSizeMegabytes)
    {
        if (maximumPoolSizeMegabytes <= 0)
        {
            return false;
        }

        if (Interlocked.Exchange(ref _configured, 1) == 1)
        {
            return false;
        }

        SixLabors.ImageSharp.Configuration.Default.MemoryAllocator = MemoryAllocator.Create(
            new MemoryAllocatorOptions { MaximumPoolSizeMegabytes = maximumPoolSizeMegabytes });

        return true;
    }
}
