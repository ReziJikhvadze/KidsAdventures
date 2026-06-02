using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Services.Interfaces;
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

    private static readonly Color[] ActivityAccentColors =
    [
        Color.FromHex("#00ACC1"),
        Color.FromHex("#43A047"),
        Color.FromHex("#FB8C00"),
        Color.FromHex("#8E24AA"),
        Color.FromHex("#1E88E5")
    ];

    public byte[] GeneratePdf(AdventureContentDto content, string themeName)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var palette = GetPalette(themeName);
        var coverImage = content.StoryPages.FirstOrDefault(p => p.ImageBytes is { Length: > 0 })?.ImageBytes;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(28);
                page.PageColor(palette.PageBackground);
                page.Header().Background(palette.HeaderBackground).Padding(8)
                    .Text("AdventurePacks").SemiBold().FontSize(14).FontColor(palette.HeaderText);
                page.Content().Column(column =>
                {
                    column.Spacing(12);
                    column.Item().AlignCenter()
                        .Text(content.Title).FontSize(30).Bold().FontColor(palette.Primary);
                    column.Item().AlignCenter()
                        .Text($"Theme: {themeName}").FontSize(17).SemiBold().FontColor(palette.Secondary);
                    column.Item().AlignCenter()
                        .Text($"Hero: {content.ChildName}").FontSize(18).FontColor(palette.Accent);
                    if (coverImage is { Length: > 0 })
                    {
                        column.Item().Border(3).BorderColor(palette.Primary).Padding(6)
                            .Image(coverImage).FitWidth();
                    }
                    else
                    {
                        column.Item().Background(palette.CardBackground).Padding(20)
                            .AlignCenter().Text("Your adventure begins!").Italic()
                            .FontSize(16).FontColor(palette.Secondary);
                    }
                });
            });

            var storyIndex = 0;
            foreach (var pageContent in content.StoryPages.Take(4))
            {
                var titleColor = StoryTitleColors[storyIndex % StoryTitleColors.Length];
                storyIndex++;

                container.Page(page =>
                {
                    page.Margin(28);
                    page.PageColor(palette.PageBackground);
                    page.Header().Background(palette.HeaderBackground).Padding(8)
                        .Text("Story Time").SemiBold().FontSize(14).FontColor(palette.HeaderText);
                    page.Content().Column(column =>
                    {
                        column.Spacing(10);
                        column.Item().Background(palette.CardBackground).Padding(12).Column(inner =>
                        {
                            inner.Item().Text(pageContent.Title).FontSize(24).Bold().FontColor(titleColor);
                            if (pageContent.ImageBytes is { Length: > 0 })
                            {
                                inner.Item().PaddingVertical(8).Border(2).BorderColor(titleColor)
                                    .Image(pageContent.ImageBytes).FitWidth();
                            }

                            inner.Item().Text(pageContent.Content).FontSize(14).LineHeight(1.5f)
                                .FontColor(palette.BodyText);
                        });
                    });
                });
            }

            var activityIndex = 0;
            foreach (var activity in content.Activities.Take(5))
            {
                var accent = ActivityAccentColors[activityIndex % ActivityAccentColors.Length];
                activityIndex++;

                container.Page(page =>
                {
                    page.Margin(28);
                    page.PageColor(palette.PageBackground);
                    page.Header().Background(palette.HeaderBackground).Padding(8)
                        .Text("Fun Activities").SemiBold().FontSize(14).FontColor(palette.HeaderText);
                    page.Content().Column(column =>
                    {
                        column.Spacing(10);
                        column.Item().Background(palette.CardBackground).Padding(14).Column(inner =>
                        {
                            inner.Item().Text(activity.Type).FontSize(13).Bold().FontColor(accent);
                            inner.Item().Text(activity.Title).FontSize(22).Bold().FontColor(palette.Primary);
                            inner.Item().Text(activity.Content).FontSize(14).LineHeight(1.5f)
                                .FontColor(palette.BodyText);
                        });
                    });
                });
            }

            container.Page(page =>
            {
                page.Margin(28);
                page.PageColor(palette.CertificateBackground);
                page.Header().Background(palette.HeaderBackground).Padding(8)
                    .Text("You Did It!").SemiBold().FontSize(14).FontColor(palette.HeaderText);
                page.Content().Column(column =>
                {
                    column.Spacing(12);
                    column.Item().Border(4).BorderColor(palette.Accent).Padding(24).Column(inner =>
                    {
                        inner.Item().AlignCenter().Text(content.Certificate.Title)
                            .FontSize(28).Bold().FontColor(palette.Primary);
                        inner.Item().PaddingTop(10).AlignCenter().Text(content.Certificate.Text)
                            .FontSize(16).FontColor(palette.BodyText);
                        inner.Item().PaddingTop(14).AlignCenter()
                            .Text($"Awarded to {content.ChildName}")
                            .FontSize(20).Bold().FontColor(palette.Secondary);
                    });
                });
            });
        });

        return document.GeneratePdf();
    }

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
