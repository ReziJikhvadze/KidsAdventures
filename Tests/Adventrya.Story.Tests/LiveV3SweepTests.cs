using System.Diagnostics;
using System.Text;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Prompts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Adventrya.Story.Tests;

/// <summary>
/// Writes one book per chain for a single world, so the three plots V3 can produce there can be
/// read side by side rather than argued about.
///
/// One world rather than all six: three books answers whether the chains produce different books,
/// and thirty-six paid calls to confirm it across every world is a bill to pay after the question
/// is settled, not before. Set ADVENTRYA_SWEEP_THEME to choose; Dinosaurs by default.
///
/// Skipped unless ADVENTRYA_OPENAI_KEY is set.
///
///   dotnet test --filter LiveV3Sweep -v normal
/// </summary>
public class LiveV3SweepTests(ITestOutputHelper output)
{
    private static string? ApiKey => Environment.GetEnvironmentVariable("ADVENTRYA_OPENAI_KEY");

    private static readonly string OutputDirectory =
        Environment.GetEnvironmentVariable("ADVENTRYA_SWEEP_DIR") ?? Path.GetTempPath();

    [SkippableFact]
    public async Task Write_one_book_for_every_branch()
    {
        Skip.If(string.IsNullOrWhiteSpace(ApiKey), "Set ADVENTRYA_OPENAI_KEY to run this.");

        // One world, all three of its chains — enough to answer whether the chains produce
        // different books, without paying for thirty-six calls to find out.
        var themes = new[] { ThemeFor(Environment.GetEnvironmentVariable("ADVENTRYA_SWEEP_THEME")) };

        var service = BuildService();
        var written = 0;

        foreach (var theme in themes)
        {
            var branches = StoryBranches.All(theme);

            for (var i = 0; i < branches.Count; i++)
            {
                var branch = branches[i];
                var input = InputFor(theme);
                var started = Stopwatch.StartNew();

                try
                {
                    var result = await service.WriteAsync(input, branch, CancellationToken.None);
                    var path = Path.Combine(
                        OutputDirectory,
                        $"{(int)theme:00}-{theme}-{i + 1}-{Safe(branch.Name)}.md");

                    await File.WriteAllTextAsync(path, Render(result, theme, branch, started.Elapsed));
                    written++;

                    output.WriteLine(
                        $"{theme,-11} {i + 1}/3  {branch.Name,-28} {started.Elapsed.TotalSeconds,5:0}s  " +
                        $"{result.CompletionTokens,6} tok  → {Path.GetFileName(path)}");
                }
                catch (Exception ex)
                {
                    output.WriteLine($"{theme,-11} {i + 1}/3  {branch.Name,-28} FAILED: {ex.Message}");
                }
            }
        }

        output.WriteLine($"\n{written}/18 written to {OutputDirectory}");
        Assert.True(written > 0, "No book was written.");
    }

    private static ThemeType ThemeFor(string? name) =>
        Enum.TryParse<ThemeType>(name, ignoreCase: true, out var theme) ? theme : ThemeType.Dinosaurs;

    private static MasterStoryInput InputFor(ThemeType theme) => new()
    {
        ChildName = "თამარი",
        Age = 3,
        Gender = "girl",
        Theme = theme,
        EyeColor = "green",
        // No photograph: the character lock has to be invented, which is the harder case.
        AppearanceDescription = null,
        SpreadCount = BookFormat.SpreadCount,
        Language = "ka"
    };

    private static V3Runner BuildService() =>
        new(new StoryModelClient(
                new SingleClientFactory(),
                Options.Create(new OpenAiOptions { ApiKey = ApiKey!, BaseUrl = "https://api.openai.com/v1" }),
                NullLogger<StoryModelClient>.Instance),
            "gpt-5.6-luna");

    private static string Safe(string name) =>
        string.Concat(name.Split(Path.GetInvalidFileNameChars())).Replace(' ', '-');

    private static string Render(
        MasterStoryResult result, ThemeType theme, StoryBranches.Branch branch, TimeSpan elapsed)
    {
        var story = result.Story;
        var text = new StringBuilder();

        text.AppendLine($"# {story.Concept.Title}");
        text.AppendLine();
        text.AppendLine($"*{theme} · „{branch.Name}“ · თამარი, 3 წლის · {elapsed.TotalSeconds:0}s · "
                        + $"{result.PromptTokens}+{result.CompletionTokens} tokens*");
        text.AppendLine();

        text.AppendLine("## ჯაჭვი");
        foreach (var (step, i) in branch.Chain.Select((s, i) => (s, i)))
        {
            text.AppendLine($"{i + 1}. {step}");
        }

        text.AppendLine();
        text.AppendLine("## ისტორია");

        foreach (var spread in story.Spreads.OrderBy(s => s.Number))
        {
            text.AppendLine();
            text.AppendLine($"### {spread.Number}. {spread.Title}");
            text.AppendLine($"*{spread.Caption}*");
            text.AppendLine();
            text.AppendLine(spread.Text);
        }

        text.AppendLine();
        text.AppendLine("## characterLock");
        text.AppendLine();
        text.AppendLine(story.CharacterLock);

        return text.ToString();
    }

    /// <summary>The model client wants a factory; one plain client is all this needs.</summary>
    private sealed class SingleClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    /// <summary>
    /// Runs V3's two calls directly, with the branch chosen by the caller rather than at random,
    /// so every chain is covered exactly once.
    /// </summary>
    private sealed class V3Runner(IStoryModelClient client, string model)
    {
        public async Task<MasterStoryResult> WriteAsync(
            MasterStoryInput input, StoryBranches.Branch branch, CancellationToken cancellationToken)
        {
            var plannerSystem = MasterStoryPromptV3.PlannerSystem(input, branch);
            var plannerUser = MasterStoryPromptV3.PlannerUser(input);

            var planned = await client.CompleteAsync<StoryPlan>(
                model, plannerSystem, plannerUser,
                StoryPlanSchema.Name, StoryPlanSchema.Build(input.SpreadCount), cancellationToken);

            var writerSystem = MasterStoryPromptV3.WriterSystem(input);
            var writerUser = MasterStoryPromptV3.WriterUser(
                planned.Value, StoryJson.Describe(planned.Value), branch);

            var written = await client.CompleteAsync<MasterStory>(
                model, writerSystem, writerUser,
                MasterStorySchema.Name, MasterStorySchema.Build(input.SpreadCount), cancellationToken);

            return new MasterStoryResult
            {
                Story = written.Value,
                SystemPrompt = plannerSystem,
                UserPrompt = writerUser,
                Model = model,
                PromptTokens = planned.PromptTokens + written.PromptTokens,
                CompletionTokens = planned.CompletionTokens + written.CompletionTokens
            };
        }
    }
}
