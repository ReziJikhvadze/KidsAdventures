namespace AdventurePacks.Api.Services.Interfaces;

public interface IBlobStorageService
{
    Task<string> UploadAsync(string blobName, byte[] bytes, string contentType, CancellationToken cancellationToken);
    Task<Stream> DownloadAsync(string blobName, CancellationToken cancellationToken);
    Task<byte[]> DownloadBytesFromStoredUrlAsync(string storedUrl, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a blob, reporting whether one was there. Deliberately quiet about a blob that has
    /// already gone: cleanup runs repeatedly and must be safe to run twice.
    /// </summary>
    Task<bool> DeleteByStoredUrlAsync(string storedUrl, CancellationToken cancellationToken);
}
