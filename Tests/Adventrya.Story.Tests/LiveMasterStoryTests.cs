using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Prompts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Adventrya.Story.Tests;

/// <summary>
/// A whole book from the master prompt, written out so it can be read rather than described.
///
/// Skipped unless ADVENTRYA_OPENAI_KEY is set. The assertions cover the things a program cares
/// about — page count, English-only prompts, the character lock actually being repeated — but
/// the real output is the story itself, saved to disk for a person to judge.
/// </summary>
public class LiveMasterStoryTests(ITestOutputHelper output)
{
    private static string? ApiKey => Environment.GetEnvironmentVariable("ADVENTRYA_OPENAI_KEY");

    [SkippableFact]
    public async Task Generate_a_whole_book_and_write_it_out()
    {
        Skip.If(string.IsNullOrWhiteSpace(ApiKey), "Set ADVENTRYA_OPENAI_KEY to run this.");

        var input = new MasterStoryInput
        {
            ChildName = "ნინი",
            Age = 5,
            Gender = "girl",
            Theme = ThemeType.Animals,
            EyeColor = "მოყავისფრო",
            ExtraWishes = "ძალიან უყვარს მელიები",
            AppearanceDescription =
                "მოკლე მუქი ყავისფერი თმა ორ კუდად, მრგვალი ღიმილიანი სახე, თბილი კანის ტონი",
            SpreadCount = BookFormat.SpreadCount,
            Language = "ka"
        };

        var client = BuildClient();
        var started = DateTime.UtcNow;

        var result = await client.CompleteAsync<MasterStory>(
            "gpt-5.6-sol",
            MasterStoryPrompt.System(input),
            MasterStoryPrompt.User(input),
            MasterStorySchema.Name,
            MasterStorySchema.Build(input.SpreadCount),
            CancellationToken.None);

        var elapsed = DateTime.UtcNow - started;
        var story = result.Value;

        var report = Render(story, input, elapsed, result);
        output.WriteLine(report);

        var path = Path.Combine(Path.GetTempPath(), "adventrya-story.md");
        await File.WriteAllTextAsync(path, report, CancellationToken.None);
        output.WriteLine($"\nSaved to {path}");

        Assert.Equal(input.SpreadCount, story.Spreads.Count);
        
        Assert.False(string.IsNullOrWhiteSpace(story.CharacterLock));

        // The point of the whole design: the lock is quoted into every prompt, not remembered.
        var lockOpening = string.Concat(story.CharacterLock.Take(40));
        var prompts = story.Spreads.Select(s => s.Illustration.Prompt).Append(story.Cover.Prompt).ToList();
        var carrying = prompts.Count(p => p.Contains(lockOpening, StringComparison.Ordinal));
        output.WriteLine($"\ncharacterLock repeated in {carrying}/{prompts.Count} prompts");

        // The cover plus one per spread. Nine rather than twelve, because a picture now has its
        // own page and no longer shares one with the words.
        Assert.Equal(BookFormat.ImageCount, prompts.Count);
    }

    private static string Render(
        MasterStory story, MasterStoryInput input, TimeSpan elapsed, ModelResult<MasterStory> result)
    {
        var skill = SkillMatrix.For(input.Theme, input.Age);
        var text = new System.Text.StringBuilder();

        text.AppendLine($"# {story.Concept.Title}");
        text.AppendLine();
        text.AppendLine($"*{elapsed.TotalSeconds:0}s · {result.PromptTokens} + {result.CompletionTokens} tokens*");
        text.AppendLine();
        text.AppendLine($"**უნარი (რუკიდან):** {skill.Georgian}");
        text.AppendLine();

        text.AppendLine("## გეგმა");
        foreach (var beat in story.Concept.Outline)
        {
            text.AppendLine($"- {beat}");
        }

        text.AppendLine();
        text.AppendLine("## ისტორია");
        foreach (var spread in story.Spreads.OrderBy(s => s.Number))
        {
            text.AppendLine();
            text.AppendLine($"### სცენა {spread.Number} — {spread.Title}  *(გვ. {spread.Number * 2 - 1}–{spread.Number * 2})*");
            text.AppendLine($"*{spread.Caption}*");
            text.AppendLine();
            text.AppendLine(spread.Text);
        }


        text.AppendLine();
        text.AppendLine("## characterLock");
        text.AppendLine();
        text.AppendLine("```");
        text.AppendLine(story.CharacterLock);
        text.AppendLine("```");

        text.AppendLine();
        text.AppendLine("## ილუსტრაციები");
        text.AppendLine();
        text.AppendLine("### ყდა");
        text.AppendLine("```");
        text.AppendLine(story.Cover.Prompt);
        text.AppendLine("```");

        foreach (var spread in story.Spreads.OrderBy(s => s.Number))
        {
            var brief = spread.Illustration;
            text.AppendLine();
            text.AppendLine($"### სცენა {spread.Number} — {spread.Title}");
            text.AppendLine();
            text.AppendLine("```");
            text.AppendLine(brief.Prompt);
            text.AppendLine("```");
        }

        return text.ToString();
    }

    private static StoryModelClient BuildClient()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        var provider = services.BuildServiceProvider();

        return new StoryModelClient(
            provider.GetRequiredService<IHttpClientFactory>(),
            Options.Create(new OpenAiOptions { ApiKey = ApiKey!, BaseUrl = "https://api.openai.com/v1" }),
            NullLogger<StoryModelClient>.Instance);
    }
}
