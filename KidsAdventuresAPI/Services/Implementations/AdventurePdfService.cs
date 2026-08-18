using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Services.Interfaces;
using AdventurePacks.Api.Services.Pdf;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SixLabors.ImageSharp.Processing;
// Aliased rather than imported: ImageSharp and QuestPDF both publish a Color, a Size and a
// PointF, and this file is mostly QuestPDF. Naming the two ImageSharp types it needs keeps
// every unqualified Color in here the one that paints.
using SourceImage = SixLabors.ImageSharp.Image;
using SourceSize = SixLabors.ImageSharp.Size;
using SourcePoint = SixLabors.ImageSharp.PointF;
using SourceColor = SixLabors.ImageSharp.Color;

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
    Blank,

    /// <summary>
    /// The last leaf: the same closing page the reader shows, with a QR where the screen has a
    /// button. It comes after the padding, because a back cover with blank leaves behind it is
    /// not a back cover.
    /// </summary>
    Back
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
        var plan = BuildPlan(request.Content, _layout, request.ForPrint);

        // The reader numbers the pages of the story, not the leaves of the printed book — "page
        // 7 of 16" counts the same sevens on paper and on screen only if the covers and the
        // blanks are left out of the counting, which is what having a story page here does.
        var totalStoryPages = plan.Count(slot => slot.Source is not null);
        var storyPage = 0;

        var document = Document.Create(container =>
        {
            foreach (var slot in plan)
            {
                if (slot.Source is not null)
                {
                    storyPage++;
                }

                switch (slot.Kind)
                {
                    case PrintPageKind.Cover:
                        ComposeCover(container, request, palette, strings);
                        break;
                    case PrintPageKind.Picture:
                        ComposePicturePage(container, slot.Source!, palette);
                        break;
                    case PrintPageKind.Prose:
                        ComposeProsePage(container, slot.Source!, strings, slot.PhysicalPage, storyPage, totalStoryPages);
                        break;
                    case PrintPageKind.Legacy:
                        ComposeLegacyPage(container, slot.Source!, palette, strings, slot.PhysicalPage, childName);
                        break;
                    case PrintPageKind.Back:
                        ComposeBackCoverPage(container, request, palette, strings, childName);
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
    public static IReadOnlyList<PrintPage> BuildPlan(
        AdventureContentDto content,
        PrintLayoutOptions layout,
        bool forPrint = true)
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
        //
        // The back cover is counted before the padding is worked out and added after it, so the
        // blanks fall inside the book and the closing page is the last thing bound.
        //
        // Only for print. A screen has no fold, and on a screen those leaves are two empty pages
        // between the last page and the ending — which reads as a book that failed to finish.
        var multiple = forPrint ? Math.Max(1, layout.BindingMultiple) : 1;
        var backLeaves = layout.IncludeBackCover ? 1 : 0;
        var remainder = (plan.Count + backLeaves) % multiple;
        var padding = remainder == 0 ? 0 : multiple - remainder;

        for (var i = 0; i < padding; i++)
        {
            plan.Add(new PrintPage(PrintPageKind.Blank, plan.Count + 1, null));
        }

        if (layout.IncludeBackCover)
        {
            plan.Add(new PrintPage(PrintPageKind.Back, plan.Count + 1, null));
        }

        return plan;
    }

    // ---- Page composition ----------------------------------------------

    /// <summary>
    /// The cover: the reader's own cover, printed.
    ///
    /// The art is the book's own cover, not page one's illustration. Those are different
    /// pictures — page one has its own moment in the story — and printing one in place of the
    /// other both duplicated a page and dropped the cover from the book.
    ///
    /// What is set over the art is `.storybook-cover` from the stylesheet, element for element:
    /// the brand mark at the head, the line naming whose story this is, the title under it, all
    /// standing on the same darkening wash, inside the same frame.
    ///
    /// It used to be a solid band in the theme's colour with "starring —" under the title. That
    /// band exists nowhere on screen: a parent who had looked at the cover for a week opened the
    /// PDF and found an orange stripe across the foot of it. The band was there for a real
    /// reason — a fitted picture stops short of the page, and a translucent scrim over bare
    /// paper leaves white text on near-white — but the fix belonged at the picture: it is
    /// cropped to the sheet now, exactly as `background-size: cover` crops it on screen, so
    /// there is no bare paper for a wash to fail on.
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
                // 48% down rather than the middle, which is where the screen holds it: a cover
                // portrait's face sits above centre, and a centred crop takes the top of the head.
                var art = FillSheet(request.CoverImage, CoverArtFocusY, CoverInk);

                page.Content().Layers(layers =>
                {
                    // Cropped to the sheet already, so an unproportional fit is an exact fill
                    // rather than a stretch — and unlike FitArea it cannot leave a letterbox.
                    layers.PrimaryLayer().Image(art).FitUnproportionally();

                    layers.Layer().AlignBottom().Height(CoverWashHeightMm, Unit.Millimetre)
                        .Column(ComposeCoverWash);

                    layers.Layer().Padding(_layout.BleedMm, Unit.Millimetre)
                        .Border(CoverFrameMm, Unit.Millimetre).BorderColor(CoverFrameColor)
                        .Padding(CoverFrameGapMm, Unit.Millimetre)
                        .Border(0.25f).BorderColor(CoverHairlineColor);

                    layers.Layer().AlignTop().PaddingTop(CoverInsetMm, Unit.Millimetre)
                        .PaddingLeft(CoverInsetMm, Unit.Millimetre)
                        .Text(BrandMark)
                        .FontFamily(PdfFontBootstrap.BodyFamily).Bold().FontSize(8)
                        .LetterSpacing(0.2f).FontColor(CoverBrandColor);

                    layers.Layer().AlignBottom().Padding(CoverInsetMm, Unit.Millimetre)
                        .Column(column =>
                        {
                            column.Spacing(6);

                            // Above the title, as on screen. This line is the cover's one piece
                            // of warmth — it names whose story it is — and it was the piece the
                            // printed cover dropped.
                            column.Item().Text(strings.BelongsTo(content.ChildName))
                                .FontFamily(PdfFontBootstrap.BodyFamily).SemiBold().FontSize(10)
                                .FontColor(CoverEyebrowColor);
                            column.Item().Text(content.Title)
                                .FontFamily(PdfFontBootstrap.DisplayFamily).FontSize(24).Bold()
                                .FontColor(CoverTitleColor).LineHeight(1.3f);
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
    /// The picture half of a spread: art to the bleed line and nothing else.
    ///
    /// No title, no running head and no caption band. On screen this page is the illustration
    /// and only the illustration — the words of the spread live on the page facing it — and the
    /// printed book is meant to be the same book. The band printed the caption a second time,
    /// over the picture, which is the exact thing giving the words their own page removed.
    ///
    /// The picture fills the sheet, as it fills its frame on screen.
    ///
    /// It used to be fitted, on the near-black the reader puts behind a page — the reasoning
    /// being that cropping a children's picture is how a character loses the top of their head.
    /// True in general, and wrong here: a 2:3 illustration on an A5 sheet is fitted with about
    /// seven millimetres to spare, which printed as two dark bands down the long edges of every
    /// picture in the book. Nobody has seen those bands on screen, where `.storybook-page-art`
    /// is `background-size: cover` and the picture is cropped to its frame.
    ///
    /// So it is cropped, by the same rule and by the same few per cent. The near-black remains
    /// for a page whose illustration never arrived, which is the one case where there is
    /// genuinely nothing to fill the sheet with.
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
                page.Content().Background(ScreenPageBackground);
                return;
            }

            page.Content().Background(ScreenPageBackground)
                .Image(FillSheet(pageContent.ImageBytes, PageArtFocusY, ScreenPageInk))
                .FitUnproportionally();
        });
    }

    /// <summary>
    /// Crops a picture to the shape of the sheet, the way `background-size: cover` crops it.
    ///
    /// Done to the pixels rather than in the layout because QuestPDF fits a picture inside its
    /// container and has no crop: asked to fill, it either letterboxes or distorts. Cropping
    /// first makes "fill this box" and "keep the proportions" the same instruction.
    ///
    /// It only ever removes pixels — the crop is taken at the picture's own resolution and never
    /// scaled up, so a 1024-wide illustration is not resampled to a print raster it has no detail
    /// for. <paramref name="focusY"/> is the point held still while the rest is trimmed away.
    ///
    /// <paramref name="backdropHex"/> is what any transparency is laid onto first. Untouched
    /// bytes used to reach the page with their alpha intact and the page colour showed through;
    /// re-encoding as JPEG throws alpha away, and JPEG's idea of a discarded alpha channel is
    /// black. The browser hit the same thing preparing portraits — a transparent PNG flattened
    /// onto nothing arrives as a silhouette — so it is laid onto the colour the page was going
    /// to paint anyway, and the result is what it always was.
    /// </summary>
    private byte[] FillSheet(byte[] source, float focusY, string backdropHex)
    {
        var sheetAspect = SheetWidthMm / SheetHeightMm;

        try
        {
            using var image = SourceImage.Load(source);

            var (targetWidth, targetHeight) = CropToAspect(image.Width, image.Height, sheetAspect);
            if (targetWidth <= 0 || targetHeight <= 0)
            {
                return source;
            }

            image.Mutate(x => x
                .Resize(new ResizeOptions
                {
                    Size = new SourceSize(targetWidth, targetHeight),
                    Mode = ResizeMode.Crop,
                    CenterCoordinates = new SourcePoint(0.5f, focusY),
                })
                .BackgroundColor(SourceColor.ParseHex(backdropHex)));

            using var buffer = new MemoryStream();
            // The encoder is named rather than reached through SaveAsJpeg, whose extension method
            // lives in the namespace this file deliberately does not import.
            image.Save(buffer, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 92 });
            return buffer.ToArray();
        }
        catch
        {
            // A picture this library cannot read is still the book's picture. Printed with a
            // letterbox it is a flawed page; dropped, it is a missing one.
            return source;
        }
    }

    /// <summary>
    /// The largest rectangle of the sheet's shape that fits inside a picture, without leaving it.
    ///
    /// The whole risk of cropping is here, and it is a comparison that reads correctly whichever
    /// way you write it: a picture proportionally *taller* than the sheet keeps its full width and
    /// loses height, and one proportionally wider does the opposite. Get it the wrong way round
    /// and the result is not a letterbox — it is a picture enlarged until the child's face leaves
    /// the page, which is the failure this crop was meant to avoid.
    /// </summary>
    internal static (int Width, int Height) CropToAspect(int width, int height, float sheetAspect)
    {
        if (width <= 0 || height <= 0 || sheetAspect <= 0)
        {
            return (width, height);
        }

        return (float)width / height > sheetAspect
            ? ((int)MathF.Round(height * sheetAspect), height)
            : (width, (int)MathF.Round(width / sheetAspect));
    }

    /// <summary>
    /// The darkening under the cover's words, as a stack of bands.
    ///
    /// The screen draws one gradient: clear through the upper half, then down to near-opaque at
    /// the foot. PDF has no gradient here, so it is stepped — at print resolution the bands are
    /// a third of a millimetre each and read as a wash rather than as steps.
    /// </summary>
    private void ComposeCoverWash(ColumnDescriptor column)
    {
        for (var i = 0; i < CoverWashBands; i++)
        {
            // The middle of the band, so the ramp is not a half-band darker than the screen's.
            var t = (i + 0.5f) / CoverWashBands;
            column.Item().Height(CoverWashHeightMm / CoverWashBands, Unit.Millimetre)
                .Background(CoverWashAt(t));
        }
    }

    /// <summary>
    /// The stylesheet's own stops: transparent where the wash begins, 0.42 just under half way
    /// down it, 0.88 at the foot.
    /// </summary>
    private static Color CoverWashAt(float t)
    {
        const float midpoint = 0.458f;
        const float midAlpha = 0.42f;
        const float footAlpha = 0.88f;

        var alpha = t <= midpoint
            ? midAlpha * (t / midpoint)
            : midAlpha + ((footAlpha - midAlpha) * ((t - midpoint) / (1f - midpoint)));

        return Color.FromHex($"#{(byte)MathF.Round(alpha * 255f):X2}{CoverWashInk}");
    }

    /// <summary>
    /// The prose half, set the way the reader sets it: a leaf of paper inside a ruled frame.
    ///
    /// This used to be a plain page — a coloured heading, a short accent rule, ranged-left body
    /// text and a running foot — which is a perfectly good page and not the page anybody had
    /// seen. On screen the words of a spread sit on the inside-cover treatment: cream stock, a
    /// broad border with two hairline rules inside it, the caption above in small capitals, the
    /// story centred in serif on a narrow measure, and the page number at the foot. A parent who
    /// reads the book on a screen and then opens the PDF is looking at the same book, so it is
    /// laid out from the same description.
    ///
    /// The colours are the screen's own rather than the theme's, exactly as they are in the
    /// stylesheet: this leaf is paper in every world, and the theme reaches the printed book
    /// through the pictures.
    /// </summary>
    private void ComposeProsePage(
        IDocumentContainer container,
        StoryPageDto pageContent,
        PrintStrings strings,
        int physicalPage,
        int storyPage,
        int totalStoryPages)
    {
        container.Page(page =>
        {
            ApplyProsePageGeometry(page, physicalPage);

            // The caption is what the screen prints above the prose, falling back to the title.
            // Reading the title first put a heading on a page that shows a caption.
            var eyebrow = FirstNonEmpty(pageContent.Caption, pageContent.Title);

            page.Content()
                .Background(PaperBackground)
                .Border(PaperBorderMm, Unit.Millimetre).BorderColor(PaperBorderColor)
                // The two rules the screen insets at 5% and 7% of the leaf.
                .Padding(PaperFrameOuterMm, Unit.Millimetre)
                .Border(0.3f).BorderColor(PaperRuleOuterColor)
                .Padding(PaperFrameInnerMm, Unit.Millimetre)
                .Border(0.3f).BorderColor(PaperRuleInnerColor)
                .Padding(PaperFrameInnerMm, Unit.Millimetre)
                /*
                  Layers, not a column with an extending first item.

                  The prose is centred in the leaf and the page number sits at its foot, which as
                  a column means one item that grows to fill and one that does not — and an item
                  told to fill inside a page is a page whose content is taller than the page, so
                  every prose leaf broke onto a second sheet and a sixteen-page book came out
                  twenty-six. A layer takes its height from the leaf it is drawn on and adds none.
                */
                .Layers(layers =>
                {
                    layers.PrimaryLayer().AlignMiddle().Column(inner =>
                    {
                        if (!string.IsNullOrWhiteSpace(eyebrow))
                        {
                            inner.Item().AlignCenter().PaddingBottom(5, Unit.Millimetre)
                                .Text(eyebrow.ToUpperInvariant())
                                .FontFamily(PdfFontBootstrap.BodyFamily).FontSize(9).Bold()
                                .LetterSpacing(0.1f).FontColor(PaperEyebrowColor);
                        }

                        inner.Item().Text(pageContent.Content)
                            .FontFamily(PdfFontBootstrap.DisplayFamily).FontSize(13)
                            .LineHeight(1.8f).FontColor(PaperTextColor).AlignCenter();
                    });

                    // Where the screen puts the page label: at the foot, centred, quiet.
                    layers.Layer().AlignBottom().AlignCenter()
                        .Text(strings.PageLabel(storyPage, totalStoryPages))
                        .FontFamily(PdfFontBootstrap.BodyFamily).FontSize(8)
                        .LetterSpacing(0.12f).FontColor(PaperFootColor);
                });
        });
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

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
                    // Capped, and fitted rather than stretched to the column width.
                    //
                    // FitWidth makes the height whatever the aspect ratio demands, so a portrait
                    // illustration came out one and a half times the text column tall, overflowed
                    // the page and made QuestPDF throw — no PDF at all for that book. It only
                    // ever fitted because every illustration used to be square.
                    column.Item().PaddingVertical(4)
                        .MaxHeight(LegacyImageMaxHeightMm, Unit.Millimetre)
                        .Image(pageContent.ImageBytes).FitArea();
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

    /// <summary>
    /// The back cover, set to match the reader's closing page.
    ///
    /// Same words, same order, same guide seeing the child off. The one thing that cannot be the
    /// same is the button: paper has no tap, so the invitation is a QR to the same address.
    /// </summary>
    private void ComposeBackCoverPage(
        IDocumentContainer container,
        PdfBookRequest request,
        ThemePalette palette,
        PrintStrings strings,
        string childName)
    {
        var guide = request.GuidePortrait ?? GuidePortraitFromDisk();
        var qr = QrPng(request.ContinueUrl);

        container.Page(page =>
        {
            page.Size(new PageSize(
                _layout.TrimWidthMm + (_layout.BleedMm * 2),
                _layout.TrimHeightMm + (_layout.BleedMm * 2),
                Unit.Millimetre));
            page.Margin(0);

            // Dark, like the closing page on screen — and it is the reason Beki can be printed
            // at all: his portrait carries its own deep backdrop, which on the book's pale paper
            // was a dark rectangle stuck to the page.
            page.PageColor(BackCoverInk);

            page.Content()
                .PaddingHorizontal(SafeInsetMm + _layout.BleedMm, Unit.Millimetre)
                .PaddingVertical(SafeInsetMm + _layout.BleedMm, Unit.Millimetre)
                .AlignMiddle()
                .Column(column =>
                {
                    column.Spacing(5, Unit.Millimetre);

                    column.Item().AlignCenter().Text("ADVENTRYA")
                        .FontFamily(PdfFontBootstrap.BodyFamily).SemiBold().FontSize(9)
                        .LetterSpacing(0.18f).FontColor(BackCoverMuted);

                    if (guide is { Length: > 0 })
                    {
                        column.Item().AlignCenter()
                            .MaxHeight(_layout.TrimHeightMm * 0.32f, Unit.Millimetre)
                            .Image(guide).FitArea();
                    }

                    column.Item().AlignCenter().Text(strings.BackTitle)
                        .FontFamily(PdfFontBootstrap.DisplayFamily).Bold().FontSize(16)
                        .FontColor(BackCoverText);

                    column.Item().AlignCenter().Text(strings.BackScan(childName))
                        .FontFamily(PdfFontBootstrap.BodyFamily).FontSize(11)
                        .LineHeight(1.45f).FontColor(BackCoverMuted);

                    if (qr is { Length: > 0 })
                    {
                        // On white, with a quiet zone. A QR printed straight onto the dark page
                        // is a QR no phone will read.
                        column.Item().PaddingTop(1, Unit.Millimetre).AlignCenter()
                            .Background(Colors.White).Padding(2.5f, Unit.Millimetre)
                            .Width(26, Unit.Millimetre).Height(26, Unit.Millimetre)
                            .Image(qr).FitArea();
                    }
                });
        });
    }

    /// <summary>The reader's closing page in print: the same near-black violet, cream and gold.</summary>
    private const string BackCoverInk = "#241A33";
    private const string BackCoverText = "#FFF8EB";
    private const string BackCoverMuted = "#C9BBD6";

    /// <summary>
    /// Beki's canonical portrait, read from the assets that ship with the app. Best effort: a
    /// back cover without him is still a back cover, and a missing file is not worth failing a
    /// book that is otherwise finished.
    /// </summary>
    private static byte[]? GuidePortraitFromDisk()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Beki", "beki-canonical-v1.png");
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The QR itself. No destination means no code rather than a code that goes nowhere.</summary>
    private static byte[]? QrPng(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        using var generator = new QRCoder.QRCodeGenerator();
        using var data = generator.CreateQrCode(url.Trim(), QRCoder.QRCodeGenerator.ECCLevel.Q);
        return new QRCoder.PngByteQRCode(data).GetGraphic(16);
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

    // ---- The reader's own colours ---------------------------------------

    /*
      Taken from the stylesheet the reader uses, not from the theme.

      A page of this book looks the same in every world: the picture carries the world, and the
      paper the words are set on is paper. These are the literal values behind `.storybook-page-content`
      and `.storybook-text-page`, so the printed leaf and the leaf on screen are the same leaf.
    */

    /// <summary>Behind an illustration that never arrived. `.storybook-page-content`.</summary>
    private const string ScreenPageInk = "#281B3F";
    private static readonly Color ScreenPageBackground = Color.FromHex(ScreenPageInk);

    // ---- The cover, from `.storybook-cover` -----------------------------

    /// <summary>
    /// The mark at the head of the cover.
    ///
    /// This is `t.story.storybook.brand`, which still reads ADVENTRYA while `BRAND_NAME` in the
    /// same frontend reads Beki — a spot the rename missed. It is copied rather than corrected
    /// because the printed cover and the cover on screen have to say the same word; change them
    /// together, and this is the one line to change on this side.
    /// </summary>
    private const string BrandMark = "ADVENTRYA";

    /// <summary>Under the cover art, where it is transparent. `.storybook-cover` background.</summary>
    private const string CoverInk = "#261738";

    /// <summary>The frame: `border: 7px solid #5c3c49` on the leaf.</summary>
    private static readonly Color CoverFrameColor = Color.FromHex("#5C3C49");

    /// <summary>
    /// The gold hairline inset inside it, `.storybook-cover:after` — rgba(244, 213, 152, 0.32).
    /// Hex here is AARRGGBB, alpha first, so 0.32 leads.
    /// </summary>
    private static readonly Color CoverHairlineColor = Color.FromHex("#52F4D598");

    private static readonly Color CoverBrandColor = Color.FromHex("#F2CD84");
    private static readonly Color CoverEyebrowColor = Color.FromHex("#F1CF8E");
    private static readonly Color CoverTitleColor = Color.FromHex("#FFF8EB");

    /// <summary>rgb(13, 7, 29) — the ink the screen's wash darkens towards.</summary>
    private const string CoverWashInk = "0D071D";

    /// <summary>The wash begins 52% down the cover, so it covers the lower 48% of it.</summary>
    private float CoverWashHeightMm => SheetHeightMm * 0.48f;

    /// <summary>Enough bands that the ramp is smooth at print resolution; more is wasted ink.</summary>
    private const int CoverWashBands = 48;

    /// <summary>7px of a 340px cover, at print scale, and its inner gold rule 4px in.</summary>
    private const float CoverFrameMm = 2.4f;
    private const float CoverFrameGapMm = 1.4f;

    /// <summary>The screen sets the cover's copy 9% in from the edge.</summary>
    private float CoverInsetMm => Math.Max(SafeInsetMm, SheetWidthMm * 0.09f);

    /// <summary>`background-position: 50% 48%` — a face sits above the middle of a portrait.</summary>
    private const float CoverArtFocusY = 0.48f;

    /// <summary>The page art has no such offset on screen; it is centred.</summary>
    private const float PageArtFocusY = 0.5f;

    /// <summary>Cream stock. `.storybook-text-page` background.</summary>
    private static readonly Color PaperBackground = Color.FromHex("#F5EAD7");

    /// <summary>The broad outer border of the leaf.</summary>
    private static readonly Color PaperBorderColor = Color.FromHex("#E7DBC4");

    /// <summary>The two hairlines inside it, the second fainter than the first.</summary>
    private static readonly Color PaperRuleOuterColor = Color.FromHex("#BC9070");
    private static readonly Color PaperRuleInnerColor = Color.FromHex("#D8BE9B");

    /// <summary>The caption above the prose, in small capitals.</summary>
    private static readonly Color PaperEyebrowColor = Color.FromHex("#9B6C4E");

    /// <summary>The story itself.</summary>
    private static readonly Color PaperTextColor = Color.FromHex("#51394D");

    /// <summary>The page number at the foot.</summary>
    private static readonly Color PaperFootColor = Color.FromHex("#8A736D");

    /// <summary>The 7px border of the screen leaf, at print scale.</summary>
    private const float PaperBorderMm = 2.4f;

    /// <summary>The screen insets its rules at 5% and 7% of the leaf; on A5 that is about this.</summary>
    private const float PaperFrameOuterMm = 7f;
    private const float PaperFrameInnerMm = 3f;

    // ---- Geometry -------------------------------------------------------

    private float SafeInsetMm => _layout.SafeMarginMm;

    /// <summary>The sheet as it leaves the press: the trim plus bleed on all four sides.</summary>
    private float SheetWidthMm => _layout.TrimWidthMm + (_layout.BleedMm * 2);
    private float SheetHeightMm => _layout.TrimHeightMm + (_layout.BleedMm * 2);

    /// <summary>
    /// The prose leaf runs to the bleed line, because the paper and its border are the design
    /// rather than a margin: a cream page with a ruled frame has to reach the trim or it prints
    /// as a small card floating on a coloured sheet. The gutter is kept as a nudge of the frame
    /// towards the outer edge, so the binding does not eat the rule on the spine side.
    /// </summary>
    private void ApplyProsePageGeometry(PageDescriptor page, int physicalPage)
    {
        page.Size(new PageSize(
            _layout.TrimWidthMm + (_layout.BleedMm * 2),
            _layout.TrimHeightMm + (_layout.BleedMm * 2),
            Unit.Millimetre));

        var isRecto = physicalPage % 2 == 1;
        page.MarginTop(_layout.BleedMm, Unit.Millimetre);
        page.MarginBottom(_layout.BleedMm, Unit.Millimetre);
        page.MarginLeft(isRecto ? _layout.BleedMm + _layout.GutterMm : _layout.BleedMm, Unit.Millimetre);
        page.MarginRight(isRecto ? _layout.BleedMm : _layout.BleedMm + _layout.GutterMm, Unit.Millimetre);

        page.PageColor(PaperBackground);
    }

    /// <summary>
    /// The most of a legacy page an illustration may take, leaving the title and prose room to
    /// sit under it. Derived from the trim so it survives a change of page size.
    /// </summary>
    private float LegacyImageMaxHeightMm =>
        (_layout.TrimHeightMm - (_layout.SafeMarginMm * 2)) * 0.5f;

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
    private sealed record PrintStrings(
        Func<string, string> Starring,
        Func<string, string> BelongsTo,
        Func<string, string> RunningHead,
        string BackTitle,
        Func<string, string> BackScan,
        Func<int, int, string> PageLabel)
    {
        public static PrintStrings For(string? language) =>
            (language ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "en" => new PrintStrings(
                    name => $"Starring {name}",
                    name => $"A story that belongs to {name.Trim()}",
                    name => $"{name}'s adventure",
                    "The adventure does not end here",
                    name => $"Scan and continue {name}'s journey in another world.",
                    (page, total) => $"Page {page} / {total}"),
                // Word for word what the reader's closing page says, so a family that reads the
                // book on paper and the book on a screen is read the same sentence.
                _ => new PrintStrings(
                    name => $"მთავარ როლში — {name}",
                    // `t.story.storybook.belongsTo`, down to the comma the screen sets.
                    name => $"ამბავი, რომელიც {name.Trim()}ს ეკუთვნის",
                    name => $"{GeorgianGenitive(name)} თავგადასავალი",
                    "თავგადასავალი აქ არ სრულდება",
                    name => $"დაასკანერე და გააგრძელე {name.Trim()}ს მოგზაურობა სხვა სამყაროში.",
                    (page, total) => $"გვერდი {page} / {total}")
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
