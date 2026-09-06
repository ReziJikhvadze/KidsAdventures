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
    /// A companion file kept beside a stored one, at the same name with <paramref name="suffix"/>
    /// appended — a thumbnail next to its portrait, say.
    ///
    /// Here rather than in the caller because only the storage service knows how its own stored
    /// value maps to a location: one implementation keeps a full Azure URL and parses the
    /// container back out of it, the other keeps a container-relative key precisely because that
    /// parse is wrong under the emulator. A caller that worked it out for itself would be right
    /// for one of them.
    ///
    /// Null when there is no such file, which is the ordinary answer the first time.
    /// </summary>
    Task<byte[]?> TryDownloadBesideAsync(string storedUrl, string suffix, CancellationToken cancellationToken);

    /// <summary>Writes, or overwrites, the companion described above.</summary>
    Task UploadBesideAsync(
        string storedUrl, string suffix, byte[] bytes, string contentType, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a blob, reporting whether one was there. Deliberately quiet about a blob that has
    /// already gone: cleanup runs repeatedly and must be safe to run twice.
    /// </summary>
    Task<bool> DeleteByStoredUrlAsync(string storedUrl, CancellationToken cancellationToken);
}
