using System.Text.Json;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Models;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;
using Hangfire;

namespace AdventurePacks.Api.Services.Implementations;

public sealed class AdventureGenerationService(
    IBackgroundJobClient backgroundJobClient,
    IAdventurePackRepository adventurePackRepository,
    IChildRepository childRepository,
    IFamilyMemberRepository familyMemberRepository,
    ISubscriptionService subscriptionService,
    IOpenAiService openAiService,
    IAdventurePdfService adventurePdfService,
    IBlobStorageService blobStorageService,
    ILogger<AdventureGenerationService> logger) : IAdventureGenerationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public async Task<Guid> QueueGenerationAsync(
        Guid userId,
        GenerateAdventurePackRequest request,
        CancellationToken cancellationToken)
    {
        _ = await childRepository.GetByIdAsync(request.ChildId, userId, cancellationToken)
            ?? throw new InvalidOperationException("Child not found.");

        await subscriptionService.EnsureGenerationAllowedAsync(userId, cancellationToken);

        var pack = new AdventurePack
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ChildId = request.ChildId,
            Theme = request.Theme,
            Status = AdventurePackStatus.Pending,
            OptionalStoryNotes = string.IsNullOrWhiteSpace(request.OptionalStoryNotes)
                ? null
                : request.OptionalStoryNotes.Trim(),
            StoryLanguage = NormalizeLanguage(request.StoryLanguage),
            ProgressMessage = "Queued — your adventure will appear in My Packs when ready (usually 3–8 minutes).",
            CreatedAt = DateTime.UtcNow
        };

        await adventurePackRepository.CreatePendingAsync(pack, cancellationToken);
        backgroundJobClient.Enqueue<IAdventureGenerationService>(service =>
            service.ProcessGenerationAsync(pack.Id, CancellationToken.None));

        return pack.Id;
    }

    public async Task ProcessGenerationAsync(Guid packId, CancellationToken cancellationToken)
    {
        var pack = await adventurePackRepository.GetByIdNoOwnershipAsync(packId, cancellationToken);
        if (pack is null)
        {
            return;
        }

        try
        {
            await adventurePackRepository.UpdateStatusAsync(
                packId,
                AdventurePackStatus.Generating,
                null,
                null,
                null,
                cancellationToken);

            await SetProgressAsync(
                packId,
                "Starting… You can leave this page — we will save your pack in My Packs.",
                cancellationToken);

            var child = await childRepository.GetByIdAsync(pack.ChildId, pack.UserId, cancellationToken)
                ?? throw new InvalidOperationException("Child not found.");

            var familyMembers = await familyMemberRepository.GetByChildIdAsync(pack.ChildId, pack.UserId, cancellationToken);

            var childAppearance = await ResolveChildAppearanceAsync(child, cancellationToken);

            await SetProgressAsync(
                packId,
                "Reading family photos (if any)… ~15%",
                cancellationToken);

            var cast = new List<FamilyMemberCastEntry>();
            foreach (var member in familyMembers)
            {
                var appearance = await ResolveFamilyAppearanceAsync(member, cancellationToken);
                cast.Add(new FamilyMemberCastEntry
                {
                    Name = member.Name,
                    Relationship = member.Relationship,
                    PhotoUrl = member.PhotoUrl,
                    AppearanceDescription = appearance
                });
            }

            var input = new AdventureGenerationInput
            {
                ChildName = child.Name,
                Age = child.Age,
                Theme = pack.Theme,
                ChildAppearanceDescription = childAppearance,
                FamilyMembers = cast,
                OptionalStoryNotes = pack.OptionalStoryNotes,
                StoryLanguage = pack.StoryLanguage ?? "en"
            };

            await SetProgressAsync(
                packId,
                "Writing your unique story… ~30% (this can take 1–2 minutes)",
                cancellationToken);

            var content = await openAiService.GenerateAdventureContentAsync(input, pack.Id, cancellationToken);

            await SetProgressAsync(
                packId,
                "Painting story illustrations… ~50% (about 1 minute per picture)",
                cancellationToken);

            for (var i = 0; i < content.StoryPages.Count; i++)
            {
                var page = content.StoryPages[i];
                var imagePrompt = AdventurePromptBuilder.BuildStoryImagePrompt(input, page, i, pack.Id);
                var imageBytes = await openAiService.GenerateStoryImageAsync(imagePrompt, cancellationToken);
                page.ImageBytes = imageBytes;

                var pct = 50 + (int)Math.Round(40.0 * (i + 1) / Math.Max(1, content.StoryPages.Count));
                await SetProgressAsync(
                    packId,
                    $"Illustration {i + 1} of {content.StoryPages.Count} done… ~{pct}%",
                    cancellationToken);
            }

            await SetProgressAsync(packId, "Building your colorful PDF… ~92%", cancellationToken);

            var pdfBytes = adventurePdfService.GeneratePdf(content, pack.Theme.ToString());
            var blobName = $"{pack.UserId}/{pack.Id}.pdf";
            var pdfUrl = await blobStorageService.UploadAsync(blobName, pdfBytes, "application/pdf", cancellationToken);

            var generatedJson = JsonSerializer.Serialize(content, JsonOptions);
            await adventurePackRepository.UpdateStatusAsync(
                packId,
                AdventurePackStatus.Completed,
                generatedJson,
                pdfUrl,
                null,
                cancellationToken);

            await SetProgressAsync(
                packId,
                "Done! Open My Packs to download your PDF.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Adventure generation failed for pack {PackId}", packId);
            await adventurePackRepository.UpdateStatusAsync(
                packId,
                AdventurePackStatus.Failed,
                null,
                null,
                ex.Message,
                cancellationToken);
            await SetProgressAsync(
                packId,
                "Something went wrong. Try again or pick a simpler theme.",
                cancellationToken);
        }
    }

    private async Task SetProgressAsync(Guid packId, string message, CancellationToken cancellationToken)
    {
        await adventurePackRepository.UpdateProgressMessageAsync(packId, message, cancellationToken);
    }

    private async Task<string?> ResolveChildAppearanceAsync(Child child, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(child.PhotoUrl))
        {
            return null;
        }

        try
        {
            var bytes = await blobStorageService.DownloadBytesFromStoredUrlAsync(child.PhotoUrl, cancellationToken);
            return await openAiService.DescribeCharacterFromPhotoAsync(
                bytes,
                "image/jpeg",
                $"This photo is the main hero of a children's adventure book named {child.Name}, age {child.Age}.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not describe child photo for {ChildId}", child.Id);
            return null;
        }
    }

    private async Task<string?> ResolveFamilyAppearanceAsync(FamilyMember member, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(member.PhotoUrl))
        {
            return null;
        }

        try
        {
            var bytes = await blobStorageService.DownloadBytesFromStoredUrlAsync(member.PhotoUrl, cancellationToken);
            return await openAiService.DescribeCharacterFromPhotoAsync(
                bytes,
                "image/jpeg",
                $"Supporting character {member.Name} ({member.Relationship}) in a children's book.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not describe family photo for {MemberId}", member.Id);
            return null;
        }
    }

    private static string NormalizeLanguage(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return "en";
        }

        var c = code.Trim().ToLowerInvariant();
        return c is "ka" or "es" or "en" or "fr" or "de" ? c : "en";
    }
}
