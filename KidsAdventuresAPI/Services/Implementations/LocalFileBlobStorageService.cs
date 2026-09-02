using System.Collections.Concurrent;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Implementations;

/// <summary>
/// Blob storage backed by a folder on disk, for local development.
///
/// It stores a container-relative key rather than an absolute URL. The Azure implementation
/// stores a full URL and parses the container back out of it, which works because a real
/// account puts the container first in the path; the storage emulator puts the account name
/// there instead, and every read then looks in a container named after the account. Storing
/// the key sidesteps that entirely, and the callers do not care either way — nothing outside
/// the storage services inspects the stored value, they only hand it back for a download.
///
/// A singleton, like the Azure implementation: the root folder is created and announced once
/// per process rather than once per request, and a folder this service has already created is
/// not stat'ed again on every upload into it.
/// </summary>
public sealed class LocalFileBlobStorageService : IBlobStorageService
{
    private readonly string _root;
    private readonly string _containerName;
    private readonly ILogger<LocalFileBlobStorageService> _logger;

    /// <summary>Folders this process has already created. A set; the value is unused.</summary>
    private readonly ConcurrentDictionary<string, byte> _knownDirectories = new(StringComparer.Ordinal);

    public LocalFileBlobStorageService(
        IOptions<LocalBlobOptions> localOptions,
        IOptions<AzureBlobOptions> blobOptions,
        IHostEnvironment environment,
        ILogger<LocalFileBlobStorageService> logger)
    {
        var configured = localOptions.Value.RootPath;

        _root = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(environment.ContentRootPath, configured);

        // The container name is shared with the Azure implementation so a key written by one
        // reads the same way under the other, and switching back is only a settings change.
        _containerName = blobOptions.Value.ContainerName;
        _logger = logger;

        Directory.CreateDirectory(_root);
        _logger.LogInformation("Blob storage is a local folder: {Root}", _root);
    }

    public async Task<string> UploadAsync(
        string blobName,
        byte[] bytes,
        string contentType,
        CancellationToken cancellationToken)
    {
        var key = $"{_containerName}/{blobName.TrimStart('/')}";
        var path = ResolvePath(key);
        var directory = Path.GetDirectoryName(path)!;

        // Once per folder per process. TryAdd is the whole synchronisation: the first caller in
        // creates the folder, and a second caller racing it is caught by the retry below if it
        // gets to the write first.
        if (_knownDirectories.TryAdd(directory, 0))
        {
            Directory.CreateDirectory(directory);
        }

        try
        {
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        }
        catch (DirectoryNotFoundException)
        {
            // Somebody emptied the storage folder under a running dev server, or the racing
            // caller above has not finished creating it yet. Either way the answer is the same.
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        }

        // contentType is deliberately not persisted. Every caller that serves one of these
        // back decides the type from the file extension, which the key carries.
        return key;
    }

    public Task<Stream> DownloadAsync(string blobName, CancellationToken cancellationToken)
    {
        var path = ResolvePath($"{_containerName}/{blobName.TrimStart('/')}");
        return Task.FromResult<Stream>(File.OpenRead(path));
    }

    public Task<bool> ExistsAsync(string blobName, CancellationToken cancellationToken) =>
        Task.FromResult(File.Exists(ResolvePath($"{_containerName}/{blobName.TrimStart('/')}")));

    public async Task<byte[]> DownloadBytesFromStoredUrlAsync(
        string storedUrl,
        CancellationToken cancellationToken)
    {
        var path = ResolvePath(ToKey(storedUrl));

        if (!File.Exists(path))
        {
            // Same shape of failure as the Azure implementation, so callers that already
            // catch a missing blob keep behaving the same way.
            throw new FileNotFoundException($"Blob not found. Key='{ToKey(storedUrl)}'.", path);
        }

        return await File.ReadAllBytesAsync(path, cancellationToken);
    }

    public Task<bool> DeleteByStoredUrlAsync(string storedUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storedUrl))
        {
            return Task.FromResult(false);
        }

        var path = ResolvePath(ToKey(storedUrl));

        if (!File.Exists(path))
        {
            return Task.FromResult(false);
        }

        File.Delete(path);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Accepts either a key this service wrote or a full URL left behind by the Azure
    /// implementation, so a database that has seen both still reads.
    /// </summary>
    private string ToKey(string storedUrl)
    {
        if (!storedUrl.Contains("://", StringComparison.Ordinal))
        {
            return storedUrl.TrimStart('/');
        }

        if (!Uri.TryCreate(storedUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Invalid blob URL: {storedUrl}");
        }

        return uri.AbsolutePath.Trim('/');
    }

    /// <summary>
    /// Maps a key to a path under the root, refusing anything that climbs out of it. Blob
    /// names reach here from user-supplied data, and a key of "../../appsettings.json" must
    /// not resolve to a real file.
    /// </summary>
    private string ResolvePath(string key)
    {
        var combined = Path.GetFullPath(Path.Combine(_root, key));
        var root = Path.GetFullPath(_root);

        if (!combined.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Blob key escapes the storage root: {key}");
        }

        return combined;
    }
}
