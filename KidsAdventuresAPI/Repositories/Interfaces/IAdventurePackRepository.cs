namespace AdventurePacks.Api.Repositories.Interfaces;

/// <summary>
/// One pack whose generation job has stopped saying anything, as the sweep sees it.
///
/// <paramref name="LastSignalUtc"/> is the heartbeat, or <c>CreatedAt</c> when the pack predates
/// the heartbeat column — it is the number the sweep judged, carried out of the query so the log
/// line can say how long the pack had actually been quiet rather than only that it was too long.
/// </summary>
public sealed record StaleGenerationPack(
    Guid Id,
    AdventurePackStatus Status,
    DateTime CreatedAt,
    DateTime? GenerationHeartbeatUtc)
{
    public DateTime LastSignalUtc => GenerationHeartbeatUtc ?? CreatedAt;

    /// <summary>True when this row predates the heartbeat column, or was never claimed.</summary>
    public bool HeartbeatMissing => GenerationHeartbeatUtc is null;
}

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
    /// <summary>
    /// Writes the status unconditionally, and stamps the generation heartbeat with it.
    ///
    /// Fine for a claim or a phase change. A <em>terminal</em> transition should go through
    /// <see cref="TryUpdateStatusAsync"/> instead, so that a job which has already been declared
    /// lost cannot quietly overrule the verdict.
    /// </summary>
    Task<bool> UpdateStatusAsync(Guid id, AdventurePackStatus status, string? generatedJson, string? pdfUrl, string? errorMessage, CancellationToken cancellationToken);

    /// <summary>
    /// Compare-and-set: writes the status only while the pack is still in
    /// <paramref name="expectedStatus"/>. False means another writer — almost always the
    /// stale-generation sweep — got there first, and the caller should log and defer to what is
    /// stored rather than overwrite it.
    /// </summary>
    Task<bool> TryUpdateStatusAsync(
        Guid id,
        AdventurePackStatus expectedStatus,
        AdventurePackStatus status,
        string? generatedJson,
        string? pdfUrl,
        string? errorMessage,
        CancellationToken cancellationToken);

    /// <summary>
    /// Packs still in a working status whose last signal — the heartbeat, or CreatedAt when the
    /// row predates that column — is older than <paramref name="cutoffUtc"/>.
    /// </summary>
    Task<IReadOnlyList<StaleGenerationPack>> ListStaleGenerationAsync(
        DateTime cutoffUtc,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Fails one stalled pack if it is still in <paramref name="expectedStatus"/> <em>and</em> its
    /// last signal is still older than <paramref name="cutoffUtc"/> — the same cutoff the listing
    /// used. Re-testing staleness inside the write is what stops a job that delivered a spread
    /// between the sweep's read and its write from being buried while alive.
    ///
    /// Touches only the status and the message: whatever the dead job managed to store stays.
    /// </summary>
    Task<bool> TryFailStaleGenerationAsync(
        Guid id,
        AdventurePackStatus expectedStatus,
        DateTime cutoffUtc,
        string errorMessage,
        CancellationToken cancellationToken);

    /// <summary>
    /// A job failing the book it was itself making: status and message only, and only while the
    /// pack is still in <paramref name="expectedStatus"/>. False means the sweep — or another
    /// writer — got there first and the caller should defer to what is stored.
    /// </summary>
    Task<bool> TryFailAsync(
        Guid id,
        AdventurePackStatus expectedStatus,
        string errorMessage,
        CancellationToken cancellationToken);
    /// <summary>
    /// Records where the printable copy was stored. Its own call rather than another argument to
    /// <see cref="UpdateStatusAsync"/>, because every other caller of that would pass null and
    /// quietly erase a url it knows nothing about.
    /// </summary>
    Task UpdatePrintPdfUrlAsync(Guid id, string? printPdfUrl, CancellationToken cancellationToken);

    /// <summary>
    /// Records the book's canonical title on the pack row — the order record's copy of the one
    /// string the cover, the intro and the PDF metadata all print. A default no-op rather than an
    /// abstract member: only fulfilment writes it, and every test double of this wide interface
    /// should not have to say so.
    /// </summary>
    Task UpdateTitleAsync(Guid id, string title, CancellationToken cancellationToken) =>
        Task.CompletedTask;

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

    /// <summary>
    /// Records which pipeline is drawing this book — <c>beki</c> or <c>legacy</c>, amendment B5.
    /// Written by whoever chose the pipeline, in the same unit of work that adopts or creates the
    /// pack, so that nothing downstream has to re-derive it from a preview run's prompt version.
    /// </summary>
    Task SetGenerationPipelineAsync(Guid id, string pipeline, CancellationToken cancellationToken);

    /// <summary>
    /// Completed Beki books with a withheld DELIVERABLE — either the parent's download or the
    /// printer's interior — which is the set a policy change re-judges (amendment B7).
    /// </summary>
    /// <param name="after">
    /// Where the previous batch stopped, or null to start at the newest. Keyset rather than an
    /// offset because the rows move underneath a scan: publishing is exactly what takes a book OUT
    /// of this set, so a second page addressed by offset would skip whatever the first page fixed.
    /// </param>
    Task<IReadOnlyList<AdventurePack>> ListWithheldBekiPacksAsync(
        int limit, BekiWithheldCursor? after, CancellationToken cancellationToken);
}

/// <summary>
/// Where a withheld-book scan left off: the last row it read, by the pair the ordering is on.
///
/// The pair rather than the timestamp alone, because two books created in the same millisecond would
/// otherwise make a batch boundary either skip one or repeat it forever.
/// </summary>
public readonly record struct BekiWithheldCursor(DateTime CreatedAtUtc, Guid PackId);
