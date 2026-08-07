using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Prompts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Adventrya.Story.Tests;

/// <summary>
/// Pins the setting to the prompt it selects.
///
/// This has gone wrong twice, and both times silently: "2" was compared against "v2" and the run
/// carried on with v1, and later a value written into the wrong settings file left v1 in force
/// while everyone believed v4 was running. A book still comes out either way, which is what makes
/// it expensive — the mistake is only visible if you read the prompt in the log.
/// </summary>
public class PromptVersionTests
{
    [Theory]
    [InlineData("1", "v1")]
    [InlineData("v1", "v1")]
    [InlineData("2", "v2")]
    [InlineData("v2", "v2")]
    [InlineData("3", "v3")]
    [InlineData("v3", "v3")]
    [InlineData("4", "v4")]
    [InlineData("v4", "v4")]
    [InlineData("V4", "v4")]
    [InlineData(" 4 ", "v4")]
    [InlineData("", "v1")]
    [InlineData("nonsense", "v1")]
    public void The_setting_selects_a_version(string configured, string expected) =>
        Assert.Equal(expected, Build(configured).PromptVersion);

    /// <summary>
    /// Resolving the name is only half of it — the version has to reach a different prompt. Each
    /// variant opens with its own first line, so the opening is enough to tell them apart.
    /// </summary>
    [Theory]
    [InlineData("1", "შენ ხარ საბავშვო მწერალი")]
    [InlineData("2", "You are a children's book architect")]
    [InlineData("3", "You are a master children's author. please remember")]
    [InlineData("4", "You are a children's author. Write a 8-scene picture book.")]
    public void Each_version_builds_its_own_prompt(string configured, string opening)
    {
        var (system, _) = Build(configured).BuildPrompts(Input);

        Assert.StartsWith(opening, system.TrimStart());
    }

    /// <summary>V4 carries the inputs and nothing that has to be looked up.</summary>
    [Fact]
    public void V4_passes_every_input_through()
    {
        var (_, user) = Build("4").BuildPrompts(Input with { ExtraWishes = "უყვარს მელიები" });

        Assert.Contains("თამარი", user);
        Assert.Contains("- ასაკი: 3", user);
        Assert.Contains("დაკარგული ხეობა", user);
        Assert.Contains("green", user);
        Assert.Contains("- სცენების რაოდენობა: 8", user);
        Assert.Contains("უყვარს მელიები", user);
    }

    private static MasterStoryInput Input => new()
    {
        ChildName = "თამარი",
        Age = 3,
        Gender = "girl",
        Theme = ThemeType.Dinosaurs,
        EyeColor = "green",
        SpreadCount = BookFormat.SpreadCount,
        Language = "ka"
    };

    private static MasterStoryService Build(string configured) => new(
        new UnusedClient(),
        Options.Create(new OpenAiOptions { StoryPromptVersion = configured }),
        NullLogger<MasterStoryService>.Instance);

    /// <summary>Building a prompt makes no call; anything reaching here is the test's own bug.</summary>
    private sealed class UnusedClient : IStoryModelClient
    {
        public Task<ModelResult<T>> CompleteAsync<T>(
            string model, string systemPrompt, string userPrompt, string schemaName,
            JsonElement schema, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("BuildPrompts must not call the model.");
    }
}
