using System.Collections.Concurrent;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Pdf;
using AdventurePacks.Api.Services.Story.Composite;
using AdventurePacks.Api.Services.Story.Composite.Poses;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AdventurePacks.Api.Services.Story;

/// <summary>One finished spread: the picture, and the words that go over it.</summary>
public sealed record BekiSpreadArtwork(int SpreadNumber, byte[] Image);

public sealed record BekiBookPersonalization(string ChildName, int Age, DateTime Date, string Theme, string WorldName);

public interface IBekiPdfComposer
{
    /// <summary>
    /// <paramref name="personalization"/> carries what the intro spread prints and which approved
    /// theme background it is built on: the child's name and age, the purchase date, and the world.
    ///
    /// Optional in the signature and required in fact. It stays optional so that every caller which
    /// only wants a page count keeps compiling, but a book composed without it cannot resolve an
    /// approved intro background and stops with <c>LAYOUT_FAILED</c> rather than printing a generic
    /// one — R11's rule, and the reason the shipped book had the wrong intro spread.
    /// </summary>
    byte[] Compose(
        MasterStory plan,
        byte[] coverImage,
        IReadOnlyList<BekiSpreadArtwork> spreads,
        BekiBookPersonalization? personalization = null);

    /// <summary>
    /// The production print interior: the twelve interior spreads and nothing else — no cover
    /// face, no back-cover face.
    ///
    /// A separate artifact because the supplier's audit rejected the alternative outright: the
    /// cover is a continuous back-spine-front wrap whose geometry comes from the printer's
    /// dieline, and a 230x210 leaf bound into the interior file is not an approximation of that,
    /// it is a different object. Until the dieline is configured, the print package is this file
    /// and a recorded LAYOUT_FAILED for the cover — never the hybrid.
    /// </summary>
    byte[] ComposeInterior(
        MasterStory plan,
        IReadOnlyList<BekiSpreadArtwork> spreads,
        BekiBookPersonalization? personalization = null);

    /// <summary>
    /// The press cover: the composited 512 × 245 mm wrap as one full-bleed page with the Ottia
    /// title typeset as vector into the locked front title-safe rectangle
    /// (<see cref="Composite.BekiCoverDieline"/>). Press preparation — boxes, colour, PDF/X —
    /// happens to the result, not here. The default refuses so test doubles need not care.
    /// </summary>
    byte[] ComposeCoverPress(string title, byte[] wrapComposite) =>
        throw new BekiLayoutException(
            CompositeFailureCodes.LayoutFailed,
            "This composer does not produce press covers.");

    /// <summary>
    /// The same book as one image per page. For looking at: a PDF cannot be inspected by anything
    /// that does not already render PDFs, and a layout nobody can see is a layout nobody can fix.
    /// </summary>
    IReadOnlyList<byte[]> RenderPages(
        MasterStory plan,
        byte[] coverImage,
        IReadOnlyList<BekiSpreadArtwork> spreads,
        BekiBookPersonalization? personalization = null);
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
/// Fourteen pages, in spec v2's locked sequence: the cover; the opening endpaper spread (approved
/// pattern left, blank free endpaper right); the personalized intro spread; eight story spreads;
/// the credits spread (blank leaf beside the credits-and-review page); the rear endpaper spread
/// (pattern across both leaves); and the back cover. A spread is one PDF page here, not two —
/// printers impose the fold themselves, and a spread split into two files is a spread with a seam
/// down the middle of the picture, the one thing a continuous illustration exists to avoid.
///
/// **The fixed pages are approved artwork, not drawings.** The endpaper pattern and the six intro
/// backgrounds arrive from <see cref="BekiLayoutAssets"/>, hash-verified before they are placed,
/// and Beki herself is composited onto the intro by the same exact engine the story spreads use.
/// There is no placeholder behind any of them any more: a missing or altered asset, or a theme with
/// no approved background, stops the book. The composer used to draw a dot field and a tinted
/// ground instead, and the first anyone noticed was a printed book with a placeholder bound into it.
///
/// **The story text is outlined vector type straight on the artwork.** Light Noto Sans Georgian
/// with its own dark rim, set once, upper-left in the reserved column — no box, no wash, no
/// background of its own, by the owner's ruling after the first live v1.5 book (the audit's
/// cream copy-sized zone was tried and rejected on sight). Not a picture of words either: the
/// rim is real vector text runs. If the copy will not fit at any size the age band allows, the
/// book stops with <c>TEXT_OVERFLOW</c>; it is never set at a size that still overflows, and it
/// is never rewritten.
///
/// Every picture is placed at the sheet's own proportions. A centred crop of more than
/// <see cref="BekiPrintLayoutOptions.PrintCropTolerance"/> per axis is refused rather than performed
/// quietly, because a crop that deep is a composition nobody approved.
/// </summary>
public sealed class BekiPdfComposer : IBekiPdfComposer
{
    private readonly BekiPrintLayoutOptions _layout;
    private readonly ILogger<BekiPdfComposer> _logger;
    private readonly BekiLayoutAssets _assets;

    public BekiPdfComposer(
        IOptions<BekiPrintLayoutOptions> options,
        ILogger<BekiPdfComposer>? logger = null)
        : this(options, logger, null)
    {
    }

    /// <summary>
    /// <paramref name="assets"/> is for the acceptance tests, which point the registry at a
    /// doctored asset tree to prove that a mismatched hash actually stops a book. Production and
    /// every ordinary test get <see cref="BekiLayoutAssets.Current"/>.
    ///
    /// <paramref name="logger"/> is defaulted so the layout tests — which build a composer directly,
    /// by the dozen, and care about pixels rather than logs — are not each made to carry one.
    /// </summary>
    internal BekiPdfComposer(
        IOptions<BekiPrintLayoutOptions> options,
        ILogger<BekiPdfComposer>? logger,
        BekiLayoutAssets? assets)
    {
        _layout = options.Value;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<BekiPdfComposer>.Instance;
        _assets = assets ?? BekiLayoutAssets.Current;
    }

    /// <summary>Every page's ground, unless a page — an endpaper — asks for its own.</summary>
    private static readonly Color PageInk = Color.FromHex("#281B3F");

    /// <summary>Cream, and the same one the reader sets its pages on.</summary>
    private static readonly Color TextColor = Color.FromHex("#FFF8EB");

    /// <summary>
    /// The ink the measurement document sets its stand-in type in. Colour never changes metrics;
    /// it exists so the throwaway page is a valid document.
    /// </summary>
    private static readonly Color StoryInk = Color.FromHex("#241A33");

    /// <summary>
    /// The inset the copy keeps from its column's edges, in millimetres.
    ///
    /// This used to be the padding of a cream wash box behind the words. The box is gone — the
    /// owner's ruling after the first live v1.5 book: the copy is outlined light type straight
    /// on the artwork, no background of its own — but the inset stays exactly as it was, because
    /// the fit ladder's measurements were made against it and a book must not overflow over a
    /// styling change.
    /// </summary>
    private const float WashPaddingMm = 6f;

    /// <summary>
    /// The wash ink behind the outlined cover title — the one place left in the book where type
    /// is set light over artwork, because a cover title is a picture. The Continue Adventure chip
    /// shared this ink until the Locked Print Specification §6 removed it with its QR.
    /// </summary>
    private const string TextWashInk = "0D071D";

    /// <summary>The glyph outline in the wash's own ink, so the rim reads as one shadow.</summary>
    private static readonly Color OutlineColor = Color.FromHex("#" + TextWashInk);

    /// <summary>
    /// The blank free endpaper's paper tone. The opening spread patterns the pastedown and leaves
    /// the facing leaf empty (handoff §5, spread 1), and "empty" on a bound hardcover is stock, not
    /// the spreads' dark ground.
    /// </summary>
    private static readonly Color EndpaperPaper = Color.FromHex("#F3E7D2");

    /// <summary>Points per millimetre, both ways, in one place.</summary>
    private const float PointsPerMm = 72f / 25.4f;

