using AdventurePacks.Api.DTOs.AdventurePacks;

namespace AdventurePacks.Api.Services.Interfaces;

public interface IAdventurePdfService
{
    byte[] GeneratePdf(AdventureContentDto content, string themeName);
}
