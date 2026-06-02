using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.DTOs.AdventurePacks;

namespace AdventurePacks.Api.Services.Interfaces;

public interface IOpenAiService
{
    Task<AdventureContentDto> GenerateAdventureContentAsync(AdventureGenerationInput input, CancellationToken cancellationToken);
}
