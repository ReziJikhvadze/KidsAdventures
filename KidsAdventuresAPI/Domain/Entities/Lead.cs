namespace AdventurePacks.Api.Domain.Entities;

/// <summary>
/// An email captured from an anonymous visitor (e.g. via the exit-intent offer) so we can nudge
/// them back to claim their free first illustrated book. One row per email address.
/// </summary>
public sealed class Lead
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Email { get; set; } = string.Empty;

    /// <summary>Where the email was captured (e.g. "exit-intent"), for funnel analysis.</summary>
    public string? Source { get; set; }

    public string? ChildName { get; set; }

    public string? Theme { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Set once the follow-up "your free book is waiting" email has been sent.</summary>
    public DateTime? EmailedAt { get; set; }
}
