using AdventurePacks.Api.Repositories.Implementations;
using AdventurePacks.Api.Repositories.Interfaces;

namespace AdventurePacks.Api.Extensions;

/// <summary>
/// What the operations console needs on top of the application's own registrations.
///
/// Its own extension rather than four more lines in <c>AddAdventurePacksApplication</c>, because
/// the console is a separable concern: everything registered here is read by an admin screen and by
/// nothing a parent can reach. Kept apart, the answer to "what does the console cost the container"
/// is one file rather than a search.
/// </summary>
public static class AdminOpsServiceCollectionExtensions
{
    public static IServiceCollection AddAdminOpsServices(this IServiceCollection services)
    {
        // Scoped, like every other Dapper repository: it holds nothing between calls and shares the
        // request's connection scope.
        services.AddScoped<IAdminOverviewRepository, AdminOverviewRepository>();

        return services;
    }
}
