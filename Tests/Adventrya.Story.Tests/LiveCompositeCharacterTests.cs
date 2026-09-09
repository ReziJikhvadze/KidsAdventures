using System.Text.Json.Nodes;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Services;
using AdventurePacks.Api.Services.Implementations;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>A two-image visual proof using the illustrated fixture child, never customer photos.
/// Opt in explicitly: this spends money. It exercises the real image provider and compositor;
/// visual identity remains a human inspection, not a claim made by a byte assertion.</summary>
public class LiveCompositeCharacterTests : CompositePipelineTestBase
{
    [SkippableFact]
    public async Task Draw_two_scenes_with_the_same_child_creature_and_exact_Beki()
    {
        var key = Environment.GetEnvironmentVariable("ADVENTRYA_CHARACTER_PROOF_KEY");
        Skip.If(string.IsNullOrWhiteSpace(key), "Set ADVENTRYA_CHARACTER_PROOF_KEY for the paid visual proof.");
        var directory = Environment.GetEnvironmentVariable("ADVENTRYA_CHARACTER_PROOF_OUTPUT")
            ?? Path.Combine(Path.GetTempPath(), "beki-character-proof");
        Directory.CreateDirectory(directory);

        // Reuse the first two Bafu scenes in a sample so both pages actually share a creature.
        var scenario = JsonNode.Parse(ScenarioFixture())!;
        scenario["spreads"]![0]!["child_world_scene"] = scenario["spreads"]![1]!["child_world_scene"]!.GetValue<string>();
        scenario["spreads"]![1]!["child_world_scene"] = scenario["spreads"]![2]!["child_world_scene"]!.GetValue<string>();
        var json = scenario.ToJsonString();
        var child = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "nina_dinosaurs", "spread_01_child_world_base.png"));
        var identity = IdentityFixture with { HairStyle = "short wavy hair", DistinctiveFeatures = "soft toddler cheeks" };
        var services = new ServiceCollection();
        services.AddHttpClient("OpenAI", client =>
        {
            client.BaseAddress = new Uri("https://api.openai.com/v1/");
            client.Timeout = TimeSpan.FromMinutes(6);
        });
        using var provider = services.BuildServiceProvider();
        var images = new OpenAiService(provider.GetRequiredService<IHttpClientFactory>(),
            new ReferenceImageNormalizer(NullLogger<ReferenceImageNormalizer>.Instance),
            Options.Create(new OpenAiOptions { ApiKey = key!, EnableStoryImages = true, LogPrompts = false }),
            NullLogger<OpenAiService>.Instance);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var result = await Pipeline(new ScriptedStoryModelClient(json), images, testingFlow: true)
            .RunAsync(Request(context: Context() with { ReleasePolicy = BekiReleasePolicySnapshot.Defaults },
                resume: new CompositeResumeState(json, new Dictionary<int, byte[]>(), new Dictionary<int, byte[]>())
                { IdentitySpecJson = CompositeChildIdentity.ToStoredJson(identity) }) with
            {
                ChildPhoto = child,
            }, timeout.Token);

        Assert.Equal(2, result.Spreads.Count);
        foreach (var spread in result.Spreads)
        {
            await File.WriteAllBytesAsync(Path.Combine(directory, $"spread-{spread.Page:00}.png"), spread.CompositePng);
            await File.WriteAllTextAsync(Path.Combine(directory, $"spread-{spread.Page:00}-prompt.txt"), spread.Prompt);
            await File.WriteAllTextAsync(Path.Combine(directory, $"spread-{spread.Page:00}-composition.json"), spread.Manifest!.ToJson());
            Assert.False(spread.Manifest.BekiLayer.Redrawn);
            Assert.Equal(0, spread.Attempts.Sum(attempt => attempt.ReviewMs));
        }
        Assert.Contains("continuity reference", result.Spreads[1].Prompt);
    }
}
