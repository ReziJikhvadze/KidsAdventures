namespace AdventurePacks.Api.Repositories.Interfaces;

public interface IAdventurePackRepository
{
    Task<Guid> CreatePendingAsync(AdventurePack pack, CancellationToken cancellationToken);
    Task<AdventurePack?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken);
    Task<AdventurePack?> GetByIdNoOwnershipAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdventurePack>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Every book a character appears in, in series order.</summary>
    Task<IReadOnlyList<AdventurePack>> GetByCharacterIdAsync(Guid characterId, Guid userId, CancellationToken cancellationToken);

    Task<int> GetNextSequenceNumberAsync(Guid seriesId, CancellationToken cancellationToken);

    /// <summary>Opens the whole book. Called on order fulfilment, never from a client request.</summary>
    Task<bool> SetAccessLevelAsync(Guid id, BookAccessLevel accessLevel, CancellationToken cancellationToken);

    /// <summary>Stamps the book as opened in the reader, scoped to its owner.</summary>
    Task<bool> MarkReadAsync(Guid id, Guid userId, CancellationToken cancellationToken);

    Task<bool> SetPrintEntitlementAsync(Guid id, CancellationToken cancellationToken);

    Task UpdateBookPresentationAsync(Guid id, string? title, string? coverImageUrl, CancellationToken cancellationToken);
    Task<int> CountForMonthAsync(Guid userId, DateTime utcMonthStart, DateTime utcMonthEnd, CancellationToken cancellationToken);
    Task<bool> UpdateStatusAsync(Guid id, AdventurePackStatus status, string? generatedJson, string? pdfUrl, string? errorMessage, CancellationToken cancellationToken);
    /// <summary>
    /// Records where the printable copy was stored. Its own call rather than another argument to
    /// <see cref="UpdateStatusAsync"/>, because every other caller of that would pass null and
    /// quietly erase a url it knows nothing about.
    /// </summary>
    Task UpdatePrintPdfUrlAsync(Guid id, string? printPdfUrl, CancellationToken cancellationToken);

    Task UpdateProgressMessageAsync(Guid id, string? progressMessage, CancellationToken cancellationToken);

    /// <summary>Message and percentage together, so a progress bar and its caption never disagree.</summary>
    Task UpdateProgressAsync(Guid id, string? progressMessage, int? progressPercent, CancellationToken cancellationToken);
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
