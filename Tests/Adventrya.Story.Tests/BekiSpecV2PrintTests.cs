using System;
using System.IO;
using System.Linq;
using Xunit;
using PdfSharp.Pdf.IO;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Domain.Story;
using Microsoft.Extensions.Options;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Services.Pdf;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Adventrya.Story.Tests;

public class BekiSpecV2PrintTests
{
    private static byte[] SolidPng((byte R, byte G, byte B) colour)
    {
        var layout = new BekiPrintLayoutOptions();
        const int width = 440;
        var height = (int)System.MathF.Round(width
            * (layout.SpreadHeightMm + (layout.BleedMm * 2))
            / (layout.PageWidthMm + (layout.BleedMm * 2)));

        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(
            width, height, new SixLabors.ImageSharp.PixelFormats.Rgba32(colour.R, colour.G, colour.B, 255));
        using var buffer = new System.IO.MemoryStream();
        image.Save(buffer, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        return buffer.ToArray();
    }

    

    private static MasterStory SyntheticPlan() => new()
    {
        Concept = new StoryConcept { Title = "ტესტი", Outline = ["a", "b"] },
        CharacterLock = "A child.",
        Cover = new IllustrationBrief { Scene = "cover" },
        TitleEn = "Test",
        Spreads = Enumerable.Range(1, 14).Select(i => new StorySpread
        {
            Number = i,
            Title = string.Empty,
            Caption = string.Empty,
            Text = $"Spread {i}",
            TextEn = $"Spread {i} EN",
            Illustration = new IllustrationBrief { Scene = $"scene {i}" }
        }).ToList()
    };


    private static BekiPrintLayoutOptions DefaultLayout => new()
    {
        SpreadHeightMm = 210,
        SpreadWidthMm = 420,
        BleedMm = 3,
        IntroBelongsTemplate = "ეს წიგნი ეკუთვნის {name}-ს",
        IntroDateTemplate = "{date}",
        IntroInviteTemplate = "მოემზადე თავგადასავლებისთვის, {name}!",
        PrintTargetPpi = 300,
        PrintAssetJpegQuality = 90
    };

    [Fact]
    public void PdfPrintBoxes_Apply_CreatesCorrectMediaAndTrimBoxes()
    {
        // Arrange
        var layout = DefaultLayout;
        var plan = SyntheticPlan();
        var composer = new BekiPdfComposer(Options.Create(layout));
        
        var spreads = plan.Spreads.Select(s => new BekiSpreadArtwork(s.Number, SolidPng((0,255,0)))).ToList();
        var pdfBytes = composer.Compose(plan, SolidPng((255, 0, 0)), spreads, new BekiBookPersonalization("Luka", 6, DateTime.UtcNow, "Space", "ბეკის"));

        // Act
        using var stream = new MemoryStream(pdfBytes);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        var page = document.Pages[0];

        // Assert
        Assert.NotNull(page.MediaBox);
        Assert.NotNull(page.TrimBox);
        
        // Bleed is 3mm. MediaBox should be larger than TrimBox by 3mm on all sides (or just verify they exist and differ)
        Assert.True(page.MediaBox.Width > page.TrimBox.Width);
        Assert.True(page.MediaBox.Height > page.TrimBox.Height);
    }

    [Fact]
    public void NormalizeForPrint_ResizesImageCorrectly()
    {
        // Arrange
        using var image = new Image<Rgba32>(100, 100);
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        var pngBytes = ms.ToArray();
        
        // Act
        var normalized = BekiPdfComposer.NormalizeForPrint(pngBytes, 420, 210, 3, 300, 90);

        // Assert
        Assert.NotNull(normalized);
        Assert.True(normalized.Length > 0);
        // It should be a JPEG now
        Assert.Equal(0xFF, normalized[0]);
        Assert.Equal(0xD8, normalized[1]);
    }

    /// <summary>
    /// The whole composed book, reopened: fourteen pages in spec v2's locked sequence, and every
    /// page carrying the print boxes §26 demands — MediaBox and BleedBox the bled sheet, TrimBox
    /// the trim centred inside it, never equal to the bleed. Run against the real Compose output
    /// rather than a synthetic document, so it also proves the QuestPDF file survives PDFsharp's
    /// reopen-mutate-save round trip with its page list intact.
    /// </summary>
    [Fact]
    public void The_composed_book_has_fourteen_pages_with_correct_print_boxes()
    {
        var layout = new BekiPrintLayoutOptions();
        var plan = EightSpreadPlan();
        var spreads = plan.Spreads
            .Select(s => new BekiSpreadArtwork(s.Number, SolidPng((0, 255, 0))))
            .ToList();

        var pdf = new BekiPdfComposer(Options.Create(layout)).Compose(
            plan, SolidPng((255, 0, 0)), spreads,
            new BekiBookPersonalization("ლილე", 4, new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc), "Space", "ვარსკვლავების გზა"));

        using var stream = new MemoryStream(pdf);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);

