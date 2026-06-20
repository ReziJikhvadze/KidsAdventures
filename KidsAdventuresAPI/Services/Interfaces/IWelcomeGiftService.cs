namespace AdventurePacks.Api.Services.Interfaces;

/// <summary>Everything the entitlement decision can depend on, gathered from the auth request.</summary>
public sealed class WelcomeGiftContext
{
    /// <summary>Legacy localStorage hint. Non-authoritative — kept only for telemetry/back-compat.</summary>
    public bool UsedGuestPreview { get; init; }

    /// <summary>Primary, server-trustable source: the id of a previously generated no-login teaser.</summary>
    public Guid? GuestPreviewId { get; init; }

    /// <summary>Fallback link to the preview when only the story identity travelled with the client.</summary>
    public Guid? StoryId { get; init; }
}

public interface IWelcomeGiftService
{
    /// <summary>
    /// Deterministic welcome-gift entitlement for a newly created account. Rules:
    /// a resolvable guest preview that is already redeemed → 0; an unredeemed preview → the default gift
    /// (redeemed as a side effect, exactly once); no preview on record (fresh signup) → the default gift.
    /// </summary>
    Task<int> GetWelcomeStoryRemainingAsync(WelcomeGiftContext context, Guid userId, CancellationToken cancellationToken);
}
