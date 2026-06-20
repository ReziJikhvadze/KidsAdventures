using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Implementations;

/// <summary>
/// Single source of truth for the "first illustrated page is free" welcome gift. Entitlement is tied to the
/// server-side <c>GuestPreviews</c> record (by id, or by storyId as a fallback) so it is reliable across
/// devices and cannot be reused by clearing localStorage.
/// </summary>
public sealed class WelcomeGiftService(
    IGuestPreviewRepository guestPreviewRepository,
    ILogger<WelcomeGiftService> logger) : IWelcomeGiftService
{
    /// <summary>How many illustrated pages a brand-new account receives for free.</summary>
    private const int DefaultGiftPages = AdventureStoryConstants.WelcomeGiftPageCount;

    public async Task<int> GetWelcomeStoryRemainingAsync(
        WelcomeGiftContext context,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var preview = await ResolvePreviewAsync(context, cancellationToken);

        // No teaser on record → a fresh signup, which always earns the standard welcome gift.
        if (preview is null)
        {
            return DefaultGiftPages;
        }

        // Tie the gift to this specific preview so a single teaser can't farm gifts across many accounts.
        // TryRedeem is atomic: only the first account to claim this preview gets the gift.
        var redeemedNow = await guestPreviewRepository.TryRedeemAsync(preview.Id, userId, cancellationToken);
        if (!redeemedNow)
        {
            logger.LogInformation(
                "Guest preview {GuestPreviewId} was already redeemed; granting no welcome gift to user {UserId}.",
                preview.Id, userId);
            return 0;
        }

        return DefaultGiftPages;
    }

    private async Task<GuestPreview?> ResolvePreviewAsync(WelcomeGiftContext context, CancellationToken cancellationToken)
    {
        if (context.GuestPreviewId is { } id && id != Guid.Empty)
        {
            var byId = await guestPreviewRepository.GetByIdAsync(id, cancellationToken);
            if (byId is not null)
            {
                return byId;
            }
        }

        if (context.StoryId is { } storyId && storyId != Guid.Empty)
        {
            return await guestPreviewRepository.GetByStoryIdAsync(storyId, cancellationToken);
        }

        return null;
    }
}
