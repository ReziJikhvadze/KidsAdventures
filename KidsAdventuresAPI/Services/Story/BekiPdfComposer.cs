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
    /// <summary>
    /// <paramref name="theme"/> is the pack's theme, and it reaches the composer for exactly one
    /// reason: the endpapers can carry a per-theme asset when the partner supplies one. Optional
    /// and defaulted, because everything else in the book is theme-blind and every existing caller
    /// — tests and harnesses included — has no theme to give.
    /// </summary>
    byte[] Compose(
        MasterStory plan,
        byte[] coverImage,
        IReadOnlyList<BekiSpreadArtwork> spreads,
        string? theme = null);

    /// <summary>
    /// The same book as one image per page. For looking at: a PDF cannot be inspected by anything
    /// that does not already render PDFs, and a layout nobody can see is a layout nobody can fix.
    /// </summary>
    IReadOnlyList<byte[]> RenderPages(
        MasterStory plan,
        byte[] coverImage,
        IReadOnlyList<BekiSpreadArtwork> spreads,
        string? theme = null);
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
/// than an absence. The day the partner delivers, nothing here has to change: four settings on
/// <see cref="BekiPrintLayoutOptions"/> name files, and a named file that exists is placed full
/// bleed in place of the drawing, per page, one page at a time. The endpapers take a themed
/// template rather than a fixed name, because they are the one reusable leaf the handoff wants to
/// vary by book.
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

    /// <summary>
    /// The Continue Adventure module, in millimetres.
    ///
    /// One chip, sized here rather than left to the layout, because every number in it depends on
    /// every other: the text gets whatever the chip's inner width has left after the QR tile and
    /// the gap between them, and a relative split would make that width something only a rendered
    /// page could tell you — which is exactly the number <see cref="OutlinedText"/> now needs in
    /// advance to raster against.
    /// </summary>
    private const float CtaChipWidthMm = 118f;
    private const float CtaChipPaddingMm = 5f;
    private const float CtaChipGapMm = 5f;
    private const float CtaChipRadiusMm = 7f;

    /// <summary>
    /// The white border around the QR inside its tile. A code needs a quiet zone — four modules of
    /// blank on every side — or scanners hunting for its finder patterns give up. QRCoder already
    /// encodes those four modules into the PNG; this tile is the second guarantee, so the code
    /// keeps its margin even against a chip ground that is nearly the same value as the code.
    /// </summary>
    private const float QrQuietZoneMm = 2.5f;
    private const float QrTileRadiusMm = 2f;

    /// <summary>What the chip leaves for the CTA line once the QR and the gap are paid for.</summary>
    private const float CtaTextWidthMm =
        CtaChipWidthMm - (CtaChipPaddingMm * 2f) - CtaChipGapMm - ContinueQrSizeMm;

    /// <summary>
    /// The chip's own ground: the wash ink, mostly opaque. Spread 8's wash is built for the
    /// opposite side of the sheet and has faded to nothing by the time it reaches this corner, so
    /// the module brings its own quiet rather than borrowing one that is not there.
    /// </summary>
    private static readonly Color CtaChipInk = Color.FromHex("#C80D071D");

    /// <summary>
    /// The ink a semantic text block is set in when a raster is already drawing the visible
    /// glyphs — fully transparent, so the words are in the PDF's text layer and nowhere on it.
    /// </summary>
    private static readonly Color InvisibleInk = Color.FromHex("#00000000");

    /// <summary>
    /// The resolution the outlined-text raster is drawn at. Print resolution, so a glyph edge in
    /// the PNG is no coarser than one the printer's RIP would have made from the vector itself.
    /// </summary>
    private const int OutlinedTextRasterDpi = 300;

    /// <summary>
    /// How far past the text's own box the outline raster is allowed to spill, in points, on top
    /// of the outline width itself. The eight offset copies push ink up to one outline-width
    /// outside the box on every side, and a page sized exactly to the text would clip that ink at
    /// the trim; the extra point covers antialiasing on top of it. Paid for in the raster only —
    /// the placed image is offset back by the same amount, so the text's layout box is unchanged.
    /// </summary>
    private const float OutlineRasterBleedPt = 1f;

    /// <summary>The wash is the same picture for every spread that shares a side; built once.</summary>
    private readonly Dictionary<bool, byte[]> _washBySide = [];

    /// <summary>
    /// Outlined-text rasters, keyed by everything that decides their pixels. Bounded by the book
    /// being composed — this service is scoped, so an instance lives for one order — and the
    /// entries earn their keep twice over: <see cref="RenderPages"/> and <see cref="Compose"/> are
    /// both called on the same instance by the inspection path, and every block would otherwise be
    /// drawn from scratch a second time.
    /// </summary>
    private readonly Dictionary<string, OutlinedTextRaster?> _outlinedTextRasters = [];

    /// <summary>
    /// Partner-supplied art for the reusable pages, keyed by the path it was configured under.
    /// Null means "asked for, not there" and is cached too: a missing file is not worth a stat
    /// call per page, and the fallback is the drawn placeholder either way.
    /// </summary>
    private readonly Dictionary<string, byte[]?> _reusablePageAssets = [];

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
        IReadOnlyList<BekiSpreadArtwork> spreads,
        string? theme = null) =>
        Build(plan, coverImage, spreads, theme).GeneratePdf();

    public IReadOnlyList<byte[]> RenderPages(
        MasterStory plan,
        byte[] coverImage,
        IReadOnlyList<BekiSpreadArtwork> spreads,
        string? theme = null) =>
        Build(plan, coverImage, spreads, theme)
            .GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 96 })
            .ToList();

    private Document Build(
        MasterStory plan,
        byte[] coverImage,
        IReadOnlyList<BekiSpreadArtwork> spreads,
        string? theme)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        PdfFontBootstrap.EnsureRegistered();

        var bySpread = plan.Spreads.ToDictionary(spread => spread.Number);

        return Document.Create(document =>
        {
            ComposeCover(document, plan.Concept.Title, coverImage);
            ComposeEndpaper(document, theme);
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
            ComposeEndpaper(document, theme);
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

                // The title band is the full width between the safe margins, and the type is
                // centred inside it rather than the block being centred around the type. The two
                // put a single line in exactly the same place — but only the first gives
                // OutlinedText a width it can know before the page is laid out, which is what the
                // raster has to be built against. A title that wraps also now centres both of its
                // lines instead of ranging them left inside a centred box.
                layers.Layer()
                    .PaddingHorizontal(_layout.SafeMarginMm, Unit.Millimetre)
                    .PaddingBottom(_layout.SafeMarginMm * 1.6f, Unit.Millimetre)
                    .AlignBottom()
                    .Element(item => OutlinedText(
                        item, title, _layout.StoryFontSize * 2f, 1.25f,
                        TextColor, OutlineColor, CoverTitleWidthPt,
                        PdfFontBootstrap.DisplayFamily, centred: true));
            });
        });
    }

    /// <summary>
    /// An endpaper: a single leaf, tinted and quiet, the way a real hardcover binds a different
    /// paper stock behind its boards. Used front and back alike — the handoff calls for both, and
    /// neither carries anything from the plan, so one method composes either.
    ///
    /// The one reusable page with a per-theme slot: a book about the sea and a book about space
    /// can bind different papers, so the configured path is a template with a <c>{theme}</c>
    /// placeholder rather than a single file. No file, no theme, or a theme with no art of its own
    /// all land on the same drawn dot field.
    /// </summary>
    private void ComposeEndpaper(IDocumentContainer container, string? theme)
    {
        container.Page(page =>
        {
            ApplyGeometry(page, _layout.PageWidthMm, EndpaperTint);

            if (PlaceSuppliedPage(
                    page, ResolveThemeTemplate(_layout.EndpaperAssetPathTemplate, theme)))
            {
                return;
            }

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

            if (PlaceSuppliedPage(page, _layout.InvitationAssetPath))
            {
                return;
            }

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

            if (PlaceSuppliedPage(page, _layout.BackCoverAssetPath))
            {
                return;
            }

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
                    var innerPaddingMm = InnerPaddingMm;

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
                                TextColor, OutlineColor, StoryColumnWidthPt));

                            if (_layout.PrintEnglishToo && !string.IsNullOrWhiteSpace(spread.TextEn))
                            {
                                column.Item().Element(item => OutlinedText(
                                    item,
                                    spread.TextEn!,
                                    _layout.StoryFontSize * 0.82f,
                                    1.5f,
                                    EnglishTextColor,
                                    EnglishOutlineColor,
                                    StoryColumnWidthPt));
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
    /// The Continue Adventure block: held to the lower-right corner of the spread's right page,
    /// as its own half-and-half row rather than reusing the text row's
    /// <see cref="BekiPrintLayoutOptions.TextColumnShare"/> split — the CTA is a corner ornament,
    /// not a text column, and sizing it like one would either crowd the QR against the fold or
    /// leave it stranded in the middle of the page.
    ///
    /// The corner holds back by the safe margin <em>plus the bleed</em>, unlike the story text,
    /// whose padding is applied inside a row that already spans the bled sheet. Measured from the
    /// sheet edge, a bare safe margin puts the module 3mm closer to the trim than the number
    /// promises — fine for a wash, not fine for the one element on the page a scanner has to find.
    /// </summary>
    private void ComposeContinueAdventureCta(IContainer container)
    {
        var inset = _layout.SafeMarginMm + _layout.BleedMm;

        container.Row(row =>
        {
            row.RelativeItem(); // the left page: left empty so nothing here touches spread 8's text
            row.RelativeItem()
                .Padding(inset, Unit.Millimetre)
                .AlignBottom()
                .AlignRight()
                .Element(ComposeContinueAdventureChip);
        });
    }

    /// <summary>
    /// One branded module rather than two loose things in a corner.
    ///
    /// It used to be a right-ranged column: a line of type, a gap, a white square. Over eight
    /// different illustrations that reads as two unrelated objects that happen to have landed near
    /// each other — the type belonging to the story's wash, the QR belonging to nothing. A single
    /// rounded chip with its own soft ground makes the pair one object the eye takes in at once,
    /// and gives the code the contrast it needs on a corner the wash never reaches.
    ///
    /// Every dimension is fixed rather than relative: the CTA line's width has to be known before
    /// layout runs, because <see cref="OutlinedText"/> rasters the outline against exactly that
    /// width. See <see cref="CtaChipWidthMm"/>.
    /// </summary>
    private void ComposeContinueAdventureChip(IContainer container)
    {
        var hasQr = !string.IsNullOrWhiteSpace(_layout.EndingQrUrl);

        // A code that scans to nothing is worse than no code, so a blank URL drops the QR and the
        // chip closes up around the line — the same stance ComposeEnding takes on its own code.
        var textWidthMm = hasQr
            ? CtaTextWidthMm
            : CtaChipWidthMm - (CtaChipPaddingMm * 2f);

        container
            .Width(CtaChipWidthMm, Unit.Millimetre)
            .Shadow(new BoxShadowStyle
            {
                OffsetX = 0f,
                OffsetY = MmToPt(1f),
                Blur = MmToPt(3.5f),
                Spread = 0f,
                Color = Color.FromHex("#500D071D"),
            })
            .Background(CtaChipInk)
            .CornerRadius(CtaChipRadiusMm, Unit.Millimetre)
            .Padding(CtaChipPaddingMm, Unit.Millimetre)
            .Row(row =>
            {
                row.Spacing(CtaChipGapMm, Unit.Millimetre);

                row.RelativeItem().AlignMiddle().Element(item => OutlinedText(
                    item, _layout.ContinueCtaText, _layout.StoryFontSize * 0.78f, 1.3f,
                    TextColor, OutlineColor, MmToPt(textWidthMm)));

                if (!hasQr)
                {
                    return;
                }

                row.ConstantItem(ContinueQrSizeMm, Unit.Millimetre)
                    .AlignMiddle()
                    .Background(Colors.White)
                    .CornerRadius(QrTileRadiusMm, Unit.Millimetre)
                    .Padding(QrQuietZoneMm, Unit.Millimetre)
                    .Image(QrPng(_layout.EndingQrUrl))
                    .FitWidth();
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

            if (PlaceSuppliedPage(page, _layout.ClosingAssetPath))
            {
                return;
            }

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
    /// Story text with its own edge — drawn once as pixels, and present once as words.
    ///
    /// The wash quiets the artwork behind the words, but a wash is a bet on the picture — and a
    /// picture the model made bright everywhere wins that bet against the reader. The outline is
    /// the part that cannot lose: every glyph carries a dark rim of its own, so the type stays
    /// legible over whatever it landed on. QuestPDF has no stroke, so the rim is the text drawn
    /// eight more times on a small circle beneath itself — the standard faux outline, and it wraps
    /// identically because every copy is measured in the same box.
    ///
    /// **Why that stack no longer reaches the finished PDF.** Nine copies of a paragraph drawn as
    /// nine real text runs are nine paragraphs as far as the file is concerned: <c>pdftotext -raw</c>
    /// returned every sentence of every book nine times over, and so does anything else that reads
    /// a PDF for its words — a screen reader, a search index, a printer's preflight, a parent
    /// copying a line out. The picture was right and the document underneath it was nonsense.
    ///
    /// So the stack is now built in a throwaway one-page document of its own — same fonts, same
    /// eight offsets, same everything — sized exactly to this block's width, given a transparent
    /// ground and a height that follows its own content, and rendered to a PNG at print
    /// resolution. That PNG is what the page draws. Over it sits exactly one real text block, in
    /// fully transparent ink, at the same width and metrics: invisible, and the only thing in the
    /// file that claims to be words. The reader sees what they saw before; the document says each
    /// paragraph once.
    ///
    /// The invisible block is the primary layer, so the page's layout is still decided by the type
    /// rather than by a raster's rounded pixel height — the block occupies exactly the box it
    /// always did. The image rides in an unconstrained layer offset back by the outline's own
    /// bleed, which is the only way a raster that is deliberately larger than its box can sit in
    /// one without QuestPDF trying to fit it.
    ///
    /// <paramref name="blockWidthPt"/> has to be passed in rather than discovered: QuestPDF hands
    /// an element its width during layout, and the raster must exist before layout begins. Every
    /// caller therefore derives it from the same constants the surrounding layout is built from —
    /// see <see cref="StoryColumnWidthPt"/> and <see cref="CoverTitleWidthPt"/> — and a width that
    /// drifted from the real one would show up as a block wrapping differently from the words
    /// underneath it, which is why those two live next to the layout they mirror.
    ///
    /// One helper for every outlined line in the book — the spread text, the cover title and the
    /// spread-8 CTA all call through here. <paramref name="fontFamily"/> defaults to the body face
    /// because most callers are story or caption text; the cover title is the one caller that
    /// passes the display face, and the one that sets <paramref name="centred"/>.
    /// </summary>
    private void OutlinedText(
        IContainer container,
        string text,
        float fontSize,
        float lineHeight,
        Color fill,
        Color outline,
        float blockWidthPt,
        string fontFamily = PdfFontBootstrap.BodyFamily,
        bool centred = false)
    {
        // No outline asked for, nothing to outline, or a box too narrow to raster into: plain
        // text, which was already a single text run and needs no help from any of this.
        if (_layout.TextOutlineWidth <= 0f || string.IsNullOrWhiteSpace(text) || blockWidthPt <= 1f)
        {
            PlainText(container, text, fontSize, lineHeight, fill, fontFamily, centred);
            return;
        }

        var raster = OutlinedTextRasterFor(
            text, fontSize, lineHeight, fill, outline, fontFamily, blockWidthPt, centred);

        if (raster is null)
        {
            // The raster is how the words stay singular, not how they stay legible. If it could
            // not be built, the book still prints — with the old nine-copy stack, whose only sin
            // is being nine copies. A missing optimisation must never cost a paid order.
            DrawOutlineStack(container, text, fontSize, lineHeight, fill, outline, fontFamily, centred);
            return;
        }

        var bleed = OutlineBleedPt;

        container.Layers(layers =>
        {
            layers.Layer()
                .Unconstrained()
                .TranslateX(-bleed)
                .TranslateY(-bleed)
                .Width(raster.WidthPt)
                .Height(raster.HeightPt)
                .Image(raster.Png)
                .FitUnproportionally()
                .UseOriginalImage();

            layers.PrimaryLayer().Element(item =>
                PlainText(item, text, fontSize, lineHeight, InvisibleInk, fontFamily, centred));
        });
    }

    /// <summary>
    /// One block of type, with no outline of its own. Every path through
    /// <see cref="OutlinedText"/> sets its words through here — the visible fill when no outline
    /// is asked for, each of the nine copies inside the raster document, and the single invisible
    /// block that carries the semantics — so all of them share one set of metrics by construction
    /// rather than by three places remembering the same four properties.
    /// </summary>
    private static void PlainText(
        IContainer container, string text, float fontSize, float lineHeight,
        Color colour, string fontFamily, bool centred)
    {
        var block = container.Text(text)
            .FontFamily(fontFamily)
            .FontSize(fontSize)
            .LineHeight(lineHeight)
            .FontColor(colour);

        if (centred) block.AlignCenter();
    }

    /// <summary>
    /// The faux outline itself: eight offset copies on a circle of the outline's own radius, then
    /// the fill. Lives here because two callers need it — the raster document below, which is what
    /// ships, and <see cref="OutlinedText"/>'s fallback for when that document cannot be made.
    /// Everything it draws is real text, which is precisely why it never reaches the book's own
    /// pages any more.
    /// </summary>
    private void DrawOutlineStack(
        IContainer container, string text, float fontSize, float lineHeight,
        Color fill, Color outline, string fontFamily, bool centred)
    {
        var width = _layout.TextOutlineWidth;

        container.Layers(layers =>
        {
            for (var step = 0; step < 8; step++)
            {
                var angle = MathF.PI / 4f * step;
                layers.Layer()
                    .TranslateX(width * MathF.Cos(angle))
                    .TranslateY(width * MathF.Sin(angle))
                    .Element(item =>
                        PlainText(item, text, fontSize, lineHeight, outline, fontFamily, centred));
            }

            layers.PrimaryLayer().Element(item =>
                PlainText(item, text, fontSize, lineHeight, fill, fontFamily, centred));
        });
    }

    /// <summary>A built outlined-text block: the pixels, and the box they belong in.</summary>
    private sealed record OutlinedTextRaster(byte[] Png, float WidthPt, float HeightPt);

    /// <summary>
    /// How far the raster spills past the text's own box on every side. The outline itself reaches
    /// one outline-width out; the extra point is for the antialiasing on top of it.
    /// </summary>
    private float OutlineBleedPt => _layout.TextOutlineWidth + OutlineRasterBleedPt;

    /// <summary>Built once per distinct block, and reused for the rest of this book.</summary>
    private OutlinedTextRaster? OutlinedTextRasterFor(
        string text, float fontSize, float lineHeight, Color fill, Color outline,
        string fontFamily, float blockWidthPt, bool centred)
    {
        // A unit separator, because every part is free text and a comma is not: two blocks
        // whose fields ran together differently must never collide on one key.
        var key = string.Join(
            '\u001F',
            text, fontSize, lineHeight, fill.Hex, outline.Hex, fontFamily, blockWidthPt, centred);

        if (_outlinedTextRasters.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var built = BuildOutlinedTextRaster(
            text, fontSize, lineHeight, fill, outline, fontFamily, blockWidthPt, centred);

        _outlinedTextRasters[key] = built;
        return built;
    }

    /// <summary>
    /// A whole QuestPDF document for one paragraph.
    ///
    /// Extravagant-looking and the cheapest correct option available: rendering glyph outlines as
    /// vectors would mean reaching into a font file and stroking paths, which is a text-shaping
    /// engine and a new dependency, and the one thing this fix may not do is add a package. Asking
    /// QuestPDF to draw the same nine copies it always drew — into a page whose width is this
    /// block's width, whose ground is transparent, and whose height simply follows the content —
    /// gets pixels that are identical to what shipped yesterday, from the same layout code, with
    /// no second opinion about how Georgian wraps.
    ///
    /// <see cref="ContinuousSize"/> is what makes the height honest: the page grows to the text
    /// rather than the text being placed on a page, so the returned PNG's own dimensions are the
    /// block's dimensions and nothing has to be measured twice.
    ///
    /// Null on any trouble at all — the caller falls back to drawing the stack directly, and a
    /// book is never worth losing over a picture of its own words.
    /// </summary>
    private OutlinedTextRaster? BuildOutlinedTextRaster(
        string text, float fontSize, float lineHeight, Color fill, Color outline,
        string fontFamily, float blockWidthPt, bool centred)
    {
        var bleed = OutlineBleedPt;

        try
        {
            var block = Document.Create(document =>
            {
                document.Page(page =>
                {
                    page.ContinuousSize(blockWidthPt + (bleed * 2f), Unit.Point);
                    page.Margin(0);

                    // Nothing behind the glyphs: this PNG is laid over artwork, and a page colour
                    // here would be an opaque rectangle across the illustration.
                    page.PageColor(Colors.Transparent);

                    page.Content()
                        .Padding(bleed, Unit.Point)
                        .Element(item => DrawOutlineStack(
                            item, text, fontSize, lineHeight, fill, outline, fontFamily, centred));
                });
            });

            var pages = block
                .GenerateImages(new ImageGenerationSettings
                {
                    ImageFormat = ImageFormat.Png,
                    RasterDpi = OutlinedTextRasterDpi,
                })
                .ToList();

            // A block that paginated is a block whose height did not follow its content, which
            // means the two halves would land on top of each other. Fall back instead.
            if (pages.Count != 1 || pages[0].Length == 0)
            {
                return null;
            }

            var size = SixLabors.ImageSharp.Image.Identify(pages[0]);
            if (size.Width < 1 || size.Height < 1)
            {
                return null;
            }

            // Back to points at the DPI it was drawn at, so the image is placed at exactly the
            // size it was rendered for — 1:1 with the box the words occupy, not a fit-to-frame.
            return new OutlinedTextRaster(
                pages[0],
                size.Width * 72f / OutlinedTextRasterDpi,
                size.Height * 72f / OutlinedTextRasterDpi);
        }
        catch (Exception)
        {
            return null;
        }
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

    /// <summary>
    /// A code, with its quiet zone drawn into the PNG rather than assumed. QRCoder defaults that
    /// flag to true, and a default is exactly the kind of thing that changes under a version bump
    /// without anybody printing a test sheet — so it is written down. The white tile the code sits
    /// in adds a second margin on top of it; between them, a phone finds the finder patterns even
    /// with the chip's dark ground running right up to the edge of the white.
    /// </summary>
    private static byte[] QrPng(string url)
    {
        using var generator = new QRCoder.QRCodeGenerator();
        using var data = generator.CreateQrCode(url.Trim(), QRCoder.QRCodeGenerator.ECCLevel.Q);
        return new QRCoder.PngByteQRCode(data).GetGraphic(16, drawQuietZones: true);
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
        var end = Math.Min(WashSpanCeiling, textColumnShare * WashSpanFactor);
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
    /// How far past the text column the wash still has anything to say, as a share of the sheet,
    /// and the ceiling that share is never allowed to pass. Both were half again as large: the
    /// falloff used to run to 1.7× the column, which on a 33% column meant the wash was still
    /// visible more than a fifth of the way across the picture.
    /// </summary>
    private const float WashSpanFactor = 1.35f;
    private const float WashSpanCeiling = 0.75f;

    /// <summary>
    /// A readability aid, not a panel.
    ///
    /// The shape is unchanged and the strength is halved. At 0.92 peak this was a dark violet
    /// slab across a third of every spread — on bright artwork it read as a design element the
    /// illustrator had not been told about, and it was the single most visible thing wrong with a
    /// printed book. The prompt side now asks the model for a naturally light, airy reserved side,
    /// which is where the legibility is supposed to come from; and the outline around every glyph
    /// is the guarantee that holds regardless. This is only the third of those three, so it can
    /// afford to be something the eye does not resolve as an edge.
    ///
    /// The falloff still starts where the words stop rather than where they start: a ramp already
    /// fading under the last line fails on exactly the line a child is still reading.
    /// </summary>
    private static float AlphaAt(float distance, float textColumnShare, float end)
    {
        const float peak = 0.45f;
        const float shoulder = 0.30f;

        if (distance <= textColumnShare * 0.7f) return peak;

        if (distance <= textColumnShare)
        {
            var t = (distance - (textColumnShare * 0.7f)) / (textColumnShare * 0.3f);
            return peak - (t * (peak - shoulder));
        }

        if (distance >= end) return 0f;

        // Eased out, so the edge of the wash is never a line the eye can find.
        var fade = (distance - textColumnShare) / (end - textColumnShare);
        return shoulder * (1f - (fade * fade));
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

    /// <summary>Points from millimetres. Every layout number in this file is written in mm.</summary>
    private static float MmToPt(float mm) => mm / 25.4f * 72f;

    /// <summary>
    /// A sheet's full width in points, bleed included — the box QuestPDF actually lays out in,
    /// since <see cref="ApplyGeometry"/> sets the page to trim plus bleed and takes no margin.
    /// </summary>
    private float SheetWidthPt(float sheetWidthMm) => MmToPt(sheetWidthMm + (_layout.BleedMm * 2f));

    /// <summary>
    /// How far a spread's text column holds back on its inner edge — see <see cref="ComposeSpread"/>
    /// for why the fold side is not simply the safe margin. Read from two places now, so it is
    /// written in one.
    /// </summary>
    private float InnerPaddingMm => MathF.Max(_layout.SafeMarginMm, _layout.GutterZoneMm / 2f);

    /// <summary>
    /// The exact width, in points, of the box a spread's story text is set in. A mirror of the
    /// arithmetic <see cref="ComposeSpread"/>'s row performs — the relative item's share of the
    /// bled sheet, less the two paddings — and it exists because <see cref="OutlinedText"/> has to
    /// raster against that width before QuestPDF has computed it. The outer and inner paddings
    /// swap sides with the text but always sum the same, so one expression covers both sides.
    /// </summary>
    private float StoryColumnWidthPt =>
        (SheetWidthPt(_layout.SpreadWidthMm) * _layout.TextColumnShare)
        - MmToPt(_layout.SafeMarginMm)
        - MmToPt(InnerPaddingMm);

    /// <summary>
    /// The exact width, in points, of the cover title's band: the leaf between its safe margins.
    /// </summary>
    private float CoverTitleWidthPt =>
        SheetWidthPt(_layout.PageWidthMm) - (MmToPt(_layout.SafeMarginMm) * 2f);

    /// <summary>
    /// Puts partner-supplied art on a reusable page, full bleed, and says whether it did.
    ///
    /// The six reusable pages have always been placeholders waiting for real art, and this is the
    /// door that art comes through: configure a path, drop the file next to the binary, and the
    /// drawn version stops being used for that page without any other change. False means there
    /// was no file — or nothing readable in it — and the caller draws what it always drew, which
    /// is why a wrong path in configuration degrades to yesterday's book rather than a blank leaf.
    /// </summary>
    private bool PlaceSuppliedPage(PageDescriptor page, string? configuredPath)
    {
        var supplied = ReusablePageAsset(configuredPath);
        if (supplied is null)
        {
            return false;
        }

        // Extend before Fit: the content slot is only as tall as what it holds, and an image told
        // to fit an unconstrained box fits its own size. The endpaper motif has always been placed
        // this way for exactly that reason, and a partner's sheet has to bleed the same way.
        page.Content().Extend().Image(CropToSheet(supplied, _layout.PageWidthMm))
            .FitUnproportionally();
        return true;
    }

    /// <summary>
    /// Resolved against <see cref="AppContext.BaseDirectory"/> — the folder the app was published
    /// into — exactly as the fonts and the canonical Beki PNG are, and deliberately not against
    /// the working directory, which under a service host is wherever the host happened to start.
    /// An absolute configured path is taken as-is, which is what <see cref="Path.Combine(string,string)"/>
    /// already does with a rooted second part.
    ///
    /// Anything that is not a readable image is treated as no asset at all: a truncated upload or
    /// a PDF someone renamed to .png should cost that page its art, not the whole order.
    /// </summary>
    private byte[]? ReusablePageAsset(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return null;
        }

        if (_reusablePageAssets.TryGetValue(configuredPath, out var cached))
        {
            return cached;
        }

        byte[]? bytes = null;

        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, configuredPath);
            if (File.Exists(path))
            {
                bytes = File.ReadAllBytes(path);
                SixLabors.ImageSharp.Image.Identify(bytes);
            }
        }
        catch (Exception)
        {
            bytes = null;
        }

        _reusablePageAssets[configuredPath] = bytes;
        return bytes;
    }

    /// <summary>
    /// A configured path with its <c>{theme}</c> placeholder filled in.
    ///
    /// The theme reaches here as an enum's own name, so it is already tame — but it is being
    /// spliced into a file path, and the one rule for that is never to trust the value that got
    /// spliced. Anything that is not a letter, a digit, a dash or an underscore is dropped, which
    /// leaves no way to write a separator or a parent-directory hop into the result.
    /// </summary>
    private static string? ResolveThemeTemplate(string? template, string? theme)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return null;
        }

        var token = new string((theme ?? string.Empty)
                .Where(character =>
                    char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
                .ToArray())
            .ToLowerInvariant();

        return template.Replace("{theme}", token, StringComparison.OrdinalIgnoreCase);
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
