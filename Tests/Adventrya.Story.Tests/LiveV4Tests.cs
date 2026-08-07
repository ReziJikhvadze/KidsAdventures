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
/// Books written the way the live site writes them: through <see cref="MasterStoryService"/> with
/// the version taken from configuration, not by calling a prompt class directly.
///
/// That indirection is the point. Calling MasterStoryPromptV4 by hand would prove the prompt works
/// and prove nothing about the setting, and the setting is what has failed twice.
///
/// Skipped unless ADVENTRYA_OPENAI_KEY is set.
///
///   $env:ADVENTRYA_OPENAI_KEY="..."; dotnet test --filter LiveV4 -v normal
/// </summary>
public class LiveV4Tests(ITestOutputHelper output)
{
    private static string? ApiKey => Environment.GetEnvironmentVariable("ADVENTRYA_OPENAI_KEY");

    private static string OutputDirectory =>
        Environment.GetEnvironmentVariable("ADVENTRYA_V4_DIR") ?? Path.GetTempPath();

    private static int BookCount =>
        int.TryParse(Environment.GetEnvironmentVariable("ADVENTRYA_V4_COUNT"), out var n) ? n : 1;

    [SkippableFact]
    public async Task Write_books_the_way_the_site_does()
    {
        Skip.If(string.IsNullOrWhiteSpace(ApiKey), "Set ADVENTRYA_OPENAI_KEY to run this.");

        var service = BuildService();

        // If this is not v4, nothing below is worth reading, so it fails before spending anything.
        Assert.Equal("v4", service.PromptVersion);

        var theme = ThemeFor(Environment.GetEnvironmentVariable("ADVENTRYA_V4_THEME"));
        var input = InputFor(theme);

        var (systemPrompt, _) = service.BuildPrompts(input);
        Assert.StartsWith("You are a children's author.", systemPrompt.TrimStart());
        output.WriteLine($"prompt version {service.PromptVersion}, system prompt {systemPrompt.Length} chars\n");

        for (var i = 1; i <= BookCount; i++)
        {
            var started = Stopwatch.StartNew();
            var result = await service.WriteAsync(input, CancellationToken.None);
            var story = result.Story;

            // What the app will actually print. A book that cannot be projected is not a book,
            // and this is cheaper to learn here than on a page a parent is waiting for.
            var content = MasterStoryProjection.ToContent(story, input.ChildName, theme.ToString());
            Assert.Equal(BookFormat.PageCount, content.StoryPages.Count);
            Assert.Equal(input.SpreadCount, MasterStoryProjection.IllustratablePageIndexes(content).Count);

            Assert.Equal(input.SpreadCount, story.Spreads.Count);
            Assert.False(string.IsNullOrWhiteSpace(story.CharacterLock));

            // Georgian in a scene means the illustration prompt goes to the image model in a
            // language it draws badly. The story text is Georgian; the scenes must not be.
            var scenes = story.Spreads.Select(s => s.Illustration.Scene).Append(story.Cover.Scene).ToList();
            var georgian = scenes.Count(s => s.Any(c => c is >= 'ა' and <= 'ჰ'));

            var path = Path.Combine(OutputDirectory, $"v4-{theme}-{i}.md");
            await File.WriteAllTextAsync(path, Render(result, theme, started.Elapsed), CancellationToken.None);

            output.WriteLine(
                $"{i}/{BookCount}  {started.Elapsed.TotalSeconds,5:0}s  " +
                $"{result.PromptTokens}+{result.CompletionTokens} tok  " +
                $"scenes with Georgian: {georgian}/{scenes.Count}  → {Path.GetFileName(path)}");

            Assert.Equal(0, georgian);
        }

        output.WriteLine($"\nwritten to {OutputDirectory}");
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
        AppearanceDescription = null,
        SpreadCount = BookFormat.SpreadCount,
        Language = "ka"
    };

    private static MasterStoryService BuildService() => new(
        new StoryModelClient(
            new SingleClientFactory(),
            Options.Create(new OpenAiOptions { ApiKey = ApiKey!, BaseUrl = "https://api.openai.com/v1" }),
            NullLogger<StoryModelClient>.Instance),
        Options.Create(new OpenAiOptions
        {
            ApiKey = ApiKey!,
            MasterStoryModel = "gpt-5.6-luna",
            StoryPromptVersion = "4"
        }),
        NullLogger<MasterStoryService>.Instance);

    private static string Render(MasterStoryResult result, ThemeType theme, TimeSpan elapsed)
    {
        var story = result.Story;
        var text = new StringBuilder();

        text.AppendLine($"# {story.Concept.Title}");
        text.AppendLine();
        text.AppendLine($"*v4 · {theme} · თამარი, 3 წლის · {elapsed.TotalSeconds:0}s · "
                        + $"{result.PromptTokens}+{result.CompletionTokens} tokens*");

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

        text.AppendLine();
        text.AppendLine("## ილუსტრაციები");
        text.AppendLine();
        text.AppendLine($"**ყდა.** {story.Cover.Scene}");

        foreach (var spread in story.Spreads.OrderBy(s => s.Number))
        {
            text.AppendLine();
            text.AppendLine($"**{spread.Number}.** {spread.Illustration.Scene}");
        }

        return text.ToString();
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new() { Timeout = TimeSpan.FromMinutes(5) };
    }
}
