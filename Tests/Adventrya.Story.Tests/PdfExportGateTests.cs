using System.Text.Json;
using AdventurePacks.Api.Domain.Entities;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Services.Implementations;

namespace Adventrya.Story.Tests;

/// <summary>
/// The gate that decides whether a book can be turned into a PDF. It once demanded an
/// illustration on all sixteen pages of a spread book, eight of which never get one, so the
/// export threw, PdfUrl stayed null, and the download button sat disabled on a finished book.
/// </summary>
public class PdfExportGateTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Eight picture pages alternating with eight prose pages, as the book format is.</summary>
    private static AdventurePack SpreadBook(int illustratedPictures, int storyPageCountColumn = 6)
    {
        var content = new AdventureContentDto { Title = "თამარიკოს თავგადასავალი" };
        var drawn = 0;
        for (var i = 0; i < 16; i++)
        {
            var isProse = i % 2 == 1;
            content.StoryPages.Add(new StoryPageDto
            {
                Title = "თავი",
                Content = isProse ? "ტექსტი" : string.Empty,
                IsTextOnlyPage = isProse,
                IllustrationUrl = !isProse && drawn++ < illustratedPictures
                    ? $"https://blob/{i}.png"
                    : null,
            });
        }

        return new AdventurePack
        {
            // Deliberately stale: rows created under the old format still say 6.
            StoryPageCount = storyPageCountColumn,
            GeneratedJson = JsonSerializer.Serialize(content, Json),
        };
    }

    [Fact]
    public void A_finished_spread_book_can_be_exported()
    {
        Assert.True(AdventureGenerationService.CanExportPdf(SpreadBook(illustratedPictures: 8)));
    }

    [Fact]
    public void A_spread_book_missing_one_picture_cannot_be_exported()
    {
        Assert.False(AdventureGenerationService.CanExportPdf(SpreadBook(illustratedPictures: 7)));
    }

    [Fact]
    public void The_stale_page_count_column_does_not_shrink_the_book()
    {
        // With the column believed, only the first six pages are inspected: three pictures, all
        // drawn, and a book missing five illustrations is declared ready.
        Assert.False(AdventureGenerationService.CanExportPdf(SpreadBook(illustratedPictures: 3)));
    }

    [Fact]
    public void The_export_gate_and_the_slideshow_gate_agree()
    {
        foreach (var drawn in new[] { 0, 3, 7, 8 })
        {
            var pack = SpreadBook(drawn);
            Assert.Equal(
                AdventureGenerationService.HasAllSlideshowIllustrations(pack),
                AdventureGenerationService.CanExportPdf(pack));
        }
    }

    [Fact]
    public void A_legacy_book_still_needs_art_on_every_page()
    {
        var content = new AdventureContentDto { Title = "ძველი წიგნი" };
        for (var i = 0; i < 6; i++)
        {
            content.StoryPages.Add(new StoryPageDto
            {
                Content = "ტექსტი",
                IllustrationUrl = i < 5 ? $"https://blob/{i}.png" : null,
            });
        }

        var pack = new AdventurePack
        {
            StoryPageCount = 6,
            GeneratedJson = JsonSerializer.Serialize(content, Json),
        };

        Assert.False(AdventureGenerationService.CanExportPdf(pack));

        content.StoryPages[5].IllustrationUrl = "https://blob/5.png";
        pack.GeneratedJson = JsonSerializer.Serialize(content, Json);
        Assert.True(AdventureGenerationService.CanExportPdf(pack));
    }
}
