using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Services.Interfaces;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AdventurePacks.Api.Services.Implementations;

public sealed class AdventurePdfService : IAdventurePdfService
{
    public byte[] GeneratePdf(AdventureContentDto content, string themeName)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Header().Text("AdventurePacks").SemiBold().FontSize(14);
                page.Content().Column(column =>
                {
                    column.Spacing(10);
                    column.Item().Text(content.Title).FontSize(28).Bold().AlignCenter();
                    column.Item().Text($"Theme: {themeName}").FontSize(16).AlignCenter();
                    column.Item().Text($"Hero: {content.ChildName}").FontSize(16).AlignCenter();
                    column.Item().Background(Colors.Grey.Lighten3).Padding(15)
                        .Text("Theme artwork placeholder").Italic().AlignCenter();
                });
            });

            foreach (var pageContent in content.StoryPages.Take(4))
            {
                container.Page(page =>
                {
                    page.Margin(30);
                    page.Header().Text("Story").SemiBold().FontSize(14);
                    page.Content().Column(column =>
                    {
                        column.Spacing(10);
                        column.Item().Text(pageContent.Title).FontSize(22).Bold();
                        column.Item().Text(pageContent.Content).FontSize(14).LineHeight(1.4f);
                    });
                });
            }

            foreach (var activity in content.Activities.Take(5))
            {
                container.Page(page =>
                {
                    page.Margin(30);
                    page.Header().Text("Activities").SemiBold().FontSize(14);
                    page.Content().Column(column =>
                    {
                        column.Spacing(10);
                        column.Item().Text($"{activity.Type}: {activity.Title}").FontSize(20).Bold();
                        column.Item().Text(activity.Content).FontSize(14).LineHeight(1.4f);
                    });
                });
            }

            container.Page(page =>
            {
                page.Margin(30);
                page.Header().Text("Achievement Certificate").SemiBold().FontSize(14);
                page.Content().Column(column =>
                {
                    column.Spacing(10);
                    column.Item().AlignCenter().Text(content.Certificate.Title).FontSize(26).Bold();
                    column.Item().AlignCenter().Text(content.Certificate.Text).FontSize(16);
                    column.Item().AlignCenter().Text($"Awarded to {content.ChildName}").FontSize(18).SemiBold();
                });
            });
        });

        return document.GeneratePdf();
    }
}
