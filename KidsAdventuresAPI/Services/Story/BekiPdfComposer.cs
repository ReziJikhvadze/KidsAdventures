using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AdventurePacks.Api.Services.Story;

/// <summary>One finished spread: the picture, and the words that go over it.</summary>
public sealed record BekiSpreadArtwork(int SpreadNumber, byte[] Image);

public interface IBekiPdfComposer
{
    byte[] Compose(MasterStory plan, byte[] coverImage, IReadOnlyList<BekiSpreadArtwork> spreads);

    /// <summary>
    /// The same book as one image per page. For looking at: a PDF cannot be inspected by anything
    /// that does not already render PDFs, and a layout nobody can see is a layout nobody can fix.
    /// </summary>
    IReadOnlyList<byte[]> RenderPages(
        MasterStory plan, byte[] coverImage, IReadOnlyList<BekiSpreadArtwork> spreads);
}

/// <summary>
/// Sets a Beki-format book for print.
///
/// A separate composer from <see cref="Implementations.AdventurePdfService"/>, which keeps
/// printing A5 books exactly as it always has. The two formats do not differ in styling; they
/// differ in what a page *is*. The A5 book gives a picture its own leaf and the words the facing
/// one, so text never crosses artwork. This book has one illustration across the whole spread and
/// the story set over it, in the right third the illustrator was told to leave quiet.
///
/// The book is three kinds of page: the cover on a single leaf, eight spreads, and a closing
/// leaf that carries the sign-off and the rate-us QR. The spread is one PDF page here, not two.
/// Printers impose the fold themselves and a spread split into two files is a spread with a seam
/// down the middle of the picture — which is the one thing a continuous illustration exists to
/// avoid.
///
/// Every picture arrives at 3:2 — the only landscape shape the image model draws — and every
/// sheet here is wider than that, so the composer centre-crops each render to its sheet before
/// placing it. A crop keeps proportions where stretching would not; the illustration prompt
/// confines faces and action to the central band so the trim never takes anything the story
/// needs.
/// </summary>
public sealed class BekiPdfComposer(IOptions<BekiPrintLayoutOptions> options) : IBekiPdfComposer
{
    private readonly BekiPrintLayoutOptions _layout = options.Value;

    /// <summary>Cream, and the same one the reader sets its pages on.</summary>
    private static readonly Color TextColor = Color.FromHex("#FFF8EB");

    /// <summary>English, when printed, sits a step behind the Georgian rather than beside it.</summary>
    private static readonly Color EnglishTextColor = Color.FromHex("#FFF8EBB0");

    /// <summary>
    /// The wash under the words. The illustrator was asked to leave the text third quiet, and
    /// mostly does, but "quiet" is sky and mist rather than a flat colour — so the type sits on a
    /// gentle darkening that follows the same edge, which costs nothing where the picture is
    /// already dark and saves the line where it is not.
    /// </summary>
    private const string TextWashInk = "0D071D";

    /// <summary>The glyph outline and the wash are the same ink, so they read as one shadow.</summary>
    private static readonly Color OutlineColor = Color.FromHex("#" + TextWashInk);

    /// <summary>
    /// The outline under the English text carries the fill's own transparency. An opaque rim
    /// under a translucent fill shows through the glyph bodies and turns the quieter language
    /// muddy instead of quiet.
    /// </summary>
    private static readonly Color EnglishOutlineColor = Color.FromHex("#" + TextWashInk + "B0");

    /// <summary>The wash is the same picture for every spread that shares a side; built once.</summary>
    private readonly Dictionary<bool, byte[]> _washBySide = [];

    public byte[] Compose(
        MasterStory plan,
        byte[] coverImage,
        IReadOnlyList<BekiSpreadArtwork> spreads) =>
        Build(plan, coverImage, spreads).GeneratePdf();

