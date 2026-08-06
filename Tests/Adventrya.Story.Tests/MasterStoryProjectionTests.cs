using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story;

namespace Adventrya.Story.Tests;

/// <summary>
/// The projection is where a book stops being the model's answer and becomes the thing a family
/// reads and pays for, so the counts it produces are worth pinning down. Nine pictures rather
/// than sixteen is the difference between the agreed format and roughly double the cost.
/// </summary>
public class MasterStoryProjectionTests
{
    [Fact]
    public void Eight_spreads_become_sixteen_pages()
    {
        var content = MasterStoryProjection.ToContent(BuildStory(8), "თამარი", "Space");

        Assert.Equal(16, content.StoryPages.Count);
    }

    [Fact]
    public void Exactly_half_the_pages_carry_a_picture()
    {
        var content = MasterStoryProjection.ToContent(BuildStory(8), "თამარი", "Space");

        // Eight here plus the separate cover is the nine images a book costs.
        Assert.Equal(8, MasterStoryProjection.IllustratablePageIndexes(content).Count);
    }

    [Fact]
    public void The_picture_comes_first_and_the_words_face_it()
    {
        var content = MasterStoryProjection.ToContent(BuildStory(2), "თამარი", "Space");

        // The order is the fix for text printed over a child's face: the words never share a
        // page with the illustration.
        Assert.False(content.StoryPages[0].IsTextOnlyPage);
        Assert.Empty(content.StoryPages[0].Content);
        Assert.True(content.StoryPages[1].IsTextOnlyPage);
        Assert.NotEmpty(content.StoryPages[1].Content);
    }

    [Fact]
    public void Text_pages_are_never_given_an_image_prompt()
    {
        var content = MasterStoryProjection.ToContent(BuildStory(4), "თამარი", "Space");

        foreach (var page in content.StoryPages.Where(p => p.IsTextOnlyPage))
        {
            Assert.Null(page.ImagePrompt);
        }
    }

    [Fact]
    public void Every_illustration_keeps_the_prompt_its_own_spread_was_written_with()
    {
        var content = MasterStoryProjection.ToContent(BuildStory(3), "თამარი", "Space");

        // Off-by-one here would put the wrong picture beside the wrong words, which is exactly
        // what pairing them on the spread was meant to make impossible.
        Assert.Contains("scene-1", content.StoryPages[0].ImagePrompt);
        Assert.Contains("scene-2", content.StoryPages[2].ImagePrompt);
        Assert.Contains("scene-3", content.StoryPages[4].ImagePrompt);
    }

    [Fact]
    public void Spreads_are_ordered_by_number_not_by_the_order_they_arrived()
    {
        var story = BuildStory(3) with
        {
            Spreads = [BuildSpread(3), BuildSpread(1), BuildSpread(2)]
        };

        var content = MasterStoryProjection.ToContent(story, "თამარი", "Space");

        Assert.Contains("scene-1", content.StoryPages[0].ImagePrompt);
        Assert.Contains("scene-3", content.StoryPages[4].ImagePrompt);
    }

    [Fact]
    public void Every_prompt_carries_the_lock_and_the_house_rules_without_the_model_writing_them()
    {
        var content = MasterStoryProjection.ToContent(BuildStory(2), "თამარი", "Space");
        var prompt = content.StoryPages[0].ImagePrompt!;

        // The model returns the scene alone. Assembling the rest here is what stopped two thirds
        // of a book's generated output being the same paragraphs typed out nine times.
        Assert.Contains("a girl with green eyes", prompt);
        Assert.Contains(IllustrationPrompt.PhotographDirective, prompt);
        Assert.Contains(IllustrationPrompt.StyleDirective, prompt);
        Assert.Contains(IllustrationPrompt.FormatDirective, prompt);
        Assert.Contains("no drift", prompt);
        Assert.Contains("text, captions, watermark, logo", prompt);
    }

    [Fact]
    public void The_character_lock_travels_with_the_book()
    {
        var content = MasterStoryProjection.ToContent(BuildStory(2), "თამარი", "Space");

        Assert.Equal("a girl with green eyes", content.CharacterLock);
    }

    private static MasterStory BuildStory(int spreads) => new()
    {
        Concept = new StoryConcept
        {
            Title = "თამარი და ვარსკვლავი",
            Outline = ["a", "b", "c", "d", "e"]
        },
        Spreads = Enumerable.Range(1, spreads).Select(BuildSpread).ToList(),
        CharacterLock = "a girl with green eyes",
        Cover = BuildBrief(0)
    };

    private static StorySpread BuildSpread(int number) => new()
    {
        Number = number,
        Title = $"title-{number}",
        Caption = $"caption-{number}",
        Text = $"text-{number}",
        Illustration = BuildBrief(number)
    };

    private static IllustrationBrief BuildBrief(int number) => new()
    {
        Scene = $"scene-{number}",
        Avoid = "no drift"
    };
}
