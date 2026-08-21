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
/// the story set over it, in the column the illustrator was told to leave quiet.
///
/// Fourteen pages, in the hardcover's own order: the cover; a front endpaper; P1, an invitation;
/// eight story spreads; P18, the closing leaf; a back endpaper; and the back cover. A spread is
/// one PDF page here, not two — printers impose the fold themselves, and a spread split into two
/// files is a spread with a seam down the middle of the picture, the one thing a continuous
/// illustration exists to avoid.
///
/// Six of those fourteen pages are reusable rather than per-customer: both endpapers, P1, P18 and
/// the back cover are the same for every order, the way a real hardcover binds identical papers
/// and boards around whatever story sits inside them. Nothing from <em>this</em> book's plan
/// reaches them. The partner has not supplied finished art for any of the six yet, so each is
/// built here from QuestPDF primitives — a tinted ground, the canonical Beki PNG, a code-drawn
/// motif — rather than left blank. A placeholder page still has to look like a decision rather
/// than an absence; the slots are shaped so a real asset can drop in behind the same layout the
/// day the partner delivers one.
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

    /// <summary>Every page's ground, unless a page — an endpaper — asks for its own.</summary>
    private static readonly Color PageInk = Color.FromHex("#281B3F");

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

    /// <summary>
    /// The endpaper's own paper tone — lighter and warmer than the spreads' dark ground, the way
    /// a real hardcover binds its endpapers from a different stock than its pages. Placeholder
    /// until the partner supplies real endpaper art; the tint is what keeps a blank leaf reading
    /// as a considered part of the book rather than a page the composer forgot.
    /// </summary>
    private static readonly Color EndpaperTint = Color.FromHex("#F3E7D2");

    /// <summary>Roughly the handoff's ~22–26mm ask for the spread-8 Continue Adventure code.</summary>
    private const float ContinueQrSizeMm = 24f;

    /// <summary>The wash is the same picture for every spread that shares a side; built once.</summary>
    private readonly Dictionary<bool, byte[]> _washBySide = [];

    /// <summary>
    /// The endpaper motif, front and back alike — one quiet dot field, built once and stamped on
    /// both leaves. There is nothing per-order about it, so there is nothing to key it by.
    /// </summary>
    private byte[]? _endpaperMotif;

    /// <summary>
    /// The canonical Beki PNG, read once per composer instance and reused everywhere the
    /// placeholder pages need the character rather than the story's own artwork. Null means the
    /// file was not found — a missing placeholder asset should drop the picture, not the book.
    /// </summary>
    private byte[]? _bekiVisual;
    private bool _bekiVisualLoaded;

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
            ComposeCover(document, plan.Concept.Title, coverImage);
            ComposeEndpaper(document);
            ComposeInvitation(document);

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
            ComposeEndpaper(document);
            ComposeBackCover(document);
        });
    }

    /// <summary>
    /// The cover: a single leaf, half the spread, artwork to the bleed, and the title set over
    /// it. The title used to be part of the artwork brief rather than typeset here — the handoff
    /// kept cover typography out of the AI image entirely — but the cover brief also promises a
    /// calm, clean band the model leaves free of anything busy, and that band is exactly where a
    /// short line of display type belongs. Bottom-centre, outlined the same way the story text
    /// is, and nothing else: one line is a title, and a second thing on the cover is a subtitle
    /// nobody asked for.
    /// </summary>
    private void ComposeCover(IDocumentContainer container, string title, byte[] image)
    {
        container.Page(page =>
        {
            ApplyGeometry(page, _layout.PageWidthMm);

            page.Content().Layers(layers =>
            {
                layers.PrimaryLayer().Image(CropToSheet(image, _layout.PageWidthMm))
                    .FitUnproportionally();

                layers.Layer()
                    .PaddingHorizontal(_layout.SafeMarginMm, Unit.Millimetre)
                    .PaddingBottom(_layout.SafeMarginMm * 1.6f, Unit.Millimetre)
                    .AlignBottom()
                    .AlignCenter()
                    .Element(item => OutlinedText(
                        item, title, _layout.StoryFontSize * 2f, 1.25f,
                        TextColor, OutlineColor, PdfFontBootstrap.DisplayFamily));
            });
        });
    }

    /// <summary>
    /// An endpaper: a single leaf, tinted and quiet, the way a real hardcover binds a different
    /// paper stock behind its boards. Used front and back alike — the handoff calls for both, and
    /// neither carries anything from the plan, so one method composes either.
    /// </summary>
    private void ComposeEndpaper(IDocumentContainer container)
    {
        container.Page(page =>
        {
            ApplyGeometry(page, _layout.PageWidthMm, EndpaperTint);
            page.Content().Extend().Image(EndpaperMotif()).FitUnproportionally();
        });
    }

    /// <summary>
    /// P1, the invitation: the character before the character's story. Reusable across every
    /// order, like the endpapers either side of it — the handoff's P1 is a fixed page, not a
    /// generated one, so nothing here reads the plan beyond what <see cref="Compose"/> already
    /// needed for the cover.
    /// </summary>
    private void ComposeInvitation(IDocumentContainer container)
    {
        container.Page(page =>
        {
            ApplyGeometry(page, _layout.PageWidthMm);

            page.Content()
                .Padding(_layout.SafeMarginMm, Unit.Millimetre)
                .AlignMiddle()
                .Column(column =>
                {
                    column.Spacing(16);

                    var visual = BekiVisual();
                    if (visual is not null)
                    {
                        column.Item().AlignCenter().Width(58, Unit.Millimetre)
                            .Image(visual).FitWidth();
                    }

                    column.Item().AlignCenter().Text(_layout.InvitationLine)
                        .FontFamily(PdfFontBootstrap.DisplayFamily)
                        .FontSize(_layout.StoryFontSize * 1.3f)
                        .LineHeight(1.5f)
                        .FontColor(TextColor);
                });
        });
    }

    /// <summary>
    /// The back cover: quiet on purpose. A cover carries the illustration the customer paid for;
    /// the back of the book carries none of that, so it gets the same tinted ground the endpapers
    /// do, a small mark, and the address — nothing a browsing shelf needs more than that.
    /// </summary>
    private void ComposeBackCover(IDocumentContainer container)
    {
        container.Page(page =>
        {
            ApplyGeometry(page, _layout.PageWidthMm);

            page.Content()
                .AlignMiddle()
                .Column(column =>
                {
                    column.Spacing(10);

                    var visual = BekiVisual();
                    if (visual is not null)
                    {
                        column.Item().AlignCenter().Width(24, Unit.Millimetre)
                            .Image(visual).FitWidth();
                    }

                    // Literal rather than an option: nothing before this needed the brand
                    // address configurable, and adding a setting nobody will ever change is a
                    // setting somebody eventually has to explain.
                    column.Item().AlignCenter().Text("beki.ge")
                        .FontFamily(PdfFontBootstrap.BodyFamily)
                        .FontSize(_layout.StoryFontSize)
                        .FontColor(TextColor);
                });
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
                    // Two edges, two jobs. The outer edge — away from the fold — only ever needs
                    // the ordinary safe margin. The inner edge sits over the low-information band
                    // the fold claims, so it holds back by half the gutter zone instead whenever
                    // that is the larger number, keeping the column inside [safe margin … page
                    // width − gutter/2] measured from its own outer edge regardless of how wide
                    // TextColumnShare is asked to be.
                    var outerPaddingMm = _layout.SafeMarginMm;
                    var innerPaddingMm = MathF.Max(_layout.SafeMarginMm, _layout.GutterZoneMm / 2f);

                    if (!textOnLeft) row.RelativeItem(1f - _layout.TextColumnShare);

                    row.RelativeItem(_layout.TextColumnShare)
                        .PaddingVertical(outerPaddingMm, Unit.Millimetre)
                        .PaddingLeft(textOnLeft ? outerPaddingMm : innerPaddingMm, Unit.Millimetre)
                        .PaddingRight(textOnLeft ? innerPaddingMm : outerPaddingMm, Unit.Millimetre)
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

                // The spec's spread-8 rule: the story ends here, and this is where the book asks
                // for more of itself. Text is forced left on this one spread (BekiSpreadRhythm)
                // precisely so this corner is free for it.
                if (spread.Number == BookFormat.SpreadCount)
                {
                    layers.Layer().Element(ComposeContinueAdventureCta);
                }
            });
        });
    }

    /// <summary>
    /// The Continue Adventure block: a short invitation and a QR, held to the lower-right corner
    /// of the spread's right page. Drawn as its own half-and-half row rather than reusing the
    /// text row's <see cref="BekiPrintLayoutOptions.TextColumnShare"/> split — the CTA is a corner
    /// ornament, not a text column, and sizing it like one would either crowd the QR against the
    /// fold or leave it stranded in the middle of the page.
    /// </summary>
    private void ComposeContinueAdventureCta(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem(); // the left page: left empty so nothing here touches spread 8's text
            row.RelativeItem()
                .Padding(_layout.SafeMarginMm, Unit.Millimetre)
                .AlignBottom()
                .AlignRight()
                .Column(column =>
                {
                    column.Spacing(6);

                    column.Item().AlignRight().Width(74, Unit.Millimetre).Element(item => OutlinedText(
                        item, _layout.ContinueCtaText, _layout.StoryFontSize * 0.78f, 1.3f,
                        TextColor, OutlineColor));

                    // White and padded, same as the closing leaf's QR: the wash is heaviest over
                    // spread 8's left-set text and fades out from there, so by the time it
                    // reaches this corner — the far right of the right page — there is little to
                    // none of it left. The code needs its own contrast rather than borrowing a
                    // wash built for the opposite side of the spread.
                    column.Item().AlignRight()
                        .Width(ContinueQrSizeMm, Unit.Millimetre)
                        .Background(Colors.White)
                        .Padding(2, Unit.Millimetre)
                        .Image(QrPng(_layout.EndingQrUrl))
                        .FitWidth();
                });
        });
    }

    /// <summary>
    /// The closing leaf — the handoff's P18: the Beki visual, a short sign-off and the rate-us
    /// QR. Reusable, the same for every order, which is why nothing from the plan appears on it.
    /// The QR now points at <see cref="BekiPrintLayoutOptions.ReviewQrUrl"/> rather than
    /// <see cref="BekiPrintLayoutOptions.EndingQrUrl"/> — that property moved to spread 8's
    /// Continue Adventure code, and this leaf kept its own destination.
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

                    var visual = BekiVisual();
                    if (visual is not null)
                    {
                        column.Item().AlignCenter().Width(32, Unit.Millimetre)
                            .Image(visual).FitWidth();
                    }

                    column.Item().AlignCenter().Text(_layout.EndingLine)
                        .FontFamily(PdfFontBootstrap.DisplayFamily)
                        .FontSize(_layout.StoryFontSize * 1.25f)
                        .LineHeight(1.5f)
                        .FontColor(TextColor);

                    // A blank URL means no QR rather than a crash or a code that scans to
                    // nothing — the same stance the A5 book's composer takes. The caption goes
                    // with it; a caption for an absent code is an instruction to squint.
                    if (!string.IsNullOrWhiteSpace(_layout.ReviewQrUrl))
                    {
                        // White, and padded: a QR needs its quiet zone and its contrast, and
                        // this page is dark. The padding is the quiet zone.
                        column.Item().AlignCenter()
                            .Width(46, Unit.Millimetre)
                            .Background(Colors.White)
                            .Padding(4, Unit.Millimetre)
                            .Image(QrPng(_layout.ReviewQrUrl))
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
    ///
    /// One helper for every outlined line in the book — the spread text, the cover title and the
    /// spread-8 CTA all call through here rather than each carrying its own copy of the eight-step
    /// loop. <paramref name="fontFamily"/> defaults to the body face because most callers are
    /// story or caption text; the cover title is the one caller that passes the display face.
    /// </summary>
    private void OutlinedText(
        IContainer container, string text, float fontSize, float lineHeight,
        Color fill, Color outline, string fontFamily = PdfFontBootstrap.BodyFamily)
    {
        var width = _layout.TextOutlineWidth;
        if (width <= 0f)
        {
            container.Text(text)
                .FontFamily(fontFamily)
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
                    .FontFamily(fontFamily)
                    .FontSize(fontSize)
                    .LineHeight(lineHeight)
                    .FontColor(outline);
            }

            layers.PrimaryLayer().Text(text)
                .FontFamily(fontFamily)
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

    /// <summary>
    /// One geometry, two widths: the spread, and the single leaf that is half of it. Every page
    /// shares the same dark ground by default; the endpapers are the one page kind that overrides
    /// it, which is why the colour is a parameter here rather than baked into the method.
    /// </summary>
    private void ApplyGeometry(PageDescriptor page, float widthMm, Color? pageColor = null)
    {
        page.Size(new PageSize(
            widthMm + (_layout.BleedMm * 2),
            _layout.SpreadHeightMm + (_layout.BleedMm * 2),
            Unit.Millimetre));

        page.Margin(0);
        page.PageColor(pageColor ?? PageInk);
    }

    /// <summary>
    /// The canonical Beki PNG, read from disk once and cached on the instance. Missing means a
    /// placeholder page loses its picture, not the book — the same "absent rather than broken"
    /// stance <see cref="ComposeEnding"/> already takes with a blank QR URL.
    /// </summary>
    private byte[]? BekiVisual()
    {
        if (_bekiVisualLoaded) return _bekiVisual;

        var path = Path.Combine(AppContext.BaseDirectory, BekiIdentity.ReferenceAssetPath);
        _bekiVisual = File.Exists(path) ? File.ReadAllBytes(path) : null;
        _bekiVisualLoaded = true;
        return _bekiVisual;
    }

    /// <summary>
    /// The endpaper's motif, built the first time either endpaper asks for it. Sized to the
    /// leaf's own aspect ratio rather than a fixed canvas — <see cref="ComposeEndpaper"/> stretches
    /// this unproportionally to fill the page the same way the artwork wash does, and a raster
    /// built at the wrong ratio turns every round dot into an ellipse before a single pixel of
    /// page reaches the reader.
    /// </summary>
    private byte[] EndpaperMotif()
    {
        var leafRatio = (_layout.PageWidthMm + (_layout.BleedMm * 2))
            / (_layout.SpreadHeightMm + (_layout.BleedMm * 2));
        return _endpaperMotif ??= BuildEndpaperMotif(leafRatio);
    }

    /// <summary>
    /// A quiet field of dots, drawn as pixels rather than through QuestPDF's own shape or SVG
    /// primitives — the wash above already found that Skia's SVG parser silently drops gradients,
    /// and a raster is the one drawing surface in this file with no parser between the code and
    /// the page to disagree with it. Every other row of dots sits half a spacing over from the
    /// one before, a brick offset rather than a grid, so the pattern reads as scattered rather
    /// than ruled — a placeholder that looks chosen, not a grid nobody finished styling.
    /// </summary>
    private static byte[] BuildEndpaperMotif(float leafRatio)
    {
        const int width = 880;
        var height = Math.Max(1, (int)MathF.Round(width / leafRatio));
        const int spacing = 64;
        const float dotRadius = 5f;
        const byte peakAlpha = 40;

        using var image = new SixLabors.ImageSharp.Image<Rgba32>(width, height);
        var ink = Convert.ToInt32(TextWashInk, 16);
        var (r, g, b) = ((byte)(ink >> 16), (byte)((ink >> 8) & 0xFF), (byte)(ink & 0xFF));

        for (var y = 0; y < height; y++)
        {
            // The offset comes from the same row the pixel snaps to. Deriving it from floor
            // division while nearestY rounds would give a row's upper and lower halves different
            // offsets, splitting every staggered dot into two displaced semicircles.
            var nearestRow = (int)MathF.Round((float)y / spacing);
            var rowOffset = nearestRow % 2 == 0 ? 0 : spacing / 2;
            var nearestY = nearestRow * (float)spacing;

            for (var x = 0; x < width; x++)
            {
                var nearestX = (MathF.Round((x - rowOffset) / (float)spacing) * spacing) + rowOffset;
                var dx = x - nearestX;
                var dy = y - nearestY;
                var distance = MathF.Sqrt((dx * dx) + (dy * dy));

                // A soft-edged dot rather than a hard-edged one: an aliased circle at this size
                // is a visible stair-step, and the whole point of the motif is to not draw the
                // eye.
                var coverage = Math.Clamp(dotRadius - distance + 0.5f, 0f, 1f);
                var alpha = (byte)MathF.Round(peakAlpha * coverage);

                image[x, y] = new Rgba32(r, g, b, alpha);
            }
        }

        using var buffer = new MemoryStream();
        image.Save(buffer, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        return buffer.ToArray();
    }
}
