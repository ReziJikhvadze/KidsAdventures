using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Pdf;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AdventurePacks.Api.Services.Implementations;

/// <summary>What a physical page of the printed book carries.</summary>
public enum PrintPageKind
{
    /// <summary>Page one. Its presence is what puts pictures on the left-hand pages.</summary>
    Cover,

    /// <summary>The picture half of a spread: art to the bleed line, caption over it, no title.</summary>
    Picture,

    /// <summary>The prose half of a spread, carrying the spread's title exactly once.</summary>
    Prose,

    /// <summary>A page of a book written before spreads existed: picture and prose together.</summary>
    Legacy,

    /// <summary>Padding, so the folded sheets divide.</summary>
    Blank
}

/// <summary>One physical page, numbered from the front cover.</summary>
/// <param name="Kind">What is printed here.</param>
/// <param name="PhysicalPage">1-based. Odd is a right-hand page, even is left-hand.</param>
/// <param name="Source">The story page this came from, if any.</param>
public sealed record PrintPage(PrintPageKind Kind, int PhysicalPage, StoryPageDto? Source);

/// <summary>
/// Sets the book for a print shop.
///
/// A book is eight spreads, and a spread is one picture facing one page of prose. That shape
/// is the whole reason this class is not a loop over pages: the two halves of a spread carry
/// the same title, so printing each page identically printed every chapter heading twice and
/// left an empty text block under every picture.
///
/// Physical page order matters here in a way it never did on screen. Pictures fall on the
/// left-hand page (verso) and prose on the right (recto), which is only true if the cover
/// occupies page one — so the cover is not optional padding, it is what makes the pairing
/// land. The binding also eats the inner edge, which is why the gutter swaps sides every page.
///
/// Geometry comes from <see cref="PrintLayoutOptions"/>. Nothing here knows what A5 is.
/// </summary>
public sealed class AdventurePdfService(IOptions<PrintLayoutOptions> layoutOptions) : IAdventurePdfService
{
    private readonly PrintLayoutOptions _layout = layoutOptions.Value;

    public byte[] GeneratePdf(PdfBookRequest request)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        PdfFontBootstrap.EnsureRegistered();

        var palette = GetPalette(request.ThemeName);
        var strings = PrintStrings.For(request.Language);
        var childName = request.Content.ChildName;
        var plan = BuildPlan(request.Content, _layout);

        var document = Document.Create(container =>
        {
            foreach (var slot in plan)
            {
                switch (slot.Kind)
                {
                    case PrintPageKind.Cover:
                        ComposeCover(container, request, palette, strings);
                        break;
                    case PrintPageKind.Picture:
                        ComposePicturePage(container, slot.Source!, palette);
                        break;
                    case PrintPageKind.Prose:
                        ComposeProsePage(container, slot.Source!, palette, strings, slot.PhysicalPage, childName);
                        break;
                    case PrintPageKind.Legacy:
                        ComposeLegacyPage(container, slot.Source!, palette, strings, slot.PhysicalPage, childName);
                        break;
                    default:
                        ComposeBlankPage(container, palette);
                        break;
                }
            }
        });