    /// <summary>
    /// The fixed pages' finished artwork, keyed by everything that decides their pixels.
    ///
    /// Static, and deliberately: the intro spread is an approved 5315×2480 background with an
    /// approved pose composited onto it, which is the same picture for every book that chose that
    /// world, and building it costs a 39-megapixel decode plus a Lanczos resize. Six entries is the
    /// ceiling, one per world, and a process that composes two Forest books does that work once.
    /// </summary>
    private static readonly ConcurrentDictionary<string, byte[]> FixedPageArtwork = new(StringComparer.Ordinal);

    /// <summary>
    /// The composite engine, loaded once per process from the published asset tree. Read-only from
    /// here: this composer asks it to place the approved pose and never does that arithmetic itself.
    /// </summary>
    private static readonly Lazy<BekiCompositeEngine> Engine =
        new(() => BekiCompositeEngine.Create(), isThreadSafe: true);

    /// <summary>
    /// Measured block heights, keyed the same way. The step-down ladder asks for the same paragraph
    /// at up to four sizes and the answer never changes within one book.
    /// </summary>
    private readonly Dictionary<string, float?> _measuredBlockHeights = [];

    /// <summary>
    /// The approved Beki mark for the credits spread and the back cover, resolved through the
    /// pose registry by the id the layout registry names.
    ///
    /// This used to be the legacy opaque raster from a hardcoded path, with null-and-drop when
    /// the file was missing — precisely the silent legacy fallback the supplier's audit rejected
    /// (P0-F): a dark-rectangle Beki nobody approved, printed in a sold book, with no receipt
    /// anywhere. Now the mark is the same class of asset as everything else on a page: named in a
    /// registry, hash-verified before use, and a missing or tampered file stops the book with
    /// LAYOUT_FAILED rather than quietly changing what prints.
    /// </summary>
    private byte[] BekiMark()
    {
        var poseId = _assets.BekiMarkPoseId;

        try
        {
            return Engine.Value.Registry.ApprovedPoseBytes(poseId);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            throw new BekiLayoutException(
                CompositeFailureCodes.LayoutFailed,
                $"The Beki mark (pose '{poseId}') could not be resolved from the approved pose "
                + $"registry: {ex.Message}");
        }
    }

    public byte[] Compose(
        MasterStory plan,
        byte[] coverImage,
        IReadOnlyList<BekiSpreadArtwork> spreads,
        BekiBookPersonalization? personalization = null) =>
        PdfPrintBoxes.Apply(Build(plan, coverImage, spreads, personalization, true).GeneratePdf(), _layout.BleedMm);

    public byte[] ComposeInterior(
        MasterStory plan,
        IReadOnlyList<BekiSpreadArtwork> spreads,
        BekiBookPersonalization? personalization = null) =>
        PdfPrintBoxes.Apply(
            Build(plan, coverImage: null, spreads, personalization, print: true).GeneratePdf(),
            _layout.BleedMm);

