using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Validation;

namespace AdventurePacks.Api.Extensions;

/// <summary>
/// Registration for story engine v2, kept apart from the rest of the container.
///
/// Separate for a reason worth stating: the engine is being built stage by stage, so at any
/// moment some of its pieces exist and some do not. Keeping the registrations here means the
/// half-built state is visible in one file, and a test can validate exactly this slice of the
/// container without standing up a database, Hangfire and everything else the application
/// needs to boot.
/// </summary>
public static class StoryEngineServiceCollectionExtensions
{
    public static IServiceCollection AddStoryEngine(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<StoryModelOptions>(
            configuration.GetSection(StoryModelOptions.SectionName));

        services.AddSingleton(sp =>
            sp.GetRequiredService<IConfiguration>()
                .GetSection($"{StoryModelOptions.SectionName}:Pipeline")
                .Get<StoryPipelineOptions>() ?? new StoryPipelineOptions());

        services.AddScoped<IStoryValidator, StoryValidator>();
        services.AddScoped<IStoryModelClient, StoryModelClient>();
        services.AddScoped<IStoryPlanner, StoryPlanner>();

        // IStoryPipeline is deliberately absent until IStoryWriter and ICraftReviewer exist.
        //
        // Registering it early cost a red build: the container validates on startup in
        // Development, found a dependency that had not been written yet, and the API refused to
        // boot. Production survived only because validation is off there by default and nothing
        // resolved it — which is luck rather than safety, and exactly the kind of difference
        // between environments that hides a fault until it is expensive.
        //
        // Add it here, in one line, when the writer and the reviewer land.

        return services;
    }
}
