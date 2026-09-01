using System.Data;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Data;
using AdventurePacks.Api.Domain.Entities;
using AdventurePacks.Api.Extensions;
using AdventurePacks.Api.Repositories.Implementations;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Story;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Adventrya.Story.Tests;

/// <summary>
/// Proves the engine's slice of the container can actually be built.
///
/// Written after registering a service whose dependencies did not exist yet. The container
/// validates on startup in Development, so the API refused to boot and the build went red;
/// Production survived only because validation is off there by default and nothing happened to
/// resolve it. That difference between environments is precisely how a fault stays hidden until
/// it is expensive, and a compiler cannot catch it — the dependency is discovered at runtime.
///
/// This can.
/// </summary>
public class ServiceRegistrationTests
{
    [Fact]
    public void Every_registered_story_service_can_be_constructed()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        // Resolving each one is the assertion: a missing dependency throws here rather than at
        // startup in an environment where it is far more expensive to discover.
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IStoryModelClient>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IMasterStoryService>());
    }

    [Fact]
    public void The_container_validates_the_way_it_does_at_startup()
    {
        // ValidateOnBuild is what Development uses, and what caught the fault. Running it here
        // means the fault is caught in a test run rather than in a deployment.
        var exception = Record.Exception(() => BuildProvider(validateOnBuild: true));

        Assert.Null(exception);
    }

    /// <summary>
    /// The release policy, its alarms and the two rescues, resolved together.
    ///
    /// Written for one fault in particular, which the first draft of these services had: the policy
    /// service triggers a reconciliation and the reconciliation reads the policy, so a direct
    /// dependency in both directions is a container that cannot build either of them. A cycle is
    /// invisible until something resolves it — and in Production, where ValidateOnBuild is off, that
    /// is the first admin who changes a check.
    /// </summary>
    [Fact]
    public void The_release_policy_services_resolve_without_a_cycle()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ISqlConnectionFactory, ThrowingConnections>();
        services.AddScoped<IBlobStorageService, ThrowingBlobs>();
        services.AddScoped<IAdminNotifier, SilentNotifier>();
        services.AddScoped<IAdventurePackRepository, AdventurePackRepository>();
        services.AddScoped<BekiReleaseGates>();

        services.AddScoped<IBekiReleasePolicyRepository, BekiReleasePolicyRepository>();
        services.AddScoped<IBekiAlarmRepository, BekiAlarmRepository>();
        services.AddScoped<IBekiAlarmService, BekiAlarmService>();
        services.AddScoped<BekiReleaseReconciliation>();
        services.AddScoped<IBekiReleaseReconciliation>(provider =>
            provider.GetRequiredService<BekiReleaseReconciliation>());
        services.AddScoped<IBekiDownloadStatusService>(provider =>
            provider.GetRequiredService<BekiReleaseReconciliation>());
        services.AddScoped<IBekiReleasePolicyService, BekiReleasePolicyService>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IBekiReleasePolicyService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IBekiReleaseReconciliation>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IBekiDownloadStatusService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IBekiAlarmService>());

        // The download status and the reconciliation are one object with two jobs, and the
        // registration is what says so — two instances would be two readings of one stored verdict.
        Assert.Same(
            scope.ServiceProvider.GetRequiredService<IBekiReleaseReconciliation>(),
            scope.ServiceProvider.GetRequiredService<IBekiDownloadStatusService>());
    }

    /// <summary>Resolution is the subject here; nothing in this test is allowed to talk to anything.</summary>
    private sealed class ThrowingConnections : ISqlConnectionFactory
    {
        public IDbConnection CreateConnection() => throw new NotSupportedException();
    }

    private sealed class ThrowingBlobs : IBlobStorageService
    {
        public Task<string> UploadAsync(string blobName, byte[] bytes, string contentType, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Stream> DownloadAsync(string blobName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string blobName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<byte[]> DownloadBytesFromStoredUrlAsync(string storedUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteByStoredUrlAsync(string storedUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class SilentNotifier : IAdminNotifier
    {
        public Task OrderPaidAsync(Order order, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task BookFailedAsync(Guid packId, string reason, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PrintOrderPlacedAsync(PrintOrder printOrder, string? bookTitle, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static ServiceProvider BuildProvider(bool validateOnBuild = false)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenAI:ApiKey"] = "test-key",
                ["OpenAI:BaseUrl"] = "https://api.openai.com/v1"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();
        services.AddSingleton<IConfiguration>(configuration);
        services.Configure<OpenAiOptions>(configuration.GetSection(OpenAiOptions.SectionName));
        services.AddStoryEngine();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = validateOnBuild,
            ValidateScopes = true
        });
    }
}
