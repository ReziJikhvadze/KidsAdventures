using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace AdventurePacks.Api.Services.Story.Composite;

/// <summary>Shared by the preview and fulfilment planning paths; never logs photos or secrets.</summary>
internal static class CompositeStoryProvenance
{
    public static async Task<MasterStoryResult> WriteAsync(
        IMasterStoryService service, ILogger logger, string jobId, CompositeStoryInput input,
        IReadOnlyList<string> problems, int attempt, CancellationToken cancellationToken)
    {
        using var config = CompositeAssets.Read(CompositeAssets.PipelineConfigPath);
        var story = config.RootElement.GetProperty("story");
        var sourceHash = story.GetProperty("prompt_source_sha256").GetString();
        var schemaHash = story.GetProperty("schema_source_sha256").GetString();
        var started = DateTimeOffset.UtcNow;
        var clock = Stopwatch.StartNew();
        var outcome = "failed";
        var model = service.ModelName;
        string? requestHash = null;
        try
        {
            var result = await service.WriteCompositePlanAsync(input, problems, cancellationToken);
            model = result.Model;
            requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                result.SystemPrompt + "\n---\n" + result.UserPrompt))).ToLowerInvariant();
            outcome = CompositePlanRules.Problems(result.Story, input.SpreadCount, input.AgeBand).Count == 0
                ? "structurally-valid-pending-final-name-and-plan-gate" : "requires-correction";
            return result;
        }
        finally
        {
            logger.LogInformation(
                "Composite planning provenance {JobId}: provider={Provider} providerModel={ProviderModel} "
                + "promptVersion={PromptVersion} frozenPromptSourceSha256={PromptSourceSha256} "
                + "requestSha256={RequestSha256} schemaVersion={SchemaVersion} schemaSourceSha256={SchemaSourceSha256} "
                + "startedAtUtc={StartedAtUtc:o} completedAtUtc={CompletedAtUtc:o} durationMs={DurationMs} "
                + "retry={Retry} outcome={Outcome}",
                jobId, service.ProviderName, model, MasterStoryPromptComposite.Version, sourceHash, requestHash,
                CompositeStorySchema.Version, schemaHash, started, DateTimeOffset.UtcNow,
                clock.ElapsedMilliseconds, attempt, outcome);
        }
    }
}
