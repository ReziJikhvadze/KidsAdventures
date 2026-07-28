using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Services;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AdventurePacks.Api.Services.Implementations;

public sealed class AdventurePdfService : IAdventurePdfService
{
    private static readonly Color[] StoryTitleColors =
    [
        Color.FromHex("#E91E63"),
        Color.FromHex("#9C27B0"),
        Color.FromHex("#2196F3"),
        Color.FromHex("#FF9800")
    ];

    public byte[] GeneratePdf(AdventureContentDto content, string themeName)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        PdfFontBootstrap.EnsureRegistered();

        var palette = GetPalette(themeName);
        var themeLabel = GetThemeLabel(themeName);
        var coverImage = content.StoryPages.FirstOrDefault(p => p.ImageBytes is { Length: > 0 })?.ImageBytes;
        var pageNumber = 0;

        var document = Document.Create(container =>
        {
            pageNumber++;
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(0);
                page.PageColor(palette.PageBackground);
                page.Content().Column(column =>
                {
                    column.Item().Element(c => ComposeTopBanner(c, palette, "Adventrya Books", "Your personalized storybook"));

                    column.Item().PaddingHorizontal(36).PaddingTop(28).PaddingBottom(32).Column(inner =>
                    {
                        inner.Spacing(14);
                        inner.Item().AlignCenter().Text(themeLabel)
                            .FontFamily(PdfFontBootstrap.BodyFamily).SemiBold().FontSize(11)
                            .LetterSpacing(0.8f).FontColor(palette.Secondary);

                        inner.Item().AlignCenter().Text(content.Title)
                            .FontFamily(PdfFontBootstrap.DisplayFamily).FontSize(34).Bold()
                            .FontColor(palette.Primary).LineHeight(1.15f);

                        inner.Item().AlignCenter().PaddingTop(4).Row(row =>
                        {
                            row.ConstantItem(48).Height(2).Background(palette.Accent);
                        });

                        inner.Item().AlignCenter().Text($"Starring {content.ChildName}")
                            .FontFamily(PdfFontBootstrap.DisplayFamily).FontSize(20)
                            .FontColor(palette.Accent);

                        if (coverImage is { Length: > 0 })
                        {
                            inner.Item().PaddingTop(10).MaxHeight(340).Border(3).BorderColor(palette.HeaderBackground)
                                .Background(Colors.White).Padding(8)
                                .Image(coverImage).FitArea();
                        }
                        else
                        {
                            inner.Item().PaddingTop(10).Background(palette.CardBackground)
                                .Border(2).BorderColor(palette.HeaderBackground)
                                .Padding(28).AlignCenter()
                                .Text("Your colorful adventure awaits inside!")
                                .FontFamily(PdfFontBootstrap.BodyFamily).Italic()
                                .FontSize(16).FontColor(palette.Secondary);
                        }
                    });

                    column.Item().Element(c => ComposePageFooter(c, palette, pageNumber, content.ChildName));
                });
            });

