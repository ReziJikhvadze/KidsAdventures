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

    /// <summary>
    /// Copies one stored blob to another name inside the same store, without the bytes travelling
    /// through this process.
    /// </summary>
    /// <remarks>
    /// It exists for the snapshot a print re-preparation takes before it replaces a finished book:
    /// a fifty-megabyte reading PDF, a contact sheet and a dozen reports were downloaded to the
    /// application and uploaded straight back under <c>previous/{timestamp}/</c>, and the rollback
    /// that puts them back did it again in the other direction. On Azure that is a hundred
    /// megabytes over the wire, twice, for bytes the storage account already had; the service can do
    /// the whole thing itself.
    ///
    /// <para>
    /// A DEFAULT member, and the default is exactly the download-and-upload the callers used to
    /// write out by hand. This interface has more hand-written doubles than implementations —
    /// eighteen of them across the test assembly — and a new required member would have been
    /// eighteen edits to teach eighteen dictionaries a trick none of their tests are about. The
    /// implementations that can do better override it; a double gets the honest, slow answer and
    /// stays correct.
    /// </para>
    /// <para>
    /// <paramref name="contentType"/> is what the copy is served as when the fallback has to
    /// re-upload it. A server-side copy carries the source's own properties and ignores it, which
    /// is the right answer in both cases: the copy of a PDF is a PDF either way.
    /// </para>
    /// </remarks>
    /// <param name="sourceName">The blob to copy, named the way <see cref="UploadAsync"/> names it.</param>
    /// <param name="destinationName">Where to put it, named the same way. Overwritten if present.</param>
    async Task CopyAsync(
        string sourceName, string destinationName, string contentType, CancellationToken cancellationToken)
    {
        await using var stream = await DownloadAsync(sourceName, cancellationToken);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        await UploadAsync(destinationName, buffer.ToArray(), contentType, cancellationToken);
    }
}
