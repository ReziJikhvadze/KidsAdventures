using AdventurePacks.Api.DTOs.AdventurePacks;

namespace AdventurePacks.Api.Services.Interfaces;

public interface IAdventureGenerationService
{
    Task<Guid> QueueGenerationAsync(Guid userId, GenerateAdventurePackRequest request, CancellationToken cancellationToken);
    Task ProcessGenerationAsync(Guid adventurePackId, CancellationToken cancellationToken);
}
