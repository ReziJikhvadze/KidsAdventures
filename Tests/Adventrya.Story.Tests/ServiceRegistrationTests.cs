using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Extensions;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Validation;
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
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IStoryValidator>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IStoryModelClient>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IStoryPlanner>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<StoryPipelineOptions>());
    }

    [Fact]
    public void The_container_validates_the_way_it_does_at_startup()
    {
        // ValidateOnBuild is what Development uses, and what caught the fault. Running it here
        // means the fault is caught in a test run rather than in a deployment.
        var exception = Record.Exception(() => BuildProvider(validateOnBuild: true));

        Assert.Null(exception);
    }

    [Fact]
    public void The_pipeline_is_only_registered_once_it_can_be_built()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var pipeline = scope.ServiceProvider.GetService<IStoryPipeline>();
        var writer = scope.ServiceProvider.GetService<IStoryWriter>();
        var reviewer = scope.ServiceProvider.GetService<ICraftReviewer>();

        // While the writer and reviewer are unwritten the pipeline must stay unregistered.
        // Once they exist this flips, and the assertion above it is what keeps them honest:
        // a pipeline registered without them fails The_container_validates.
        if (writer is null || reviewer is null)
        {
            Assert.Null(pipeline);
        }
        else
        {
            Assert.NotNull(pipeline);
        }
    }

    private static ServiceProvider BuildProvider(bool validateOnBuild = false)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenAI:ApiKey"] = "test-key",
                ["OpenAI:BaseUrl"] = "https://api.openai.com/v1",
                ["StoryEngine:PlannerModel"] = "test-model"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();
        services.AddSingleton<IConfiguration>(configuration);
        services.Configure<OpenAiOptions>(configuration.GetSection(OpenAiOptions.SectionName));
        services.AddStoryEngine(configuration);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = validateOnBuild,
            ValidateScopes = true
        });
    }
}