            var storyIndex = 0;
            foreach (var pageContent in content.StoryPages.Take(AdventureStoryConstants.PageCount))
            {
                var titleColor = StoryTitleColors[storyIndex % StoryTitleColors.Length];
                storyIndex++;
                pageNumber++;

                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(0);
                    page.PageColor(palette.PageBackground);
                    page.Content().Column(column =>
                    {
                        column.Item().Element(c => ComposeTopBanner(c, palette, "Story Time", $"Chapter {storyIndex}"));

                        column.Item().PaddingHorizontal(32).PaddingTop(18).PaddingBottom(24).Row(row =>
                        {
                            row.ConstantItem(8).Background(titleColor);
                            row.RelativeItem().PaddingLeft(14).Background(palette.CardBackground)
                                .Border(1).BorderColor(palette.HeaderBackground)
                                .Padding(18).Column(inner =>
                                {
                                    inner.Spacing(10);
                                    inner.Item().Text(pageContent.Title)
                                        .FontFamily(PdfFontBootstrap.DisplayFamily).FontSize(26).Bold()
                                        .FontColor(titleColor).LineHeight(1.2f);

                                    if (pageContent.ImageBytes is { Length: > 0 })
                                    {
                                        inner.Item().PaddingVertical(6).Border(2)
                                            .BorderColor(palette.HeaderBackground).Padding(6)
                                            .Image(pageContent.ImageBytes).FitWidth();
                                    }

                                    inner.Item().Text(pageContent.Content)
                                        .FontFamily(PdfFontBootstrap.BodyFamily).FontSize(13.5f)
                                        .LineHeight(1.65f).FontColor(palette.BodyText);
                                });
                        });

                        column.Item().Element(c => ComposePageFooter(c, palette, pageNumber, content.ChildName));
                    });
                });
            }

        });

        return document.GeneratePdf();
    }

    private static void ComposeTopBanner(IContainer container, ThemePalette palette, string title, string subtitle)
    {
        container.Background(palette.HeaderBackground).PaddingVertical(14).PaddingHorizontal(32).Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text(title)
                    .FontFamily(PdfFontBootstrap.DisplayFamily).FontSize(18).Bold()
                    .FontColor(palette.HeaderText);
                col.Item().Text(subtitle)
                    .FontFamily(PdfFontBootstrap.BodyFamily).FontSize(10)
                    .FontColor(palette.Secondary);
            });

            row.AutoItem().AlignMiddle().Background(palette.Primary).Padding(8)
                .Text("LH")
                .FontFamily(PdfFontBootstrap.DisplayFamily).Bold().FontSize(12)
                .FontColor(Colors.White);
        });
    }

    private static void ComposePageFooter(IContainer container, ThemePalette palette, int pageNumber, string childName)
    {
        container.Background(palette.PageBackground).PaddingVertical(10).PaddingHorizontal(32)
            .Row(row =>
            {
                row.RelativeItem().AlignMiddle().Text($"{childName}'s adventure")
                    .FontFamily(PdfFontBootstrap.BodyFamily).FontSize(9)
                    .FontColor(palette.Secondary);

                row.AutoItem().AlignMiddle().Text($"Page {pageNumber}")
                    .FontFamily(PdfFontBootstrap.BodyFamily).SemiBold().FontSize(9)
                    .FontColor(palette.HeaderText);
            });
    }

    private static string GetThemeLabel(string themeName) => themeName switch
    {
        "Airplanes" => "ცის მკვლევარის გამოცემა",
        "Dinosaurs" => "დინოზავრების აღმოჩენა",
        "Space" => "გალაქტიკური თავგადასავალი",
        "Pirates" => "განძის ძებნა",
        "Animals" => "ველური მეგობრები",
        "Magic" => "ჯადოსნური სამყარო",
        _ => "თავგადასავლის გამოცემა"
    };

    private static ThemePalette GetPalette(string themeName) => themeName switch
    {
        "Dinosaurs" => new ThemePalette(
            Primary: Color.FromHex("#2E7D32"),
            Secondary: Color.FromHex("#558B2F"),
            Accent: Color.FromHex("#F9A825"),
            PageBackground: Color.FromHex("#F1F8E9"),
            CardBackground: Color.FromHex("#FFFFFF"),
            HeaderBackground: Color.FromHex("#C5E1A5"),
            HeaderText: Color.FromHex("#1B5E20"),
            BodyText: Color.FromHex("#33691E"),
            CertificateBackground: Color.FromHex("#DCEDC8")),
        "Space" => new ThemePalette(
            Primary: Color.FromHex("#5E35B1"),
            Secondary: Color.FromHex("#3949AB"),
            Accent: Color.FromHex("#FFD54F"),
            PageBackground: Color.FromHex("#EDE7F6"),
            CardBackground: Color.FromHex("#FFFFFF"),
            HeaderBackground: Color.FromHex("#B39DDB"),
            HeaderText: Color.FromHex("#311B92"),
            BodyText: Color.FromHex("#4527A0"),
            CertificateBackground: Color.FromHex("#D1C4E9")),
        "Pirates" => new ThemePalette(
            Primary: Color.FromHex("#1565C0"),
            Secondary: Color.FromHex("#EF6C00"),
            Accent: Color.FromHex("#FFCA28"),
            PageBackground: Color.FromHex("#E3F2FD"),
            CardBackground: Color.FromHex("#FFFFFF"),
            HeaderBackground: Color.FromHex("#90CAF9"),
            HeaderText: Color.FromHex("#0D47A1"),
            BodyText: Color.FromHex("#1A237E"),
            CertificateBackground: Color.FromHex("#BBDEFB")),
        "Airplanes" => new ThemePalette(
            Primary: Color.FromHex("#0277BD"),
            Secondary: Color.FromHex("#00838F"),
            Accent: Color.FromHex("#FF7043"),
            PageBackground: Color.FromHex("#E1F5FE"),
            CardBackground: Color.FromHex("#FFFFFF"),
            HeaderBackground: Color.FromHex("#81D4FA"),
            HeaderText: Color.FromHex("#01579B"),
            BodyText: Color.FromHex("#006064"),
            CertificateBackground: Color.FromHex("#B3E5FC")),
        "Animals" => new ThemePalette(
            Primary: Color.FromHex("#F57C00"),
            Secondary: Color.FromHex("#7CB342"),
            Accent: Color.FromHex("#FF8A65"),
            PageBackground: Color.FromHex("#FFF3E0"),
            CardBackground: Color.FromHex("#FFFFFF"),
            HeaderBackground: Color.FromHex("#FFCC80"),
            HeaderText: Color.FromHex("#E65100"),
            BodyText: Color.FromHex("#4E342E"),
            CertificateBackground: Color.FromHex("#FFE0B2")),
        "Magic" => new ThemePalette(
            Primary: Color.FromHex("#3B2A67"),
            Secondary: Color.FromHex("#7564B5"),
            Accent: Color.FromHex("#E8C98A"),
            PageBackground: Color.FromHex("#F8F2E5"),
            CardBackground: Color.FromHex("#FFFFFF"),
            HeaderBackground: Color.FromHex("#EFE3CF"),
            HeaderText: Color.FromHex("#21183F"),
            BodyText: Color.FromHex("#29233A"),
            CertificateBackground: Color.FromHex("#EFE3CF")),
        _ => new ThemePalette(
            Primary: Color.FromHex("#D81B60"),
            Secondary: Color.FromHex("#00897B"),
            Accent: Color.FromHex("#FDD835"),
            PageBackground: Color.FromHex("#FFF8E1"),
            CardBackground: Color.FromHex("#FFFFFF"),
            HeaderBackground: Color.FromHex("#F8BBD0"),
            HeaderText: Color.FromHex("#880E4F"),
            BodyText: Color.FromHex("#37474F"),
            CertificateBackground: Color.FromHex("#FFECB3"))
    };

    private sealed record ThemePalette(
        Color Primary,
        Color Secondary,
        Color Accent,
        Color PageBackground,
        Color CardBackground,
        Color HeaderBackground,
        Color HeaderText,
        Color BodyText,
        Color CertificateBackground);
}
