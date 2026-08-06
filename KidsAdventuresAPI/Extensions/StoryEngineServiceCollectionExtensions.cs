using AdventurePacks.Api.Services.Story;

namespace AdventurePacks.Api.Extensions;

/// <summary>
/// Registration for the story engine, kept apart from the rest of the container so a test can
/// validate exactly this slice without standing up a database, Hangfire and everything else the
/// application needs to boot.
///
/// It is two services. It used to be a planner, a validator, a pipeline and twenty-four rules,
/// none of which any running code ever called — that architecture was designed, built, and then
/// superseded by the single master call before it was ever wired in. Keeping it made the project
/// look like something it was not, to the point where a review of the codebase critiqued the
/// pipeline as though books were being written by it.
/// </summary>
public static class StoryEngineServiceCollectionExtensions
{
    /// <summary>
    /// Takes no configuration: everything either service needs comes from OpenAiOptions, which
    /// the application already binds.
    /// </summary>
    public static IServiceCollection AddStoryEngine(this IServiceCollection services)
    {
        // The only door to a model, and the one call that writes a book.
        services.AddScoped<IStoryModelClient, StoryModelClient>();
        services.AddScoped<IMasterStoryService, MasterStoryService>();

        // IMasterBookService is registered with the application services instead. It reaches
        // outside the engine — blob storage, the image model, Hangfire — and this slice is kept
        // resolvable on its own so a test can validate it without any of that.

        return services;
    }
}
