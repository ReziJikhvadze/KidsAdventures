using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Services.Ai;
using AdventurePacks.Api.Services.Story;
using Microsoft.Extensions.Options;

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
    /// Takes no configuration: everything either service needs comes from the options the
    /// application already binds — OpenAiOptions, and now the two that decide which vendor
    /// answers.
    /// </summary>
    public static IServiceCollection AddStoryEngine(this IServiceCollection services)
    {
        // The only door to a model, and the one call that writes a book. Which vendor stands
        // behind that door is read at resolution rather than fixed here, so a slice test that
        // binds nothing still gets the OpenAI client the options default to.
        services.AddScoped<IStoryModelClient>(sp =>
            sp.GetRequiredService<IOptions<AiProviderOptions>>().Value.UsesGeminiForStory
                ? ActivatorUtilities.CreateInstance<GeminiStoryModelClient>(sp)
                : ActivatorUtilities.CreateInstance<StoryModelClient>(sp));

        // The editor, resolved separately from the writer. Same two implementations, a different
        // switch — and a model name chosen here rather than inside the service, because which
        // setting names the model is a fact about the vendor, and the vendor is decided here.
        services.AddScoped(sp =>
        {
            var providers = sp.GetRequiredService<IOptions<AiProviderOptions>>().Value;

            if (providers.UsesGeminiForStoryPolish)
            {
                var gemini = sp.GetRequiredService<IOptions<GeminiOptions>>().Value;
                return new StoryPolishClient(
                    ActivatorUtilities.CreateInstance<GeminiStoryModelClient>(sp),
                    gemini.StoryModel);
            }

            var openAi = sp.GetRequiredService<IOptions<OpenAiOptions>>().Value;
            return new StoryPolishClient(
                ActivatorUtilities.CreateInstance<StoryModelClient>(sp),
                string.IsNullOrWhiteSpace(openAi.MasterStoryModel) ? openAi.Model : openAi.MasterStoryModel);
        });

        services.AddScoped<IMasterStoryService, MasterStoryService>();

        // IMasterBookService is registered with the application services instead. It reaches
        // outside the engine — blob storage, the image model, Hangfire — and this slice is kept
        // resolvable on its own so a test can validate it without any of that.

        return services;
    }
}
