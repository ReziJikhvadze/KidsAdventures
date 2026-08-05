using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Story;

public interface IMasterStoryRunCleanupService
{
    Task PurgeExpiredAsync();
}

/// <summary>
/// Deletes guest runs that have outlived their expiry, and the files they own.
///
/// The preview path used to store nothing at all, so a visitor who never signed up left no
/// trace. Polling made storing something unavoidable, and the expiry column was how that promise
/// was meant to be kept — but nothing was ever deleting on it, so the rows and the portraits sat
/// there indefinitely. An expiry nobody acts on is a comment, not a guarantee.
///
/// The portrait matters more than the row. It is a photograph of a named child, uploaded by a
/// parent who may never have created an account.
/// </summary>
public sealed class MasterStoryRunCleanupService(
    IMasterStoryRunRepository runRepository,
    IBlobStorageService blobStorageService,
    ILogger<MasterStoryRunCleanupService> logger) : IMasterStoryRunCleanupService
{
    /// <summary>Bounded so one sweep cannot hold a connection open over a long backlog.</summary>
    private const int BatchSize = 200;

    public async Task PurgeExpiredAsync()
    {
        var expired = await runRepository.ListExpiredAsync(BatchSize, CancellationToken.None);
        if (expired.Count == 0)
        {
            return;
        }

        var filesRemoved = 0;
        foreach (var run in expired)
        {
            filesRemoved += await TryDeleteAsync(run.PhotoBlobUrl, run.Id);
            filesRemoved += await TryDeleteAsync(run.CoverImageUrl, run.Id);
        }

        // Rows go last. If a blob delete fails, the row survives to be tried again on the next
        // sweep — the opposite order would drop the only record of where the file lives.
        var rowsRemoved = await runRepository.DeleteAsync(
            expired.Select(r => r.Id).ToList(), CancellationToken.None);

        logger.LogInformation(
            "Purged {Rows} expired guest runs and {Files} of their files.", rowsRemoved, filesRemoved);
    }

    private async Task<int> TryDeleteAsync(string? storedUrl, Guid runId)
    {
        if (string.IsNullOrWhiteSpace(storedUrl))
        {
            return 0;
        }

        try
        {
            return await blobStorageService.DeleteByStoredUrlAsync(storedUrl, CancellationToken.None)
                ? 1
                : 0;
        }
        catch (Exception ex)
        {
            // Logged rather than thrown: one unreachable file must not stop the sweep clearing
            // everything else, and the row it belongs to stays behind to be retried.
            logger.LogWarning(ex, "Could not delete a file belonging to expired run {RunId}.", runId);
            return 0;
        }
    }
}
