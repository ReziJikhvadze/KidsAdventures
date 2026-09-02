using AdventurePacks.Api.Services.Story;

namespace AdventurePacks.Api.Extensions;

/// <summary>
/// The services only the operations console needs.
///
/// Its own extension rather than another block in <c>AddAdventurePacksApplication</c>, and the
/// reason is the file's length rather than taste: the application registration is already three
/// hundred lines that every campaign appends to, and admin-only machinery is the one slice with a
/// clean boundary — nothing a parent's request touches resolves any of it.
///
/// It is also what makes the boundary checkable. A service registered here and injected into a
/// parent-facing controller is a compile-time fact somebody can look for; the same service in the
/// general pile is not.
/// </summary>
public static class AdminServiceCollectionExtensions
{
    public static IServiceCollection AddAdminServices(this IServiceCollection services)
    {
        // Scoped, like everything it depends on: it reads a pack, writes its status and enqueues a
        // job, all inside one request. A singleton here would capture a scoped repository.
        services.AddScoped<IBekiRegeneration, BekiRegeneration>();

        return services;
    }
}