        return document.GeneratePdf();
    }

    /// <summary>
    /// Works out what lands on each physical page, before anything is drawn.
    ///
    /// Separate from the drawing because the page *order* is the part that can be wrong in ways
    /// a rendered PDF will not tell you: a picture on the wrong side of the fold, a title set
    /// twice, a book whose page count will not divide by four. Those are decisions, so they are
    /// made somewhere they can be read back and asserted on.
    /// </summary>
    public static IReadOnlyList<PrintPage> BuildPlan(AdventureContentDto content, PrintLayoutOptions layout)
    {
        // Books written before spreads existed have no text-only pages and carry a picture and
        // prose together on every page. They are still in the library, and they still print.
        var pages = content.StoryPages.Take(AdventureStoryConstants.MaxPageCount).ToList();
        var isSpreadBook = pages.Any(p => p.IsTextOnlyPage);

        var plan = new List<PrintPage>();

        if (layout.IncludeCoverInInterior)
        {
            plan.Add(new PrintPage(PrintPageKind.Cover, plan.Count + 1, null));
        }

        foreach (var page in pages)
        {
            var kind = isSpreadBook
                ? page.IsTextOnlyPage ? PrintPageKind.Prose : PrintPageKind.Picture
                : PrintPageKind.Legacy;

            plan.Add(new PrintPage(kind, plan.Count + 1, page));
        }

        // Folded sheets, so the count has to divide. Left to the printer this becomes a surprise
        // on the quote; done here it is a few blank leaves at the back, which is what a picture
        // book has anyway.
        var multiple = Math.Max(1, layout.BindingMultiple);
        var remainder = plan.Count % multiple;
        var padding = remainder == 0 ? 0 : multiple - remainder;

        for (var i = 0; i < padding; i++)
        {
            plan.Add(new PrintPage(PrintPageKind.Blank, plan.Count + 1, null));
        }

        return plan;
    }

    // ---- Page composition ----------------------------------------------

    /// <summary>
    /// The cover: full-bleed art if the book has any, and the title set over it.
    ///
    /// The art is the book's own cover, not page one's illustration. Those are different
    /// pictures — page one has its own moment in the story — and printing one in place of the
    /// other both duplicated a page and dropped the cover from the book.
    /// </summary>
    private void ComposeCover(
        IDocumentContainer container,
        PdfBookRequest request,
        ThemePalette palette,
        PrintStrings strings)
    {
        var content = request.Content;

        container.Page(page =>
        {
            ApplyBleedPageGeometry(page, palette);

            if (request.CoverImage is { Length: > 0 })
            {
                page.Content().Layers(layers =>
                {
                    layers.PrimaryLayer().Image(request.CoverImage).FitArea();

                    // A solid band, not a translucent scrim over the art.
                    //
                    // The picture is fitted rather than cropped, so it does not always reach the
                    // foot of the page — and where it stops, a scrim tints the pale page colour
                    // instead of the illustration, leaving white text on near-white. A band in
                    // the theme's own colour is legible wherever the picture happens to end.
                    layers.Layer().AlignBottom().Background(palette.Primary)
                        .PaddingVertical(SafeInsetMm, Unit.Millimetre)
                        .PaddingHorizontal(SafeInsetMm, Unit.Millimetre)
                        .Column(column =>
                        {
                            column.Spacing(4);
                            column.Item().Text(content.Title)
                                .FontFamily(PdfFontBootstrap.DisplayFamily).FontSize(26).Bold()
                                .FontColor(Colors.White).LineHeight(1.15f);
                            column.Item().Text(strings.Starring(content.ChildName))
                                .FontFamily(PdfFontBootstrap.BodyFamily).FontSize(13)
                                .FontColor(Colors.White);
                        });
                });

                return;
            }

            page.Content().Background(palette.PageBackground)
                .PaddingHorizontal(SafeInsetMm, Unit.Millimetre)
                .PaddingVertical(SafeInsetMm * 2, Unit.Millimetre)
                .AlignMiddle().Column(column =>
                {
                    column.Spacing(10);
                    column.Item().AlignCenter().Text(GetThemeLabel(request.ThemeName))
                        .FontFamily(PdfFontBootstrap.BodyFamily).SemiBold().FontSize(11)
                        .LetterSpacing(0.8f).FontColor(palette.Secondary);
                    column.Item().AlignCenter().Text(content.Title)
                        .FontFamily(PdfFontBootstrap.DisplayFamily).FontSize(28).Bold()
                        .FontColor(palette.Primary).LineHeight(1.15f);
                    column.Item().AlignCenter().PaddingTop(2)
                        .Width(40).Height(2).Background(palette.Accent);
                    column.Item().AlignCenter().Text(strings.Starring(content.ChildName))
                        .FontFamily(PdfFontBootstrap.DisplayFamily).FontSize(16)
                        .FontColor(palette.Accent);
                });
        });
    }

    /// <summary>
    /// The picture half of a spread: art to the bleed line, caption over it, nothing else.
    ///
    /// No title and no running head. The title belongs to the spread and is set once, on the
    /// prose page facing this one; repeating it here is what made every chapter heading appear
    /// twice in the printed book.
    /// </summary>
    private void ComposePicturePage(
        IDocumentContainer container,
        StoryPageDto pageContent,
        ThemePalette palette)
    {
        container.Page(page =>
        {
            ApplyBleedPageGeometry(page, palette);

            if (pageContent.ImageBytes is not { Length: > 0 })
            {
                page.Content().Background(palette.CardBackground);
                return;
            }

            page.Content().Layers(layers =>
            {
                // FitArea, not a crop-to-fill. The illustration was composed as a whole frame,
                // and cropping a children's picture to the page is how a character loses the
                // top of their head. Any letterboxing sits on the theme colour.
                layers.PrimaryLayer().Image(pageContent.ImageBytes).FitArea();

                var caption = pageContent.Caption?.Trim();
                if (!string.IsNullOrWhiteSpace(caption))
                {
                    layers.Layer().AlignBottom().Background(palette.Primary)
                        .PaddingVertical(6, Unit.Millimetre)
                        .PaddingHorizontal(SafeInsetMm, Unit.Millimetre)
                        .Text(caption)
                        .FontFamily(PdfFontBootstrap.DisplayFamily).FontSize(15)
                        .FontColor(Colors.White).LineHeight(1.3f);
                }
            });
        });
    }

    /// <summary>The prose half: the spread's title once, then the text, set clear of the binding.</summary>
    private void ComposeProsePage(
        IDocumentContainer container,
        StoryPageDto pageContent,
        ThemePalette palette,
        PrintStrings strings,
        int physicalPage,
        string childName)
    {
        container.Page(page =>
        {
            ApplyTextPageGeometry(page, palette, physicalPage);

            page.Content().PaddingTop(6, Unit.Millimetre).Column(column =>
            {
                column.Spacing(8);

                column.Item().Text(pageContent.Title)
                    .FontFamily(PdfFontBootstrap.DisplayFamily).FontSize(20).Bold()
                    .FontColor(palette.Primary).LineHeight(1.2f);

                column.Item().Width(28).Height(2).Background(palette.Accent);

                column.Item().PaddingTop(4).Text(pageContent.Content)
                    .FontFamily(PdfFontBootstrap.BodyFamily).FontSize(15)
                    .LineHeight(1.7f).FontColor(palette.BodyText);
            });

            ComposeRunningFoot(page, palette, strings, physicalPage, childName);
        });
    }

    /// <summary>A page from a book written before spreads: picture and prose together.</summary>
    private void ComposeLegacyPage(
        IDocumentContainer container,
        StoryPageDto pageContent,
        ThemePalette palette,
        PrintStrings strings,
        int physicalPage,
        string childName)
    {
        container.Page(page =>
        {
            ApplyTextPageGeometry(page, palette, physicalPage);

            page.Content().PaddingTop(6, Unit.Millimetre).Column(column =>
            {
                column.Spacing(8);

                column.Item().Text(pageContent.Title)
                    .FontFamily(PdfFontBootstrap.DisplayFamily).FontSize(20).Bold()
                    .FontColor(palette.Primary).LineHeight(1.2f);

                if (pageContent.ImageBytes is { Length: > 0 })
                {
                    column.Item().PaddingVertical(4)
                        .Image(pageContent.ImageBytes).FitWidth();
                }

                if (!string.IsNullOrWhiteSpace(pageContent.Content))
                {
                    column.Item().Text(pageContent.Content)
                        .FontFamily(PdfFontBootstrap.BodyFamily).FontSize(14)
                        .LineHeight(1.65f).FontColor(palette.BodyText);
                }
            });

            ComposeRunningFoot(page, palette, strings, physicalPage, childName);
        });
    }

    /// <summary>Padding to the binding multiple. Blank, but the right size and the right colour.</summary>
    private void ComposeBlankPage(IDocumentContainer container, ThemePalette palette)
    {
        container.Page(page =>
        {
            ApplyBleedPageGeometry(page, palette);
            page.Content().Background(palette.PageBackground);
        });
    }

    private static void ComposeRunningFoot(
        PageDescriptor page,
        ThemePalette palette,
        PrintStrings strings,
        int physicalPage,
        string childName)
    {
        page.Footer().PaddingBottom(2, Unit.Millimetre).Row(row =>
        {
            row.RelativeItem().AlignMiddle().Text(strings.RunningHead(childName))
                .FontFamily(PdfFontBootstrap.BodyFamily).FontSize(8)
                .FontColor(palette.Secondary);

            row.AutoItem().AlignMiddle().Text(physicalPage.ToString())
                .FontFamily(PdfFontBootstrap.BodyFamily).SemiBold().FontSize(8)
                .FontColor(palette.Secondary);
        });
    }

    // ---- Geometry -------------------------------------------------------

    private float SafeInsetMm => _layout.SafeMarginMm;

    /// <summary>
    /// A page whose content runs to the bleed line: covers, pictures, blanks. The sheet is the
    /// trim size plus bleed on every side, and the printer cuts the bleed away.
    /// </summary>
    private void ApplyBleedPageGeometry(PageDescriptor page, ThemePalette palette)
    {
        page.Size(new PageSize(
            _layout.TrimWidthMm + (_layout.BleedMm * 2),
            _layout.TrimHeightMm + (_layout.BleedMm * 2),
            Unit.Millimetre));
        page.Margin(0);
        page.PageColor(palette.PageBackground);
    }

    /// <summary>
    /// A page carrying text, inset far enough from the trim to survive the guillotine and far
    /// enough from the spine to survive the binding.
    ///
    /// The gutter swaps sides: on a recto (odd page) the binding is on the left, on a verso it
    /// is on the right. A single symmetric margin looks fine flat and pushes the text of every
    /// other page into the spine once the book is bound.
    /// </summary>
    private void ApplyTextPageGeometry(PageDescriptor page, ThemePalette palette, int physicalPage)
    {
        page.Size(new PageSize(
            _layout.TrimWidthMm + (_layout.BleedMm * 2),
            _layout.TrimHeightMm + (_layout.BleedMm * 2),
            Unit.Millimetre));

        var outer = _layout.BleedMm + _layout.SafeMarginMm;
        var inner = outer + _layout.GutterMm;
        var isRecto = physicalPage % 2 == 1;

        page.MarginTop(outer, Unit.Millimetre);
        page.MarginBottom(outer, Unit.Millimetre);
        page.MarginLeft(isRecto ? inner : outer, Unit.Millimetre);
        page.MarginRight(isRecto ? outer : inner, Unit.Millimetre);

        page.PageColor(palette.PageBackground);
    }

    // ---- Wording --------------------------------------------------------

    /// <summary>
    /// The few words the book says in its own voice rather than the story's.
    ///
    /// These used to be English regardless of the book's language, so a Georgian storybook
    /// carried "Story Time", "Chapter 3" and "Page 7" — alongside a logo box reading "LH",
    /// which belonged to no brand this product has ever had.
    /// </summary>
    private sealed record PrintStrings(Func<string, string> Starring, Func<string, string> RunningHead)
    {
        public static PrintStrings For(string? language) =>
            (language ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "en" => new PrintStrings(
                    name => $"Starring {name}",
                    name => $"{name}'s adventure"),
                _ => new PrintStrings(
                    name => $"მთავარ როლში — {name}",
                    name => $"{GeorgianGenitive(name)} თავგადასავალი")
            };

        /// <summary>
        /// Puts a Georgian name into the genitive.
        ///
        /// Naive string interpolation produced "თამარიკო-ის თავგადასავალი" on the foot of every
        /// page — a hyphen no Georgian writes, in a book a Georgian parent reads aloud. A name
        /// ending in a vowel takes ს; a consonant takes ის. That is not the whole grammar, but
        /// it is right for the first names this product actually prints.
        /// </summary>
        private static string GeorgianGenitive(string name)
        {
            var trimmed = (name ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return trimmed;
            }

            const string vowels = "აეიოუ";
            return vowels.Contains(trimmed[^1]) ? $"{trimmed}ს" : $"{trimmed}ის";
        }
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

    // ---- Palette --------------------------------------------------------

    private static ThemePalette GetPalette(string themeName) => themeName switch
    {
        "Dinosaurs" => new ThemePalette(
            Primary: Color.FromHex("#2E7D32"),
            Secondary: Color.FromHex("#558B2F"),
            Accent: Color.FromHex("#F9A825"),
            PageBackground: Color.FromHex("#F1F8E9"),
            CardBackground: Color.FromHex("#FFFFFF"),
            BodyText: Color.FromHex("#33691E")),
        "Space" => new ThemePalette(
            Primary: Color.FromHex("#5E35B1"),
            Secondary: Color.FromHex("#3949AB"),
            Accent: Color.FromHex("#FFD54F"),
            PageBackground: Color.FromHex("#EDE7F6"),
            CardBackground: Color.FromHex("#FFFFFF"),
            BodyText: Color.FromHex("#4527A0")),
        "Pirates" => new ThemePalette(
            Primary: Color.FromHex("#1565C0"),
            Secondary: Color.FromHex("#EF6C00"),
            Accent: Color.FromHex("#FFCA28"),
            PageBackground: Color.FromHex("#E3F2FD"),
            CardBackground: Color.FromHex("#FFFFFF"),
            BodyText: Color.FromHex("#1A237E")),
        "Airplanes" => new ThemePalette(
            Primary: Color.FromHex("#0277BD"),
            Secondary: Color.FromHex("#00838F"),
            Accent: Color.FromHex("#FF7043"),
            PageBackground: Color.FromHex("#E1F5FE"),
            CardBackground: Color.FromHex("#FFFFFF"),
            BodyText: Color.FromHex("#006064")),
        "Animals" => new ThemePalette(
            Primary: Color.FromHex("#F57C00"),
            Secondary: Color.FromHex("#7CB342"),
            Accent: Color.FromHex("#FF8A65"),
            PageBackground: Color.FromHex("#FFF3E0"),
            CardBackground: Color.FromHex("#FFFFFF"),
            BodyText: Color.FromHex("#4E342E")),
        "Magic" => new ThemePalette(
            Primary: Color.FromHex("#3B2A67"),
            Secondary: Color.FromHex("#7564B5"),
            Accent: Color.FromHex("#E8C98A"),
            PageBackground: Color.FromHex("#F8F2E5"),
            CardBackground: Color.FromHex("#FFFFFF"),
            BodyText: Color.FromHex("#29233A")),
        _ => new ThemePalette(
            Primary: Color.FromHex("#D81B60"),
            Secondary: Color.FromHex("#00897B"),
            Accent: Color.FromHex("#FDD835"),
            PageBackground: Color.FromHex("#FFF8E1"),
            CardBackground: Color.FromHex("#FFFFFF"),
            BodyText: Color.FromHex("#37474F"))
    };

    private sealed record ThemePalette(
        Color Primary,
        Color Secondary,
        Color Accent,
        Color PageBackground,
        Color CardBackground,
        Color BodyText);
}
