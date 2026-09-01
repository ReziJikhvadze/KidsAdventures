using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
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

/// <summary>
/// The cream wash under one page's copy — a shape this book no longer draws.
///
/// **Nothing produces one of these any more.** Owner ruling 2026-09-01, the third and final on the
/// question: book copy is outlined type straight on the artwork, so there is no rectangle behind the
/// words to describe and <see cref="BekiLayoutPageReceipt.Wash"/> is null on every page of every
/// mode. The record is kept rather than deleted because a receipt's shape is a stored document's
/// shape: books already in the blob store carry a <c>"wash"</c> block, and a reader that could not
/// deserialize them would make yesterday's evidence unreadable to answer a question about today's.
///
/// The rectangle was stated in millimetres from the page's TOP-LEFT corner, which is both how a
/// layout is written and how <see cref="BekiTextProbeRect"/> is read, so a receipt could be handed
/// straight to the press probe without a conversion nobody would remember to make.
/// </summary>
/// <param name="PageSide">"left" or "right" — which leaf of the spread the wash belongs to.</param>
/// <param name="FoldClearanceMm">Distance from the wash's inner edge to the centre fold.</param>
/// <param name="TrimClearanceMm">Smallest distance from the wash to any trim edge.</param>
public sealed record BekiWashGeometry(
    [property: JsonPropertyName("x_mm")] double XMm,
    [property: JsonPropertyName("y_mm")] double YMm,
    [property: JsonPropertyName("width_mm")] double WidthMm,
    [property: JsonPropertyName("height_mm")] double HeightMm,
    [property: JsonPropertyName("padding_mm")] double PaddingMm,
    [property: JsonPropertyName("corner_radius_mm")] double CornerRadiusMm,
    [property: JsonPropertyName("ink")] string Ink,
    [property: JsonPropertyName("page_side")] string PageSide,
    [property: JsonPropertyName("fold_clearance_mm")] double FoldClearanceMm,
    [property: JsonPropertyName("trim_clearance_mm")] double TrimClearanceMm);

/// <summary>
/// Where one placed raster's pixels came from — the honesty half of owner ruling 2026-09-01, rule 4.
///
/// The ruling is that "the sizes we have indicated for printing are correct", so the composer now
/// delivers the press sheet at the stated size instead of refusing to build it. The audit finding the
/// refusal came from (P1-01) is not thereby wrong: 2528 × 1180 story art Lanczos-stretched to
/// 5315 × 2480 measures 300 PPI and carries about 143. Both things are true, and this record is how
/// they are both said out loud — the delivered file is the right size, and its receipt states the
/// pixels it actually started from.
///
/// Written into every page's layout receipt, so the answer to "was this sheet enlarged, and from
/// what" is stored beside the book rather than inferred later from the file's own metadata, which is
/// precisely the number that lied in the rejected release.
/// </summary>
/// <param name="Role">The page this raster was placed on — "spread-04", "intro", "cover-front".</param>
/// <param name="Factor">
/// Linear, source width to delivered width. At or under 1 is a reduction, which loses nothing a
/// press would have seen and is never flagged.
/// </param>
/// <param name="Interpolated">
/// True when the enlargement is past <see cref="BekiPrintLayoutOptions.MaxPrintUpscale"/> — pixels
/// the resampler invented rather than detail that arrived. A press-resolution gate reading this
/// receipt must fail on it; what a failed gate is worth is the release policy's decision, not this
/// record's.
/// </param>
public sealed record BekiRasterProvenance(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("source_width_px")] int SourceWidthPx,
    [property: JsonPropertyName("source_height_px")] int SourceHeightPx,
    [property: JsonPropertyName("delivered_width_px")] int DeliveredWidthPx,
    [property: JsonPropertyName("delivered_height_px")] int DeliveredHeightPx,
    [property: JsonPropertyName("factor")] double Factor,
    [property: JsonPropertyName("resampler")] string Resampler,
    [property: JsonPropertyName("interpolated")] bool Interpolated);

/// <summary>One block of type on a page, as it was set: the face, the size, and the ink.</summary>
public sealed record BekiTypographyRecord(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("family")] string Family,
    [property: JsonPropertyName("size_pt")] double SizePt,
    [property: JsonPropertyName("line_height")] double LineHeight,
    [property: JsonPropertyName("colour")] string Colour);

/// <summary>
/// Everything a gate needs to know about one finished page that only layout can answer.
/// </summary>
/// <param name="ImageSha256">
/// The hash of every raster this page actually placed, in placement order — the bytes as embedded,
/// after cropping and any normalization, not the bytes that arrived. Audit §9 asks for a receipt
/// that names what a printed page carries; a hash of the source would name something else.
/// </param>
/// <param name="TextLines">The wrapped lines, measured. The customer gate compares these.</param>
/// <param name="SourceSha256">
/// The hash of every APPROVED asset this page's rasters were derived from, in the same order — the
/// provenance half of the same question <see cref="ImageSha256"/> answers about the artefact.
///
/// The two are not the same number and were being read as though they were. A reading-mode endpaper
/// is the approved pattern downsampled for a screen, and the intro is a background with Beki
/// composited onto it: both embed bytes that cannot possibly hash to a locked file, so a placement
/// check comparing the embedded raster against the asset lock failed every approved page it looked
/// at. The final hash says what the printed page carries; this one says which approved file it came
/// from, and ASSET_PLACEMENT is a question about the second.
///
/// Null on a page whose art is this book's own — the cover boards are crops of the generated wrap,
/// and there is no approved source to name.
/// </param>
public sealed record BekiLayoutPageReceipt(
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("page_width_mm")] double PageWidthMm,
    [property: JsonPropertyName("page_height_mm")] double PageHeightMm,
    [property: JsonPropertyName("bleed_mm")] double BleedMm,
    [property: JsonPropertyName("image_sha256")] IReadOnlyList<string> ImageSha256,
    // Always null since owner ruling 2026-09-01 (third and final): there is no box behind the words
    // to describe. Kept in the shape so receipts written before the ruling still read back.
    [property: JsonPropertyName("wash")] BekiWashGeometry? Wash,
    [property: JsonPropertyName("typography")] IReadOnlyList<BekiTypographyRecord> Typography,
    [property: JsonPropertyName("text_lines")] IReadOnlyList<string> TextLines,
    [property: JsonPropertyName("text_probe")] BekiTextProbeRect? TextProbe,
    [property: JsonPropertyName("source_sha256")] IReadOnlyList<string>? SourceSha256 = null,
    // Owner ruling 2026-09-01, rule 4: the press sheet is built at the stated size, and the size it
    // was built FROM is stated here. One entry per raster this page placed, in placement order —
    // the same order as ImageSha256.
    [property: JsonPropertyName("rasters")] IReadOnlyList<BekiRasterProvenance>? Rasters = null)
{
    /// <summary>The name fulfillment stores this under: <c>receipts/page-NN-layout.json</c>.</summary>
    [JsonIgnore]
    public string FileName => $"page-{Page:00}-layout.json";

    public string ToJson() => JsonSerializer.Serialize(this, BekiLayoutReceipts.JsonOptions);
}