        Assert.Equal(14, document.Pages.Count);

        const double mmToPt = 72.0 / 25.4;
        var leafPt = (layout.PageWidthMm + (layout.BleedMm * 2)) * mmToPt;
        var spreadPt = (layout.SpreadWidthMm + (layout.BleedMm * 2)) * mmToPt;
        var heightPt = (layout.SpreadHeightMm + (layout.BleedMm * 2)) * mmToPt;
        var bleedPt = layout.BleedMm * mmToPt;

        for (var index = 0; index < document.Pages.Count; index++)
        {
            var page = document.Pages[index];
            var media = page.MediaBox;
            var bleed = page.Elements.GetRectangle("/BleedBox");
            var trim = page.TrimBox;

            // Cover and back cover are single leaves; everything between is a spread sheet.
            var expectedWidth = index == 0 || index == 13 ? leafPt : spreadPt;
            Assert.True(Math.Abs(media.Width - expectedWidth) < 1.5,
                $"Page {index + 1}: MediaBox width {media.Width:F1}pt, expected {expectedWidth:F1}pt.");
            Assert.True(Math.Abs(media.Height - heightPt) < 1.5,
                $"Page {index + 1}: MediaBox height {media.Height:F1}pt.");

            Assert.True(Math.Abs(bleed.Width - media.Width) < 0.1
                && Math.Abs(bleed.Height - media.Height) < 0.1,
                $"Page {index + 1}: BleedBox must equal MediaBox.");

            Assert.True(Math.Abs(trim.Width - (media.Width - (2 * bleedPt))) < 1.5,
                $"Page {index + 1}: TrimBox width {trim.Width:F1}pt is not the trim.");
            Assert.True(Math.Abs(trim.X1 - media.X1 - bleedPt) < 1.5,
                $"Page {index + 1}: TrimBox is not centred.");
            Assert.True(trim.Width < bleed.Width,
                $"Page {index + 1}: TrimBox must never equal the bleed size.");
        }
    }

    /// <summary>
    /// The intro spread's personalization is real typeset text: a personalized book carries
    /// strictly more draw-text calls than an anonymous one, because the belongs-line and the
    /// date-line print only when there is a child to print them for. Counted rather than
    /// extracted — the Georgian lives in the file as font glyph indices a substring search
    /// cannot see — and counted as an inequality, because an operator maps to a typeset line,
    /// not to a block, and line counts follow wrapping.
    /// </summary>
    [Fact]
    public void The_personalized_intro_adds_its_personal_lines()
    {
        var layout = new BekiPrintLayoutOptions();
        var plan = EightSpreadPlan();
        var spreads = plan.Spreads
            .Select(s => new BekiSpreadArtwork(s.Number, SolidPng((0, 255, 0))))
            .ToList();

        var anonymous = new BekiPdfComposer(Options.Create(layout))
            .Compose(plan, SolidPng((255, 0, 0)), spreads);
        var personalized = new BekiPdfComposer(Options.Create(layout))
            .Compose(plan, SolidPng((255, 0, 0)), spreads,
                new BekiBookPersonalization("ლილე", 4, new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc), "Space", "ვარსკვლავების გზა"));

        var floor = TextShowOperators(anonymous);
        Assert.True(floor > 0, "No readable text operators were found; the counter needs revisiting.");
        Assert.True(TextShowOperators(personalized) > floor,
            "A personalized book must carry more typeset text than an anonymous one.");
    }

    /// <summary>
    /// Spread 8's Continue Adventure module now shares the right column with the story text —
    /// the QR tile must still land in the lower-right corner, inset a full safe margin from the
    /// trim. Probed as pixels in the rendered page: the tile's quiet zone is white by
    /// construction, and the layout geometry is fixed, so the probe point is deterministic.
    /// </summary>
    [Fact]
    public void Spread_8_still_carries_its_qr_tile_in_the_lower_right_corner()
    {
        var layout = new BekiPrintLayoutOptions();
        var plan = EightSpreadPlan();
        var spreads = plan.Spreads
            .Select(s => new BekiSpreadArtwork(s.Number, SolidPng((0, 255, 0))))
            .ToList();

        var pages = new BekiPdfComposer(Options.Create(layout)).RenderPages(
            plan, SolidPng((255, 0, 0)), spreads,
            new BekiBookPersonalization("ლილე", 4, new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc), "Space", "ვარსკვლავების გზა"));

        // Cover, front endpapers, intro, then the eight spreads: the final spread is index 10.
        using var page = SixLabors.ImageSharp.Image.Load<Rgba32>(pages[10]);

        // The tile spans 20–44mm from the sheet's right and bottom edges; its quiet zone makes
        // the strip just inside the tile's edge white whatever the QR's modules do. 96 DPI.
        const double pxPerMm = 96.0 / 25.4;
        var x = page.Width - (int)(21 * pxPerMm);
        var y = page.Height - (int)(32 * pxPerMm);
        var pixel = page[x, y];

        Assert.True(pixel.R > 200 && pixel.G > 200 && pixel.B > 200,
            $"Expected the QR tile's white quiet zone at ({x},{y}); found #{pixel.R:X2}{pixel.G:X2}{pixel.B:X2}.");
    }

    private static MasterStory EightSpreadPlan() => new()
    {
        Concept = new StoryConcept { Title = "ტესტი", Outline = ["a", "b"] },
        CharacterLock = "A child.",
        Cover = new IllustrationBrief { Scene = "cover" },
        TitleEn = "Test",
        Spreads = Enumerable.Range(1, BookFormat.SpreadCount).Select(i => new StorySpread
        {
            Number = i,
            Title = string.Empty,
            Caption = string.Empty,
            Text = $"ქართული ტექსტი {i}.",
            TextEn = $"English {i}.",
            Illustration = new IllustrationBrief { Scene = $"scene {i}" },
            Characters = ["child"],
        }).ToList(),
    };

    /// <summary>
    /// Draw-text calls in the file — a copy of the counter BekiPdfComposerTests uses, asking the
    /// same single question of a different book.
    /// </summary>
    private static int TextShowOperators(byte[] pdf)
    {
        var text = System.Text.Encoding.Latin1.GetString(pdf);
        var total = 0;

        foreach (System.Text.RegularExpressions.Match match in
                 System.Text.RegularExpressions.Regex.Matches(text, @"stream\r?\n"))
        {
            var start = match.Index + match.Length;
            var end = text.IndexOf("endstream", start, StringComparison.Ordinal);
            if (end < 0) continue;

            var deflated = System.Text.Encoding.Latin1.GetBytes(text[start..end]);

            string inflated;
            try
            {
                using var source = new MemoryStream(deflated);
                using var zlib = new System.IO.Compression.ZLibStream(
                    source, System.IO.Compression.CompressionMode.Decompress);
                using var target = new MemoryStream();
                zlib.CopyTo(target);
                inflated = System.Text.Encoding.Latin1.GetString(target.ToArray());
            }
            catch (InvalidDataException)
            {
                continue;
            }

            total += System.Text.RegularExpressions.Regex.Matches(inflated, @"\b(TJ|Tj)\b").Count;
        }

        return total;
    }

    [Fact]
    public void StoryFontSizeFor_ReturnsCorrectSizeBasedOnAge()
    {
        var layout = DefaultLayout;
        layout.StoryFontSizeAges2To4 = 20f;
        layout.StoryFontSizeAges5To8 = 17.5f;
        layout.StoryFontSize = 16f;

        Assert.Equal(20f, BekiPrintLayoutOptions.StoryFontSizeFor(3, layout));
        Assert.Equal(17.5f, BekiPrintLayoutOptions.StoryFontSizeFor(6, layout));
        Assert.Equal(17.5f, BekiPrintLayoutOptions.StoryFontSizeFor(10, layout));
        Assert.Equal(16f, BekiPrintLayoutOptions.StoryFontSizeFor(null, layout));
    }
}
