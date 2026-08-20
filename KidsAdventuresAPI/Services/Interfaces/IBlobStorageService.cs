namespace AdventurePacks.Api.Services.Interfaces;

public interface IBlobStorageService
{
    Task<string> UploadAsync(string blobName, byte[] bytes, string contentType, CancellationToken cancellationToken);
    Task<Stream> DownloadAsync(string blobName, CancellationToken cancellationToken);

    /// <summary>
    /// Whether a blob exists, named the way <see cref="UploadAsync"/> names it — a bare name
    /// under the configured container, never a stored URL. Cheap by design: it backs polling.
    /// </summary>
    Task<bool> ExistsAsync(string blobName, CancellationToken cancellationToken);
    Task<byte[]> DownloadBytesFromStoredUrlAsync(string storedUrl, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a blob, reporting whether one was there. Deliberately quiet about a blob that has
    /// already gone: cleanup runs repeatedly and must be safe to run twice.
    /// </summary>
    Task<bool> DeleteByStoredUrlAsync(string storedUrl, CancellationToken cancellationToken);
}
