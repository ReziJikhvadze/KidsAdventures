using System.Text.Json;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
using Hangfire;

namespace AdventurePacks.Api.Services.Implementations;

public sealed class AdventureGenerationService(
    IBackgroundJobClient backgroundJobClient,
    IChildRepository childRepository,
    IFamilyMemberRepository familyMemberRepository,
    IAdventurePackRepository adventurePackRepository,
    ISubscriptionService subscriptionService,
    IOpenAiService openAiService,
    IAdventurePdfService pdfService,
    IBlobStorageService blobStorageService) : IAdventureGenerationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public async Task<Guid> QueueGenerationAsync(Guid userId, GenerateAdventurePackRequest request, CancellationToken cancellationToken)
    {
        var child = await childRepository.GetByIdAsync(request.ChildId, userId, cancellationToken)
                    ?? throw new InvalidOperationException("Child not found.");

        await subscriptionService.EnsureGenerationAllowedAsync(userId, cancellationToken);

        var packId = await adventurePackRepository.CreatePendingAsync(new AdventurePack
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ChildId = request.ChildId,
            Theme = request.Theme,
            Status = AdventurePackStatus.Pending,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);

        backgroundJobClient.Enqueue<IAdventureGenerationService>(x => x.ProcessGenerationAsync(packId, CancellationToken.None));
        return packId;
    }

    public async Task ProcessGenerationAsync(Guid adventurePackId, CancellationToken cancellationToken)
    {
        var pack = await adventurePackRepository.GetByIdNoOwnershipAsync(adventurePackId, cancellationToken)
                   ?? throw new InvalidOperationException("Adventure pack not found.");

        await adventurePackRepository.UpdateStatusAsync(pack.Id, AdventurePackStatus.Generating, null, null, null, cancellationToken);

        try
        {
            var child = await childRepository.GetByIdAsync(pack.ChildId, pack.UserId, cancellationToken)
                        ?? throw new InvalidOperationException("Child not found.");
            var familyMembers = await familyMemberRepository.GetByChildIdAsync(pack.ChildId, pack.UserId, cancellationToken);

            var aiContent = await openAiService.GenerateAdventureContentAsync(new AdventureGenerationInput
            {
                ChildName = child.Name,
                Age = child.Age,
                Theme = pack.Theme,
                FamilyMembers = familyMembers.Select(x => $"{x.Name} ({x.Relationship})").ToList()
            }, cancellationToken);

            var pdfBytes = pdfService.GeneratePdf(aiContent, pack.Theme.ToString());
            var blobName = $"{pack.UserId}/{pack.Id}.pdf";
            var pdfUrl = await blobStorageService.UploadAsync(blobName, pdfBytes, "application/pdf", cancellationToken);

            var generatedJson = JsonSerializer.Serialize(aiContent, JsonOptions);
            await adventurePackRepository.UpdateStatusAsync(pack.Id, AdventurePackStatus.Completed, generatedJson, pdfUrl, null, cancellationToken);
        }
        catch (Exception ex)
        {
            await adventurePackRepository.UpdateStatusAsync(pack.Id, AdventurePackStatus.Failed, null, null, ex.Message, cancellationToken);
            throw;
        }
    }
}