    public IReadOnlyList<byte[]> RenderPages(
        MasterStory plan,
        byte[] coverImage,
        IReadOnlyList<BekiSpreadArtwork> spreads,
        BekiBookPersonalization? personalization = null) =>
        Build(plan, coverImage, spreads, personalization, false)
            .GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 96 })
            .ToList();

    /// <summary>
    /// <inheritdoc cref="IBekiPdfComposer.ComposeCoverPress"/>
    ///
    /// One page at the wrap's own 512 × 245 — no bleed added, because the locked spec's turn-ins
    /// ARE the wrap's overrun and its boxes are all equal. The artwork arrives already composited
    /// (base plus the exact approved pose); this only sets the title, in the same outlined Ottia
    /// the reading cover uses, centred inside the locked title-safe rectangle.
    /// </summary>
    public byte[] ComposeCoverPress(string title, byte[] wrapComposite)
    {
        ArgumentNullException.ThrowIfNull(wrapComposite);

        QuestPDF.Settings.License = LicenseType.Community;

        // Only the faces: the wrap carries no registry artwork of its own — the pose is already
        // composited into the bytes, hash-verified where the compositing happened.
        _assets.VerifyFonts();
        PdfFontBootstrap.EnsureRegistered();

        var titleWidthPt = MmToPt(BekiCoverDieline.TitleSafeWidthMm);

        return Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(new PageSize(
                    BekiCoverDieline.CanvasWidthMm, BekiCoverDieline.CanvasHeightMm,
                    Unit.Millimetre));
                page.Margin(0);

                page.Content().Layers(layers =>
                {
                    layers.PrimaryLayer().Image(wrapComposite)
                        .FitUnproportionally().UseOriginalImage();

                    layers.Layer()
                        .PaddingLeft(BekiCoverDieline.TitleSafeLeftMm, Unit.Millimetre)
                        .PaddingTop(BekiCoverDieline.TitleSafeTopMm, Unit.Millimetre)
                        .AlignLeft()
                        .AlignTop()
                        .Width(BekiCoverDieline.TitleSafeWidthMm, Unit.Millimetre)
                        .Height(BekiCoverDieline.TitleSafeHeightMm, Unit.Millimetre)
                        .AlignMiddle()
                        .Element(item => OutlinedText(
                            item, title, _layout.StoryFontSize * 2f, 1.25f,
                            TextColor, OutlineColor, titleWidthPt,
                            PdfFontBootstrap.TitleFamily, centred: true));
                });
            });
        }).WithMetadata(new DocumentMetadata { Title = title }).GeneratePdf();
    }

    /// <param name="coverImage">
    /// Null builds the print interior: the twelve interior spreads with no cover faces. The cover
    /// is a printer-dieline wrap, not two leaves of this document, and the audit's finding stands
    /// as the reason this is a parameter and not a second builder: the hybrid 14-page file must
    /// never again be the production deliverable.
    /// </param>
    private Document Build(
        MasterStory plan,
        byte[]? coverImage,
        IReadOnlyList<BekiSpreadArtwork> spreads,
        BekiBookPersonalization? personalization,
        bool print)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        // Before a single page is laid out: the four licensed font files and the approved pattern
        // are proven, and so is the background for this book's own world. Verified here rather than
        // discovered halfway through a book, and thrown rather than logged — a missing font used to
        // print the whole book in whatever Skia found lying around, and nobody found out until a
        // parent opened it.
        var themeId = CanonicalThemeId(personalization);
        _assets.VerifyForBook(themeId);

        // The mark beside the fonts and the pattern: proven before a page exists, and its receipt
        // — id, file, hash — in the build log, which is where the audit asks to be able to read
        // which fixed asset a printed book actually carries.
        _ = BekiMark();
        var mark = Engine.Value.Registry.Pose(_assets.BekiMarkPoseId);
        _logger.LogInformation(
            "Beki PDF: credits mark resolved — pose {PoseId}, file {FileName}, "
            + "sha256 {Sha256}.",
            mark.Id, mark.FileName, mark.Sha256);

        PdfFontBootstrap.EnsureRegistered();

        // The registry above has already proven the four faces this book may be set in. This is
        // about the rest of the bootstrap's list — the A5 book's faces, registered from the same
        // folder — which a bad deploy can still lose without stopping anything. It does not affect
        // a Beki page, and it is exactly the kind of thing that goes unnoticed until somebody opens
        // a PDF and finds it set in whatever Skia had lying around.
        if (PdfFontBootstrap.MissingFontFiles.Count > 0)
        {
            _logger.LogWarning(
                "Beki PDF: font file(s) missing from the published output: {MissingFonts}",
                string.Join(", ", PdfFontBootstrap.MissingFontFiles));
        }

        var bySpread = plan.Spreads.ToDictionary(spread => spread.Number);

        return Document.Create(document =>
        {
            if (coverImage is not null)
            {
                ComposeCover(document, plan.Concept.Title, coverImage, print);
            }

            ComposeEndpaper(document, rear: false);
            ComposeIntro(document, themeId, plan.Concept.Title, personalization);

            foreach (var artwork in spreads.OrderBy(spread => spread.SpreadNumber))
            {
                if (!bySpread.TryGetValue(artwork.SpreadNumber, out var spread))
                {
                    // A picture with no words is still a page of the book; dropping it would
                    // silently shorten the story.
                    ComposeArtOnly(document, artwork.Image, print);
                    continue;
                }

                ComposeSpread(document, artwork.Image, spread, personalization, print);
            }

            ComposeCredits(document);
            ComposeEndpaper(document, rear: true);

            if (coverImage is not null)
            {
                ComposeBackCover(document);
            }
        }).WithMetadata(new DocumentMetadata
        {
            // The canonical book title — the same string the cover and the intro print. The
            // audited file carried QuestPDF's defaults here while its pages disagreed with each
            // other about the book's name; one field now feeds all of them.
            Title = plan.Concept.Title,
        });
    }

    /// <summary>
    /// The canonical BEKI theme id behind the personalization the book was ordered with.
    ///
    /// The mapping from the backend's own theme value is
    /// <see cref="InputNormalization.CanonicalThemeId"/> — one map, at the application boundary,
    /// which this reads rather than restates. A value that maps to nothing is a hard failure here
    /// and not a default world: the handoff's own integration rule for the theme table is "do not
    /// infer unknown aliases".
    /// </summary>
    private static string CanonicalThemeId(BekiBookPersonalization? personalization)
    {
        if (personalization is null)
        {
            throw new BekiLayoutException(
                CompositeFailureCodes.LayoutFailed,
                "A Beki book cannot be composed without personalization: the intro spread is built "
                + "on the approved background for the child's chosen world, and there is no generic "
                + "one to fall back to.");
        }

        return InputNormalization.CanonicalThemeId(personalization.Theme)
            ?? throw new BekiLayoutException(
                CompositeFailureCodes.LayoutFailed,
                $"The book's theme '{personalization.Theme}' maps to no canonical BEKI theme id, so "
                + "no approved intro background can be selected for it.");
    }

    /// <summary>
    /// The cover: a single leaf, half the spread, artwork to the bleed, and the title set over
    /// it in the licensed display face. Bottom-centre, outlined, and nothing else: one line is a
    /// title, and a second thing on the cover is a subtitle nobody asked for.
    ///
    /// The cover is deliberately outside the interior rules this campaign tightened. Its geometry
    /// is the printer's wrap and not this sheet (handoff §5), its print artifact stays withheld
    /// until the dieline arrives, and its artwork is therefore not held to the interior's crop
    /// tolerance — a cover drawn 3:2 for a leaf-shaped page loses its outer thirds by design.
    /// </summary>
    private void ComposeCover(IDocumentContainer container, string title, byte[] image, bool print)
    {
        container.Page(page =>
        {
            ApplyGeometry(page, _layout.PageWidthMm);

            page.Content().Layers(layers =>
            {
                layers.PrimaryLayer()
                    .Image(CropToSheet(image, _layout.PageWidthMm, print, enforceCropTolerance: false))
                    .FitUnproportionally().UseOriginalImage();

                // The title band is the full width between the safe margins, and the type is
                // centred inside it rather than the block being centred around the type. The two
                // put a single line in exactly the same place — but only the first gives
                // OutlinedText a width it can know before the page is laid out, which is what the
                // raster has to be built against.
                layers.Layer()
                    .PaddingHorizontal(_layout.SafeMarginMm, Unit.Millimetre)
                    .PaddingBottom(_layout.SafeMarginMm * 1.6f, Unit.Millimetre)
                    .AlignBottom()
                    .Element(item => OutlinedText(
                        item, title, _layout.StoryFontSize * 2f, 1.25f,
                        TextColor, OutlineColor, CoverTitleWidthPt,
                        PdfFontBootstrap.TitleFamily, centred: true));
            });
        });
    }

    /// <summary>
    /// An endpaper spread, from the approved pattern — placed once, across the whole sheet.
    ///
    /// Once matters. The pattern is one 450×210 mm artwork at 300 PPI, and the obvious way to build
    /// this page — two halves, each given the pattern — centre-crops the same file twice and prints
    /// its middle band on both leaves, mirrored about a fold that is not in the artwork. So the
    /// image is the page: one placement, full bleed, exactly the shape it was drawn at.
    ///
    /// The opening spread binds the pattern to the pastedown and leaves the free endpaper blank
    /// (handoff §5, spread 1), which is a paper-coloured leaf laid over the right half rather than
    /// a second, differently-cropped copy of the artwork. The rear spread patterns both leaves.
    /// </summary>
    private void ComposeEndpaper(IDocumentContainer container, bool rear)
    {
        container.Page(page =>
        {
            ApplyGeometry(page, _layout.SpreadWidthMm, EndpaperPaper);

            page.Content().Layers(layers =>
            {
                layers.PrimaryLayer().Image(EndpaperArtwork())
                    .FitUnproportionally().UseOriginalImage();

                if (rear)
                {
                    return;
                }

                layers.Layer().Row(row =>
                {
                    row.RelativeItem();
                    row.RelativeItem().Extend().Background(EndpaperPaper);
                });
            });
        });
    }

    /// <summary>
    /// The personalized intro spread (handoff §9): the approved theme background across the whole
    /// sheet, the exact <c>pose_07_curious_lean</c> composited onto its right half, and the child's
    /// own lines set in vector Noto on the left.
    ///
    /// Beki is placed by <see cref="BekiCompositeEngine"/> — the same engine, the same hash-verified
    /// PNG and the same arithmetic every story spread uses — at the anchor the supplier proved, with
    /// one conversion the config cannot express: their <c>visible_center_y</c> is measured from the
    /// bottom of the sheet and the engine measures from the top, so 0.48095 is placed as
    /// 1 − 0.48095. Used unconverted it puts her about 8 mm low, which is a difference nobody would
    /// have caught by looking. <see cref="IntroAnchor"/> holds the conversion; a golden test holds
    /// the proof's own millimetres.
    ///
    /// The copy is a hierarchy rather than a paragraph — whose book this is, how old they are, which
    /// world it opens into, and the invitation — and it carries no date. A date on the intro spread
    /// makes a reprint a different book from the one that was bought.
    /// </summary>
    private void ComposeIntro(
        IDocumentContainer container,
        string themeId,
        string title,
        BekiBookPersonalization? personalization)
    {
        container.Page(page =>
        {
            ApplyGeometry(page, _layout.SpreadWidthMm);

            page.Content().Layers(layers =>
            {
                layers.PrimaryLayer().Image(IntroArtwork(themeId))
                    .FitUnproportionally().UseOriginalImage();

                layers.Layer().Row(row =>
                {
                    // The left leaf carries the words; the right one is Beki's, which is where the
                    // composite engine has just put her.
                    row.RelativeItem()
                        .PaddingTop(_layout.SafeMarginMm, Unit.Millimetre)
                        .PaddingBottom(_layout.SafeMarginMm, Unit.Millimetre)
                        .PaddingLeft(_layout.SafeMarginMm, Unit.Millimetre)
                        .PaddingRight(InnerPaddingMm, Unit.Millimetre)
                        .AlignMiddle()
                        .AlignLeft()
                        .MaxWidth(_layout.MaxTextWidthMm, Unit.Millimetre)
                        // Same ruling as the story spreads: no box behind the words — outlined
                        // light type straight on the approved background, padding kept as inset.
                        .Padding(WashPaddingMm, Unit.Millimetre)
                        .Column(column => ComposeIntroCopy(column, title, personalization));

                    row.RelativeItem();
                });
            });
        });
    }

    /// <summary>
    /// The intro spread's four lines, in the proof's own order: the dedication, the age under it,
    /// the world it opens into, and the invitation.
    ///
    /// The child's name is inflected rather than concatenated. The shipped book printed „თემო-ს“,
    /// which is a template that glued a hyphen and a case ending onto whatever it was given;
    /// Georgian writes the dative straight onto a Georgian-script name — ნინო becomes ნინოს — and
    /// keeps the hyphen only for a name written in another alphabet. See
    /// <see cref="GeorgianNameSuffix.Dative"/>.
    /// </summary>
    private void ComposeIntroCopy(
        ColumnDescriptor column, string title, BekiBookPersonalization? personalization)
    {
        column.Spacing(WashPaddingMm * PointsPerMm * 0.8f);

        var headerSize = _layout.StoryFontSize * 1.35f;
        var bodySize = _layout.StoryFontSize;
        var quietSize = _layout.StoryFontSize * 0.8f;
        var lineWidthPt = MmToPt(_layout.MaxTextWidthMm - (WashPaddingMm * 2f));

        if (personalization is not null)
        {
            var belongs = _layout.IntroBelongsTemplate
                .Replace("{name_dative}", GeorgianNameSuffix.Dative(personalization.ChildName))
                .Replace("{name}", personalization.ChildName);

            if (!string.IsNullOrWhiteSpace(belongs))
            {
                column.Item().Element(item => OutlinedText(
                    item, belongs, headerSize, StoryLineHeight,
                    TextColor, OutlineColor, lineWidthPt));
            }

            var age = _layout.IntroAgeTemplate.Replace("{age}", personalization.Age.ToString());
            if (!string.IsNullOrWhiteSpace(age))
            {
                column.Item().Element(item => OutlinedText(
                    item, age, quietSize, StoryLineHeight,
                    TextColor, OutlineColor, lineWidthPt));
            }
        }

        /*
          The quoted line is the BOOK'S OWN TITLE — the same string the cover prints — not the
          theme world's fixed name. It used to be StoryWorlds' per-theme place („სინათლის
          ქალაქი“), which reads as a title on the page, and the supplier's audit duly read it as
          one: the cover said „სინათლის პატარა ქალაქი“ and the intro appeared to disagree about
          what the book is called. One canonical title now feeds the cover, this line, and the
          PDF metadata; the world's name still steers the story planner, where it belongs.
        */
        var theme = string.IsNullOrWhiteSpace(title)
            ? string.Empty
            : _layout.IntroThemeTemplate.Replace("{world}", title.Trim());

        if (!string.IsNullOrWhiteSpace(theme))
        {
            column.Item().Element(item => OutlinedText(
                item, theme, bodySize, StoryLineHeight,
                TextColor, OutlineColor, lineWidthPt));
        }

        // The invitation addresses the child by name in the vocative, which in Georgian is the
        // plain name — so this template takes {name} untouched and must never take a suffix.
        var invite = personalization is null
            ? _layout.IntroInviteTemplate.Replace("{name}, ", string.Empty)
            : _layout.IntroInviteTemplate.Replace("{name}", personalization.ChildName);

        if (!string.IsNullOrWhiteSpace(invite))
        {
            column.Item().Element(item => OutlinedText(
                item, invite, bodySize, StoryLineHeight,
                TextColor, OutlineColor, lineWidthPt));
        }
    }

    /// <summary>
    /// The credits spread — spec v2's replacement for the standalone closing leaf: the left
    /// half deliberately blank, the right half carrying the Beki mark, the sign-off line, the
    /// rate-us QR and the credits line, all reusable across every order. One combined
    /// credits-and-review page, exactly one — the deprecated P18 must not come back beside it.
    /// The blank-URL-drops-the-QR stance is inherited unchanged: a code that scans to nothing
    /// is worse than no code.
    ///
    /// Reused exactly, per handoff §5 and R9 — the layout is pinned by
    /// <c>BekiCreditsLayoutTests</c>, which was written before anything on this page moved. The one
    /// change is the face: the sign-off used to be set in Noto Serif Georgian, which R10 removes
    /// from the interior altogether.
    /// </summary>
    private void ComposeCredits(IDocumentContainer container)
    {
        container.Page(page =>
        {
            ApplyGeometry(page, _layout.SpreadWidthMm);

            page.Content().Row(row =>
            {
                row.RelativeItem().Background(PageInk);

                row.RelativeItem().Element(right =>
                {
                    right.Background(PageInk)
                         .Padding(_layout.SafeMarginMm, Unit.Millimetre)
                         .AlignMiddle()
                         .Column(column =>
                         {
                             column.Spacing(14);

                             column.Item().AlignCenter().Width(32, Unit.Millimetre)
                                 .Image(BekiMark()).FitWidth();

                             column.Item().AlignCenter().Text(_layout.EndingLine)
                                 .FontFamily(PdfFontBootstrap.BodyFamily)
                                 .FontSize(_layout.StoryFontSize * 1.05f)
                                 .LineHeight(1.5f)
                                 .FontColor(TextColor);

                             if (!string.IsNullOrWhiteSpace(_layout.ReviewQrUrl))
                             {
                                 column.Item().AlignCenter()
                                     .Width(46, Unit.Millimetre)
                                     .Background(Colors.White)
                                     .Padding(4, Unit.Millimetre)
                                     .Svg(QrSvg(_layout.ReviewQrUrl))
                                     .FitWidth();

                                 column.Item().AlignCenter().Text(_layout.EndingQrCaption)
                                     .FontFamily(PdfFontBootstrap.BodyFamily)
                                     .FontSize(_layout.StoryFontSize * 0.7f)
                                     .FontColor(TextColor);
                             }

                             column.Item().AlignCenter().Text(_layout.CreditsLine)
                                 .FontFamily(PdfFontBootstrap.BodyFamily)
                                 .FontSize(_layout.StoryFontSize * 0.85f)
                                 .FontColor(TextColor);
                         });
                });
            });
        });
    }

    /// <summary>
    /// The back cover: quiet on purpose, and since the Locked Print Specification §6, without a
    /// Beki on it — "keep the back cover environment-only, without a Beki character or draft
    /// wordmark". The mark that used to sit here belongs to the credits spread, the one place §6
    /// approves it. What remains is the address, which is type, not a character.
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

                    // Literal rather than an option: nothing before this needed the brand
                    // address configurable, and adding a setting nobody will ever change is a
                    // setting somebody eventually has to explain.
                    column.Item().AlignCenter().Text("beki.ge")
                        .FontFamily(PdfFontBootstrap.BodyFamily)
                        .FontSize(_layout.StoryFontSize * 0.85f)
                        .FontColor(TextColor);
                });
        });
    }

    /// <summary>A spread whose text went missing: artwork to the bleed and nothing else.</summary>
    private void ComposeArtOnly(IDocumentContainer container, byte[] image, bool print)
    {
        container.Page(page =>
        {
            ApplyGeometry(page, _layout.SpreadWidthMm);
            page.Content().Image(CropToSheet(image, _layout.SpreadWidthMm, print))
                .FitUnproportionally().UseOriginalImage();
        });
    }

    private void ComposeSpread(
        IDocumentContainer container, byte[] image, StorySpread spread,
        BekiBookPersonalization? personalization, bool print)
    {
        var textSide = Prompts.BekiSpreadRhythm.TextSideFor(spread.Number);
        var textOnLeft = textSide.Equals("left", StringComparison.OrdinalIgnoreCase);

        // Spread 8 is an ordinary spread. It carried a Continue Adventure chip with a second QR
        // until the Locked Print Specification §6 ruled: exactly one QR in the book, on the
        // credits spread — the chip and its zone reservation are gone, and the last story page
        // got its full text column back.
        var outerPaddingMm = _layout.SafeMarginMm;
        var innerPaddingMm = InnerPaddingMm;

        var usableHeightPt = SheetHeightPt(_layout.SpreadHeightMm)
            - MmToPt(outerPaddingMm * 2f);

        // Decided before the page is laid out, because the ladder is allowed to fail the book and a
        // failure has to happen before any of it is drawn.
        var fitted = FitStoryText(spread, personalization, usableHeightPt);

        container.Page(page =>
        {
            ApplyGeometry(page, _layout.SpreadWidthMm);

            page.Content().Layers(layers =>
            {
                // Cropped to the sheet's own proportions, so filling the frame is exact rather
                // than a stretch.
                layers.PrimaryLayer().Image(CropToSheet(image, _layout.SpreadWidthMm, print))
                    .FitUnproportionally().UseOriginalImage();

                layers.Layer().Row(row =>
                {
                    // Two edges, two jobs. The outer edge — away from the fold — only ever needs
                    // the ordinary safe margin. The inner edge sits over the low-information band
                    // the fold claims, so it holds back by half the gutter zone instead whenever
                    // that is the larger number.
                    if (!textOnLeft) row.RelativeItem(1f - _layout.TextColumnShare);

                    row.RelativeItem(_layout.TextColumnShare)
                        .PaddingTop(outerPaddingMm, Unit.Millimetre)
                        .PaddingBottom(outerPaddingMm, Unit.Millimetre)
                        .PaddingLeft(textOnLeft ? outerPaddingMm : innerPaddingMm, Unit.Millimetre)
                        .PaddingRight(textOnLeft ? innerPaddingMm : outerPaddingMm, Unit.Millimetre)
                        // Upper-left, per the approved spread-1 reference: the copy starts at the
                        // top of its column and the wash starts with it.
                        .AlignTop()
                        .AlignLeft()
                        .MaxWidth(StoryColumnWidthPt / PointsPerMm, Unit.Millimetre)
                        // No box behind the words — the owner's ruling after the first live
                        // v1.5 book: the copy sits straight on the artwork as light type with
                        // its own dark rim, the way earlier versions set it. The padding stays
                        // as a plain inset so the fit ladder's measurements are unchanged.
                        .Padding(WashPaddingMm, Unit.Millimetre)
                        .Column(column =>
                        {
                            column.Spacing(10);

                            column.Item().Element(item => OutlinedText(
                                item, spread.Text, fitted.FontSize, StoryLineHeight,
                                TextColor, OutlineColor,
                                StoryColumnWidthPt - (MmToPt(WashPaddingMm) * 2f)));

                            if (fitted.EnglishFontSize is { } englishSize)
                            {
                                column.Item().Element(item => OutlinedText(
                                    item, spread.TextEn!, englishSize, StoryLineHeight,
                                    TextColor, OutlineColor,
                                    StoryColumnWidthPt - (MmToPt(WashPaddingMm) * 2f)));
                            }
                        });

                    if (textOnLeft) row.RelativeItem(1f - _layout.TextColumnShare);
                });

            });
        });
    }

    /// <summary>The type size a spread's copy is set at, and the English size if it prints too.</summary>
    private readonly record struct FittedStoryText(float FontSize, float? EnglishFontSize);

    /// <summary>
    /// The step-down ladder (§6 Step 8), and the failure at the end of it.
    ///
    /// Start at the size this reader's age band asks for and walk down the configured ladder until
    /// the measured block fits its column. If none of them fits, the book stops with
    /// <c>TEXT_OVERFLOW</c> for a human to look at.
    ///
    /// That last sentence is the change. The old ladder took the smallest size on the list whether
    /// or not it fitted — <c>if (fits || isLastRung)</c> — so an overlong paragraph was set at 15 pt
    /// and printed straight off the bottom of the page, and the failure code the handoff reserved
    /// for exactly this was never reachable. Copy is never rewritten to make it fit; §6 Step 8 is
    /// explicit, and rewriting a bought book's words to save a layout is the wrong trade.
    /// </summary>
    private FittedStoryText FitStoryText(
        StorySpread spread, BekiBookPersonalization? personalization, float usableHeightPt)
    {
        var printEnglish = _layout.PrintEnglishToo && !string.IsNullOrWhiteSpace(spread.TextEn);
        var columnWidthPt = StoryColumnWidthPt - (MmToPt(WashPaddingMm) * 2f);
        var washHeightPt = MmToPt(WashPaddingMm) * 2f;

        var ladder = StoryFontSizeLadder(personalization?.Age);
        var measured = new List<string>(ladder.Count);

        foreach (var size in ladder)
        {
            var height = MeasureBlockHeightPt(spread.Text, size, columnWidthPt);
            var englishSize = printEnglish ? size * 0.82f : (float?)null;

            if (englishSize is { } english)
            {
                height += 10f + MeasureBlockHeightPt(spread.TextEn!, english, columnWidthPt);
            }

            measured.Add($"{size:0.##}pt→{height + washHeightPt:0}pt");

            if (height + washHeightPt <= usableHeightPt)
            {
                return new FittedStoryText(size, englishSize);
            }
        }

        throw new BekiLayoutException(
            CompositeFailureCodes.TextOverflow,
            $"Spread {spread.Number}'s Georgian copy does not fit its column at any size the age "
            + $"band allows ({string.Join(", ", measured)}; the column holds {usableHeightPt:0}pt). "
            + "The copy is not rewritten to make it fit — this book needs a human.");
    }

    /// <summary>
    /// The sizes this reader's copy may be set at, largest first.
    ///
    /// The age band picks where the ladder starts; every configured rung below it is a permitted
    /// reduction, and there is nothing below the last rung but <c>TEXT_OVERFLOW</c>. Written as a
    /// list rather than as a loop over a step, because "which sizes are allowed" is a typographic
    /// decision the owner should be able to read off a config file.
    /// </summary>
    private IReadOnlyList<float> StoryFontSizeLadder(int? age)
    {
        var start = BekiPrintLayoutOptions.StoryFontSizeFor(age, _layout);
        var rungs = new List<float> { start };

        foreach (var rung in _layout.StoryFontSizeLadderPt.OrderByDescending(size => size))
        {
            if (rung < start) rungs.Add(rung);
        }

        return rungs;
    }

    /// <summary>
    /// The leading every block of story type is set on, as QuestPDF's multiple of the size.
    ///
    /// The approved reference is 18 pt on 27 pt, which the composer keeps as a ratio rather than as
    /// a pair: a spread that steps down to 16 pt tightens its leading with the type, where a fixed
    /// 27 pt would leave the block barely shorter than it was and the ladder would stop helping.
    /// </summary>
    private float StoryLineHeight
        => _layout.StoryFontSize <= 0f
            ? 1.5f
            : _layout.StoryLeadingPt / _layout.StoryFontSize;

    /// <summary>
    /// How tall one block of Georgian is, set at this size in this column, in points.
    ///
    /// Measured by setting it — a one-page document whose width is the column's, whose height
    /// follows its content, rendered at 72 DPI so a pixel is a point. There is no cheaper honest
    /// answer available: Georgian wrapping is Skia's business, and any arithmetic here would be a
    /// second opinion about it that the page would then contradict.
    ///
    /// A block that could not be measured is a failure and not a guess. The composer's whole job on
    /// this page is deciding whether the copy fits, and "we could not tell" is the one answer that
    /// must not become "print it anyway" — which is what the old code did.
    /// </summary>
    private float MeasureBlockHeightPt(string text, float fontSize, float widthPt)
    {
        var key = string.Join('\u001F', text, fontSize, widthPt);

        if (!_measuredBlockHeights.TryGetValue(key, out var cached))
        {
            cached = BuildBlockHeightPt(text, fontSize, widthPt);
            _measuredBlockHeights[key] = cached;
        }

        return cached ?? throw new BekiLayoutException(
            CompositeFailureCodes.LayoutFailed,
            $"The composer could not measure a {fontSize:0.##}pt block of story text in a "
            + $"{widthPt:0}pt column, so it cannot tell whether the copy fits the page.");
    }

    private float? BuildBlockHeightPt(string text, float fontSize, float widthPt)
    {
        if (string.IsNullOrWhiteSpace(text) || widthPt <= 1f)
        {
            return 0f;
        }

        try
        {
            var block = Document.Create(document => document.Page(page =>
            {
                page.ContinuousSize(widthPt, Unit.Point);
                page.Margin(0);
                page.PageColor(Colors.Transparent);
                page.DefaultTextStyle(style => style.FontFamily(PdfFontBootstrap.BodyFamily));

                page.Content().Text(text)
                    .FontFamily(PdfFontBootstrap.BodyFamily)
                    .FontSize(fontSize)
                    .LineHeight(StoryLineHeight)
                    .FontColor(StoryInk);
            }));

            var pages = block
                .GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 72 })
                .ToList();

            // A block that paginated is a block whose height did not follow its content, so its
            // first page's height is not the block's height.
            if (pages.Count != 1 || pages[0].Length == 0)
            {
                return null;
            }

            var size = SixLabors.ImageSharp.Image.Identify(pages[0]);
            return size.Height < 1 ? null : size.Height;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Light type with its own dark edge, drawn entirely as vector text.
    ///
    /// Every piece of type set over artwork comes through here again — the cover title, the
    /// story copy, and the intro's lines — since the owner removed the cream box: a glyph on a
    /// picture needs a rim of its own, and the rim is the text drawn eight more times beneath
    /// the fill, all of it real vector runs.
    ///
    /// This used to be a raster: the nine-copy stack rendered to a PNG at 300 DPI with one
    /// invisible text run over it, so that <c>pdftotext</c> said each line once. The supplier's
    /// preflight rejected exactly that trade — "a raster title-effect image is placed underneath"
    /// the vector text, and a printed glyph should be the RIP's own edge, not a picture of one.
    /// So the visible glyphs are the vector stack again: QuestPDF has no stroke, and the rim is
    /// the text drawn eight more times on a small circle beneath the fill. The cost, accepted
    /// knowingly, is that a text extractor reads an outlined line nine times.
    ///
    /// <paramref name="blockWidthPt"/> is kept for the callers' layout arithmetic even though no
    /// raster needs sizing any more; a box too narrow to set type in still falls back to plain.
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
        // No outline asked for, nothing to outline, or a box too narrow: plain text, which is a
        // single run and needs no rim.
        if (_layout.TextOutlineWidth <= 0f || string.IsNullOrWhiteSpace(text) || blockWidthPt <= 1f)
        {
            PlainText(container, text, fontSize, lineHeight, fill, fontFamily, centred);
            return;
        }

        DrawOutlineStack(container, text, fontSize, lineHeight, fill, outline, fontFamily, centred);
    }

    /// <summary>
    /// One block of type, with no outline of its own.
    ///
    /// The family behind the first one is a per-glyph fallback, not a preference: QuestPDF asks the
    /// next family for a character the one before it lacks, which is how the cover title keeps
    /// Ottia's letters and borrows a dash from Noto instead of printing a box. Noto Serif Georgian
    /// used to sit in the middle of that chain; R10 removes it, because a chain is a way for a face
    /// nobody chose to end up embedded in the book.
    /// </summary>
    private static void PlainText(
        IContainer container, string text, float fontSize, float lineHeight,
        Color colour, string fontFamily, bool centred)
    {
        var block = container.Text(text)
            .FontFamily(fontFamily, PdfFontBootstrap.BodyFamily)
            .FontSize(fontSize)
            .LineHeight(lineHeight)
            .FontColor(colour);

        if (centred) block.AlignCenter();
    }

    /// <summary>
    /// The faux outline itself: eight offset copies on a circle of the outline's own radius, then
    /// the fill — all real vector text runs, which since the supplier's preflight ruling is the
    /// shipped form rather than the source of a raster.
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

    /// <summary>
    /// The approved endpaper pattern, ready for the sheet it is going onto.
    ///
    /// It arrives at exactly the working raster — 5315 × 2480, 300 PPI, sRGB — so on the print path
    /// it passes through byte-identical. "Use the approved endpaper pattern exactly; do not
    /// regenerate it" (§9), and a lossy re-encode of an approved asset is a regeneration.
    /// </summary>
    private byte[] EndpaperArtwork()
        => FixedPageArtwork.GetOrAdd(
            FixedPageKey("endpaper", _assets.EndpaperPattern.Sha256),
            _ => NormalizeForPrint(
                _assets.EndpaperPatternBytes(), PrintRaster, preserveApprovedBytes: true));

    /// <summary>
    /// The intro spread's finished artwork: the approved background for this world with the approved
    /// Beki composited onto it, built once per world per process.
    ///
    /// The composite is the engine's, not this composer's. Everything about where Beki lands — the
    /// alpha bounding box, the proportional resize, the half-to-even rounding, the bounds checks and
    /// the manifest — belongs to <see cref="BekiCompositeEngine"/>, and duplicating any of it here
    /// would be a second implementation of the one thing in the pipeline that is supposed to be
    /// provably identical between the proof and the book.
    /// </summary>
    private byte[] IntroArtwork(string themeId)
        => FixedPageArtwork.GetOrAdd(
            FixedPageKey($"intro|{themeId}", _assets.IntroBackground(themeId).Sha256),
            _ =>
            {
                var background = _assets.IntroBackgroundBytes(themeId);
                var composite = Engine.Value.CompositeIntro(
                    background,
                    _assets.IntroBackground(themeId).FileName,
                    $"beki_intro_{themeId}.png",
                    IntroAnchor(Engine.Value.Config));

                _logger.LogInformation(
                    "Beki intro spread composed for theme {ThemeId}: pose {PoseId} rendered "
                    + "{RenderedWidth}×{RenderedHeight} at {PlacementX},{PlacementY} on "
                    + "{CanvasWidth}×{CanvasHeight}.",
                    themeId,
                    composite.Manifest.BekiLayer.PoseId,
                    composite.Manifest.BekiLayer.RenderedSizePx.WidthPx,
                    composite.Manifest.BekiLayer.RenderedSizePx.HeightPx,
                    composite.Manifest.BekiLayer.PlacementPx.XPx,
                    composite.Manifest.BekiLayer.PlacementPx.YPx,
                    composite.Manifest.Canvas.WidthPx,
                    composite.Manifest.Canvas.HeightPx);

                return NormalizeForPrint(composite.Png, PrintRaster);
            });

    /// <summary>
    /// The intro anchor the engine is given: the supplier's numbers with their origin converted.
    ///
    /// <c>pipeline_config_v1.json</c> states the intro placement as a visible centre 0.48095 up from
    /// the <em>bottom</em> of the sheet — that is what its own
    /// <c>source_proof_position_mm</c> block describes, a visible bottom edge 19 mm above the trim.
    /// <see cref="BekiCompositeEngine"/> measures <c>visible_center_y</c> down from the top, like
    /// every other pixel coordinate in the pipeline. Handing the config's number over unconverted
    /// places Beki about 8 mm below where the proof has her, which on a 210 mm page is a difference
    /// you would only find by measuring a print.
    ///
    /// The conversion is here rather than in the config because the config is the supplier's
    /// document and its numbers are the ones on their proof; rewriting it would make our tree
    /// disagree with theirs about what was approved.
    /// </summary>
    internal static BekiCompositeAnchor IntroAnchor(BekiCompositeConfig config)
        => config.IntroAnchor with { VisibleCenterY = 1d - config.IntroAnchor.VisibleCenterY };

    /// <summary>
    /// A code as vector geometry, with its quiet zone drawn rather than assumed. QRCoder defaults
    /// that flag to true, and a default is exactly the kind of thing that changes under a version
    /// bump without anybody printing a test sheet — so it is written down.
    ///
    /// SVG rather than the PNG this used to be, because the supplier's preflight found the codes
    /// as raster image objects: a bitmap module edge softens under resampling and colour
    /// conversion on its way to a press, and a scanner reads edges. Deterministic vector
    /// rectangles have no resolution to lose. QuestPDF places SVG as PDF vector content.
    /// </summary>
    private static string QrSvg(string url)
    {
        using var generator = new QRCoder.QRCodeGenerator();
        using var data = generator.CreateQrCode(url.Trim(), QRCoder.QRCodeGenerator.ECCLevel.Q);
        return new QRCoder.SvgQRCode(data).GetGraphic(
            pixelsPerModule: 16,
            darkColorHex: "#000000",
            lightColorHex: "#FFFFFF",
            drawQuietZones: true);
    }

    /// <summary>
    /// The print raster contract for one interior sheet: the exact pixel dimensions, the density
    /// and the colour space every layer of a printed spread has to arrive in (§6 Step 8).
    /// </summary>
    /// <param name="WidthPx">5315 on the handoff's 450 mm sheet at 300 PPI.</param>
    /// <param name="HeightPx">2480 on its 210 mm height.</param>
    internal readonly record struct PrintRasterTarget(int WidthPx, int HeightPx, int Ppi, int JpegQuality);

    /// <summary>This book's print raster target, computed from the sheet rather than written down.</summary>
    private PrintRasterTarget PrintRaster => new(
        PixelsFor(_layout.SpreadWidthMm + (_layout.BleedMm * 2f), _layout.PrintTargetPpi),
        PixelsFor(_layout.SpreadHeightMm + (_layout.BleedMm * 2f), _layout.PrintTargetPpi),
        _layout.PrintTargetPpi,
        _layout.PrintAssetJpegQuality);

    /// <summary>
    /// The cache key for one fixed page's finished artwork.
    ///
    /// The source asset's own hash is in it, which is what makes a process-wide cache safe: a test
    /// pointing the registry at a different tree, or a pack revision under a running service, keys
    /// differently rather than being served yesterday's picture. So do the sheet and the raster
    /// target, because two books on different geometry are two different pages.
    /// </summary>
    private string FixedPageKey(string page, string sourceSha256) =>
        $"{page}|{sourceSha256}|{_layout.PrintTargetPpi}|{_layout.PrintAssetJpegQuality}"
        + $"|{_layout.SpreadWidthMm}x{_layout.SpreadHeightMm}+{_layout.BleedMm}";

    private static int PixelsFor(float millimetres, int ppi)
        => Math.Max(1, (int)MathF.Round(millimetres / 25.4f * ppi));

    /// <summary>
    /// One interior layer at exactly the working raster the handoff specifies: 5315 × 2480 px,
    /// 300 PPI in the file's own metadata, and an sRGB profile embedded rather than assumed.
    ///
    /// Three things changed here. It used to resize by width alone and let the height fall where it
    /// fell, so a layer was "about" the right size; it used to skip anything already wide enough,
    /// so a 6000-pixel render shipped at 6000 pixels; and it never wrote a density or a colour
    /// profile at all, which is how a book reached a printer as an untagged RGB file. Now the
    /// dimensions are exact in both axes, and the ratio is checked before the resize — the caller
    /// has already cropped to the sheet, so anything that still disagrees would be a stretch, and
    /// §6 Step 8 forbids stretching in as many words.
    ///
    /// <paramref name="preserveApprovedBytes"/> lets an approved asset that already satisfies every
    /// clause pass through byte-identical, which is what §9 asks for of the endpaper pattern: "use
    /// the approved endpaper pattern exactly; do not regenerate it", and a lossy re-encode of an
    /// approved asset is a regeneration. Everything else — generated spreads, the intro composite —
    /// is re-encoded, so the layer that reaches the printer is one this method wrote and not one it
    /// merely inspected.
    /// </summary>
    internal static byte[] NormalizeForPrint(
        byte[] source, PrintRasterTarget target, bool preserveApprovedBytes = false)
    {
        if (target.Ppi <= 0)
        {
            return source;
        }

        using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(source);

        var sourceRatio = (double)image.Width / image.Height;
        var targetRatio = (double)target.WidthPx / target.HeightPx;

        // The allowance is one pixel on each of the source's own axes, which is the finest a crop
        // could ever have made it: the crop that precedes this makes the two ratios equal to within
        // its own rounding, so anything wider than that rounding is an image that was never cropped
        // to this sheet and is about to be squashed onto it. Written against the source rather than
        // the target because a very small image genuinely cannot express the ratio more precisely —
        // a one-pixel test sheet is not a stretch, it is a picture with nowhere to put the decimal.
        var allowed = targetRatio * ((1.0 / image.Width) + (1.0 / image.Height));
        if (Math.Abs(sourceRatio - targetRatio) > allowed)
        {
            throw new BekiLayoutException(
                CompositeFailureCodes.LayoutFailed,
                $"A print layer is {image.Width}×{image.Height} ({sourceRatio:0.0000}) and the sheet "
                + $"is {target.WidthPx}×{target.HeightPx} ({targetRatio:0.0000}). Resizing it would "
                + "stretch the artwork, which the interior layout rules forbid.");
        }

        if (preserveApprovedBytes
            && image.Width == target.WidthPx
            && image.Height == target.HeightPx
            && HasPrintMetadata(image.Metadata, target.Ppi))
        {
            return source;
        }

        if (image.Width != target.WidthPx || image.Height != target.HeightPx)
        {
            image.Mutate(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new SixLabors.ImageSharp.Size(target.WidthPx, target.HeightPx),
                Mode = ResizeMode.Stretch,
                Sampler = KnownResamplers.Lanczos3,
            }));
        }

        image.Metadata.ResolutionUnits = PixelResolutionUnit.PixelsPerInch;
        image.Metadata.HorizontalResolution = target.Ppi;
        image.Metadata.VerticalResolution = target.Ppi;

        // The colour space is carried, not invented: an approved asset arrives with the partner's
        // own sRGB profile and keeps it, and anything that arrived untagged is given the profile
        // from the approved endpaper pattern rather than a guess about what its numbers meant.
        image.Metadata.IccProfile ??= SrgbProfile();

        using var buffer = new MemoryStream();
        image.Save(buffer, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder
        {
            Quality = target.JpegQuality,
        });

        return buffer.ToArray();
    }

    /// <summary>
    /// Whether a layer already carries the density and the colour profile print needs.
    ///
    /// The density is compared in dots per inch whatever the file states it in. A PNG's own density
    /// chunk is written in pixels per metre, so the approved 300-PPI pattern reads back as 11811 —
    /// and comparing that number to 300 would report every approved asset as untagged and re-encode
    /// the one file §9 says not to touch.
    /// </summary>
    private static bool HasPrintMetadata(ImageMetadata metadata, int ppi)
        => metadata.IccProfile is not null
           && Math.Abs(InchesFrom(metadata.HorizontalResolution, metadata.ResolutionUnits) - ppi) < 1
           && Math.Abs(InchesFrom(metadata.VerticalResolution, metadata.ResolutionUnits) - ppi) < 1;

    private static double InchesFrom(double resolution, PixelResolutionUnit units) => units switch
    {
        PixelResolutionUnit.PixelsPerInch => resolution,
        PixelResolutionUnit.PixelsPerMeter => resolution * 0.0254d,
        PixelResolutionUnit.PixelsPerCentimeter => resolution * 2.54d,
        // AspectRatio states no density at all, so nothing it could hold is 300 PPI.
        _ => 0d,
    };

    /// <summary>
    /// The sRGB profile the book is tagged with — the one embedded in the approved endpaper
    /// pattern, which the partner exported as sRGB and which the registry has already proven.
    ///
    /// Borrowed rather than bundled: shipping a second ICC binary would mean another licensed file
    /// to keep in step with the pack, and the pack already contains the exact profile the approved
    /// artwork was made in. Null if the pattern somehow carries none, in which case the layer is
    /// written untagged and the print preflight campaign catches it — this is not the stage that
    /// gets to decide what a printer accepts.
    /// </summary>
    private static SixLabors.ImageSharp.Metadata.Profiles.Icc.IccProfile? SrgbProfile()
    {
        if (_srgbProfileLoaded)
        {
            return _srgbProfile;
        }

        try
        {
            var pattern = BekiLayoutAssets.Current.EndpaperPatternBytes();

            // Loaded rather than identified: ImageSharp reads a PNG's iCCP chunk only on a full
            // decode, and Image.Identify reports every approved asset as untagged.
            using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(pattern);
            _srgbProfile = image.Metadata.IccProfile;
        }
        catch (Exception)
        {
            _srgbProfile = null;
        }

        _srgbProfileLoaded = true;
        return _srgbProfile;
    }

    private static SixLabors.ImageSharp.Metadata.Profiles.Icc.IccProfile? _srgbProfile;
    private static bool _srgbProfileLoaded;

    /// <summary>
    /// The centre of the picture, at the sheet's own shape — and a refusal when the centre is not
    /// most of the picture.
    ///
    /// §6 Step 8 allows "a tiny centered crop … only to normalize to 15:7" and forbids stretching.
    /// The sheet is exactly 15:7 once the 5 mm bleed is on it (450 ÷ 210), so an image that arrived
    /// normalized passes through untouched. An image that did not — a raw 3:2 render, say, which
    /// loses three tenths of its height to this crop — is not tiny, and taking it silently is how a
    /// book ends up with the composition trimmed off the page it was drawn for.
    /// <see cref="BekiPrintLayoutOptions.PrintCropTolerance"/> is where that line is drawn, and it is
    /// configuration so that an owner who decides to accept a deeper crop records the decision.
    /// </summary>
    private byte[] CropToSheet(byte[] png, float sheetWidthMm, bool print = false, bool enforceCropTolerance = true)
    {
        var targetRatio = (sheetWidthMm + (_layout.BleedMm * 2f))
            / (_layout.SpreadHeightMm + (_layout.BleedMm * 2f));

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

        if (enforceCropTolerance)
        {
            var lostWidth = 1f - ((float)cropWidth / width);
            var lostHeight = 1f - ((float)cropHeight / height);

            if (MathF.Max(lostWidth, lostHeight) > _layout.PrintCropTolerance)
            {
                throw new BekiLayoutException(
                    CompositeFailureCodes.LayoutFailed,
                    $"Fitting a {width}×{height} illustration to the {sheetWidthMm:0}×"
                    + $"{_layout.SpreadHeightMm:0} mm sheet would crop {lostWidth:P1} of its width and "
                    + $"{lostHeight:P1} of its height, past the {_layout.PrintCropTolerance:P0} the "
                    + "layout allows. Normalize the artwork to the sheet's ratio upstream, or record "
                    + "the deeper crop by raising BekiPrintLayout:PrintCropTolerance.");
            }
        }

        byte[] outBytes;
        if (cropWidth == width && cropHeight == height)
        {
            outBytes = png;
        }
        else
        {
            image.Mutate(ctx => ctx.Crop(new SixLabors.ImageSharp.Rectangle(
                (width - cropWidth) / 2, (height - cropHeight) / 2, cropWidth, cropHeight)));

            using var buffer = new MemoryStream();
            image.Save(buffer, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
            outBytes = buffer.ToArray();
        }

        return print
            ? NormalizeForPrint(outBytes, PrintRaster with
            {
                WidthPx = PixelsFor(sheetWidthMm + (_layout.BleedMm * 2f), _layout.PrintTargetPpi),
            })
            : outBytes;
    }

    /// <summary>
    /// One geometry, two widths: the spread, and the single leaf that is half of it.
    ///
    /// The default text style is set here as well, and it is not decoration. QuestPDF's own default
    /// family is Lato; a run that named no family, or a glyph no named family carried, fell through
    /// to it and embedded a Latin face in a Georgian book — which is exactly what the supplier found
    /// in the shipped PDF. Naming the body face as the page default means there is nothing left for
    /// a fall-through to reach.
    /// </summary>
    private void ApplyGeometry(PageDescriptor page, float widthMm, Color? pageColor = null)
    {
        page.Size(new PageSize(
            widthMm + (_layout.BleedMm * 2),
            _layout.SpreadHeightMm + (_layout.BleedMm * 2),
            Unit.Millimetre));

        page.Margin(0);
        page.PageColor(pageColor ?? PageInk);
        page.DefaultTextStyle(style => style.FontFamily(PdfFontBootstrap.BodyFamily));
    }

    /// <summary>Points from millimetres. Every layout number in this file is written in mm.</summary>
    private static float MmToPt(float mm) => mm * PointsPerMm;

    /// <summary>
    /// A sheet's full width in points, bleed included — the box QuestPDF actually lays out in,
    /// since <see cref="ApplyGeometry"/> sets the page to trim plus bleed and takes no margin.
    /// </summary>
    private float SheetWidthPt(float sheetWidthMm) => MmToPt(sheetWidthMm + (_layout.BleedMm * 2f));
    private float SheetHeightPt(float sheetHeightMm) => MmToPt(sheetHeightMm + (_layout.BleedMm * 2f));

    /// <summary>
    /// How far a spread's text column holds back on its inner edge — see <see cref="ComposeSpread"/>
    /// for why the fold side is not simply the safe margin.
    /// </summary>
    private float InnerPaddingMm => MathF.Max(_layout.SafeMarginMm, _layout.GutterZoneMm / 2f);

    /// <summary>
    /// The width, in points, of the box a spread's story text is set in: the reserved column less
    /// its two paddings, and never wider than the configured maximum.
    ///
    /// The maximum is the approved reference's 170 mm (§6 Step 8). On a 450 mm spread a third of the
    /// sheet is narrower than that, so today the cap does not bind — it is written down because the
    /// column share is configuration, and a book whose column was widened must not get a 200 mm
    /// measure that no reading age can track across.
    /// </summary>
    private float StoryColumnWidthPt => MathF.Min(
        (SheetWidthPt(_layout.SpreadWidthMm) * _layout.TextColumnShare)
            - MmToPt(_layout.SafeMarginMm)
            - MmToPt(InnerPaddingMm),
        MmToPt(_layout.MaxTextWidthMm));

    /// <summary>
    /// The exact width, in points, of the cover title's band: the leaf between its safe margins.
    /// </summary>
    private float CoverTitleWidthPt =>
        SheetWidthPt(_layout.PageWidthMm) - (MmToPt(_layout.SafeMarginMm) * 2f);

}