    public IReadOnlyList<byte[]> RenderPages(
        MasterStory plan,
        byte[] coverImage,
        IReadOnlyList<BekiSpreadArtwork> spreads) =>
        Build(plan, coverImage, spreads)
            .GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 96 })
            .ToList();

    private Document Build(
        MasterStory plan,
        byte[] coverImage,
        IReadOnlyList<BekiSpreadArtwork> spreads)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        PdfFontBootstrap.EnsureRegistered();

        var bySpread = plan.Spreads.ToDictionary(spread => spread.Number);

        return Document.Create(document =>
        {
            ComposeCover(document, coverImage);

            foreach (var artwork in spreads.OrderBy(spread => spread.SpreadNumber))
            {
                if (!bySpread.TryGetValue(artwork.SpreadNumber, out var spread))
                {
                    // A picture with no words is still a page of the book; dropping it would
                    // silently shorten the story.
                    ComposeArtOnly(document, artwork.Image);
                    continue;
                }

                ComposeSpread(document, artwork.Image, spread);
            }

            ComposeEnding(document);
        });
    }

    /// <summary>
    /// The cover: a single leaf, half the spread, artwork to the bleed and nothing else. The
    /// title is part of the artwork brief rather than typeset here — the handoff keeps cover
    /// typography out of the AI image, and the layout keeps itself out of the cover.
    /// </summary>
    private void ComposeCover(IDocumentContainer container, byte[] image)
    {
        container.Page(page =>
        {
            ApplyGeometry(page, _layout.PageWidthMm);
            page.Content().Image(CropToSheet(image, _layout.PageWidthMm)).FitUnproportionally();
        });
    }

    /// <summary>A spread whose text went missing: artwork to the bleed and nothing else.</summary>
    private void ComposeArtOnly(IDocumentContainer container, byte[] image)
    {
        container.Page(page =>
        {
            ApplyGeometry(page, _layout.SpreadWidthMm);
            page.Content().Image(CropToSheet(image, _layout.SpreadWidthMm)).FitUnproportionally();
        });
    }

    private void ComposeSpread(IDocumentContainer container, byte[] image, StorySpread spread)
    {
        var textSide = Prompts.BekiSpreadRhythm.TextSideFor(spread.Number);
        var textOnLeft = textSide.Equals("left", StringComparison.OrdinalIgnoreCase);

        container.Page(page =>
        {
            ApplyGeometry(page, _layout.SpreadWidthMm);

            page.Content().Layers(layers =>
            {
                // Cropped to the sheet's own proportions, so filling the frame is exact rather
                // than a stretch.
                layers.PrimaryLayer().Image(CropToSheet(image, _layout.SpreadWidthMm))
                    .FitUnproportionally();

                // The wash runs wider than the text column so it has somewhere to fade out; a
                // gradient that ends where the words end is a panel with a soft edge, not a wash.
                // One shape across the whole sheet, with the fade written into the gradient's own
                // stops — see WashImage for why it is raster.
                layers.Layer().Extend().Image(WashFor(textOnLeft)).FitUnproportionally();

                layers.Layer().Row(row =>
                {
                    if (!textOnLeft) row.RelativeItem(1f - _layout.TextColumnShare);

                    row.RelativeItem(_layout.TextColumnShare)
                        .Padding(_layout.SafeMarginMm, Unit.Millimetre)
                        .AlignMiddle()
                        .Column(column =>
                        {
                            column.Spacing(10);

                            column.Item().Element(item => OutlinedText(
                                item, spread.Text, _layout.StoryFontSize, 1.55f,
                                TextColor, OutlineColor));

                            if (_layout.PrintEnglishToo && !string.IsNullOrWhiteSpace(spread.TextEn))
                            {
                                column.Item().Element(item => OutlinedText(
                                    item,
                                    spread.TextEn!,
                                    _layout.StoryFontSize * 0.82f,
                                    1.5f,
                                    EnglishTextColor,
                                    EnglishOutlineColor));
                            }
                        });

                    if (textOnLeft) row.RelativeItem(1f - _layout.TextColumnShare);
                });
            });
        });
    }

    /// <summary>
    /// The closing leaf — the handoff's P18: a short sign-off and the rate-us QR. Reusable, the
    /// same for every order, which is why nothing from the plan appears on it.
    /// </summary>
    private void ComposeEnding(IDocumentContainer container)
    {
        container.Page(page =>
        {
            ApplyGeometry(page, _layout.PageWidthMm);

            page.Content()
                .Padding(_layout.SafeMarginMm, Unit.Millimetre)
                .AlignMiddle()
                .Column(column =>
                {
                    column.Spacing(14);

                    column.Item().AlignCenter().Text(_layout.EndingLine)
                        .FontFamily(PdfFontBootstrap.DisplayFamily)
                        .FontSize(_layout.StoryFontSize * 1.25f)
                        .LineHeight(1.5f)
                        .FontColor(TextColor);

                    // A blank URL means no QR rather than a crash or a code that scans to
                    // nothing — the same stance the A5 book's composer takes. The caption goes
                    // with it; a caption for an absent code is an instruction to squint.
                    if (!string.IsNullOrWhiteSpace(_layout.EndingQrUrl))
                    {
                        // White, and padded: a QR needs its quiet zone and its contrast, and
                        // this page is dark. The padding is the quiet zone.
                        column.Item().AlignCenter()
                            .Width(46, Unit.Millimetre)
                            .Background(Colors.White)
                            .Padding(4, Unit.Millimetre)
                            .Image(QrPng(_layout.EndingQrUrl))
                            .FitWidth();

                        column.Item().AlignCenter().Text(_layout.EndingQrCaption)
                            .FontFamily(PdfFontBootstrap.BodyFamily)
                            .FontSize(_layout.StoryFontSize * 0.8f)
                            .FontColor(TextColor);
                    }
                });
        });
    }

    /// <summary>
    /// Story text with its own edge.
    ///
    /// The wash quiets the artwork behind the words, but a wash is a bet on the picture — and a
    /// picture the model made bright everywhere wins that bet against the reader. The outline is
    /// the part that cannot lose: every glyph carries a dark rim of its own, so the type stays
    /// legible over whatever it landed on. QuestPDF has no stroke, so the rim is the text drawn
    /// eight more times on a small circle beneath itself — the standard faux outline, and it
    /// wraps identically because every copy is measured in the same box. The copies sit in
    /// plain layers, so only the primary fill decides layout.
    /// </summary>
    private void OutlinedText(
        IContainer container, string text, float fontSize, float lineHeight,
        Color fill, Color outline)
    {
        var width = _layout.TextOutlineWidth;
        if (width <= 0f)
        {
            container.Text(text)
                .FontFamily(PdfFontBootstrap.BodyFamily)
                .FontSize(fontSize)
                .LineHeight(lineHeight)
                .FontColor(fill);
            return;
        }

        container.Layers(layers =>
        {
            for (var step = 0; step < 8; step++)
            {
                var angle = MathF.PI / 4f * step;
                layers.Layer()
                    .TranslateX(width * MathF.Cos(angle))
                    .TranslateY(width * MathF.Sin(angle))
                    .Text(text)
                    .FontFamily(PdfFontBootstrap.BodyFamily)
                    .FontSize(fontSize)
                    .LineHeight(lineHeight)
                    .FontColor(outline);
            }

            layers.PrimaryLayer().Text(text)
                .FontFamily(PdfFontBootstrap.BodyFamily)
                .FontSize(fontSize)
                .LineHeight(lineHeight)
                .FontColor(fill);
        });
    }

    /// <summary>The wash for a side, built the first time that side is asked for.</summary>
    private byte[] WashFor(bool textOnLeft)
    {
        if (_washBySide.TryGetValue(textOnLeft, out var cached)) return cached;

        var wash = WashImage(textOnLeft, _layout.TextColumnShare);
        _washBySide[textOnLeft] = wash;
        return wash;
    }

    /// <summary>
    /// The centre of the picture, at the sheet's own shape.
    ///
    /// Renders are 3:2 and every sheet here is wider, so fitting without a crop means either
    /// squashing the picture by a third or letterboxing the bleed. The crop keeps the middle and
    /// gives the trim the top and bottom bands — the same bands the illustration prompt tells
    /// the model to spend on sky and ground. An image already at the sheet's shape passes
    /// through untouched, which is also what keeps the tests' one-pixel artwork valid.
    /// </summary>
    private byte[] CropToSheet(byte[] png, float sheetWidthMm)
    {
        var targetRatio = (sheetWidthMm + (_layout.BleedMm * 2))
            / (_layout.SpreadHeightMm + (_layout.BleedMm * 2));

        using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(png);

        var width = image.Width;
        var height = image.Height;
        var cropWidth = width;
        var cropHeight = height;

        if ((float)width / height > targetRatio)
        {
            cropWidth = Math.Clamp((int)MathF.Round(height * targetRatio), 1, width);
        }
        else
        {
            cropHeight = Math.Clamp((int)MathF.Round(width / targetRatio), 1, height);
        }

        if (cropWidth == width && cropHeight == height) return png;

        image.Mutate(ctx => ctx.Crop(new SixLabors.ImageSharp.Rectangle(
            (width - cropWidth) / 2, (height - cropHeight) / 2, cropWidth, cropHeight)));

        using var buffer = new MemoryStream();
        image.Save(buffer, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        return buffer.ToArray();
    }

    private static byte[] QrPng(string url)
    {
        using var generator = new QRCoder.QRCodeGenerator();
        using var data = generator.CreateQrCode(url.Trim(), QRCoder.QRCodeGenerator.ECCLevel.Q);
        return new QRCoder.PngByteQRCode(data).GetGraphic(16);
    }

    /// <summary>
    /// The wash, drawn as pixels.
    ///
    /// Third attempt, and the first that works. Thirty-two adjacent rectangles left hairline seams
    /// where their edges met — thin dark stripes down the artwork, exactly like a border. An SVG
    /// linear gradient has no seams to leave, but QuestPDF draws SVG through Skia's parser and
    /// that parser ignores <c>linearGradient</c>: the element rendered as nothing at all, which
    /// looked like a wash that was merely too weak until the alpha was doubled and nothing changed.
    ///
    /// A raster image has neither problem. One row of pixels is enough — the sheet stretches it —
    /// and the ramp is computed rather than approximated, so there is nothing to seam and nothing
    /// for a parser to decline.
    /// </summary>
    private static byte[] WashImage(bool textOnLeft, float textColumnShare)
    {
        var end = Math.Min(0.92f, textColumnShare * 1.7f);
        const int width = 1024;

        using var image = new SixLabors.ImageSharp.Image<Rgba32>(width, 2);
        var ink = Convert.ToInt32(TextWashInk, 16);
        var (r, g, b) = ((byte)(ink >> 16), (byte)((ink >> 8) & 0xFF), (byte)(ink & 0xFF));

        for (var x = 0; x < width; x++)
        {
            // Measured from the text edge, whichever side that is.
            var t = (x + 0.5f) / width;
            var distance = textOnLeft ? t : 1f - t;
            var alpha = (byte)MathF.Round(AlphaAt(distance, textColumnShare, end) * 255f);

            image[x, 0] = new Rgba32(r, g, b, alpha);
            image[x, 1] = new Rgba32(r, g, b, alpha);
        }

        using var buffer = new MemoryStream();
        image.Save(buffer, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        return buffer.ToArray();
    }

    /// <summary>
    /// Nearly full strength across the whole text column, then away.
    ///
    /// A ramp that has already begun fading where the last line sits fails on exactly the line a
    /// child is still reading, so the falloff starts where the words stop rather than where they
    /// start. White type over sunlit green is the case that decides this, not a night scene.
    /// </summary>
    private static float AlphaAt(float distance, float textColumnShare, float end)
    {
        const float peak = 0.92f;

        if (distance <= textColumnShare * 0.7f) return peak;

        if (distance <= textColumnShare)
        {
            var t = (distance - (textColumnShare * 0.7f)) / (textColumnShare * 0.3f);
            return peak - (t * (peak - 0.70f));
        }

        if (distance >= end) return 0f;

        // Eased out, so the edge of the wash is never a line the eye can find.
        var fade = (distance - textColumnShare) / (end - textColumnShare);
        return 0.70f * (1f - (fade * fade));
    }

    /// <summary>One geometry, two widths: the spread, and the single leaf that is half of it.</summary>
    private void ApplyGeometry(PageDescriptor page, float widthMm)
    {
        page.Size(new PageSize(
            widthMm + (_layout.BleedMm * 2),
            _layout.SpreadHeightMm + (_layout.BleedMm * 2),
            Unit.Millimetre));

        page.Margin(0);
        page.PageColor(Color.FromHex("#281B3F"));
    }
}
