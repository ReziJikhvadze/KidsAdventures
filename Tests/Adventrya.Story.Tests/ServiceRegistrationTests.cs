using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Extensions;
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