/// <summary>
/// Attaching a Georgian case ending to a child's name.
///
/// One rule, in one place, because the shipped book got it wrong in the most visible line it has:
/// the intro spread printed „ეს წიგნი ეკუთვნის თემო-ს“. The hyphen came from a template that spliced
/// <c>{name}</c> and <c>-ს</c> together, which is what Georgian does to a word written in another
/// alphabet — <c>Luka-ს</c> — and never to a Georgian one. A Georgian name simply takes the ending:
/// ნინო → ნინოს, გიორგი → გიორგის, ლუკა → ლუკას, ბორის → ბორისს.
///
/// So the rule is about the script, not about the name: a name written in Georgian letters gets the
/// ending written onto it, and anything else keeps the hyphen the orthography actually calls for.
/// That is correct for every Georgian name rather than for the ones somebody thought to test.
/// </summary>
public static class GeorgianNameSuffix
{
    /// <summary>The dative — the case „ეკუთვნის“ governs, and the one the intro spread needs.</summary>
    public const string DativeSuffix = "ს";

    /// <summary><paramref name="name"/> in the dative: whose book this is.</summary>
    public static string Dative(string? name) => WithSuffix(name, DativeSuffix);

    /// <summary>
    /// <paramref name="name"/> with a Georgian case ending attached the way the script requires.
    /// An empty name comes back empty rather than as a bare suffix.
    /// </summary>
    public static string WithSuffix(string? name, string suffix)
    {
        var trimmed = name?.Trim() ?? string.Empty;

        if (trimmed.Length == 0 || string.IsNullOrEmpty(suffix))
        {
            return trimmed;
        }

        return IsGeorgian(trimmed[^1])
            ? trimmed + suffix
            : trimmed + "-" + suffix;
    }

    /// <summary>
    /// Mkhedruli and Mtavruli, the two alphabets a Georgian name is actually written in today. The
    /// older Asomtavruli and Nuskhuri blocks are liturgical and are deliberately not accepted: a
    /// name arriving in one of them is far more likely to be mojibake than a child's name.
    /// </summary>
    private static bool IsGeorgian(char character)
        => character is >= 'ა' and <= 'ჿ' or >= 'Ა' and <= 'Ჺ';
}
