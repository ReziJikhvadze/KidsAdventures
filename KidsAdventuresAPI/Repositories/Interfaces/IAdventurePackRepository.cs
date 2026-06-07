namespace AdventurePacks.Api.Repositories.Interfaces;

public interface IAdventurePackRepository
{
    Task<Guid> CreatePendingAsync(AdventurePack pack, CancellationToken cancellationToken);
    Task<AdventurePack?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken);
    Task<AdventurePack?> GetByIdNoOwnershipAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdventurePack>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);
    Task<int> CountForMonthAsync(Guid userId, DateTime utcMonthStart, DateTime utcMonthEnd, CancellationToken cancellationToken);
    Task<bool> UpdateStatusAsync(Guid id, AdventurePackStatus status, string? generatedJson, string? pdfUrl, string? errorMessage, CancellationToken cancellationToken);
    Task UpdateProgressMessageAsync(Guid id, string? progressMessage, CancellationToken cancellationToken);
    Task SetPdfCreditChargedAsync(Guid id, bool charged, CancellationToken cancellationToken);
    Task UpdatePreviewIllustrationAsync(
        Guid id,
        PreviewIllustrationStatus status,
        string? illustrationUrl,
        CancellationToken cancellationToken);
    /// <summary>Atomically marks preview generation as in-flight; reclaims stale Generating locks.</summary>
    Task<bool> TryClaimPreviewIllustrationGenerationAsync(
        Guid id,
        int staleAfterMinutes,
        CancellationToken cancellationToken);
    Task TouchPreviewIllustrationHeartbeatAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> UpdateGeneratedJsonAsync(Guid id, string generatedJson, CancellationToken cancellationToken);
}
