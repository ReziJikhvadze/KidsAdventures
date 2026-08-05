using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Services.Interfaces;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace AdventurePacks.Api.Services.Implementations;

public sealed class AzureBlobStorageService(IOptions<AzureBlobOptions> options) : IBlobStorageService
{
    private readonly AzureBlobOptions _options = options.Value;

    public async Task<string> UploadAsync(string blobName, byte[] bytes, string contentType, CancellationToken cancellationToken)
    {
        var container = await GetContainerAsync(_options.ContainerName, cancellationToken);
        var blobClient = container.GetBlobClient(blobName);
        using var stream = new MemoryStream(bytes);

        await blobClient.UploadAsync(
            stream,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = contentType
                }
            },
            cancellationToken);

        return blobClient.Uri.ToString();
    }

    public async Task<Stream> DownloadAsync(string blobName, CancellationToken cancellationToken)
    {
        var container = await GetContainerAsync(_options.ContainerName, cancellationToken);
        var blobClient = container.GetBlobClient(blobName);
        var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
        return response.Value.Content;
    }

    public async Task<byte[]> DownloadBytesFromStoredUrlAsync(string storedUrl, CancellationToken cancellationToken)
    {
        var (containerName, blobName) = ResolveBlobLocation(storedUrl);
        var container = await GetContainerAsync(containerName, cancellationToken);
        var blobClient = container.GetBlobClient(blobName);

        if (!await blobClient.ExistsAsync(cancellationToken))
        {
            throw new FileNotFoundException(
                $"Blob not found. Container='{containerName}', Blob='{blobName}'.");
        }

        var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
        await using var stream = response.Value.Content;
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        return memory.ToArray();
    }

    public async Task<bool> DeleteByStoredUrlAsync(string storedUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storedUrl))
        {
            return false;
        }

        var (containerName, blobName) = ResolveBlobLocation(storedUrl);
        var container = await GetContainerAsync(containerName, cancellationToken);
        var response = await container.GetBlobClient(blobName).DeleteIfExistsAsync(
            cancellationToken: cancellationToken);
        return response.Value;
    }

    /// <summary>
    /// Parses container + blob path from a full Azure blob URL, or falls back to configured container.
    /// </summary>
    private (string ContainerName, string BlobName) ResolveBlobLocation(string storedUrl)
    {
        if (!storedUrl.Contains("://", StringComparison.Ordinal))
        {
            return (_options.ContainerName, storedUrl);
        }

        if (!Uri.TryCreate(storedUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Invalid blob URL.");
        }

        var path = uri.AbsolutePath.Trim('/');
        var slash = path.IndexOf('/');
        if (slash <= 0 || slash >= path.Length - 1)
        {
            throw new InvalidOperationException($"Invalid blob URL path: {storedUrl}");
        }

        var containerFromUrl = path[..slash];
        var blobName = path[(slash + 1)..];
        return (containerFromUrl, blobName);
    }

    private async Task<BlobContainerClient> GetContainerAsync(string containerName, CancellationToken cancellationToken)
    {
        var serviceClient = new BlobServiceClient(_options.ConnectionString);
        var container = serviceClient.GetBlobContainerClient(containerName);
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        return container;
    }
}
