namespace AdventurePacks.Api.Services.Interfaces;

public interface IBlobStorageService
{
    Task<string> UploadAsync(string blobName, byte[] bytes, string contentType, CancellationToken cancellationToken);
    Task<Stream> DownloadAsync(string blobName, CancellationToken cancellationToken);
    Task<byte[]> DownloadBytesFromStoredUrlAsync(string storedUrl, CancellationToken cancellationToken);
}
