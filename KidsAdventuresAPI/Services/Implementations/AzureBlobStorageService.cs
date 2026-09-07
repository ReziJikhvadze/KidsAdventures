using System.Collections.Concurrent;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Services.Interfaces;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace AdventurePacks.Api.Services.Implementations;

/// <summary>
/// Blob storage on an Azure account.
///
/// One container client per container, for the life of the process. Every operation used to
/// build a fresh <see cref="BlobServiceClient"/> and call <c>CreateIfNotExists</c> before doing
/// its own work, which made every upload, download and existence check two round trips instead
/// of one — and a composite book performs somewhere between one hundred and fifty and two hundred
/// and fifty of them. The container is created once, on first use, and the client that knows it
/// exists is reused; a container that does not exist yet is still created, exactly as before,
/// just not re-confirmed on every call.
///
/// Registered as a singleton (see <c>ServiceCollectionExtensions</c>) so the cache below is
/// process-wide. The class holds no per-request state, so that lifetime is safe.
/// </summary>
public sealed class AzureBlobStorageService : IBlobStorageService
{
    private readonly AzureBlobOptions _options;
    private readonly Func<string, BlobContainerClient> _containerFactory;

    /// <summary>
    /// The containers this process has already made sure of, keyed by name. A <see cref="Lazy{T}"/>
    /// around the task so that two operations racing on a cold cache start one creation, not two.
    /// </summary>
    private readonly ConcurrentDictionary<string, Lazy<Task<BlobContainerClient>>> _containers =
        new(StringComparer.Ordinal);

    public AzureBlobStorageService(IOptions<AzureBlobOptions> options)
        : this(options, containerFactory: null)
    {
    }

    /// <summary>
    /// The seam the tests use: a factory that hands back a container client for a name. Production
    /// builds the real one from the connection string, lazily, so a deployment with the local
    /// folder switched on never parses an Azure connection string it does not have.
    /// </summary>
    internal AzureBlobStorageService(
        IOptions<AzureBlobOptions> options,
        Func<string, BlobContainerClient>? containerFactory)
    {
        _options = options.Value;

        if (containerFactory is null)
        {
            var serviceClient = new Lazy<BlobServiceClient>(
                () => new BlobServiceClient(_options.ConnectionString));
            containerFactory = name => serviceClient.Value.GetBlobContainerClient(name);
        }

        _containerFactory = containerFactory;
    }

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

    public async Task<bool> ExistsAsync(string blobName, CancellationToken cancellationToken)
    {
        var container = await GetContainerAsync(_options.ContainerName, cancellationToken);
        return await container.GetBlobClient(blobName).ExistsAsync(cancellationToken);
    }

    public async Task<byte[]?> TryDownloadBesideAsync(
        string storedUrl, string suffix, CancellationToken cancellationToken)
    {
        var (containerName, blobName) = ResolveBlobLocation(storedUrl);
        var container = await GetContainerAsync(containerName, cancellationToken);
        var blobClient = container.GetBlobClient(blobName + suffix);

        if (!await blobClient.ExistsAsync(cancellationToken))
        {
            return null;
        }

        var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
        await using var stream = response.Value.Content;
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        return memory.ToArray();
    }

    public async Task UploadBesideAsync(
        string storedUrl, string suffix, byte[] bytes, string contentType, CancellationToken cancellationToken)
    {
        var (containerName, blobName) = ResolveBlobLocation(storedUrl);
        var container = await GetContainerAsync(containerName, cancellationToken);
        var blobClient = container.GetBlobClient(blobName + suffix);
        using var stream = new MemoryStream(bytes);

        await blobClient.UploadAsync(
            stream,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
            },
            cancellationToken);
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
    /// The account's own copy, rather than a round trip through this process.
    ///
    /// Both names are container-relative, so source and destination are in the same account and the
    /// copy is authorized by the same shared key the rest of this class uses — no SAS, no public
    /// source. An intra-account copy normally completes on the first response; the poll is there
    /// because the service is allowed to take longer and a snapshot that has not finished is not a
    /// snapshot.
    ///
    /// It does not fall back to download-and-upload when the service refuses. A copy that failed is
    /// a storage failure, and the caller — the snapshot taken before a book is replaced — must hear
    /// about it rather than be handed a slower path that may be failing for the same reason. A copy
    /// the service ends as <see cref="CopyStatus.Failed"/> or <see cref="CopyStatus.Aborted"/>
    /// surfaces as the exception the wait throws, renamed here so the message says which two blobs.
    /// <paramref name="contentType"/> is unused on purpose: the copy inherits the source blob's own
    /// properties, which is the same answer and a truer one.
    /// </summary>
    public async Task CopyAsync(
        string sourceName, string destinationName, string contentType, CancellationToken cancellationToken)
    {
        var container = await GetContainerAsync(_options.ContainerName, cancellationToken);
        var source = container.GetBlobClient(sourceName);
        var destination = container.GetBlobClient(destinationName);

        // The options overload, named explicitly: the older parameter list is still there and
        // binding to it by accident is the kind of thing that reads identically and behaves
        // differently.
        var operation = await destination.StartCopyFromUriAsync(
            source.Uri, options: new BlobCopyFromUriOptions(), cancellationToken);

        try
        {
            await operation.WaitForCompletionAsync(cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            // The service's own message names neither blob, and a snapshot that did not happen is
            // exactly the failure an operator has to be able to read at a glance.
            throw new InvalidOperationException(
                $"Copying '{sourceName}' to '{destinationName}' did not complete: {ex.Message}", ex);
        }
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

    /// <summary>
    /// The client for a container that is known to exist — created on this process's first use of
    /// that container and reused after.
    ///
    /// The creation runs under no caller's token, because its result is shared: a first caller that
    /// gave up waiting must not leave every later caller holding a cancelled task. The caller's own
    /// wait is still cancellable. A creation that genuinely failed — a bad connection string, a
    /// network that was down for a moment — is forgotten rather than cached, so the next operation
    /// tries again instead of replaying the same exception for the life of the process.
    /// </summary>
    private async Task<BlobContainerClient> GetContainerAsync(string containerName, CancellationToken cancellationToken)
    {
        var entry = _containers.GetOrAdd(
            containerName,
            static (name, self) => new Lazy<Task<BlobContainerClient>>(() => self.EnsureContainerAsync(name)),
            this);

        try
        {
            return await entry.Value.WaitAsync(cancellationToken);
        }
        catch when (entry.Value.IsFaulted || entry.Value.IsCanceled)
        {
            _containers.TryRemove(new KeyValuePair<string, Lazy<Task<BlobContainerClient>>>(containerName, entry));
            throw;
        }
    }

    private async Task<BlobContainerClient> EnsureContainerAsync(string containerName)
    {
        var container = _containerFactory(containerName);
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: CancellationToken.None);
        return container;
    }
}
