namespace AdventurePacks.Api.Domain.Entities;

/// <summary>
/// Server-side record of a free, no-login teaser generation. It makes the welcome-gift entitlement
/// reliable and device-independent: the gift is tied to this row (and its <see cref="StoryId"/>), not to a
/// browser's localStorage. A preview can be redeemed for the welcome gift exactly once.
/// </summary>
public sealed class GuestPreview
{
    /// <summary>The guestPreviewId handed back to the client and replayed during sign-up.</summary>
    public Guid Id { get; set; }

    /// <summary>Identity of the story produced by the teaser (ties preview → story for fallback lookups).</summary>
    public Guid StoryId { get; set; }

    /// <summary>Always true once a teaser has been generated for this row.</summary>
    public bool PreviewUsed { get; set; } = true;

    /// <summary>True once the welcome gift has been granted for this preview (prevents cross-account farming).</summary>
    public bool Redeemed { get; set; }

    public Guid? RedeemedByUserId { get; set; }

    /// <summary>Best-effort client identity (IP) captured at generation time, for abuse analysis.</summary>
    public string? ClientKey { get; set; }

    public string? ChildName { get; set; }

    public string? Theme { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? RedeemedAt { get; set; }
}
