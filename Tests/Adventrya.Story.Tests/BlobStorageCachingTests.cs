using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Extensions;
using AdventurePacks.Api.Services.Implementations;
using AdventurePacks.Api.Services.Interfaces;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Adventrya.Story.Tests;

/// <summary>
/// What a blob operation costs beyond itself.
///
/// Every operation used to build a fresh service client and confirm the container existed before
/// doing its own work — two round trips for one, a couple of hundred times per composite book. The
/// container is now created once per process and the client reused; these prove that through the
/// factory seam rather than against an account, because the property under test is how many
/// times the seam is asked, not what Azure says.
/// </summary>
public class BlobStorageCachingTests
{
    [Fact]
    public async Task A_container_is_created_once_however_many_operations_use_it()
    {
        var world = new FakeAccount();
        var service = world.Service("books");

        for (var i = 0; i < 25; i++)
        {
            await service.ExistsAsync($"blob-{i}", CancellationToken.None);
        }

        // A stored URL naming another container reaches that container, created once too.
        await service.DeleteByStoredUrlAsync(
            "https://account.blob.core.windows.net/other/x.png", CancellationToken.None);
        await service.DeleteByStoredUrlAsync(
            "https://account.blob.core.windows.net/other/y.png", CancellationToken.None);

        Assert.Equal(2, world.FactoryCalls);
        Assert.Equal(1, world.Containers["books"].Creations);
        Assert.Equal(1, world.Containers["other"].Creations);
        Assert.Equal(25, world.Containers["books"].ExistsCalls);
    }

    [Fact]
    public async Task A_cold_cache_hit_from_every_side_at_once_still_creates_the_container_once()
    {
        var world = new FakeAccount { SlowCreation = true };
        var service = world.Service("books");

        await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(i => service.ExistsAsync($"blob-{i}", CancellationToken.None)));

