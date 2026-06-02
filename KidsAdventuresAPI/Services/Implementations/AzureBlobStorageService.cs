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
        var container = await GetContainerAsync(cancellationToken);
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
        var container = await GetContainerAsync(cancellationToken);
        var blobClient = container.GetBlobClient(blobName);
        var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
        return response.Value.Content;
    }

    public async Task<byte[]> DownloadBytesFromStoredUrlAsync(string storedUrl, CancellationToken cancellationToken)
    {
        var blobName = ResolveBlobName(storedUrl);
        await using var stream = await DownloadAsync(blobName, cancellationToken);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        return memory.ToArray();
    }

    private string ResolveBlobName(string storedUrl)
    {
        if (!storedUrl.Contains("://", StringComparison.Ordinal))
        {
            return storedUrl;
        }

        if (!Uri.TryCreate(storedUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Invalid blob URL.");
        }

        var path = uri.AbsolutePath.Trim('/');
        var prefix = _options.ContainerName + "/";
        if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return path[prefix.Length..];
        }

        var slash = path.IndexOf('/');
        return slash >= 0 ? path[(slash + 1)..] : path;
    }

    private async Task<BlobContainerClient> GetContainerAsync(CancellationToken cancellationToken)
    {
        var serviceClient = new BlobServiceClient(_options.ConnectionString);
        var container = serviceClient.GetBlobContainerClient(_options.ContainerName);
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        return container;
    }
}