/// <summary>
/// The post-layout receipts for one composed document — amendment A4's "final post-layout receipts
/// and QA for every page".
///
/// Pre-layout illustration QA cannot evidence any of this. It knows what was drawn; it does not know
/// where the words landed on it, how they broke, or what colour they ended up. The rejected book's
/// package had eight spread QA files missing and no layout evidence at all, and the text panel that
/// crossed the fold on Story Spread 4 was invisible to every check that existed.
/// </summary>
/// <param name="Mode">"press", "proof" or "reading" — which of the three outputs this describes.</param>
public sealed record BekiLayoutReceipts(
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("pages")] IReadOnlyList<BekiLayoutPageReceipt> Pages)
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // For the one record in here this file does not own: BekiTextProbeRect belongs to print
        // prep and carries no naming attributes, and a receipt whose probe block was PascalCase
        // while everything around it was snake_case would be a receipt somebody has to read twice.
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        // Georgian in a receipt should be legible to a human opening the file, not ნ escapes.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>
    /// The rectangles amendment A10a's rendered-pixel probe samples: every page whose text was
    /// authored light on a FLAT ground. Today that is the credits page and only the credits page.
    /// The rest of the book is light type too, but it is light type over artwork, where sampling
    /// luminance inside a rectangle would be measuring the illustration and not the words — those
    /// pages are judged by the content-stream half of the same gate instead.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<BekiTextProbeRect> FlatGroundTextProbes =>
        Pages.Where(page => page.TextProbe is not null).Select(page => page.TextProbe!).ToList();

    /// <summary>
    /// Every raster in this document, as the resolution receipt <see cref="BekiPrintPrep"/> wants it.
    ///
    /// The bridge owner ruling 2026-09-01 rule 4 needs between the two halves of the honesty. Layout
    /// is the only stage that knows a sheet was enlarged on its way onto the page — the upscaler in
    /// front of it reports on its own attempt and says nothing about what the composer then did — so
    /// a press caller that builds its <see cref="BekiResolutionReceipt"/> from the upscaler alone
    /// hands the gate a receipt with the enlargement missing from it. Handing this list over as well
    /// is what keeps <c>PRESS_RESOLUTION</c> able to see the stretch it exists to see.
    ///
    /// Every raster, not only the enlarged ones: a receipt that lists only its failures is a receipt
    /// nobody can tell "clean" from "not looked at" in.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<BekiResolutionSource> RasterSources =>
        Pages.SelectMany(page => page.Rasters ?? [])
             .Select(raster => new BekiResolutionSource(
                 raster.Role,
                 raster.SourceWidthPx, raster.SourceHeightPx,
                 raster.DeliveredWidthPx, raster.DeliveredHeightPx,
                 raster.Resampler,
                 raster.Factor,
                 raster.Interpolated))
             .ToList();

    /// <summary>The 1-based page numbers whose type was authored light, for the same probe.</summary>
    [JsonIgnore]
    public IReadOnlyList<int> LightTextPages =>
        Pages.Where(page => page.Typography.Any(type => IsLight(type.Colour)))
             .Select(page => page.Page)
             .ToList();

    private static bool IsLight(string hex) =>
        hex.Equals("#FFF8EB", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One composed document and the evidence for it. Amendment A4's return shape.</summary>
public sealed record BekiComposedBook(byte[] Pdf, BekiLayoutReceipts Receipts);

/// <summary>
/// One candidate treatment for the story copy, for a proof sheet the owner picks from.
///
/// It exists because "the border is wrong" is not a number. The rim's strength, the type's size and
/// the cream's exact tone were each settled by a measurement (see
/// <see cref="BekiPrintLayoutOptions.TextOutlineWidthFactor"/>) and a measurement cannot tell anybody
/// which of several passing treatments is the one the book should be set in. That is the owner's
/// call, and the only honest way to make it is to look at the same real spread ten times.
///
/// **Everything here is a value the shipped path already computes for itself.** The fill is
/// <c>TextColor</c>, the rim is <c>OutlineColor</c>, the radius is <c>RimRadiusPt</c> and the step
/// count is <see cref="BekiPrintLayoutOptions.TextOutlineSteps"/> — so a style the owner chooses off
/// the proof sheet is expressible as configuration and two constants, and there is no treatment on
/// the sheet that production could not then print. Nothing in this record can draw a shape: it is
/// type colour, type size and rim geometry, which is the whole of what the ruling put in question.
///
/// A null of this type — everywhere it is accepted — means "as the book ships", and the composer
/// then evaluates every one of those expressions exactly as it did before this record existed. That
/// is deliberate: the proof path must not be able to move the production path by a pixel.
/// </summary>
internal sealed record BekiTextStyleProof
{
    /// <summary>The type size, in points, taken as given rather than fitted by the step-down ladder:
    /// an owner comparing 18 pt with 22 pt has to be shown 22 pt.</summary>
    public required float FontSizePt { get; init; }

    /// <summary>The leading, in points — the reference states 18 on 27, and so does this.</summary>
    public required float LeadingPt { get; init; }

    /// <summary>
    /// Which cut of the body family: <c>Regular</c>, <c>SemiBold</c> or <c>Bold</c>.
    ///
    /// All three are already registered under <see cref="PdfFontBootstrap.BodyFamily"/>, so this
    /// selects a real licensed face rather than asking Skia to thicken one — a synthesised bold is
    /// a stroke around a Regular glyph, which on Georgian counters is the fault the rim exists to
    /// avoid. Note that only Regular and Bold are on
    /// <see cref="PdfFontBootstrap.BekiFontWhitelist"/>: a SemiBold sample can be looked at, and a
    /// SemiBold book would need that list amended before it could be printed.
    /// </summary>
    public string Weight { get; init; } = "Regular";

    /// <summary>The letter's own colour, <c>RRGGBB</c>, with or without its hash.</summary>
    public string FillColorHex { get; init; } = "FFF8EB";

    /// <summary>How opaque that fill is, 0–1. Under 1 the rim shows through the letter.</summary>
    public float FillOpacity { get; init; } = 1f;

    /// <summary>The rim's colour, <c>RRGGBB</c>.</summary>
    public string RimColorHex { get; init; } = "0D071D";

    /// <summary>How opaque the rim is, 0–1. Under 1 it reads as a halo rather than as an edge.</summary>
    public float RimOpacity { get; init; } = 1f;

    /// <summary>
    /// The rim's reach as a fraction of the em — the shipped 0.09, and zero for no rim at all.
    ///
    /// No floor under it, unlike <see cref="BekiPrintLayoutOptions.TextOutlineWidth"/>: a proof states
    /// its rim exactly, and a floor would silently give the "no rim" sample a hairline.
    /// </summary>
    public float RimWidthFactor { get; init; } = 0.09f;

    /// <summary>How many offset copies make the rim. Sixteen is the shipped count.</summary>
    public int RimSteps { get; init; } = 16;

    /// <summary>QuestPDF's leading, which is a multiple of the size rather than a second size.</summary>
    internal float LineHeight => FontSizePt <= 0f ? 1.5f : LeadingPt / FontSizePt;

    /// <summary>How far the rim reaches from a glyph at this style's own size, in points.</summary>
    internal float RimRadiusPt => MathF.Max(0f, FontSizePt) * MathF.Max(0f, RimWidthFactor);

    internal string FillArgbHex => Argb(FillColorHex, FillOpacity);

    internal string RimArgbHex => Argb(RimColorHex, RimOpacity);

    internal Color Fill => Color.FromHex(FillArgbHex);

    internal Color Rim => Color.FromHex(RimArgbHex);

    /// <summary>
    /// The named weight as QuestPDF's own, with anything unrecognised falling to Regular rather
    /// than throwing: a proof sheet whose fifteenth sample has a typo in it should come back with a
    /// sample somebody can look at and a name they can correct.
    /// </summary>
    internal FontWeight WeightValue => Weight?.Trim().ToUpperInvariant() switch
    {
        "BOLD" => FontWeight.Bold,
        "SEMIBOLD" or "SEMI-BOLD" or "SEMI BOLD" => FontWeight.SemiBold,
        _ => FontWeight.Normal,
    };

    /// <summary>
    /// <c>#AARRGGBB</c>, which is the one form QuestPDF reads an alpha out of — and the same form the
    /// shipped English secondary line is already written in (<c>#D9FFF8EB</c>).
    /// </summary>
    private static string Argb(string hex, float opacity)
    {
        var rgb = hex.TrimStart('#');
        var alpha = (int)MathF.Round(Math.Clamp(opacity, 0f, 1f) * 255f);
        return $"#{alpha:X2}{rgb.ToUpperInvariant()}";
    }
}

public interface IBekiPdfComposer
{
    // ------------------------------------------------------------------------------------------
    // The receipt-returning API (amendment A4).
    //
    // These carry `WithReceipts` names for a reason that is now historical: three byte[]-returning
    // methods stood beside them through the audit-2 campaign, because C# cannot overload on the
    // return type and the batches did not all own each other's call sites. Every caller has moved
    // and the three are gone. The names stay as they are rather than being taken back, because a
    // rename across this many call sites buys nothing a reader does not already have — and
    // "WithReceipts" is a true description: a composed document that leaves no evidence is the state
    // amendment A4 exists to end.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// **The customer's book** — audit P0-08's dedicated trim-size export, and P0-01/P0-02's single
    /// cover master.
    ///
    /// Fourteen pages at the finished size and nothing else: 220 × 200 mm front and back covers,
    /// 440 × 200 mm spreads, zero bleed, CropBox equal to MediaBox, <c>/Lang ka-GE</c>, rasters at
    /// their own resolution and in sRGB. The cover pages are crops of <paramref name="wrapComposite"/>
    /// — the same canonical wrap the press cover is made from — so the parent's book and the printed
    /// book are one design rather than two.
    /// </summary>
    BekiComposedBook ComposeReading(
        MasterStory plan,
        byte[] wrapComposite,
        IReadOnlyList<BekiSpreadArtwork> spreads,
        BekiBookPersonalization? personalization = null) =>
        throw new BekiLayoutException(
            CompositeFailureCodes.LayoutFailed,
            "This composer does not produce reading copies.");

    /// <summary>
    /// The legacy fourteen-page document, laid out on printer geometry from a front-only cover
    /// image — what the previous (non-composite) pipeline still ships and what audit P0-08 rejected
    /// for customer delivery. Composite books use <see cref="ComposeReading"/> instead.
    ///
    /// <paramref name="personalization"/> carries what the intro spread prints and which approved
    /// theme background it is built on. Optional in the signature and required in fact: a book
    /// composed without it cannot resolve an approved intro background and stops with
    /// <c>LAYOUT_FAILED</c> rather than printing a generic one.
    /// </summary>
    BekiComposedBook ComposeWithReceipts(
        MasterStory plan,
        byte[] coverImage,
        IReadOnlyList<BekiSpreadArtwork> spreads,
        BekiBookPersonalization? personalization = null) =>
        throw new BekiLayoutException(
            CompositeFailureCodes.LayoutFailed,
            "This composer does not produce layout receipts.");

    /// <summary>
    /// The production print interior: the twelve interior spreads and nothing else — no cover face,
    /// no back-cover face.
    ///
    /// A separate artifact because the supplier's audit rejected the alternative outright: the cover
    /// is a continuous back-spine-front wrap whose geometry comes from the printer's dieline, and a
    /// 230x210 leaf bound into the interior file is not an approximation of that, it is a different
    /// object.
    /// </summary>
    BekiComposedBook ComposeInteriorWithReceipts(
        MasterStory plan,
        IReadOnlyList<BekiSpreadArtwork> spreads,
        BekiBookPersonalization? personalization = null) =>
        throw new BekiLayoutException(
            CompositeFailureCodes.LayoutFailed,
            "This composer does not produce layout receipts.");

    /// <summary>
    /// The press cover: the composited 512 × 245 mm wrap as one full-bleed page with the Ottia title
    /// typeset as vector into the locked front title-safe rectangle
    /// (<see cref="Composite.BekiCoverDieline"/>). Press preparation — boxes, colour, PDF/X —
    /// happens to the result, not here. The default refuses so test doubles need not care.
    /// </summary>
    BekiComposedBook ComposeCoverPressWithReceipts(string title, byte[] wrapComposite) =>
        throw new BekiLayoutException(
            CompositeFailureCodes.LayoutFailed,
            "This composer does not produce layout receipts.");

    /// <summary>
    /// The canonical wrap's front board, cropped for a 220 × 200 mm page — the same bytes the
    /// customer's front cover is built on, exposed so that the reader UI's own cover image is that
    /// crop rather than a third design (audit P0-01, plan D1).
    /// </summary>
    byte[] CropFrontBoard(byte[] wrapPng) =>
        throw new BekiLayoutException(
            CompositeFailureCodes.LayoutFailed,
            "This composer does not crop cover boards.");

    /// <summary>The same for the back board (audit P0-02).</summary>
    byte[] CropBackBoard(byte[] wrapPng) =>
        throw new BekiLayoutException(
            CompositeFailureCodes.LayoutFailed,
            "This composer does not crop cover boards.");

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
/// Sets a Beki-format book for print and for the parent's own screen.
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
/// **Three outputs, one layout state.** Audit §10.2 is explicit that the press interior, the press
/// cover and the customer reading PDF must be derived from one logical layout and not from
/// independent pipelines — the rejected release had two cover producers and a reading copy built
/// straight through the print path. So the text geometry here is computed on the TRIM in every mode
/// and the press page is that geometry with the bleed added around it: the same column, the same
/// fitted size, the same wrapped lines, offset by five millimetres. What differs between the press
/// file and the download is the paper it is imagined on, not the book.
///
/// **The fixed pages are approved artwork, not drawings.** The endpaper pattern and the six intro
/// backgrounds arrive from <see cref="BekiLayoutAssets"/>, hash-verified before they are placed,
/// and Beki herself is composited onto the intro by the same exact engine the story spreads use.
/// There is no placeholder behind any of them any more: a missing or altered asset, or a theme with
/// no approved background, stops the book. The composer used to draw a dot field and a tinted
/// ground instead, and the first anyone noticed was a printed book with a placeholder bound into it.
///
/// **The book's copy is outlined type drawn straight on the artwork. No box behind the words.**
/// That sentence has now been written three times in this file, and this is the version that stands:
/// owner ruling 2026-09-01, the owner's third and FINAL ruling on it. A cream wash was built, removed
/// by the owner after the first live v1.5 book, restored by audit 1.0's P1-04 on the supplier's
/// evidence, and removed again here — the owner's ruling overrides the audit, and the contract files
/// are left saying what the supplier said. Every block of book copy is cream #FFF8EB with a #0D071D
/// rim, set as vector text over the picture: the cover title, the back-cover address, the intro
/// spread's four lines, and each story spread's Georgian paragraph with its English sibling under it.
/// The credits page is the one page this does not describe, and it never did — its ground is the
/// book's own purple, so its type is plain light text on a flat colour and needs no rim.
///
/// The layout arithmetic the wash brought with it stays, because it was never really about the wash:
/// the copy column is still measured to the wrapped copy, and it is still refused if it would cross
/// the centre fold or reach past the trim safety margin. What is gone is the rectangle that used to
/// be painted at those millimetres. If the copy will not fit at any size the age band allows, the
/// book stops with <c>TEXT_OVERFLOW</c>; it is never set at a size that still overflows, and it is
/// never rewritten.
///
/// Every picture is placed at the sheet's own proportions. A centred crop of more than
/// <see cref="BekiPrintLayoutOptions.PrintCropTolerance"/> per axis is refused rather than performed
/// quietly, because a crop that deep is a composition nobody approved.
///
/// **And every press sheet is built at the size the product states.** Owner ruling 2026-09-01,
/// rule 4, verbatim: "the sizes we have indicated for printing are correct." The composer used to
/// refuse to enlarge a short raster onto the 300-PPI sheet, which on a deployment with no approved
/// super-resolver meant no press interior was ever produced — a rule about honesty that had turned
/// into a rule about not shipping. <see cref="NormalizeForPrint"/> performs the enlargement again,
/// and <see cref="RasterProvenance"/> writes what it did into the page's receipt: the pixels that
/// arrived, the pixels that were delivered, the factor, and <c>interpolated: true</c> past
/// <see cref="BekiPrintLayoutOptions.MaxPrintUpscale"/>. The press-resolution gate still fails on
/// that, in the preflight report, where the release policy can see it. The file is the right size
/// and the receipt is the truth; neither is traded for the other.
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

    /// <summary>Which of the three documents is being built, and on whose geometry.</summary>
    private enum BekiRenderMode
    {
        /// <summary>The press file: trim plus bleed, and every raster normalized to the sheet.</summary>
        Press,

        /// <summary>The proof render: the press geometry, native rasters, for looking at.</summary>
        Proof,

        /// <summary>The parent's download: trim exactly, no bleed, rasters at their own resolution.</summary>
        Reading,
    }

    /// <summary>Every page's ground, unless a page — an endpaper — asks for its own.</summary>
    private static readonly Color PageInk = Color.FromHex("#281B3F");

    /// <summary>Cream, and the same one the reader sets its pages on.</summary>
    private const string TextColorHex = "#FFF8EB";

    private static readonly Color TextColor = Color.FromHex(TextColorHex);

    /// <summary>
    /// The quieter cream the second language is set in, when a book prints both.
    ///
    /// Translucent rather than a second hue, so the English line reads as the same voice said more
    /// quietly. It is held at 85% and not lower on purpose: the fill sits on top of the rim, and a
    /// fill much thinner than this lets the #0D071D show through the middle of the glyphs and turns
    /// cream type grey.
    /// </summary>
    private const string EnglishTextColorHex = "#D9FFF8EB";

    private static readonly Color EnglishTextColor = Color.FromHex(EnglishTextColorHex);

    /// <summary>
    /// The rim under every outlined line in the book — the page's own near-black, so the edge reads
    /// as one soft shadow the artwork casts rather than as a second colour of type.
    ///
    /// Named for a wash it once sat behind: the Continue Adventure chip shared this ink until the
    /// Locked Print Specification §6 removed it with its QR, and the story wash borrowed the name
    /// after that. Both are gone (owner ruling 2026-09-01, third and final); the ink stays, as the
    /// outline it always really was.
    /// </summary>
    private const string TextOutlineInk = "0D071D";

    /// <summary>The glyph outline, so the rim reads as one shadow.</summary>
    private static readonly Color OutlineColor = Color.FromHex("#" + TextOutlineInk);

    /// <summary>
    /// The blank free endpaper's paper tone. The opening spread patterns the pastedown and leaves
    /// the facing leaf empty (handoff §5, spread 1), and "empty" on a bound hardcover is stock, not
    /// the spreads' dark ground.
    /// </summary>
    private static readonly Color EndpaperPaper = Color.FromHex("#F3E7D2");

    /// <summary>The brand address on the back cover. Type, not a character — Locked Spec §6.</summary>
    private const string BackCoverAddress = "beki.ge";

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
    private static readonly ConcurrentDictionary<string, FixedPage> FixedPageArtwork =
        new(StringComparer.Ordinal);

    /// <summary>
    /// One fixed page's finished raster and the record of how it got that way.
    ///
    /// The two are cached together rather than the provenance being recomputed at receipt time,
    /// because working it out means measuring the approved source — a 39-megapixel PNG read off disk
    /// — and a book has two endpaper spreads and an intro that would each pay for it again.
    /// </summary>
    private readonly record struct FixedPage(byte[] Bytes, BekiRasterProvenance Provenance);

    /// <summary>
    /// The wrapped lines of one block of copy, keyed by the text, the size and the measure.
    ///
    /// Static for the same reason and with the same safety: wrapping is a pure function of those
    /// three plus the face, which does not change within a process. It is cached because working the
    /// lines out costs one measurement document per word, and the receipts want them for every
    /// spread of every book — a service composing its second Forest book should not pay twice for
    /// the same sentence.
    /// </summary>
    private static readonly ConcurrentDictionary<string, IReadOnlyList<string>> WrappedLines =
        new(StringComparer.Ordinal);

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
    /// The approved Beki mark for the credits spread, resolved through the pose registry by the id
    /// the layout registry names.
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

    // ==============================================================================================
    // The public compose methods
    // ==============================================================================================

    public BekiComposedBook ComposeWithReceipts(
        MasterStory plan,
        byte[] coverImage,
        IReadOnlyList<BekiSpreadArtwork> spreads,
        BekiBookPersonalization? personalization = null)
    {
        var receipts = new ReceiptBook(BekiRenderMode.Press);
        var pdf = PdfPrintBoxes.Apply(
            Build(plan, coverImage, null, spreads, personalization, BekiRenderMode.Press, receipts)
                .GeneratePdf(),
            _layout.BleedMm);

        return new BekiComposedBook(pdf, receipts.Build());
    }

    public BekiComposedBook ComposeInteriorWithReceipts(
        MasterStory plan,
        IReadOnlyList<BekiSpreadArtwork> spreads,
        BekiBookPersonalization? personalization = null)
    {
        var receipts = new ReceiptBook(BekiRenderMode.Press);
        var pdf = PdfPrintBoxes.Apply(
            Build(plan, null, null, spreads, personalization, BekiRenderMode.Press, receipts)
                .GeneratePdf(),
            _layout.BleedMm);

        return new BekiComposedBook(pdf, receipts.Build());
    }

    /// <summary>
    /// <inheritdoc cref="IBekiPdfComposer.ComposeReading"/>
    ///
    /// Three things make this a different document rather than the print file with a flag flipped,
    /// and each of them is an audit finding:
    ///
    /// * **Zero bleed, CropBox present** (P0-08). The page IS the trim, so there is no overrun to
    ///   hide and no box to disagree about; <see cref="PdfReaderBoxes"/> then states the CropBox a
    ///   viewer needs and the document language P2-2 asks for.
    /// * **No print normalization** (P1-01, P2-1). Nothing is stretched to a 300-PPI target here.
    ///   The rasters are embedded as they arrived, which is both the honest resolution and the
    ///   reason the download stops being an inflated copy of the press file.
    /// * **Cover pages cropped from the wrap** (P0-01, P0-02). The front page is the front board of
    ///   <paramref name="wrapComposite"/> with the same Ottia title set into the same relative
    ///   rectangle; the back page is the back board, environment-only by construction, carrying
    ///   nothing but the address. There is no second cover design and no flat purple placeholder.
    /// </summary>
    public BekiComposedBook ComposeReading(
        MasterStory plan,
        byte[] wrapComposite,
        IReadOnlyList<BekiSpreadArtwork> spreads,
        BekiBookPersonalization? personalization = null)
    {
        ArgumentNullException.ThrowIfNull(wrapComposite);

        var receipts = new ReceiptBook(BekiRenderMode.Reading);
        var pdf = PdfReaderBoxes.Apply(
            Build(plan, null, wrapComposite, spreads, personalization, BekiRenderMode.Reading, receipts)
                .GeneratePdf());

        return new BekiComposedBook(pdf, receipts.Build());
    }

    public IReadOnlyList<byte[]> RenderPages(
        MasterStory plan,
        byte[] coverImage,
        IReadOnlyList<BekiSpreadArtwork> spreads,
        BekiBookPersonalization? personalization = null) =>
        RenderPages(plan, coverImage, spreads, personalization, ProofRasterDpi);

    /// <summary>The proof render's density: enough to see a layout, cheap enough to render fourteen.</summary>
    internal const int ProofRasterDpi = 96;

    /// <summary>
    /// <inheritdoc cref="IBekiPdfComposer.RenderPages"/>
    ///
    /// <paramref name="rasterDpi"/> is for measurement rather than for looking. A rim measured at
    /// proof density is measured mostly in antialiasing — a two-pixel edge is one pixel of rim and
    /// one of blend — and rule 3's readability evidence has to be about the letterform, not about the
    /// rasteriser. <c>BekiTextRimReadabilityTests</c> renders the worst-case spread at press-ish
    /// density and counts pixels there.
    /// </summary>
    internal IReadOnlyList<byte[]> RenderPages(
        MasterStory plan,
        byte[] coverImage,
        IReadOnlyList<BekiSpreadArtwork> spreads,
        BekiBookPersonalization? personalization,
        int rasterDpi) =>
        Build(plan, coverImage, null, spreads, personalization,
                BekiRenderMode.Proof, new ReceiptBook(BekiRenderMode.Proof))
            .GenerateImages(new ImageGenerationSettings
            {
                ImageFormat = ImageFormat.Png,
                RasterDpi = rasterDpi > 0 ? rasterDpi : ProofRasterDpi,
            })
            .ToList();

    /// <summary>The style proof's density: press-ish, so a rim is a letterform and not a blur.</summary>
    internal const int StyleProofRasterDpi = 200;

    /// <summary>
    /// One story spread, set in one candidate text treatment, rendered to a PNG somebody can look at.
    ///
    /// **The proofing rig for the owner's question about the border.** It goes through
    /// <see cref="ComposeSpread"/> — the real one, the same method that sets a sold book — with the
    /// real registered faces, the real outline stack, the real column arithmetic and the real
    /// artwork off the pack. Nothing about the picture is drawn here: what comes back is a page of
    /// the book with one thing changed, so a treatment the owner points at is a treatment production
    /// can print by construction rather than by a second implementation agreeing with a first.
    ///
    /// <paramref name="style"/> of null renders the spread exactly as the book ships it, which is
    /// what makes the CURRENT sample on the sheet checkable: the shipped-style sample and the null
    /// render are the same pixels, or the parameterization moved something it should not have.
    ///
    /// Only the faces are verified, not the whole book's asset set: this page carries no endpaper
    /// pattern, no intro background and no Beki mark, so demanding a theme would be asking the proof
    /// to prove something it does not use.
    /// </summary>
    internal byte[] RenderStyleProofSpread(
        StorySpread spread,
        byte[] artwork,
        BekiBookPersonalization? personalization,
        BekiTextStyleProof? style,
        int rasterDpi = StyleProofRasterDpi)
    {
        ArgumentNullException.ThrowIfNull(spread);
        ArgumentNullException.ThrowIfNull(artwork);

        QuestPDF.Settings.License = LicenseType.Community;

        _assets.VerifyFonts();
        PdfFontBootstrap.EnsureRegistered();

        // Proof geometry: the press sheet, trim plus bleed, with the artwork at its own resolution.
        // The same mode RenderPages uses, so a sample and a proof page of the same book are the same
        // picture at the same size.
        var pages = Document.Create(document => ComposeSpread(
                document, artwork, spread, personalization,
                BekiRenderMode.Proof, new ReceiptBook(BekiRenderMode.Proof), style))
            .GenerateImages(new ImageGenerationSettings
            {
                ImageFormat = ImageFormat.Png,
                RasterDpi = rasterDpi > 0 ? rasterDpi : StyleProofRasterDpi,
            })
            .ToList();

        return pages.Count == 1
            ? pages[0]
            : throw new InvalidOperationException(
                $"A one-spread style proof rendered {pages.Count} pages; a spread is one page.");
    }

    /// <summary>
    /// <inheritdoc cref="IBekiPdfComposer.ComposeCoverPressWithReceipts"/>
    ///
    /// One page at the wrap's own 512 × 245 — no bleed added, because the locked spec's turn-ins
    /// ARE the wrap's overrun and its boxes are all equal. The artwork arrives already composited
    /// (base plus the exact approved pose); this only sets the title, in the same outlined Ottia
    /// the reading cover uses, centred inside the locked title-safe rectangle.
    /// </summary>
    public BekiComposedBook ComposeCoverPressWithReceipts(string title, byte[] wrapComposite)
    {
        ArgumentNullException.ThrowIfNull(wrapComposite);

        QuestPDF.Settings.License = LicenseType.Community;

        // Only the faces: the wrap carries no registry artwork of its own — the pose is already
        // composited into the bytes, hash-verified where the compositing happened.
        _assets.VerifyFonts();
        PdfFontBootstrap.EnsureRegistered();

        var titleWidthPt = MmToPt(BekiCoverDieline.TitleSafeWidthMm);
        var titleSize = _layout.StoryFontSize * 2f;

        var pdf = Document.Create(document =>
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
                            item, title, titleSize, 1.25f,
                            TextColor, OutlineColor, titleWidthPt,
                            PdfFontBootstrap.TitleFamily, centred: true));
                });
            });
        }).WithMetadata(new DocumentMetadata { Title = title }).GeneratePdf();

        var receipts = new ReceiptBook(BekiRenderMode.Press);
        receipts.Add("cover-press-wrap", page => new BekiLayoutPageReceipt(
            page,
            "cover-press-wrap",
            BekiCoverDieline.CanvasWidthMm,
            BekiCoverDieline.CanvasHeightMm,
            0d,
            [Sha256(wrapComposite)],
            Wash: null,
            [new BekiTypographyRecord(
                "cover-title", PdfFontBootstrap.TitleFamily, titleSize, 1.25d, TextColorHex)],
            WrapLines(title, titleSize, titleWidthPt, PdfFontBootstrap.TitleFamily),
            TextProbe: null,
            // The wrap arrives composited and is placed as it arrived — this page resizes nothing.
            // Whether the wrap itself carries the dieline's 6047 × 2894 is the upscaler's receipt to
            // answer, and it is a separate line in the same preflight.
            Rasters: [Provenance("cover-press-wrap", wrapComposite, wrapComposite)]));

        return new BekiComposedBook(pdf, receipts.Build());
    }

    // ==============================================================================================
    // The build
    // ==============================================================================================

    /// <param name="coverImage">
    /// The legacy front-only cover face, for <see cref="BekiRenderMode.Press"/> and
    /// <see cref="BekiRenderMode.Proof"/> only. Null builds the print interior: the twelve interior
    /// spreads with no cover faces. The cover is a printer-dieline wrap, not two leaves of this
    /// document, and the audit's finding stands as the reason this is a parameter and not a second
    /// builder: the hybrid 14-page file must never again be the production deliverable.
    /// </param>
    /// <param name="wrapComposite">
    /// The canonical cover master, for <see cref="BekiRenderMode.Reading"/>. Its two board crops
    /// become the customer's front and back pages (P0-01, P0-02).
    /// </param>
    private Document Build(
        MasterStory plan,
        byte[]? coverImage,
        byte[]? wrapComposite,
        IReadOnlyList<BekiSpreadArtwork> spreads,
        BekiBookPersonalization? personalization,
        BekiRenderMode mode,
        ReceiptBook receipts)
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
            if (mode == BekiRenderMode.Reading)
            {
                ComposeReadingFrontCover(document, plan.Concept.Title, wrapComposite!, receipts);
            }
            else if (coverImage is not null)
            {
                ComposeCover(document, plan.Concept.Title, coverImage, mode, receipts);
            }

            ComposeEndpaper(document, rear: false, mode, receipts);
            ComposeIntro(document, themeId, plan.Concept.Title, personalization, mode, receipts);

            foreach (var artwork in spreads.OrderBy(spread => spread.SpreadNumber))
            {
                if (!bySpread.TryGetValue(artwork.SpreadNumber, out var spread))
                {
                    // A picture with no words is still a page of the book; dropping it would
                    // silently shorten the story.
                    ComposeArtOnly(document, artwork.Image, artwork.SpreadNumber, mode, receipts);
                    continue;
                }

                ComposeSpread(document, artwork.Image, spread, personalization, mode, receipts);
            }

            ComposeCredits(document, mode, receipts);
            ComposeEndpaper(document, rear: true, mode, receipts);

            if (mode == BekiRenderMode.Reading)
            {
                ComposeReadingBackCover(document, wrapComposite!, receipts);
            }
            else if (coverImage is not null)
            {
                ComposeBackCover(document, mode, receipts);
            }
        }).WithMetadata(new DocumentMetadata
        {
            // The canonical book title — the same string the cover and the intro print. The
            // audited file carried QuestPDF's defaults here while its pages disagreed with each
            // other about the book's name; one field now feeds all of them.
            Title = plan.Concept.Title,
            Language = PdfReaderBoxes.DocumentLanguage,
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

    // ==============================================================================================
    // Cover pages
    // ==============================================================================================

    /// <summary>
    /// The legacy cover leaf: a single page, half the spread, artwork to the bleed, and the title
    /// set over it in the licensed display face.
    ///
    /// **Not a customer deliverable any more.** Audit P0-01 found this page and the press cover to
    /// be two different designs — this one built from a separately AI-redrawn cover with a Beki that
    /// is not the approved asset. The customer's front page is now
    /// <see cref="ComposeReadingFrontCover"/>, cropped from the same wrap the press cover uses. What
    /// remains here serves the proof render and any caller not yet moved off
    /// <see cref="Compose"/>.
    /// </summary>
    private void ComposeCover(
        IDocumentContainer container, string title, byte[] image,
        BekiRenderMode mode, ReceiptBook receipts)
    {
        var placed = CropToPage(image, _layout.PageWidthMm, mode, enforceCropTolerance: false);
        var titleSize = _layout.StoryFontSize * 2f;

        container.Page(page =>
        {
            ApplyGeometry(page, _layout.PageWidthMm, mode);

            page.Content().Layers(layers =>
            {
                layers.PrimaryLayer().Image(placed).FitUnproportionally().UseOriginalImage();

                // The title band is the full width between the safe margins, and the type is
                // centred inside it rather than the block being centred around the type. The two
                // put a single line in exactly the same place — but only the first gives
                // OutlinedText a width it can know before the page is laid out.
                layers.Layer()
                    .Padding(Bleed(mode), Unit.Millimetre)
                    .PaddingHorizontal(_layout.SafeMarginMm, Unit.Millimetre)
                    .PaddingBottom(_layout.SafeMarginMm * 1.6f, Unit.Millimetre)
                    .AlignBottom()
                    .Element(item => OutlinedText(
                        item, title, titleSize, 1.25f,
                        TextColor, OutlineColor, CoverTitleWidthPt,
                        PdfFontBootstrap.TitleFamily, centred: true));
            });
        });

        receipts.Add("cover-front-legacy", page => new BekiLayoutPageReceipt(
            page, "cover-front-legacy",
            _layout.PageWidthMm + (Bleed(mode) * 2f), _layout.SpreadHeightMm + (Bleed(mode) * 2f),
            Bleed(mode),
            [Sha256(placed)],
            Wash: null,
            [new BekiTypographyRecord(
                "cover-title", PdfFontBootstrap.TitleFamily, titleSize, 1.25d, TextColorHex)],
            WrapLines(title, titleSize, CoverTitleWidthPt, PdfFontBootstrap.TitleFamily),
            TextProbe: null,
            Rasters: [Provenance("cover-front-legacy", image, placed)]));
    }

    /// <summary>
    /// The customer's front cover: the wrap master's front board, and the same title on it.
    ///
    /// Audit P0-01's required correction, clause 5 — "crop the front and back board panels from that
    /// same master for the customer PDF". The crop window is
    /// <see cref="BekiCoverDieline.FrontBoardDigitalCrop"/>, which takes the board's full width and
    /// the page's own ratio out of its height so that the placement onto 220 × 200 mm is a uniform
    /// scale and not a squash (amendment A3).
    ///
    /// The title is the identical string, in the identical face, set into the identical rectangle —
    /// expressed as fractions of the crop window so that "the same place on the cover" survives the
    /// change of coordinate system. What the parent downloads and what the press prints are the same
    /// design, which is the whole of what P0-01 asked for.
    /// </summary>
    private void ComposeReadingFrontCover(
        IDocumentContainer container, string title, byte[] wrapComposite, ReceiptBook receipts)
    {
        var crop = CropFrontBoard(wrapComposite);
        var board = FitForScreen(crop, BekiCoverDieline.DigitalPageWidthMm);

        var (leftFraction, topFraction, widthFraction, heightFraction) =
            BekiCoverDieline.InsideFrontBoardCrop(
                BekiCoverDieline.TitleSafeLeftMm, BekiCoverDieline.TitleSafeTopMm,
                BekiCoverDieline.TitleSafeWidthMm, BekiCoverDieline.TitleSafeHeightMm);

        var titleLeftMm = leftFraction * BekiCoverDieline.DigitalPageWidthMm;
        var titleTopMm = topFraction * BekiCoverDieline.DigitalPageHeightMm;
        var titleWidthMm = widthFraction * BekiCoverDieline.DigitalPageWidthMm;
        var titleHeightMm = heightFraction * BekiCoverDieline.DigitalPageHeightMm;

        // The type scales with the board (BekiCoverDieline.DigitalScale). Setting the press size on
        // a page 1.12% smaller would break the title's lines somewhere the printed cover does not.
        var titleSize = _layout.StoryFontSize * 2f * BekiCoverDieline.DigitalScale;

        container.Page(page =>
        {
            ApplyGeometry(page, _layout.PageWidthMm, BekiRenderMode.Reading);

            page.Content().Layers(layers =>
            {
                layers.PrimaryLayer().Image(board).FitUnproportionally().UseOriginalImage();

                layers.Layer()
                    .PaddingLeft(titleLeftMm, Unit.Millimetre)
                    .PaddingTop(titleTopMm, Unit.Millimetre)
                    .AlignLeft()
                    .AlignTop()
                    .Width(titleWidthMm, Unit.Millimetre)
                    .Height(titleHeightMm, Unit.Millimetre)
                    .AlignMiddle()
                    .Element(item => OutlinedText(
                        item, title, titleSize, 1.25f,
                        TextColor, OutlineColor, MmToPt(titleWidthMm),
                        PdfFontBootstrap.TitleFamily, centred: true));
            });
        });

        receipts.Add("cover-front", page => new BekiLayoutPageReceipt(
            page, "cover-front",
            BekiCoverDieline.DigitalPageWidthMm, BekiCoverDieline.DigitalPageHeightMm, 0d,
            [Sha256(board)],
            Wash: null,
            [new BekiTypographyRecord(
                "cover-title", PdfFontBootstrap.TitleFamily, titleSize, 1.25d, TextColorHex)],
            WrapLines(title, titleSize, MmToPt(titleWidthMm), PdfFontBootstrap.TitleFamily),
            TextProbe: null,
            // The board crop is the source: the wrap is a different picture, and a factor measured
            // against it would be describing a crop rather than a resize.
            Rasters: [Provenance("cover-front", crop, board)]));
    }

    /// <summary>
    /// The customer's back cover: the wrap master's back board, and the address on it.
    ///
    /// Audit P0-02 in one sentence — the shipped back cover was "a flat dark-purple page with only
    /// beki.ge", a placeholder where the printed book carries the back panel of a continuous world.
    /// So this page is that panel, cropped by the same rule as the front. It carries no Beki: the
    /// crop is environment-only by construction, because the wrap prompt forbids a character on the
    /// left of the picture and the exact pose is composited on the right — and Locked Spec §6 keeps
    /// the back cover Beki-free besides. Nothing is drawn here but the address, which is type.
    /// </summary>
    private void ComposeReadingBackCover(
        IDocumentContainer container, byte[] wrapComposite, ReceiptBook receipts)
    {
        var crop = CropBackBoard(wrapComposite);
        var board = FitForScreen(crop, BekiCoverDieline.DigitalPageWidthMm);
        var addressSize = _layout.StoryFontSize * 0.85f;

        container.Page(page =>
        {
            ApplyGeometry(page, _layout.PageWidthMm, BekiRenderMode.Reading);

            page.Content().Layers(layers =>
            {
                layers.PrimaryLayer().Image(board).FitUnproportionally().UseOriginalImage();

                layers.Layer()
                    .PaddingHorizontal(_layout.SafeMarginMm, Unit.Millimetre)
                    .PaddingBottom(_layout.SafeMarginMm, Unit.Millimetre)
                    .AlignBottom()
                    .Element(item => OutlinedText(
                        item, BackCoverAddress, addressSize, 1.25f,
                        TextColor, OutlineColor,
                        MmToPt(BekiCoverDieline.DigitalPageWidthMm - (_layout.SafeMarginMm * 2f)),
                        PdfFontBootstrap.BodyFamily, centred: true));
            });
        });

        receipts.Add("cover-back", page => new BekiLayoutPageReceipt(
            page, "cover-back",
            BekiCoverDieline.DigitalPageWidthMm, BekiCoverDieline.DigitalPageHeightMm, 0d,
            [Sha256(board)],
            Wash: null,
            [new BekiTypographyRecord(
                "back-cover-address", PdfFontBootstrap.BodyFamily, addressSize, 1.25d, TextColorHex)],
            [BackCoverAddress],
            TextProbe: null,
            Rasters: [Provenance("cover-back", crop, board)]));
    }

    /// <summary>
    /// The legacy back cover: the book's ground and the address, and since the Locked Print
    /// Specification §6 no Beki on it. Superseded for customer delivery by
    /// <see cref="ComposeReadingBackCover"/>, which shows the wrap's own back panel instead of a
    /// flat colour — audit P0-02.
    /// </summary>
    private void ComposeBackCover(
        IDocumentContainer container, BekiRenderMode mode, ReceiptBook receipts)
    {
        var addressSize = _layout.StoryFontSize * 0.85f;

        container.Page(page =>
        {
            ApplyGeometry(page, _layout.PageWidthMm, mode);

            page.Content()
                .AlignMiddle()
                .Column(column =>
                {
                    column.Spacing(10);

                    column.Item().AlignCenter().Text(BackCoverAddress)
                        .FontFamily(PdfFontBootstrap.BodyFamily)
                        .FontSize(addressSize)
                        .FontColor(TextColor);
                });
        });

        receipts.Add("cover-back-legacy", page => new BekiLayoutPageReceipt(
            page, "cover-back-legacy",
            _layout.PageWidthMm + (Bleed(mode) * 2f), _layout.SpreadHeightMm + (Bleed(mode) * 2f),
            Bleed(mode),
            [],
            Wash: null,
            [new BekiTypographyRecord(
                "back-cover-address", PdfFontBootstrap.BodyFamily, addressSize, 1.25d, TextColorHex)],
            [BackCoverAddress],
            TextProbe: null));
    }

    /// <summary>The front board of the canonical wrap, cropped for the customer's front page.</summary>
    public byte[] CropFrontBoard(byte[] wrapPng)
        => CropBoard(wrapPng, front: true);

    /// <summary>The back board of the canonical wrap, cropped for the customer's back page.</summary>
    public byte[] CropBackBoard(byte[] wrapPng)
        => CropBoard(wrapPng, front: false);

    /// <summary>
    /// One board out of the wrap, and nothing else done to it.
    ///
    /// Crop only — no resize. The window already carries the customer page's ratio (amendment A3),
    /// so the scale onto 220 × 200 mm happens once, at placement, uniformly; resampling here would
    /// be a second resize of the same pixels and would throw away detail before the page even knows
    /// how large it will be shown.
    /// </summary>
    private static byte[] CropBoard(byte[] wrapPng, bool front)
    {
        ArgumentNullException.ThrowIfNull(wrapPng);

        using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(wrapPng);

        var window = front
            ? BekiCoverDieline.FrontBoardDigitalCrop(image.Width, image.Height)
            : BekiCoverDieline.BackBoardDigitalCrop(image.Width, image.Height);

        image.Mutate(ctx => ctx.Crop(new SixLabors.ImageSharp.Rectangle(
            window.XPx, window.YPx, window.WidthPx, window.HeightPx)));

        using var buffer = new MemoryStream();
        image.Save(buffer, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        return buffer.ToArray();
    }

    // ==============================================================================================
    // Fixed interior pages
    // ==============================================================================================

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
    private void ComposeEndpaper(
        IDocumentContainer container, bool rear, BekiRenderMode mode, ReceiptBook receipts)
    {
        var artwork = EndpaperArtwork(mode);

        container.Page(page =>
        {
            ApplyGeometry(page, _layout.SpreadWidthMm, mode, EndpaperPaper);

            page.Content().Layers(layers =>
            {
                layers.PrimaryLayer().Image(artwork.Bytes)
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

        var endpaperRole = rear ? "endpaper-rear" : "endpaper-front";

        receipts.Add(endpaperRole, page => new BekiLayoutPageReceipt(
            page, endpaperRole,
            _layout.SpreadWidthMm + (Bleed(mode) * 2f), _layout.SpreadHeightMm + (Bleed(mode) * 2f),
            Bleed(mode),
            [Sha256(artwork.Bytes)],
            Wash: null, [], [], TextProbe: null,
            // The approved pattern this page's raster was derived from. On the press path the two
            // hashes are the same file; on the reading path the embedded bytes are a downsample of
            // it, and only this one can be looked up in the asset lock.
            SourceSha256: [_assets.EndpaperPattern.Sha256],
            Rasters: [artwork.Provenance with { Role = endpaperRole }]));
    }

    /// <summary>
    /// The personalized intro spread (handoff §9): the approved theme background across the whole
    /// sheet, the exact <c>pose_07_curious_lean</c> composited onto its right half, and the child's
    /// own lines set in outlined vector Noto straight on the artwork on the left.
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
    ///
    /// The copy is cream with its own dark rim, drawn straight on the background — owner ruling
    /// 2026-09-01, third and final. Audit P1-04's "the intro has no controlled local support" was a
    /// finding about this page in particular and was answered with a cream wash; the owner has
    /// overruled it, and the support the lines have now is the rim each glyph carries.
    /// </summary>
    private void ComposeIntro(
        IDocumentContainer container,
        string themeId,
        string title,
        BekiBookPersonalization? personalization,
        BekiRenderMode mode,
        ReceiptBook receipts)
    {
        var artwork = IntroArtwork(themeId, mode);
        var lines = IntroLines(title, personalization);
        var columnWidthMm = IntroColumnWidthMm;
        var copyWidthPt = MmToPt(columnWidthMm) - (MmToPt(_layout.WashPaddingMm) * 2f);

        // Measured before the page exists, for the same reason the story ladder is: the block has to
        // be centred on the leaf and refused if it will not fit, and both need its height first.
        var spacingPt = _layout.WashPaddingMm * PointsPerMm * 0.8f;
        var copyHeightPt = 0f;
        for (var index = 0; index < lines.Count; index++)
        {
            if (index > 0) copyHeightPt += spacingPt;
            copyHeightPt += MeasureBlockHeightPt(lines[index].Text, lines[index].SizePt, copyWidthPt);
        }

        var columnHeightMm = (copyHeightPt / PointsPerMm) + (_layout.WashPaddingMm * 2f);
        var availableHeightMm = _layout.SpreadHeightMm - (_layout.SafeMarginMm * 2f);

        if (columnHeightMm > availableHeightMm)
        {
            throw new BekiLayoutException(
                CompositeFailureCodes.TextOverflow,
                $"The intro spread's copy needs {columnHeightMm:0.#} mm of leaf and the safe area "
                + $"holds {availableHeightMm:0.#} mm. The intro has no step-down ladder — its lines "
                + "come from configured templates — so this is a templates change, not a layout one.");
        }

        var columnTopMm = Bleed(mode) + _layout.SafeMarginMm
            + ((availableHeightMm - columnHeightMm) / 2f);
        var columnLeftMm = Bleed(mode) + _layout.SafeMarginMm;

        container.Page(page =>
        {
            ApplyGeometry(page, _layout.SpreadWidthMm, mode);

            page.Content().Layers(layers =>
            {
                layers.PrimaryLayer().Image(artwork.Bytes)
                    .FitUnproportionally().UseOriginalImage();

                // Everything below is written on the TRIM: the bleed is a frame around the layout,
                // never part of it, which is what lets the press file and the download break their
                // lines identically (audit §10.2, "one canonical layout state").
                layers.Layer().Padding(Bleed(mode), Unit.Millimetre).Row(row =>
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
                        .Width(columnWidthMm, Unit.Millimetre)
                        // No box behind the words (owner ruling 2026-09-01, third and final). The
                        // padding stays as a plain inset, so the measure the lines are wrapped to —
                        // and therefore every line break the receipt records — is unchanged.
                        .Padding(_layout.WashPaddingMm, Unit.Millimetre)
                        .Column(column =>
                        {
                            column.Spacing(spacingPt);

                            foreach (var line in lines)
                            {
                                var text = line;
                                column.Item().Element(item => OutlinedText(
                                    item, text.Text, text.SizePt, StoryLineHeight,
                                    TextColor, OutlineColor, copyWidthPt,
                                    PdfFontBootstrap.BodyFamily, centred: false));
                            }
                        });

                    row.RelativeItem();
                });
            });
        });

        EnforceCopyColumnSafety(
            columnLeftMm, columnTopMm, columnWidthMm, columnHeightMm, "left", mode, "intro");

        receipts.Add("intro", page => new BekiLayoutPageReceipt(
            page, "intro",
            _layout.SpreadWidthMm + (Bleed(mode) * 2f), _layout.SpreadHeightMm + (Bleed(mode) * 2f),
            Bleed(mode),
            [Sha256(artwork.Bytes)],
            Wash: null,
            lines.Select(line => new BekiTypographyRecord(
                line.Role, PdfFontBootstrap.BodyFamily, line.SizePt, StoryLineHeight, TextColorHex))
                .ToList(),
            lines.SelectMany(line =>
                WrapLines(line.Text, line.SizePt, copyWidthPt, PdfFontBootstrap.BodyFamily)).ToList(),
            TextProbe: null,
            // Both approved files this page's single raster was composited from: the world's
            // background and the pose pasted onto it. The composite itself hashes to neither, which
            // is exactly why the placement check needs these.
            SourceSha256: [_assets.IntroBackground(themeId).Sha256, IntroPoseSha256()],
            Rasters: [artwork.Provenance]));
    }

    /// <summary>One typeset line of the intro spread: what it says, how big, and what it is for.</summary>
    private readonly record struct IntroLine(string Role, string Text, float SizePt);

    /// <summary>
    /// The intro spread's four lines, in the proof's own order: the dedication, the age under it,
    /// the world it opens into, and the invitation.
    ///
    /// The child's name is inflected rather than concatenated. The shipped book printed „თემო-ს“,
    /// which is a template that glued a hyphen and a case ending onto whatever it was given;
    /// Georgian writes the dative straight onto a Georgian-script name — ნინო becomes ნინოს — and
    /// keeps the hyphen only for a name written in another alphabet. See
    /// <see cref="GeorgianNameSuffix.Dative"/>.
    ///
    /// Returned as data rather than drawn, because the block above has to know how tall they are —
    /// it is centred on the leaf, and it refuses a book whose lines will not fit — before any of
    /// them is set.
    /// </summary>
    private List<IntroLine> IntroLines(string title, BekiBookPersonalization? personalization)
    {
        var headerSize = _layout.StoryFontSize * 1.35f;
        var bodySize = _layout.StoryFontSize;
        var quietSize = _layout.StoryFontSize * 0.8f;

        var lines = new List<IntroLine>(4);

        if (personalization is not null)
        {
            var belongs = _layout.IntroBelongsTemplate
                .Replace("{name_dative}", GeorgianNameSuffix.Dative(personalization.ChildName))
                .Replace("{name}", personalization.ChildName);

            if (!string.IsNullOrWhiteSpace(belongs))
            {
                lines.Add(new IntroLine("intro-dedication", belongs, headerSize));
            }

            var age = _layout.IntroAgeTemplate.Replace("{age}", personalization.Age.ToString());
            if (!string.IsNullOrWhiteSpace(age))
            {
                lines.Add(new IntroLine("intro-age", age, quietSize));
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
            lines.Add(new IntroLine("intro-world", theme, bodySize));
        }

        // The invitation addresses the child by name in the vocative, which in Georgian is the
        // plain name — so this template takes {name} untouched and must never take a suffix.
        var invite = personalization is null
            ? _layout.IntroInviteTemplate.Replace("{name}, ", string.Empty)
            : _layout.IntroInviteTemplate.Replace("{name}", personalization.ChildName);

        if (!string.IsNullOrWhiteSpace(invite))
        {
            lines.Add(new IntroLine("intro-invite", invite, bodySize));
        }

        return lines;
    }

    /// <summary>
    /// The credits spread — spec v2's replacement for the standalone closing leaf: the left
    /// half deliberately blank, the right half carrying the Beki mark, the sign-off line, the
    /// rate-us QR and the credits line, all reusable across every order. One combined
    /// credits-and-review page, exactly one — the deprecated P18 must not come back beside it.
    /// The blank-URL-drops-the-QR stance is inherited unchanged: a code that scans to nothing
    /// is worse than no code.
    ///
    /// **The one page whose light type stands on a flat ground, and the one that nearly lost it.**
    /// Every other page sets cream over artwork with a dark rim under it; this one sets plain cream
    /// on the book's own purple, which is why it is the only page a pixel probe can judge. Audit
    /// P0-07: the CMYK conversion turned this cream into `0 g` black on the book's own purple and
    /// the credits became "nearly invisible". The colour authored here is unchanged and correct; the
    /// fix is upstream, in print prep, and the evidence it needs is the text rectangle this page
    /// reports in its layout receipt — a rendered-pixel probe has to be told where to look, and
    /// amendment A10a says the layout receipt is what tells it.
    ///
    /// The mark is placed with <c>UseOriginalImage</c>, which is not a detail: without it QuestPDF
    /// re-rasters a 32 mm placement at 288 DPI and the press resolution gate — which since amendment
    /// A1 measures effective PPI per placed image and not per page — fails the book on the one image
    /// in it that was never short of pixels.
    /// </summary>
    private void ComposeCredits(
        IDocumentContainer container, BekiRenderMode mode, ReceiptBook receipts)
    {
        var mark = BekiMark();
        var endingSize = _layout.StoryFontSize * 1.05f;
        var captionSize = _layout.StoryFontSize * 0.7f;
        var creditsSize = _layout.StoryFontSize * 0.85f;
        var hasQr = !string.IsNullOrWhiteSpace(_layout.ReviewQrUrl);

        container.Page(page =>
        {
            ApplyGeometry(page, _layout.SpreadWidthMm, mode);

            page.Content().Padding(Bleed(mode), Unit.Millimetre).Row(row =>
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

                             column.Item().AlignCenter().Width(CreditsMarkWidthMm, Unit.Millimetre)
                                 .Image(mark).FitWidth().UseOriginalImage();

                             column.Item().AlignCenter().Text(_layout.EndingLine)
                                 .FontFamily(PdfFontBootstrap.BodyFamily)
                                 .FontSize(endingSize)
                                 .LineHeight(1.5f)
                                 .FontColor(TextColor);

                             if (hasQr)
                             {
                                 column.Item().AlignCenter()
                                     .Width(46, Unit.Millimetre)
                                     .Background(Colors.White)
                                     .Padding(4, Unit.Millimetre)
                                     .Svg(QrSvg(_layout.ReviewQrUrl))
                                     .FitWidth();

                                 column.Item().AlignCenter().Text(_layout.EndingQrCaption)
                                     .FontFamily(PdfFontBootstrap.BodyFamily)
                                     .FontSize(captionSize)
                                     .FontColor(TextColor);
                             }

                             column.Item().AlignCenter().Text(_layout.CreditsLine)
                                 .FontFamily(PdfFontBootstrap.BodyFamily)
                                 .FontSize(creditsSize)
                                 .FontColor(TextColor);
                         });
                });
            });
        });

        receipts.Add("credits", page => new BekiLayoutPageReceipt(
            page, "credits",
            _layout.SpreadWidthMm + (Bleed(mode) * 2f), _layout.SpreadHeightMm + (Bleed(mode) * 2f),
            Bleed(mode),
            [Sha256(mark)],
            Wash: null,
            [
                new BekiTypographyRecord(
                    "credits-ending", PdfFontBootstrap.BodyFamily, endingSize, 1.5d, TextColorHex),
                new BekiTypographyRecord(
                    "credits-line", PdfFontBootstrap.BodyFamily, creditsSize, 1.0d, TextColorHex),
            ],
            WrapLines(_layout.EndingLine, endingSize, CreditsColumnWidthPt, PdfFontBootstrap.BodyFamily),
            CreditsTextProbe(page, mode, endingSize, captionSize, creditsSize, hasQr),
            // The mark is placed verbatim, so the two hashes agree here — stated anyway, because a
            // provenance the gate has to infer from a coincidence is not a provenance.
            SourceSha256: [Engine.Value.Registry.Pose(_assets.BekiMarkPoseId).Sha256],
            // Placed verbatim at the approved pose's own pixels: nothing resampled it, and the
            // receipt says so rather than leaving the one page that was never short of pixels
            // looking like the one page nobody measured.
            Rasters: [Provenance("credits", mark, mark)]));
    }

    /// <summary>
    /// Where the credits column's own type sits, in millimetres from the page's top-left — the
    /// rectangle amendment A10a's rendered-pixel probe samples.
    ///
    /// Computed rather than eyeballed: the column is vertically centred in the right leaf's safe
    /// area, so its top follows from the total height of what is in it, and each item's height is
    /// either a known millimetre width times the mark's own aspect or a measured block. The
    /// rectangle returned is the sign-off line's band, widened to the column, because that is the
    /// largest continuous run of cream glyphs on the page and therefore the easiest thing on it to
    /// measure the luminance of.
    /// </summary>
    private BekiTextProbeRect? CreditsTextProbe(
        int page, BekiRenderMode mode, float endingSize, float captionSize, float creditsSize,
        bool hasQr)
    {
        try
        {
            const float spacingPt = 14f;
            var columnWidthPt = CreditsColumnWidthPt;

            var markHeightPt = MmToPt(CreditsMarkWidthMm) / MarkAspect();
            var endingHeightPt = MeasureBlockHeightPt(_layout.EndingLine, endingSize, columnWidthPt);
            var qrHeightPt = hasQr ? MmToPt(46f) : 0f;
            var captionHeightPt = hasQr
                ? MeasureBlockHeightPt(_layout.EndingQrCaption, captionSize, columnWidthPt)
                : 0f;
            var creditsHeightPt = MeasureBlockHeightPt(_layout.CreditsLine, creditsSize, columnWidthPt);

            var items = hasQr ? 5 : 3;
            var totalPt = markHeightPt + endingHeightPt + qrHeightPt + captionHeightPt
                + creditsHeightPt + (spacingPt * (items - 1));

            var availablePt = MmToPt(_layout.SpreadHeightMm - (_layout.SafeMarginMm * 2f));
            var topPt = MmToPt(Bleed(mode) + _layout.SafeMarginMm)
                + MathF.Max(0f, (availablePt - totalPt) / 2f);

            var endingTopPt = topPt + markHeightPt + spacingPt;

            var leftMm = Bleed(mode) + (_layout.SpreadWidthMm / 2f) + _layout.SafeMarginMm;

            return new BekiTextProbeRect(
                page,
                Math.Round(leftMm, 2),
                Math.Round(endingTopPt / PointsPerMm, 2),
                Math.Round(columnWidthPt / PointsPerMm, 2),
                Math.Round(endingHeightPt / PointsPerMm, 2),
                "credits-text");
        }
        catch (BekiLayoutException)
        {
            // A probe rectangle is evidence, not a gate. If the credits line cannot be measured the
            // page is still correct and the press probe simply has nothing to sample here; failing
            // a paid book over a missing measurement would be the wrong trade in the wrong place.
            return null;
        }
    }

    /// <summary>The credits mark's own width-to-height ratio, so its placed height is known.</summary>
    private float MarkAspect()
    {
        try
        {
            var info = SixLabors.ImageSharp.Image.Identify(BekiMark());
            return info.Height <= 0 ? 1f : (float)info.Width / info.Height;
        }
        catch (Exception)
        {
            return 1f;
        }
    }

    /// <summary>The credits column's measure: the right leaf between its safe margins.</summary>
    private float CreditsColumnWidthPt =>
        MmToPt((_layout.SpreadWidthMm / 2f) - (_layout.SafeMarginMm * 2f));

    /// <summary>The Beki mark's placed width on the credits spread, in millimetres.</summary>
    private const float CreditsMarkWidthMm = 32f;

    // ==============================================================================================
    // Story spreads
    // ==============================================================================================

    /// <summary>A spread whose text went missing: artwork to the bleed and nothing else.</summary>
    private void ComposeArtOnly(
        IDocumentContainer container, byte[] image, int number,
        BekiRenderMode mode, ReceiptBook receipts)
    {
        var placed = CropToPage(image, _layout.SpreadWidthMm, mode);

        container.Page(page =>
        {
            ApplyGeometry(page, _layout.SpreadWidthMm, mode);
            page.Content().Image(placed).FitUnproportionally().UseOriginalImage();
        });

        var artOnlyRole = $"spread-{number:00}-art-only";

        receipts.Add(artOnlyRole, page => new BekiLayoutPageReceipt(
            page, artOnlyRole,
            _layout.SpreadWidthMm + (Bleed(mode) * 2f), _layout.SpreadHeightMm + (Bleed(mode) * 2f),
            Bleed(mode),
            [Sha256(placed)],
            Wash: null, [], [], TextProbe: null,
            Rasters: [Provenance(artOnlyRole, image, placed)]));
    }

    /// <param name="proof">
    /// A candidate text treatment for the style proof sheet, or null — which is what production and
    /// every other caller passes, and which makes every expression below the one it was before this
    /// parameter existed. The layout itself is never parameterized: a proof render is the real page,
    /// with the real column arithmetic, the real safety refusals and the real artwork, differing
    /// only in the type set into it.
    /// </param>
    private void ComposeSpread(
        IDocumentContainer container, byte[] image, StorySpread spread,
        BekiBookPersonalization? personalization, BekiRenderMode mode, ReceiptBook receipts,
        BekiTextStyleProof? proof = null)
    {
        var textSide = Prompts.BekiSpreadRhythm.TextSideFor(spread.Number);
        var textOnLeft = textSide.Equals("left", StringComparison.OrdinalIgnoreCase);

        // Spread 8 is an ordinary spread. It carried a Continue Adventure chip with a second QR
        // until the Locked Print Specification §6 ruled: exactly one QR in the book, on the
        // credits spread — the chip and its zone reservation are gone, and the last story page
        // got its full text column back.
        var outerPaddingMm = _layout.SafeMarginMm;
        var innerPaddingMm = InnerPaddingMm;

        var usableHeightPt = MmToPt(_layout.SpreadHeightMm - (outerPaddingMm * 2f));

        // Decided before the page is laid out, because the ladder is allowed to fail the book and a
        // failure has to happen before any of it is drawn.
        var fitted = proof is null
            ? FitStoryText(spread, personalization, usableHeightPt)
            : FitProofText(spread, proof);

        // The type the copy is actually set in. Read off the layout when nothing is being proofed,
        // which is every book this composer has ever produced.
        var lineHeight = proof?.LineHeight ?? StoryLineHeight;
        var fill = proof is null ? TextColor : proof.Fill;
        var rim = proof is null ? OutlineColor : proof.Rim;
        var fillHex = proof is null ? TextColorHex : proof.FillArgbHex;

        var placed = CropToPage(image, _layout.SpreadWidthMm, mode);

        var columnWidthMm = StoryColumnWidthPt / PointsPerMm;
        var columnHeightMm = (fitted.ContentHeightPt / PointsPerMm) + (_layout.WashPaddingMm * 2f);
        var columnTopMm = Bleed(mode) + outerPaddingMm;
        var columnLeftMm = Bleed(mode) + (textOnLeft
            ? outerPaddingMm
            : (_layout.SpreadWidthMm * (1f - _layout.TextColumnShare)) + innerPaddingMm);

        EnforceCopyColumnSafety(
            columnLeftMm, columnTopMm, columnWidthMm, columnHeightMm,
            textOnLeft ? "left" : "right", mode, $"spread {spread.Number}");

        container.Page(page =>
        {
            ApplyGeometry(page, _layout.SpreadWidthMm, mode);

            page.Content().Layers(layers =>
            {
                // Cropped to the sheet's own proportions, so filling the frame is exact rather
                // than a stretch.
                layers.PrimaryLayer().Image(placed).FitUnproportionally().UseOriginalImage();

                // The bleed is a frame around the layout, not part of it: the row below divides the
                // TRIM, so the column, the fitted size and the wrapped lines are the same numbers in
                // the press file and in the download (audit §10.2).
                layers.Layer().Padding(Bleed(mode), Unit.Millimetre).Row(row =>
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
                        // top of its column.
                        .AlignTop()
                        .AlignLeft()
                        .Width(columnWidthMm, Unit.Millimetre)
                        // No box behind the words (owner ruling 2026-09-01, third and final): the
                        // copy sits straight on the artwork as cream type with its own dark rim.
                        // The padding stays as a plain inset, so the measure the ladder fitted the
                        // copy to — and every line break it produced — is unchanged.
                        .Padding(_layout.WashPaddingMm, Unit.Millimetre)
                        .Column(column =>
                        {
                            column.Spacing(EnglishGapPt);

                            column.Item().Element(item => OutlinedText(
                                item, spread.Text, fitted.FontSize, lineHeight,
                                fill, rim, StoryCopyWidthPt,
                                PdfFontBootstrap.BodyFamily, centred: false, proof));

                            // The second language follows its Georgian sibling in the same
                            // treatment — smaller, and a quieter cream — rather than being set some
                            // other way, which would read as a caption instead of a translation.
                            if (fitted.EnglishFontSize is { } englishSize)
                            {
                                column.Item().Element(item => OutlinedText(
                                    item, spread.TextEn!, englishSize, lineHeight,
                                    EnglishTextColor, rim, StoryCopyWidthPt,
                                    PdfFontBootstrap.BodyFamily, centred: false, proof));
                            }
                        });

                    if (textOnLeft) row.RelativeItem(1f - _layout.TextColumnShare);
                });
            });
        });

        var typography = new List<BekiTypographyRecord>
        {
            new($"spread-{spread.Number:00}-ka", PdfFontBootstrap.BodyFamily,
                fitted.FontSize, lineHeight, fillHex),
        };

        var textLines = WrapLines(
            spread.Text, fitted.FontSize, StoryCopyWidthPt, PdfFontBootstrap.BodyFamily).ToList();

        if (fitted.EnglishFontSize is { } english)
        {
            typography.Add(new BekiTypographyRecord(
                $"spread-{spread.Number:00}-en", PdfFontBootstrap.BodyFamily,
                english, lineHeight, EnglishTextColorHex));
            textLines.AddRange(
                WrapLines(spread.TextEn!, english, StoryCopyWidthPt, PdfFontBootstrap.BodyFamily));
        }

        var spreadRole = $"spread-{spread.Number:00}";

        receipts.Add(spreadRole, page => new BekiLayoutPageReceipt(
            page, spreadRole,
            _layout.SpreadWidthMm + (Bleed(mode) * 2f), _layout.SpreadHeightMm + (Bleed(mode) * 2f),
            Bleed(mode),
            [Sha256(placed)],
            Wash: null, typography, textLines, TextProbe: null,
            // Rule 4's disclosure, per spread: what the illustration stage delivered, what the sheet
            // took, and whether the difference was made up by a resampler.
            Rasters: [Provenance(spreadRole, image, placed)]));
    }

    /// <summary>The air between the Georgian block and its English sibling, in points.</summary>
    private const float EnglishGapPt = 10f;

    /// <summary>The type size a spread's copy is set at, the English size if it prints too, and
    /// the measured height of the two together.</summary>
    private readonly record struct FittedStoryText(
        float FontSize, float? EnglishFontSize, float ContentHeightPt);

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
    ///
    /// The measured height comes back with the size, because the copy column is that height plus its
    /// inset and measuring it a second time would be a second opinion about the same page.
    /// </summary>
    private FittedStoryText FitStoryText(
        StorySpread spread, BekiBookPersonalization? personalization, float usableHeightPt)
    {
        var printEnglish = _layout.PrintEnglishToo && !string.IsNullOrWhiteSpace(spread.TextEn);
        var columnWidthPt = StoryCopyWidthPt;
        var insetHeightPt = MmToPt(_layout.WashPaddingMm) * 2f;

        var ladder = StoryFontSizeLadder(personalization?.Age);
        var measured = new List<string>(ladder.Count);

        foreach (var size in ladder)
        {
            var height = MeasureBlockHeightPt(spread.Text, size, columnWidthPt);
            var englishSize = printEnglish ? size * 0.82f : (float?)null;

            if (englishSize is { } english)
            {
                height += EnglishGapPt + MeasureBlockHeightPt(spread.TextEn!, english, columnWidthPt);
            }

            measured.Add($"{size:0.##}pt→{height + insetHeightPt:0}pt");

            if (height + insetHeightPt <= usableHeightPt)
            {
                return new FittedStoryText(size, englishSize, height);
            }
        }

        throw new BekiLayoutException(
            CompositeFailureCodes.TextOverflow,
            $"Spread {spread.Number}'s Georgian copy does not fit its column at any size the age "
            + $"band allows ({string.Join(", ", measured)}; the column holds {usableHeightPt:0}pt). "
            + "The copy is not rewritten to make it fit — this book needs a human.");
    }

    /// <summary>
    /// The same block, at the size a proof style states, with the ladder taken out of the way.
    ///
    /// The ladder exists to keep a bought book inside its page, and it is right to. On a proof sheet
    /// it would be a liar: the owner asks to see 22 pt, the ladder finds 22 pt a millimetre too tall,
    /// and the sample that comes back says 20 pt in a filename that says 22. So a proof states its
    /// size and the page is measured to it — and if that block is genuinely too tall for the column,
    /// <see cref="EnforceCopyColumnSafety"/> refuses the render exactly as it would refuse a book,
    /// which is the answer the owner actually needs about that size.
    ///
    /// No English sibling: the proof is about the Georgian the book is set in, and a second block
    /// under it would change what the sample is a sample of.
    /// </summary>
    private FittedStoryText FitProofText(StorySpread spread, BekiTextStyleProof proof) =>
        new(proof.FontSizePt,
            EnglishFontSize: null,
            // Measured in the proof's own leading and its own cut of the family, so the column the
            // safety rules are applied to is the paragraph that is actually drawn.
            MeasureBlockHeightPt(spread.Text, proof.FontSizePt, StoryCopyWidthPt, proof));

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

    // ==============================================================================================
    // The copy column and its rules
    // ==============================================================================================

    /// <summary>
    /// One block of copy, checked against the three rules its rectangle has to obey, and the book
    /// refused if it breaks any of them.
    ///
    /// "Keep it within the selected page, outside the fold safety area and trim safety margins."
    /// Audit P1-04 wrote that about a cream wash, and the owner has since ruled the wash away
    /// (2026-09-01, third and final) — but the sentence was never really about the wash. It is about
    /// where a reader's eye has to go, and words that run into the gutter are unreadable whether or
    /// not there is a box behind them. So the three assertions stay, applied to the copy column
    /// itself: Story Spread 4's panel crossed the fold and nothing in the pipeline was in a position
    /// to notice, and that remains the failure this guard exists to make impossible.
    ///
    /// A refusal here is a layout bug and not a content one: the geometry that produces these
    /// numbers is entirely ours.
    /// </summary>
    private void EnforceCopyColumnSafety(
        float leftMm, float topMm, float widthMm, float heightMm,
        string side, BekiRenderMode mode, string what)
    {
        // A twentieth of a millimetre. The rules are exactly met by the default geometry — the safe
        // margin IS the trim clearance — so a comparison without slack would fail on the last bit
        // of a float rather than on a layout anybody could see.
        const float Slack = 0.05f;

        var bleed = Bleed(mode);
        var pageWidthMm = _layout.SpreadWidthMm + (bleed * 2f);
        var trimLeft = bleed;
        var trimTop = bleed;
        var trimRight = bleed + _layout.SpreadWidthMm;
        var trimBottom = bleed + _layout.SpreadHeightMm;
        var fold = pageWidthMm / 2f;

        var right = leftMm + widthMm;
        var bottom = topMm + heightMm;

        var onLeft = side == "left";
        var foldClearance = onLeft ? fold - right : leftMm - fold;

        var trimClearance = MathF.Min(
            MathF.Min(leftMm - trimLeft, trimRight - right),
            MathF.Min(topMm - trimTop, trimBottom - bottom));

        if (onLeft ? right > fold + Slack : leftMm < fold - Slack)
        {
            throw new BekiLayoutException(
                CompositeFailureCodes.LayoutFailed,
                $"The {what} copy column crosses the centre fold: it is the {side} leaf's, and it "
                + $"runs from {leftMm:0.#} mm to {right:0.#} mm on a page whose fold is at "
                + $"{fold:0.#} mm.");
        }

        if (foldClearance < _layout.FoldSafetyMm - Slack)
        {
            throw new BekiLayoutException(
                CompositeFailureCodes.LayoutFailed,
                $"The {what} copy column comes within {foldClearance:0.#} mm of the centre fold; the "
                + $"layout keeps it outside the {_layout.FoldSafetyMm:0.#} mm fold safety area.");
        }

        if (trimClearance < _layout.SafeMarginMm - Slack)
        {
            throw new BekiLayoutException(
                CompositeFailureCodes.LayoutFailed,
                $"The {what} copy column comes within {trimClearance:0.#} mm of the trim; the layout "
                + $"keeps it inside the {_layout.SafeMarginMm:0.#} mm trim safety margin.");
        }
    }

    // ==============================================================================================
    // Measurement
    // ==============================================================================================

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
    /// <param name="proof">
    /// A proof style, whose leading and weight the measurement has to borrow, or null for the
    /// book's own — which every caller in the book passes, and which leaves both the cache key
    /// and the measurement document exactly as they were.
    /// </param>
    private float MeasureBlockHeightPt(
        string text, float fontSize, float widthPt, BekiTextStyleProof? proof = null)
    {
        var key = proof is null
            ? string.Join('', text, fontSize, widthPt)
            : string.Join('', text, fontSize, widthPt, proof.LineHeight, proof.Weight);

        if (!_measuredBlockHeights.TryGetValue(key, out var cached))
        {
            cached = BuildBlockHeightPt(text, fontSize, widthPt, proof: proof);
            _measuredBlockHeights[key] = cached;
        }

        return cached ?? throw new BekiLayoutException(
            CompositeFailureCodes.LayoutFailed,
            $"The composer could not measure a {fontSize:0.##}pt block of story text in a "
            + $"{widthPt:0}pt column, so it cannot tell whether the copy fits the page.");
    }

    private float? BuildBlockHeightPt(
        string text, float fontSize, float widthPt, string? fontFamily = null,
        BekiTextStyleProof? proof = null)
    {
        if (string.IsNullOrWhiteSpace(text) || widthPt <= 1f)
        {
            return 0f;
        }

        var family = fontFamily ?? PdfFontBootstrap.BodyFamily;

        // A proof's block is measured the way the proof's block is set. Its leading is its own —
        // the designers' sheets are not all on the reference's 18:27 — and a Bold cut is a wider
        // letter that wraps a line earlier, so measuring it in Regular would size the column for a
        // paragraph that is not the one being drawn.
        var lineHeight = proof?.LineHeight ?? StoryLineHeight;

        try
        {
            var block = Document.Create(document => document.Page(page =>
            {
                page.ContinuousSize(widthPt, Unit.Point);
                page.Margin(0);
                page.PageColor(Colors.Transparent);
                page.DefaultTextStyle(style => proof is null
                    ? style.FontFamily(family)
                    : style.FontFamily(family).Weight(proof.WeightValue));

                page.Content().Text(text)
                    .FontFamily(family, PdfFontBootstrap.BodyFamily)
                    .FontSize(fontSize)
                    .LineHeight(lineHeight)
                    // Any ink: this document is measured and thrown away, never looked at.
                    .FontColor(TextColor);
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
    /// Where a block of copy actually breaks, line by line — amendment A4's "text line breaks (the
    /// measured wrapped lines)".
    ///
    /// Measured, not modelled. The line breaks are Skia's and HarfBuzz's business, so the only
    /// honest way to learn them is the way the composer learns a block's height: set the text and
    /// look. Words are added one at a time and the accumulated string is measured; the word that
    /// makes the block two lines tall is the first word of the second line. That is one measurement
    /// document per word, which is why the answers are cached process-wide — the function is pure in
    /// the text, the size, the measure and the face.
    ///
    /// Best-effort by design. These lines are evidence for the customer-PDF gate ("visual content,
    /// line breaks, text colors, and asset versions match the canonical master"), and evidence that
    /// cannot be gathered is a weaker receipt, never a failed book: a measurement that will not run
    /// returns the block as one line and the page is unaffected.
    /// </summary>
    private IReadOnlyList<string> WrapLines(
        string text, float fontSize, float widthPt, string fontFamily)
    {
        if (string.IsNullOrWhiteSpace(text) || widthPt <= 1f)
        {
            return [];
        }

        var key = string.Join(
            '', fontFamily, fontSize.ToString("R"), widthPt.ToString("R"),
            StoryLineHeight.ToString("R"), text);

        return WrappedLines.GetOrAdd(key, _ => MeasureWrappedLines(text, fontSize, widthPt, fontFamily));
    }

    private IReadOnlyList<string> MeasureWrappedLines(
        string text, float fontSize, float widthPt, string fontFamily)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length <= 1)
        {
            return [text.Trim()];
        }

        var oneLinePt = BuildBlockHeightPt(words[0], fontSize, widthPt, fontFamily);
        if (oneLinePt is not { } unit || unit <= 0f)
        {
            return [text.Trim()];
        }

        // Half a line of slack: the raster measurement rounds to whole pixels, and a block that is
        // one line tall must never be read as two because of a rounding pixel.
        var ceiling = unit * 1.5f;

        var lines = new List<string>();
        var current = words[0];

        for (var index = 1; index < words.Length; index++)
        {
            var candidate = current + " " + words[index];
            var height = BuildBlockHeightPt(candidate, fontSize, widthPt, fontFamily);

            if (height is null)
            {
                return [text.Trim()];
            }

            if (height <= ceiling)
            {
                current = candidate;
                continue;
            }

            lines.Add(current);
            current = words[index];
        }

        lines.Add(current);
        return lines;
    }

    // ==============================================================================================
    // Artwork
    // ==============================================================================================

    /// <summary>
    /// The approved endpaper pattern, ready for the sheet it is going onto.
    ///
    /// On the press path it arrives at exactly the working raster — 5315 × 2480, 300 PPI, sRGB — so
    /// it passes through byte-identical. "Use the approved endpaper pattern exactly; do not
    /// regenerate it" (§9), and a lossy re-encode of an approved asset is a regeneration. On the
    /// reading path nothing touches it at all: the download embeds the approved bytes, which is both
    /// §9's instruction and audit P1-01's — no resampling in either direction.
    /// </summary>
    /// <summary>
    /// The provenance in the cached entry carries no role — the same bytes serve the front endpaper
    /// and the rear, and a role baked in at first use would label the second page with the first
    /// page's name. Callers stamp it with <c>with { Role = … }</c>.
    /// </summary>
    private FixedPage EndpaperArtwork(BekiRenderMode mode)
        => FixedPageArtwork.GetOrAdd(
            FixedPageKey(
                $"endpaper|{(mode == BekiRenderMode.Reading ? "screen" : "press")}",
                _assets.EndpaperPattern.Sha256),
            _ =>
            {
                var pattern = _assets.EndpaperPatternBytes();
                var placed = mode == BekiRenderMode.Reading
                    ? FitForScreen(pattern, _layout.SpreadWidthMm)
                    : NormalizeForPrint(pattern, PrintRaster, preserveApprovedBytes: true);

                return new FixedPage(placed, Provenance(string.Empty, pattern, placed));
            });

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
    private FixedPage IntroArtwork(string themeId, BekiRenderMode mode)
        => FixedPageArtwork.GetOrAdd(
            FixedPageKey(
                $"intro|{themeId}|{(mode == BekiRenderMode.Reading ? "screen" : "press")}",
                _assets.IntroBackground(themeId).Sha256),
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

                var placed = mode == BekiRenderMode.Reading
                    ? FitForScreen(composite.Png, _layout.SpreadWidthMm)
                    : NormalizeForPrint(composite.Png, PrintRaster);

                // The source is the composite, not the background: what this page places is the
                // background with Beki already on it, and that is the picture whose pixels either
                // reached the sheet or were stretched to it.
                return new FixedPage(placed, Provenance("intro", composite.Png, placed));
            });

    /// <summary>
    /// One raster made fit for a screen, which here means made no larger than it needs to be.
    ///
    /// The whole of what this does is reduce. Audit P1-01's finding is that enlargement is a lie
    /// about detail, and P2-1's is that a 34 MB download is a press master somebody forgot to
    /// export; both are answered by a function that has no branch for making an image bigger. A
    /// story spread arrives at about 143 PPI of the finished page and passes through untouched; the
    /// approved endpaper and intro artwork arrive at 300 and come out at
    /// <see cref="BekiPrintLayoutOptions.ScreenTargetPpi"/>. The press masters are not involved:
    /// nothing on the print path calls this.
    ///
    /// Any failure to read or resize returns the bytes as they came. A reading copy carrying an
    /// oversized picture is a large file; a reading copy that failed to build is a parent without
    /// their book.
    /// </summary>
    private byte[] FitForScreen(byte[] png, float pageWidthMm)
    {
        if (_layout.ScreenTargetPpi <= 0 || pageWidthMm <= 0f)
        {
            return png;
        }

        try
        {
            using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(png);

            var ceiling = PixelsFor(pageWidthMm, _layout.ScreenTargetPpi);
            if (image.Width <= ceiling)
            {
                return png;
            }

            var height = Math.Max(1, (int)MathF.Round(
                (float)image.Height * ceiling / image.Width));

            image.Mutate(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new SixLabors.ImageSharp.Size(ceiling, height),
                Mode = ResizeMode.Stretch,
                Sampler = KnownResamplers.Lanczos3,
            }));

            image.Metadata.ResolutionUnits = PixelResolutionUnit.PixelsPerInch;
            image.Metadata.HorizontalResolution = _layout.ScreenTargetPpi;
            image.Metadata.VerticalResolution = _layout.ScreenTargetPpi;
            image.Metadata.IccProfile ??= SrgbProfile();

            using var buffer = new MemoryStream();
            image.Save(buffer, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder
            {
                Quality = _layout.PrintAssetJpegQuality,
            });

            return buffer.ToArray();
        }
        catch (Exception)
        {
            return png;
        }
    }

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
    /// The approved pose the intro spread composites, by hash — the second half of that page's
    /// source provenance, read from the same registry the engine pastes from.
    /// </summary>
    private string IntroPoseSha256() =>
        Engine.Value.Registry.Pose(Engine.Value.Config.IntroPoseId).Sha256;

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
    internal readonly record struct PrintRasterTarget(
        int WidthPx, int HeightPx, int Ppi, int JpegQuality);

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
        + $"|{_layout.SpreadWidthMm}x{_layout.SpreadHeightMm}+{_layout.BleedMm}"
        // The entry carries a provenance now, and the threshold that decides whether it says
        // "interpolated" is configuration: two books composed at different thresholds must not share
        // one answer about what was declared.
        + $"|{_layout.MaxPrintUpscale}";

    private static int PixelsFor(float millimetres, int ppi)
        => Math.Max(1, (int)MathF.Round(millimetres / 25.4f * ppi));

    /// <summary>This book's honesty threshold applied to one raster. See <see cref="RasterProvenance"/>.</summary>
    private BekiRasterProvenance Provenance(string role, byte[] source, byte[] delivered)
        => RasterProvenance(role, source, delivered, _layout.MaxPrintUpscale);

    /// <summary>
    /// One interior layer at exactly the working raster the handoff specifies: 5315 × 2480 px,
    /// 300 PPI in the file's own metadata, and an sRGB profile embedded rather than assumed.
    ///
    /// Three things changed here originally. It used to resize by width alone and let the height
    /// fall where it fell, so a layer was "about" the right size; it used to skip anything already
    /// wide enough, so a 6000-pixel render shipped at 6000 pixels; and it never wrote a density or a
    /// colour profile at all, which is how a book reached a printer as an untagged RGB file. Now the
    /// dimensions are exact in both axes, and the ratio is checked before the resize — the caller
    /// has already cropped to the sheet, so anything that still disagrees would be a stretch, and
    /// §6 Step 8 forbids stretching in as many words.
    ///
    /// **And the fourth thing, which was D5b and is now the owner's.** All of the above was still
    /// satisfied by taking a 2528 × 1180 story render and Lanczos-stretching it to 5315 × 2480: the
    /// file then says 300 PPI and carries about 143 PPI of detail, which is precisely what audit
    /// P1-01 found in the rejected press interior. D5b answered that by REFUSING the enlargement,
    /// which — with no super-resolver on the deployment — means the press interior is never built at
    /// all, at any size. Owner ruling 2026-09-01, rule 4, verbatim: <b>"the sizes we have indicated
    /// for printing are correct."</b> So the sheet is delivered at the size the product states, and
    /// the enlargement happens.
    ///
    /// What the audit was right about is kept, and moved to where it belongs — the evidence. An
    /// enlargement past <see cref="BekiPrintLayoutOptions.MaxPrintUpscale"/> is delivered AND
    /// declared: <see cref="RasterProvenance"/> writes the source pixels, the delivered pixels and
    /// <c>interpolated: true</c> into the page's layout receipt, print prep's <c>PRESS_RESOLUTION</c>
    /// gate still fails on it in the preflight report, and the release policy decides what a failed
    /// gate is worth. Nobody is told 300 PPI of detail arrived when it did not. Reduction is
    /// untouched and never declared: making an approved asset smaller loses nothing a press would
    /// have printed.
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
            // Up as well as down, since owner ruling 2026-09-01 rule 4. What used to stand here was
            // a refusal above BekiPrintLayoutOptions.MaxPrintUpscale; the factor is now recorded by
            // RasterProvenance instead of being thrown, so the sheet exists at the stated size and
            // the receipt says exactly how it got there.
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
    /// What happened to one raster between arriving and being placed — measured off the two files
    /// rather than remembered from the code path that produced them.
    ///
    /// Measured, because the number that matters is the one a printer could check: open the source,
    /// open the page's raster, divide. A flag set by whichever branch happened to run would be a
    /// claim about intent, and intent is what the rejected release's 300-PPI metadata was.
    ///
    /// <paramref name="maxSilentUpscale"/> is the line between a rounding difference and a claim
    /// about detail — <see cref="BekiPrintLayoutOptions.MaxPrintUpscale"/>, five per cent by default.
    /// Zero or less declares nothing, which is what the screen-proof fixture asks for. A reduction is
    /// never declared: it loses nothing a press would have printed.
    ///
    /// Unreadable bytes come back as a zero-sized, uninterpolated record rather than throwing. This
    /// is evidence about a page that has already been laid out; a book must not fail because its
    /// receipt could not be written, and a receipt of zeroes is visibly a receipt of zeroes.
    /// </summary>
    internal static BekiRasterProvenance RasterProvenance(
        string role, byte[] source, byte[] delivered, float maxSilentUpscale)
    {
        var (sourceWidth, sourceHeight) = Dimensions(source);
        var (deliveredWidth, deliveredHeight) = Dimensions(delivered);

        if (sourceWidth <= 0 || deliveredWidth <= 0)
        {
            return new BekiRasterProvenance(
                role, sourceWidth, sourceHeight, deliveredWidth, deliveredHeight,
                0d, "unreadable", false);
        }

        var factor = Math.Max(
            (double)deliveredWidth / sourceWidth,
            (double)deliveredHeight / sourceHeight);

        var resized = deliveredWidth != sourceWidth || deliveredHeight != sourceHeight;

        return new BekiRasterProvenance(
            role, sourceWidth, sourceHeight, deliveredWidth, deliveredHeight,
            Math.Round(factor, 4),
            resized ? "lanczos3" : "none",
            maxSilentUpscale > 0f && factor > maxSilentUpscale);
    }

    private static (int Width, int Height) Dimensions(byte[] png)
    {
        try
        {
            var info = SixLabors.ImageSharp.Image.Identify(png);
            return (info.Width, info.Height);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return (0, 0);
        }
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
    ///
    /// **The reading copy takes the same crop and then the trim out of it.** Not a crop of its own:
    /// the parent's page has to show exactly what the printed page shows, so the artwork is fitted
    /// to the bled sheet the way the press file fits it and then the bleed is taken off all four
    /// edges. Cropping straight to 440 : 200 instead would keep a slightly different part of the
    /// picture, and the customer gate asks for the two to match.
    /// </summary>
    private byte[] CropToPage(
        byte[] png, float sheetWidthMm, BekiRenderMode mode, bool enforceCropTolerance = true)
    {
        var bledWidthMm = sheetWidthMm + (_layout.BleedMm * 2f);
        var bledHeightMm = _layout.SpreadHeightMm + (_layout.BleedMm * 2f);
        var targetRatio = bledWidthMm / bledHeightMm;

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

        var x = (width - cropWidth) / 2;
        var y = (height - cropHeight) / 2;

        if (mode == BekiRenderMode.Reading && _layout.BleedMm > 0f)
        {
            var insetX = (int)MathF.Round(cropWidth * _layout.BleedMm / bledWidthMm);
            var insetY = (int)MathF.Round(cropHeight * _layout.BleedMm / bledHeightMm);

            if (cropWidth - (insetX * 2) >= 1 && cropHeight - (insetY * 2) >= 1)
            {
                x += insetX;
                y += insetY;
                cropWidth -= insetX * 2;
                cropHeight -= insetY * 2;
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
                x, y, cropWidth, cropHeight)));

            using var buffer = new MemoryStream();
            image.Save(buffer, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
            outBytes = buffer.ToArray();
        }

        return mode switch
        {
            BekiRenderMode.Press => NormalizeForPrint(outBytes, PrintRaster with
            {
                WidthPx = PixelsFor(bledWidthMm, _layout.PrintTargetPpi),
            }),
            BekiRenderMode.Reading => FitForScreen(outBytes, sheetWidthMm),
            _ => outBytes,
        };
    }

    // ==============================================================================================
    // Page geometry
    // ==============================================================================================

    /// <summary>How far the artwork runs past the trim on this document. Zero on the download.</summary>
    private float Bleed(BekiRenderMode mode)
        => mode == BekiRenderMode.Reading ? 0f : _layout.BleedMm;

    /// <summary>
    /// One geometry, two widths and three modes: the spread, the single leaf that is half of it,
    /// and whether the page carries the printer's bleed or is the finished trim exactly.
    ///
    /// The default text style is set here as well, and it is not decoration. QuestPDF's own default
    /// family is Lato; a run that named no family, or a glyph no named family carried, fell through
    /// to it and embedded a Latin face in a Georgian book — which is exactly what the supplier found
    /// in the shipped PDF. Naming the body face as the page default means there is nothing left for
    /// a fall-through to reach.
    /// </summary>
    private void ApplyGeometry(
        PageDescriptor page, float widthMm, BekiRenderMode mode, Color? pageColor = null)
    {
        var bleed = Bleed(mode);

        page.Size(new PageSize(
            widthMm + (bleed * 2f),
            _layout.SpreadHeightMm + (bleed * 2f),
            Unit.Millimetre));

        page.Margin(0);
        page.PageColor(pageColor ?? PageInk);
        page.DefaultTextStyle(style => style.FontFamily(PdfFontBootstrap.BodyFamily));
    }

    /// <summary>Points from millimetres. Every layout number in this file is written in mm.</summary>
    private static float MmToPt(float mm) => mm * PointsPerMm;

    /// <summary>
    /// How far a spread's text column holds back on its inner edge — see <see cref="ComposeSpread"/>
    /// for why the fold side is not simply the safe margin.
    /// </summary>
    private float InnerPaddingMm => MathF.Max(_layout.SafeMarginMm, _layout.GutterZoneMm / 2f);

    /// <summary>
    /// The width, in points, of the column a spread's story text is set in: the reserved share of
    /// the sheet less its two paddings, and never wider than the configured maximum.
    ///
    /// Written on the TRIM and not on the sheet, which is the whole of what makes the press file and
    /// the download the same layout: the bleed is a frame the press page carries and the download
    /// does not, and a column measured from the sheet's edge would be five millimetres narrower on
    /// one of them and break its lines somewhere else.
    ///
    /// The maximum is the approved reference's 170 mm (§6 Step 8). On a 440 mm spread a third of the
    /// sheet is narrower than that, so today the cap does not bind — it is written down because the
    /// column share is configuration, and a book whose column was widened must not get a 200 mm
    /// measure that no reading age can track across.
    /// </summary>
    private float StoryColumnWidthPt => MathF.Min(
        (MmToPt(_layout.SpreadWidthMm) * _layout.TextColumnShare)
            - MmToPt(_layout.SafeMarginMm)
            - MmToPt(InnerPaddingMm),
        MmToPt(_layout.MaxTextWidthMm));

    /// <summary>The measure the copy itself is set to: the column, less the column's own inset.</summary>
    private float StoryCopyWidthPt => StoryColumnWidthPt - (MmToPt(_layout.WashPaddingMm) * 2f);

    /// <summary>The intro copy column's width: the left leaf's text area, capped at the same measure.</summary>
    private float IntroColumnWidthMm => MathF.Min(
        (_layout.SpreadWidthMm / 2f) - _layout.SafeMarginMm - InnerPaddingMm,
        _layout.MaxTextWidthMm);

    /// <summary>
    /// The exact width, in points, of the legacy cover title's band: the leaf between its safe
    /// margins, on the trim.
    /// </summary>
    private float CoverTitleWidthPt =>
        MmToPt(_layout.PageWidthMm) - (MmToPt(_layout.SafeMarginMm) * 2f);

    // ==============================================================================================
    // Type
    // ==============================================================================================

    /// <summary>
    /// Light type with its own dark edge, drawn entirely as vector text.
    ///
    /// Every word the book prints over artwork comes through here: the cover title, the back-cover
    /// address, the intro spread's lines, and each story spread's copy in both languages. The story
    /// and intro copy left for one campaign, when audit P1-04 put a cream wash under them and dark
    /// ink on cream needs no rim; owner ruling 2026-09-01 — the third and final on the question —
    /// took the wash away again, and they came back.
    ///
    /// This used to be a raster: the offset stack rendered to a PNG at 300 DPI with one
    /// invisible text run over it, so that <c>pdftotext</c> said each line once. The supplier's
    /// preflight rejected exactly that trade — "a raster title-effect image is placed underneath"
    /// the vector text, and a printed glyph should be the RIP's own edge, not a picture of one.
    /// So the visible glyphs are the vector stack again: QuestPDF has no stroke, and the rim is
    /// the text drawn <see cref="BekiPrintLayoutOptions.TextOutlineSteps"/> more times on a small
    /// circle beneath the fill. The cost, accepted knowingly, is that a text extractor reads an
    /// outlined line once per copy.
    ///
    /// **How strong that rim is, is now the type's own business.** Owner ruling 2026-09-01, rule 3:
    /// "text must have a STRONGER border so it is readable on all backgrounds." The radius is
    /// <see cref="RimRadiusPt"/> — a proportion of this block's size, floored at the old fixed
    /// width — so the cover title's rim grows with the cover title and the English secondary line's
    /// with the English secondary line. It is a rim and only ever a rim: no background box, no wash,
    /// no panel, which is the ruling before it and is not in question here.
    ///
    /// <paramref name="blockWidthPt"/> is kept for the callers' layout arithmetic even though no
    /// raster needs sizing any more; a box too narrow to set type in still falls back to plain.
    /// </summary>
    /// <param name="proof">
    /// A proof style whose rim replaces the layout's, or null for the shipped rim — which is what
    /// every caller in the book passes.
    /// </param>
    private void OutlinedText(
        IContainer container,
        string text,
        float fontSize,
        float lineHeight,
        Color fill,
        Color outline,
        float blockWidthPt,
        string fontFamily = PdfFontBootstrap.BodyFamily,
        bool centred = false,
        BekiTextStyleProof? proof = null)
    {
        // Two ways to have no rim, and they are not the same switch. The book's is
        // TextOutlineWidth = 0 — the floor taken away, which is how a caller asks for plain type. A
        // proof's is a factor of zero, which is the contrast sample the owner asked for: dark ink
        // straight on the artwork with nothing under it.
        var rimOff = proof is null ? _layout.TextOutlineWidth <= 0f : proof.RimRadiusPt <= 0f;

        // No outline asked for, nothing to outline, or a box too narrow: plain text, which is a
        // single run and needs no rim.
        if (rimOff || string.IsNullOrWhiteSpace(text) || blockWidthPt <= 1f)
        {
            PlainText(
                container, text, fontSize, lineHeight, fill, fontFamily, centred, proof?.WeightValue);
            return;
        }

        DrawOutlineStack(
            container, text, fontSize, lineHeight, fill, outline, fontFamily, centred, proof);
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
    /// <param name="weight">
    /// A cut of the family other than the one the face registers as, or null — which is what the
    /// book itself always passes, and which leaves the run exactly as it was before proofing
    /// existed.
    /// </param>
    private static void PlainText(
        IContainer container, string text, float fontSize, float lineHeight,
        Color colour, string fontFamily, bool centred, FontWeight? weight = null)
    {
        // The cut goes on as a default rather than on the run: QuestPDF exposes family, size,
        // leading and colour on a text block and the weight only on a style, and a default the
        // block does not override is the same thing said in the place the API keeps it.
        var target = weight is { } cut
            ? container.DefaultTextStyle(style => style.Weight(cut))
            : container;

        var block = target.Text(text)
            .FontFamily(fontFamily, PdfFontBootstrap.BodyFamily)
            .FontSize(fontSize)
            .LineHeight(lineHeight)
            .FontColor(colour);

        if (centred) block.AlignCenter();
    }

    /// <summary>
    /// The faux outline itself: <see cref="BekiPrintLayoutOptions.TextOutlineSteps"/> offset copies
    /// evenly around a circle of <see cref="RimRadiusPt"/>, then the fill on top — all real vector
    /// text runs, which since the supplier's preflight ruling is the shipped form rather than the
    /// source of a raster.
    ///
    /// The step count is the rim's thickness talking. Between two neighbouring directions the rim's
    /// outer edge falls inside a true circle by <c>r·(1 − cos(π/steps))</c>, so a radius that grows
    /// without the count growing with it produces a rim that is round on the axes and flat at 45° —
    /// which on a round Georgian letter looks like a printing fault. See the option for the numbers.
    /// </summary>
    private void DrawOutlineStack(
        IContainer container, string text, float fontSize, float lineHeight,
        Color fill, Color outline, string fontFamily, bool centred,
        BekiTextStyleProof? proof = null)
    {
        var radius = proof?.RimRadiusPt ?? RimRadiusPt(fontSize);

        // Four is the fewest directions that still surround a glyph; beyond sixty-four the copies
        // are closer together than any press can resolve and only the file grows.
        var steps = Math.Clamp(proof?.RimSteps ?? _layout.TextOutlineSteps, 4, 64);

        // The rim is the same letter as the fill, so it is the same cut of the family too — a rim
        // drawn in Regular under a Bold fill would show as a shadow the letter has outgrown.
        var weight = proof?.WeightValue;

        container.Layers(layers =>
        {
            for (var step = 0; step < steps; step++)
            {
                var angle = MathF.Tau / steps * step;
                layers.Layer()
                    .TranslateX(radius * MathF.Cos(angle))
                    .TranslateY(radius * MathF.Sin(angle))
                    .Element(item => PlainText(
                        item, text, fontSize, lineHeight, outline, fontFamily, centred, weight));
            }

            layers.PrimaryLayer().Element(item => PlainText(
                item, text, fontSize, lineHeight, fill, fontFamily, centred, weight));
        });
    }

    /// <summary>
    /// How far the rim reaches out from a glyph set at <paramref name="fontSize"/>, in points.
    ///
    /// Owner ruling 2026-09-01, rule 3, verbatim: "text must have a STRONGER border so it is
    /// readable on all backgrounds." Proportional to the type rather than fixed, because a rim is
    /// only strong relative to the letter it is drawn around — the same 0.6 pt that was a visible
    /// edge on 18 pt story copy was a hairline on the 36 pt cover title, and the cover is where type
    /// lands on the busiest artwork in the book. <see cref="BekiPrintLayoutOptions.TextOutlineWidth"/>
    /// is the floor under the proportion, so nothing ends up with less rim than the book has always
    /// had.
    /// </summary>
    internal float RimRadiusPt(float fontSize) => MathF.Max(
        _layout.TextOutlineWidth,
        MathF.Max(0f, fontSize) * MathF.Max(0f, _layout.TextOutlineWidthFactor));

    // ==============================================================================================
    // Receipts
    // ==============================================================================================

    /// <summary>
    /// The page receipts as they accumulate, in page order, with the page numbering that a
    /// fourteen-page book and a twelve-page interior each need.
    ///
    /// Keyed by role and upserted rather than appended, which is not fussiness: QuestPDF is free to
    /// walk a document's composition more than once, and a receipts list that grew on the second
    /// walk would report a twenty-eight page book. A role — "intro", "spread-04", "credits" — occurs
    /// exactly once in a Beki book, so the second visit rewrites the first's entry in place and the
    /// page numbering stays the document's own.
    /// </summary>
    private sealed class ReceiptBook(BekiRenderMode mode)
    {
        private readonly List<BekiLayoutPageReceipt> _pages = [];

        public void Add(string role, Func<int, BekiLayoutPageReceipt> build)
        {
            var existing = _pages.FindIndex(page => page.Role == role);

            if (existing >= 0)
            {
                _pages[existing] = build(existing + 1);
                return;
            }

            _pages.Add(build(_pages.Count + 1));
        }

        public BekiLayoutReceipts Build() => new(
            mode switch
            {
                BekiRenderMode.Reading => "reading",
                BekiRenderMode.Proof => "proof",
                _ => "press",
            },
            _pages);
    }

    /// <summary>The hash of exactly the bytes a page placed, which is what a receipt is about.</summary>
    private static string Sha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
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