        Assert.Equal(1, world.FactoryCalls);
        Assert.Equal(1, world.Containers["books"].Creations);
    }

    [Fact]
    public async Task A_creation_that_failed_is_tried_again_rather_than_remembered()
    {
        // The cache must not turn one bad moment — the network down at first use — into a
        // process that can never store a blob again.
        var world = new FakeAccount { FailFirstCreation = true };
        var service = world.Service("books");

        await Assert.ThrowsAsync<RequestFailedException>(
            () => service.ExistsAsync("blob", CancellationToken.None));

        Assert.True(await service.ExistsAsync("blob", CancellationToken.None));

        // Two creations across the account: the forgotten one and the one that worked. Per
        // container it is one each, because the seam hands out a fresh client per attempt.
        Assert.Equal(2, world.TotalCreations);
        Assert.Equal(2, world.FactoryCalls);
    }

    [Fact]
    public void The_registration_makes_the_blob_service_one_instance_for_the_process()
    {
        // The cache is only worth anything if the instance outlives the request. Scoped, every
        // request and every job started cold.
        var root = TempRoot();
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment(root));
            services.Configure<LocalBlobOptions>(options =>
            {
                options.Enabled = true;
                options.RootPath = root;
            });
            services.Configure<AzureBlobOptions>(_ => { });
            services.AddAdventurePacksApplication();

            using var provider = services.BuildServiceProvider();
            using var first = provider.CreateScope();
            using var second = provider.CreateScope();

            Assert.Same(
                first.ServiceProvider.GetRequiredService<IBlobStorageService>(),
                second.ServiceProvider.GetRequiredService<IBlobStorageService>());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task The_local_folder_creates_a_directory_once_and_survives_it_being_removed()
    {
        var root = TempRoot();
        try
        {
            var service = new LocalFileBlobStorageService(
                Options.Create(new LocalBlobOptions { Enabled = true, RootPath = root }),
                Options.Create(new AzureBlobOptions { ContainerName = "books" }),
                new FakeHostEnvironment(root),
                NullLogger<LocalFileBlobStorageService>.Instance);

            var first = await service.UploadAsync("runs/one/a.png", [1], "image/png", CancellationToken.None);
            await service.UploadAsync("runs/one/b.png", [2], "image/png", CancellationToken.None);
            await service.UploadAsync("runs/one/c.png", [3], "image/png", CancellationToken.None);

            Assert.Equal("books/runs/one/a.png", first);
            Assert.True(await service.ExistsAsync("runs/one/c.png", CancellationToken.None));

            // A developer empties the storage folder under a running server. The folder cache
            // says it is there; the write must recover rather than fail.
            Directory.Delete(Path.Combine(root, "books"), recursive: true);

            await service.UploadAsync("runs/one/d.png", [4], "image/png", CancellationToken.None);
            Assert.Equal([4], await service.DownloadBytesFromStoredUrlAsync("books/runs/one/d.png", CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ---- harness ---------------------------------------------------------

    private static string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "adventrya-blob-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>The account, as the factory seam sees it: a container per name, counting.</summary>
    private sealed class FakeAccount
    {
        public Dictionary<string, FakeContainer> Containers { get; } = [];
        public int FactoryCalls { get; private set; }
        public bool SlowCreation { get; init; }
        public bool FailFirstCreation { get; init; }

        /// <summary>Creations across every container the seam handed out, not per instance.</summary>
        public int TotalCreations { get; set; }

        public AzureBlobStorageService Service(string containerName) =>
            new(Options.Create(new AzureBlobOptions { ContainerName = containerName }), name =>
            {
                FactoryCalls++;
                var container = new FakeContainer(this);
                Containers[name] = container;
                return container;
            });
    }

    private sealed class FakeContainer(FakeAccount account) : BlobContainerClient
    {
        public int Creations { get; private set; }
        public int ExistsCalls { get; private set; }

        public override Task<Response<BlobContainerInfo>> CreateIfNotExistsAsync(
            PublicAccessType publicAccessType = PublicAccessType.None,
            IDictionary<string, string>? metadata = null,
            BlobContainerEncryptionScopeOptions? encryptionScopeOptions = null,
            CancellationToken cancellationToken = default) =>
            CreateAsync();

        public override Task<Response<BlobContainerInfo>> CreateIfNotExistsAsync(
            PublicAccessType publicAccessType,
            IDictionary<string, string> metadata,
            CancellationToken cancellationToken) =>
            CreateAsync();

        private async Task<Response<BlobContainerInfo>> CreateAsync()
        {
            Creations++;
            account.TotalCreations++;

            if (account.SlowCreation)
            {
                // Wide enough a window for twenty callers to arrive on a cold cache.
                await Task.Delay(20);
            }

            // Counted on the account, because a forgotten creation is retried through a fresh
            // client from the seam — a per-instance count would fail every attempt.
            if (account.FailFirstCreation && account.TotalCreations == 1)
            {
                throw new RequestFailedException("the network was away");
            }

            return new ValueOnly<BlobContainerInfo>(null!);
        }

        public override BlobClient GetBlobClient(string blobName) => new FakeBlob(this);

        internal void CountExists() => ExistsCalls++;
    }

    private sealed class FakeBlob(FakeContainer container) : BlobClient
    {
        public override Task<Response<bool>> ExistsAsync(CancellationToken cancellationToken = default)
        {
            container.CountExists();
            return Task.FromResult<Response<bool>>(new ValueOnly<bool>(true));
        }

        public override Task<Response<bool>> DeleteIfExistsAsync(
            DeleteSnapshotsOption snapshotsOption = DeleteSnapshotsOption.None,
            BlobRequestConditions? conditions = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Response<bool>>(new ValueOnly<bool>(true));
    }

    /// <summary>A response that is only its value; the service never asks for the raw one.</summary>
    private sealed class ValueOnly<T>(T value) : Response<T>
    {
        public override T Value => value;

        public override Response GetRawResponse() => throw new NotSupportedException();
    }

    private sealed class FakeHostEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Adventrya.Story.Tests";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
