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

    private async Task<BlobContainerClient> GetContainerAsync(CancellationToken cancellationToken)
    {
        var serviceClient = new BlobServiceClient(_options.ConnectionString);
        var container = serviceClient.GetBlobContainerClient(_options.ContainerName);
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        return container;
    }
}
