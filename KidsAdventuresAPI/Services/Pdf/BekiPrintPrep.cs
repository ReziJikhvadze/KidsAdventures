using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AdventurePacks.Api.Services.Pdf;

/// <summary>
/// Which pages the caller authored light text on, and where to look for it on the flat-ground ones.
///
/// Audit P0-07: credits text was authored cream on dark purple and came out of the CMYK conversion
/// as <c>0 g</c> black — nearly invisible, on a page whose whole job is to be read. Correction-plan
/// amendment A10a splits the check in two, because the two kinds of page cannot be checked the same
/// way. Text over artwork (the cover title) is checked by inspecting the converted content stream:
/// no authored-light text object may have acquired a device-black fill. Text on a flat ground (the
/// credits page) is additionally checked by rendering it and looking: a bright glyph mode over a
/// dark ground mode, measured, because a content stream can be innocent and the plate still wrong.
///
/// Nullable throughout: a caller that has not yet worked out its rects still gets the content-stream
/// assertion for every page it flags, and a caller that flags nothing still gets the colour evidence
/// recorded in the preflight.
/// </summary>
/// <param name="LightTextPages">1-based page numbers whose text was authored light.</param>
/// <param name="FlatGroundRects">Where to sample, on the pages whose ground is a flat colour.</param>
public sealed record BekiPrintProbe(
    IReadOnlyList<int> LightTextPages,
    IReadOnlyList<BekiTextProbeRect>? FlatGroundRects = null,
    IReadOnlyDictionary<int, int>? MaximumVisibleTextDrawsByPage = null);

/// <summary>
/// One rectangle to sample, in millimetres from the page's top-left corner — the same corner a
/// layout is written from, so the number in a layout receipt can be handed straight to this.
/// </summary>
public sealed record BekiTextProbeRect(
    int Page, double XMm, double YMm, double WidthMm, double HeightMm, string Role = "text");

/// <summary>
/// Where the pixels in a press raster actually came from.
///
/// P1-01's finding is that pixel count is not evidence: 2528×1180 story art Lanczos-stretched to
/// 5315×2480 measures 300 PPI and carries 143 PPI of detail. So the preflight is told, per raster,
/// what the source was and what enlarged it — and amendment A1 makes an interpolation-only
/// enlargement a <c>PRESS_RESOLUTION</c> failure rather than a note.
/// </summary>
public sealed record BekiResolutionReceipt(IReadOnlyList<BekiResolutionSource> Sources);

/// <summary>One raster's provenance. <paramref name="Tool"/> "none" means it was never enlarged.</summary>
public sealed record BekiResolutionSource(
    string Role,
    int SourceWidthPx,
    int SourceHeightPx,
    int DeliveredWidthPx,
    int DeliveredHeightPx,
    string Tool,
    double Factor,
    bool InterpolationOnly)
{
    /// <summary>
    /// Resamplers that move pixels around without adding detail. Named here rather than trusted
    /// from the caller's flag, because the failure this catches is precisely a caller who believes
    /// a Lanczos stretch counts as resolution — the shipped book's own belief.
    /// </summary>
    private static readonly string[] Interpolators =
        ["none", "", "resize", "resample", "lanczos", "lanczos3", "bicubic", "bilinear",
         "nearest", "catmullrom", "mitchell", "box", "spline", "welch", "hermite"];

    /// <summary>
    /// Whether this raster was enlarged by interpolation alone. True either because the caller said
    /// so or because the tool it named is a resampler and the factor is greater than one.
    /// </summary>
    public bool IsInterpolationOnly =>
        InterpolationOnly
        || (Factor > 1.0001d
            && Interpolators.Contains(Tool.Trim().ToLowerInvariant(), StringComparer.Ordinal));
}

/// <summary>
/// The print-preparation stage — the one the supplier's audit found did not exist.
///
/// The shipped file was "an ordinary PDF 1.7 created directly by PDFsharp": no PDF/X
/// identification, no output intent, no colour conversion, no preflight, and nothing anywhere
/// recording that those steps had been skipped. This stage makes the skip impossible rather than
/// the steps optional: a print artifact either comes out of here — every raster converted to
/// CMYK through the locked FOGRA39 profile by Ghostscript, the PDF/X-4 claim written, the intent
/// embedded, the preflight hard-failing on what the Locked Print Specification §5 says it must —
/// or it does not exist, with <c>PRINT_PREFLIGHT_FAILED</c> naming exactly what refused.
///
/// The colour conversion is Ghostscript's <c>pdfwrite</c> rather than hand-rolled image surgery,
/// deliberately: a press file's colour transform is the exact job a maintained conversion
/// pipeline exists for, and spec §5 requires the Ghostscript binary on the deployment as a render
/// validator anyway.
///
/// Since the deliverables audit (2026-08-31, verdict REJECTED) the stage also measures two things
/// it used to assert: what resolution each raster is actually placed at (P0-04/P1-01 — the cover
/// shipped at ~125 PPI through a preflight that read <c>/ColorSpace</c> and never <c>/Width</c>),
/// and what colour the text came out of the conversion (P0-07 — a global black-text option turned
/// the cream credits page to near-invisible <c>0 g</c>). Both are gates, not notes, and both answer
/// with the numbers they measured so that the report can be argued with.
///
/// The two differ in one thing since owner ruling 2026-09-01, rule 4 — "the sizes we have indicated
/// for printing are correct". <c>TEXT_COLOR_INTEGRITY</c> still refuses: an unreadable page is not a
/// press file. <c>PRESS_RESOLUTION</c> now reports its verdict instead of throwing it, because
/// refusing to build the press interior does not give anybody a 300-PPI book — it gives them no book
/// — and the number it measured is worth having either way. The file is produced at the stated size,
/// the gate says FAIL in the report and in <c>failed_gates</c>, and the release policy decides. The
/// measurement itself is untouched to the pixel.
/// </summary>
public static class BekiPrintPrep
{
    /// <summary>PDF/X-4's version string, as XMP and the preflight report both name it.</summary>
    public const string PdfxVersion = "PDF/X-4";

    /// <summary>
    /// The acceptance gates this stage answers for, named as <c>BEKI_Acceptance_Gates_v1.json</c>
    /// names them. The exception type is still <c>PRINT_PREFLIGHT_FAILED</c> — that word belongs to
    /// the supplier's own failure vocabulary and is not ours to extend — so the gate id travels in
    /// the message, where the admin view and the log both show it.
    /// </summary>
    public const string PressResolutionGate = "PRESS_RESOLUTION";

    /// <inheritdoc cref="PressResolutionGate"/>
    public const string TextColorIntegrityGate = "TEXT_COLOR_INTEGRITY";

    /// <summary>Rejects the superseded multi-copy faux-outline text treatment.</summary>
    public const string SingleTextLayerGate = "SINGLE_TEXT_LAYER";

    /// <summary>The canonical cover permits its artwork raster and no rasterized logo.</summary>
    public const string VectorLogoGate = "VECTOR_LOGO";

    /// <summary>The supplied gates document, read at runtime rather than transcribed into C#.</summary>
    private const string AcceptanceGatesFile = "BEKI_Acceptance_Gates_v1.json";

    /// <summary>
    /// Applies print preparation to one laid-out artifact and proves what it did.
    /// </summary>
    /// <param name="laidOutPdf">The composed document, straight from layout.</param>
    /// <param name="title">The canonical book title, for the info dictionary and XMP.</param>
    /// <param name="options">The locked print configuration.</param>
    /// <param name="trimInsetMm">
    /// How far the TrimBox sits inside the MediaBox on every edge after conversion — 5 for the
    /// interior's bleed, 0 for the cover, whose locked spec sets every box equal. Re-applied here
    /// because the conversion pass rewrites the document and page boxes must be stated on the
    /// file that ships, not on an ancestor of it.
    /// </param>
    /// <param name="baseDirectory">
    /// Test override for resolving the profile path and the supplied acceptance-gates document.
    /// </param>
    /// <param name="probe">
    /// Which pages carry authored-light text and where to sample the flat-ground ones (amendment
    /// A10a). Null runs the colour evidence without the assertions, which is what a caller that has
    /// not yet produced layout receipts can honestly ask for.
    /// </param>
    /// <param name="resolutionReceipt">
    /// Where each press raster's pixels came from (amendment A1). Null means no enlargement is
    /// claimed; a receipt that admits interpolation-only enlargement fails the resolution gate in
    /// the report and in the returned gate list.
    ///
    /// The composer knows things the upscaler in front of it does not — it is the stage that
    /// enlarges a short raster onto the stated sheet — so a caller building this from the upscaler
    /// alone hands the gate a receipt with that enlargement missing. Combine it with
    /// <see cref="BekiLayoutReceipts.RasterSources"/> from the composed book.
    /// </param>
    /// <returns>
    /// The prepared PDF, the preflight report as JSON ready to store beside it, and the acceptance
    /// gates that FAILED without withholding the file.
    ///
    /// That third value is the whole of what owner ruling 2026-09-01 rule 4 changed here. Exactly one
    /// gate travels in it today — <c>PRESS_RESOLUTION</c> — because a press file that refuses to
    /// exist is not a printing size being correct, and the audit's measurement is worth keeping
    /// whether or not it is worth stopping a release for. Every other check in this stage still
    /// refuses outright and throws. A caller that ignores this list is publishing a file whose
    /// resolution gate may have failed; the release policy is where that is weighed.
    /// </returns>
    /// <exception cref="BekiLayoutException">
    /// <c>PRINT_PREFLIGHT_FAILED</c> — a required input is missing or a hard check failed. The
    /// message names the cause, because the log is where somebody finds out what is still owed.
    /// </exception>
    public static (byte[] Pdf, string ReportJson, IReadOnlyList<string> FailedGates) PrepareWithGates(
        byte[] laidOutPdf,
        string title,
        BekiPrintPrepOptions options,
        float trimInsetMm = 5f,
        string? baseDirectory = null,
        BekiPrintProbe? probe = null,
        BekiResolutionReceipt? resolutionReceipt = null,
        bool canonicalMixedGeometry = false,
        bool requirePressResolution = false)
    {
        ArgumentNullException.ThrowIfNull(laidOutPdf);
        ArgumentNullException.ThrowIfNull(options);

        var root = baseDirectory ?? AppContext.BaseDirectory;
        var (iccPath, iccBytes) = ReadOutputIntentProfile(options, root);
        var requiredPpi = ReadRequiredPressRasterPpi(root);

        // The input is proven before anything expensive touches it, and its page count becomes
        // the contract the conversion must honour. Ghostscript recovers from a broken input by
        // emitting a valid BLANK document and exiting clean — a torn-off header comes back as
        // one empty page — and a blank page then passes every per-page check by having nothing
        // on it to fail. The count is the only witness the layout leaves behind.
        int expectedPages;
        try
        {
            // Import rather than InformationOnly: PDFsharp 6.2 marks that mode obsolete as never
            // implemented, and even as documented it only ever promised the Info dictionary. A page
            // count needs the pages.
            using var laidOut = PdfReader.Open(
                new MemoryStream(laidOutPdf), PdfDocumentOpenMode.Import);
            expectedPages = laidOut.PageCount;
        }
        catch (Exception ex) when (ex is not BekiLayoutException)
        {
            throw Failure($"the laid-out document is not a readable PDF ({ex.GetType().Name}).");
        }

        if (expectedPages == 0)
        {
            throw Failure("the laid-out document has no pages.");
        }

        if (canonicalMixedGeometry && expectedPages != 12)
        {
            throw Failure(
                $"the canonical BEKI document must contain exactly 12 PDF pages; layout returned {expectedPages}.");
        }

        // Ghostscript is allowed to rewrite or merge text-show operators during colour conversion,
        // so inspect the authored layout as well as the prepared bytes. The defect is the composer
        // painting the same glyphs repeatedly; a converter hiding that signature does not make the
        // source layout compliant.
        object authoredTextLayers;
        using (var authored = PdfReader.Open(
                   new MemoryStream(laidOutPdf), PdfDocumentOpenMode.Import))
        {
            authoredTextLayers = EnforceSingleTextLayer(
                authored.Pages.OfType<PdfPage>()
                    .Select((page, index) => BekiContentWalker.Walk(page, index + 1))
                    .ToList(), probe);
        }

        string? conversion = null;
        var pdf = laidOutPdf;

        if (options.RequireAllCmyk)
        {
            (pdf, conversion) = ConvertToCmyk(pdf, iccPath, options);
        }

        using var stream = new MemoryStream(pdf);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);

        if (document.Pages.Count != expectedPages)
        {
            throw Failure(
                $"the conversion returned {document.Pages.Count} page(s) where the layout has "
                + $"{expectedPages} — content was dropped rather than converted.");
        }

        // PDF/X requires a document title and an explicit trapped state; both live in the info
        // dictionary and again in the XMP packet, and the two must agree.
        document.Info.Title = title;
        document.Info.Elements["/Trapped"] = new PdfName("/False");

        ApplyBoxes(document, trimInsetMm, canonicalMixedGeometry);
        if (canonicalMixedGeometry)
        {
            ValidateCanonicalGeometry(document);
        }
        WriteOutputIntent(document, iccBytes, options);
        WriteXmpMetadata(document, title);

        var fonts = InspectFonts(document);
        var images = InspectImages(document);
        var boxes = InspectBoxes(document);

        var unembedded = fonts.Where(font => !font.Embedded).Select(font => font.Name).ToList();
        if (unembedded.Count > 0)
        {
            throw Failure(
                $"font(s) not embedded: {string.Join(", ", unembedded)}. A press cannot be asked "
                + "to guess at a face.");
        }

        var boxProblems = boxes.Where(box => box.Problem is not null).ToList();
        if (boxProblems.Count > 0)
        {
            throw Failure(
                "page box(es) wrong: "
                + string.Join(" ", boxProblems.Select(box => $"page {box.Page}: {box.Problem}.")));
        }

        // Spec §4, stated as a hard failure and not a note: "fail print preflight if any raster
        // image object remains RGB" — and the RGB object in the old benchmark is named there as a
        // defect, not a precedent. Grey soft masks are transparency, not colour content, and CMYK
        // has no alpha; they stay grey by design.
        if (options.RequireAllCmyk)
        {
            var rgb = images.Where(image => image.IsRgb && !image.IsMask).ToList();
            if (rgb.Count > 0)
            {
                throw Failure(
                    $"{rgb.Count} raster image object(s) remain RGB after conversion "
                    + $"({string.Join(", ", rgb.Select(image => image.ColourSpace).Distinct())}). "
                    + "The locked spec requires every raster CMYK.");
            }
        }

        // Everything below is measured on the converted document, because every defect these
        // checks exist for was introduced BY the conversion or survived it unnoticed: the placed
        // resolution the old preflight never computed (P0-04/A1), and the text colour the
        // conversion itself destroyed (P0-07/A10a).
        var contents = new List<BekiContentWalker.PageContent>();
        for (var index = 0; index < document.Pages.Count; index++)
        {
            contents.Add(BekiContentWalker.Walk(document.Pages[index], index + 1));
        }

        var (resolution, resolutionProblems) =
            MeasurePressResolution(contents, requiredPpi, resolutionReceipt);

        if (requirePressResolution && resolutionProblems.Count > 0)
        {
            throw Failure(
                $"{PressResolutionGate}: " + string.Join(" ", resolutionProblems));
        }
        var textColour = EnforceTextColourIntegrity(contents, probe);
        var textLayers = EnforceSingleTextLayer(contents, probe);
        var vectorLogo = canonicalMixedGeometry ? EnforceVectorCoverLogo(contents) : null;

        // The one gate this stage measures and does not act on. Everything else in here still
        // refuses outright: a missing ICC profile, an unembedded face, a wrong page box, a dropped
        // page, an RGB raster, cream text converted to device black. See MeasurePressResolution.
        var failedGates = resolutionProblems.Count > 0 ? new[] { PressResolutionGate } : [];

        // Page heights are read before the save, while the document is unambiguously ours: the
        // probe rectangles arrive measured from the top-left corner, and turning that into a
        // renderer's coordinates needs the page's own height.
        var pageHeightsMm = document.Pages
            .OfType<PdfPage>()
            .Select(page => page.MediaBox.Height / 72d * 25.4d)
            .ToList();

        using var output = new MemoryStream();
        document.Save(output);
        var prepared = output.ToArray();

        // The rendered-pixel half of A10a runs on the bytes that ship, not on an ancestor of them:
        // the whole point is to look at what a press would look at.
        var pixelProbes = RunTextPixelProbes(prepared, pageHeightsMm, options, probe);

        var report = JsonSerializer.Serialize(
            new
            {
                stage = canonicalMixedGeometry ? "beki-canonical-print-prep-v3" : "beki-print-prep-v2",
                spec = "BEKI_Print_Production_Locked_Spec_v1",
                prepared_at_utc = DateTime.UtcNow,
                // The gates that failed and did not stop the file being written. Empty on a clean
                // artifact. This exists because owner ruling 2026-09-01 rule 4 moved the decision on
                // PRESS_RESOLUTION out of this stage: the file is produced at the stated size, and
                // whoever publishes it needs to be able to read, in one place, what is wrong with it.
                failed_gates = failedGates,
                pdfx = new
                {
                    version = PdfxVersion,
                    output_condition_identifier = options.OutputConditionIdentifier,
                    output_condition_info = options.OutputConditionInfo,
                    registry_name = options.RegistryName,
                    icc_profile_bytes = iccBytes.Length,
                    icc_profile_sha256 = Convert.ToHexString(SHA256.HashData(iccBytes)).ToLowerInvariant(),
                },
                colour = new
                {
                    require_all_cmyk = options.RequireAllCmyk,
                    conversion = conversion ?? "not required by configuration",
                    image_colour_spaces = images
                        .GroupBy(image => image.ColourSpace + (image.IsMask ? " (soft mask)" : string.Empty))
                        .ToDictionary(group => group.Key, group => group.Count()),
                },
                // P0-04 and P1-01, answered with numbers instead of metadata. Every image the
                // content stream actually paints, with the pixels it carries, the millimetres it
                // covers and the resolution that arithmetic implies.
                resolution,
                // P0-07, answered the same way: what colour every text-showing operator was
                // painted with once Ghostscript had finished with the document.
                text_colour = textColour,
                text_layers = new { authored = authoredTextLayers, prepared = textLayers },
                vector_logo = vectorLogo,
                text_pixel_probes = pixelProbes,
                fonts = fonts
                    .Select(font => new { name = font.Name, embedded = font.Embedded })
                    .ToList(),
                pages = boxes
                    .Select(box => new
                    {
                        page = box.Page,
                        media_box = box.MediaBox,
                        trim_box = box.TrimBox,
                        bleed_box = box.BleedBox,
                    })
                    .ToList(),
                renderers = new
                {
                    // Render checks run on the stored artifact where the tools live — the report
                    // never claims a check this stage did not run. The conversion pass above IS a
                    // full Ghostscript interpretation of the document, which is recorded.
                    ghostscript = conversion is null
                        ? "not run; convert-and-validate with gs on the stored artifact"
                        : "interpreted the full document during colour conversion",
                    poppler = "not run in this stage; BekiRenderValidation runs pdftoppm and "
                              + "pdffonts on the stored artifact",
                    qr_scan = "not run in this stage; BekiRenderValidation decodes story spread "
                              + "8 on PDF page 11",
                },
            },
            new JsonSerializerOptions { WriteIndented = true });

        return (prepared, report, failedGates);
    }

    /// <summary>
    /// <inheritdoc cref="PrepareWithGates"/>
    ///
    /// The two-value form, for callers that read the preflight JSON rather than the gate list.
    ///
    /// No delivery path uses it any more, and that is the shape of review finding 1 rather than an
    /// accident: a caller that drops the gate list has to read <c>failed_gates</c> out of the report
    /// to learn the same thing, and the previous pipeline's press branch did neither — it published
    /// whatever came back. Both delivery paths call <see cref="PrepareWithGates"/> now. What is left
    /// here is the tests, which assert on the report, and any future caller that genuinely only
    /// wants the document; the gate list it forgoes is still in the report under <c>failed_gates</c>.
    /// </summary>
    public static (byte[] Pdf, string ReportJson) Prepare(
        byte[] laidOutPdf,
        string title,
        BekiPrintPrepOptions options,
        float trimInsetMm = 5f,
        string? baseDirectory = null,
        BekiPrintProbe? probe = null,
        BekiResolutionReceipt? resolutionReceipt = null)
    {
        var (pdf, report, _) = PrepareWithGates(
            laidOutPdf, title, options, trimInsetMm, baseDirectory, probe, resolutionReceipt);

        return (pdf, report);
    }

    /// <summary>
    /// The locked profile's bytes, refused loudly when missing, malformed, or not the exact
    /// bytes the spec pinned.
    /// </summary>
    private static (string Path, byte[] Bytes) ReadOutputIntentProfile(
        BekiPrintPrepOptions options, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(options.OutputIntentIccPath))
        {
            throw Failure(
                "no output intent ICC profile is configured (Beki:PrintPrep:OutputIntentIccPath); "
                + "the locked spec ships one and this deployment has unset it.");
        }

        var path = Path.IsPathRooted(options.OutputIntentIccPath)
            ? options.OutputIntentIccPath
            : Path.Combine(baseDirectory, options.OutputIntentIccPath);

        if (!File.Exists(path))
        {
            throw Failure($"the output intent ICC profile is missing at '{path}'.");
        }

        var bytes = File.ReadAllBytes(path);

        // 'acsp' at offset 36 is the ICC signature. A wrong file here would ship a print PDF
        // whose colour meaning is undefined, which no press would tell us about until paper.
        if (bytes.Length < 128
            || bytes[36] != (byte)'a' || bytes[37] != (byte)'c'
            || bytes[38] != (byte)'s' || bytes[39] != (byte)'p')
        {
            throw Failure($"the file at '{path}' is not an ICC profile (no 'acsp' signature).");
        }

        if (!string.IsNullOrWhiteSpace(options.OutputIntentIccSha256))
        {
            var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!string.Equals(actual, options.OutputIntentIccSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw Failure(
                    $"the ICC profile at '{path}' hashes to {actual}, not the locked "
                    + $"{options.OutputIntentIccSha256}. A press file must not be built on a "
                    + "profile nobody approved.");
            }
        }

        return (path, bytes);
    }

    /// <summary>
    /// The press resolution the supplier's acceptance gates lock, read from the shipped document
    /// rather than restated in C#.
    ///
    /// <c>BEKI_Acceptance_Gates_v1.json</c> carries <c>required_press_raster_ppi</c> among its
    /// locked values, and the number belongs to the people printing the book. A copy in code is a
    /// second source of truth that nobody updates in the same commit, and the symptom of the drift
    /// is a file that passes here and is rejected on paper. A missing document is a deployment
    /// fault and is refused by name, exactly as a missing ICC profile is.
    /// </summary>
    private static int ReadRequiredPressRasterPpi(string baseDirectory)
    {
        var path = Path.Combine(
            baseDirectory, "Assets", "BekiComposite", "contracts", AcceptanceGatesFile);

        if (!File.Exists(path))
        {
            throw Failure(
                $"the acceptance gates document is missing at '{path}'. The press resolution gate "
                + "reads its threshold from the supplier's own file and will not guess one.");
        }

        try
        {
            using var gates = JsonDocument.Parse(File.ReadAllText(path));
            var required = gates.RootElement
                .GetProperty("locked_values")
                .GetProperty("required_press_raster_ppi")
                .GetInt32();

            if (required <= 0)
            {
                throw Failure(
                    $"'{AcceptanceGatesFile}' states required_press_raster_ppi as {required}, "
                    + "which is not a resolution anything can be measured against.");
            }

            return required;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException
                                       or InvalidOperationException and not BekiLayoutException)
        {
            throw Failure(
                $"'{AcceptanceGatesFile}' does not state locked_values.required_press_raster_ppi "
                + $"({ex.GetType().Name}).");
        }
    }

    /// <summary>
    /// <c>PRESS_RESOLUTION</c>: "Every press raster has at least 300 effective source PPI at
    /// placement size; interpolation-only upscaling is a failure."
    ///
    /// Three ways to fail, and the last is the one the audit had to invent. The first is a placement
    /// that could not be measured — amendment A1 is explicit that unknown is not a pass, because the
    /// shipped cover passed a preflight that simply never asked. The second is arithmetic: pixels
    /// over placed inches, per axis, for every image the content stream actually paints, which is why
    /// there is a content-stream walker at all (the credits Beki mark is a 32 mm image on a 440 mm
    /// page, and page-size arithmetic would report it at forty times its density). The third is
    /// provenance: a raster can measure 300 PPI and carry 143, because something stretched it, and
    /// only the receipt knows.
    ///
    /// **It measures and it reports; it no longer decides.** Every one of those three used to throw,
    /// which meant a book whose art was thin produced no press file at all — and with no
    /// super-resolver on the deployment, that is every book. Owner ruling 2026-09-01, rule 4: "the
    /// sizes we have indicated for printing are correct", and a press build that refuses to exist is
    /// not a size being correct. So the measurement is unchanged to the pixel, the message is
    /// unchanged word for word, and the verdict lands in the report and in
    /// <c>failed_gates</c> instead of in an exception. Whether a failed <c>PRESS_RESOLUTION</c>
    /// withholds a release is the release policy's call, made where the other gates are weighed.
    ///
    /// Nothing here is softened. A gate that reads FAIL in the preflight is a gate that failed, and
    /// the supplier handback carries the same numbers it always did.
    /// </summary>
    /// <returns>The report block, and the problems found — empty when the gate passes.</returns>
    private static (object Report, IReadOnlyList<string> Problems) MeasurePressResolution(
        IReadOnlyList<BekiContentWalker.PageContent> contents,
        int requiredPpi,
        BekiResolutionReceipt? receipt)
    {
        var images = contents.SelectMany(page => page.Images).ToList();
        var unresolved = contents.SelectMany(page => page.Unresolved).ToList();
        var problems = new List<string>();

        if (unresolved.Count > 0)
        {
            problems.Add(
                $"{unresolved.Count} paint operation(s) could not be "
                + "measured, so their resolution is unknown — and unknown is not a pass. "
                + string.Join(" ", unresolved
                    .Take(6)
                    .Select(item => $"page {item.Page} '{item.Name}': {item.Reason}.")));
        }

        var thin = images.Where(image => image.EffectivePpi < requiredPpi - 0.5d).ToList();
        if (thin.Count > 0)
        {
            problems.Add(
                $"{thin.Count} of {images.Count} placed raster(s) fall "
                + $"below {requiredPpi} effective PPI. "
                + string.Join(" ", thin
                    .Take(6)
                    .Select(image =>
                        $"page {image.Page} '{image.Name}': {image.WidthPx}×{image.HeightPx} px at "
                        + $"{image.PlacedWidthMm:F1}×{image.PlacedHeightMm:F1} mm is "
                        + $"{image.EffectivePpiX:F0}×{image.EffectivePpiY:F0} PPI.")));
        }

        var stretched = (receipt?.Sources ?? []).Where(source => source.IsInterpolationOnly).ToList();
        if (stretched.Count > 0)
        {
            problems.Add(
                $"{stretched.Count} raster(s) reached their pixel count by "
                + "interpolation alone, and upscaling changes pixel count rather than source "
                + "detail (audit P1-01). "
                + string.Join(" ", stretched
                    .Take(6)
                    .Select(source =>
                        $"'{source.Role}': {source.SourceWidthPx}×{source.SourceHeightPx} px "
                        + $"enlarged ×{source.Factor:F2} by '{source.Tool}'.")));
        }

        return (new
        {
            gate = PressResolutionGate,
            contract = AcceptanceGatesFile,
            required_press_raster_ppi = requiredPpi,
            verdict = problems.Count == 0 ? "PASS" : "FAIL",
            // Named so a reader of the JSON is not left to guess why a FAIL still produced a file:
            // the gate does not withhold anything on its own, and the stage that does is named.
            decision = problems.Count == 0
                ? "none needed"
                : "withheld from this stage: BekiReleasePolicy weighs a failed PRESS_RESOLUTION "
                  + "against the rest of the gates (owner ruling 2026-09-01, rule 4)",
            problems,
            placed_images = images
                .Select(image => new
                {
                    page = image.Page,
                    name = image.Name,
                    width_px = image.WidthPx,
                    height_px = image.HeightPx,
                    placed_width_mm = Math.Round(image.PlacedWidthMm, 2),
                    placed_height_mm = Math.Round(image.PlacedHeightMm, 2),
                    effective_ppi_x = Math.Round(image.EffectivePpiX, 1),
                    effective_ppi_y = Math.Round(image.EffectivePpiY, 1),
                    stencil_mask = image.IsStencilMask,
                    inline = image.Inline,
                })
                .ToList(),
            // Declared but never painted: no ink, so no gate — recorded because an XObject nobody
            // draws is usually a sign that a layout changed and something was left behind.
            declared_but_never_painted = contents
                .SelectMany(page => page.ImagesNeverPlaced.Select(name => new { page = page.Page, name }))
                .ToList(),
            receipt = receipt is null
                ? (object)"no resolution receipt supplied; no enlargement is claimed for any raster"
                : receipt.Sources
                    .Select(source => new
                    {
                        role = source.Role,
                        source_px = new[] { source.SourceWidthPx, source.SourceHeightPx },
                        delivered_px = new[] { source.DeliveredWidthPx, source.DeliveredHeightPx },
                        tool = source.Tool,
                        factor = Math.Round(source.Factor, 4),
                        interpolation_only = source.IsInterpolationOnly,
                    })
                    .ToList(),
        }, problems);
    }

    /// <summary>
    /// <c>TEXT_COLOR_INTEGRITY</c>, content-stream half (amendment A10a).
    ///
    /// After the conversion, every text-showing operator is asked what fill colour was in force
    /// when it ran. On a page the caller has flagged as carrying authored-light text — the credits
    /// page, the cover title — a device-black fill is the exact signature of P0-07: cream text that
    /// left the conversion as <c>0 0 0 1 k</c> and would print as a page nobody can read. Over
    /// artwork this is the only honest test available, because sampling luminance across a painting
    /// says nothing about the glyphs sitting on it.
    ///
    /// Pages nobody flagged are recorded and not judged: the evidence is worth having even where
    /// there is no rule to apply to it.
    /// </summary>
    private static object EnforceTextColourIntegrity(
        IReadOnlyList<BekiContentWalker.PageContent> contents, BekiPrintProbe? probe)
    {
        var lightPages = (probe?.LightTextPages ?? []).ToHashSet();

        var offenders = contents
            .Where(page => lightPages.Contains(page.Page))
            .SelectMany(page => page.TextFills.Where(fill => fill.IsDeviceBlack))
            .ToList();

        if (offenders.Count > 0)
        {
            throw Failure(
                $"{TextColorIntegrityGate}: text authored light came out of the CMYK conversion "
                + "as device black. "
                + string.Join(" ", offenders
                    .Take(6)
                    .Select(fill =>
                        $"page {fill.Page}: {fill.Occurrences} text operator(s) filled "
                        + $"{fill.Describe()}."))
                + " Audit P0-07 is exactly this defect; a light page converted to black is not a "
                + "press file, it is a blank one.");
        }

        return new
        {
            gate = TextColorIntegrityGate,
            verdict = "PASS",
            light_text_pages = lightPages.OrderBy(page => page).ToList(),
            fills = contents
                .Where(page => page.TextFills.Count > 0)
                .Select(page => new
                {
                    page = page.Page,
                    text_fills = page.TextFills
                        .Select(fill => new
                        {
                            colour = fill.Describe(),
                            device_black = fill.IsDeviceBlack,
                            occurrences = fill.Occurrences,
                        })
                        .ToList(),
                })
                .ToList(),
        };
    }

    /// <summary>
    /// Rejects a glyph sequence painted four or more times on one page. Ordinary repeated prose
    /// can legitimately say the same short line twice; the superseded outline paints every line
    /// 17 times at tiny offsets. Four is therefore a conservative, deterministic signature of the
    /// forbidden treatment without attempting to decode subset-font glyphs.
    /// </summary>
    private static object EnforceSingleTextLayer(
        IReadOnlyList<BekiContentWalker.PageContent> contents,
        BekiPrintProbe? probe)
    {
        var repeated = contents
            .SelectMany(page => page.TextDraws
                .Where(draw => draw.Occurrences >= 4)
                .Select(draw => new { page.Page, draw.Signature, draw.Occurrences }))
            .ToList();

        var overBudget = contents
            .Select(page => new
            {
                page.Page,
                Actual = page.TextFills.Sum(fill => fill.Occurrences),
                Maximum = probe?.MaximumVisibleTextDrawsByPage is { } limits
                          && limits.TryGetValue(page.Page, out var maximum)
                    ? maximum
                    : (int?)null,
            })
            .Where(item => item.Maximum.HasValue && item.Actual > item.Maximum.Value)
            .ToList();

        if (repeated.Count > 0 || overBudget.Count > 0)
        {
            throw Failure(
                $"{SingleTextLayerGate}: repeated text drawing detected. "
                + string.Join(" ", repeated.Take(8).Select(draw =>
                    $"page {draw.Page}: encoded glyph sequence {draw.Signature[..12]} was painted "
                    + $"{draw.Occurrences} times."))
                + string.Join(" ", overBudget.Take(8).Select(item =>
                    $"page {item.Page}: {item.Actual} visible text draws exceed the layout budget "
                    + $"of {item.Maximum}."))
                + " The canonical book requires one visible vector text layer, not an offset-copy outline.");
        }

        return new
        {
            gate = SingleTextLayerGate,
            verdict = "PASS",
            maximum_identical_draws_on_one_page = contents
                .SelectMany(page => page.TextDraws)
                .Select(draw => draw.Occurrences)
                .DefaultIfEmpty(0)
                .Max(),
        };
    }

    private static object EnforceVectorCoverLogo(IReadOnlyList<BekiContentWalker.PageContent> contents)
    {
        var cover = contents.Single(page => page.Page == 1);
        var rasterCount = cover.Images.Count(image => !image.IsStencilMask);
        if (rasterCount != 1)
        {
            throw Failure(
                $"{VectorLogoGate}: canonical cover contains {rasterCount} placed raster image "
                + "objects; exactly one cover-art raster is allowed and the approved logo must remain vector.");
        }

        return new { gate = VectorLogoGate, verdict = "PASS", cover_raster_count = rasterCount };
    }

    /// <summary>
    /// <c>TEXT_COLOR_INTEGRITY</c>, rendered-pixel half (amendment A10a) — for flat-ground pages
    /// only, where "the text is lighter than what it sits on" is a statement pixels can settle.
    ///
    /// The page is rendered by Ghostscript at the configured validation density and the caller's
    /// rectangle is sampled. Two modes are looked for inside it: a bright one that is the glyphs
    /// and a dark one that is the ground. The thresholds are the correction plan's — glyphs at
    /// luma ≥ 200 in a cream (warm, not blue) direction, ground at ≤ 90 — and they are stated as
    /// measurements in the report either way, so a future shift can be argued about with numbers.
    /// </summary>
    private static object RunTextPixelProbes(
        byte[] prepared,
        IReadOnlyList<double> pageHeightsMm,
        BekiPrintPrepOptions options,
        BekiPrintProbe? probe)
    {
        var rects = probe?.FlatGroundRects;
        if (rects is null || rects.Count == 0)
        {
            return "no flat-ground probe rects supplied; the content-stream assertion above is the "
                   + "whole of the text-colour check for this artifact";
        }

        var dpi = options.RenderDpi > 0 ? options.RenderDpi : 120;
        var results = new List<object>();
        var failures = new List<string>();

        var work = Path.Combine(Path.GetTempPath(), $"beki-text-probe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);

        try
        {
            var input = Path.Combine(work, "in.pdf");
            File.WriteAllBytes(input, prepared);

            foreach (var rect in rects)
            {
                if (rect.Page < 1 || rect.Page > pageHeightsMm.Count)
                {
                    failures.Add(
                        $"page {rect.Page} was asked for and the document has "
                        + $"{pageHeightsMm.Count} page(s)");
                    continue;
                }

                var png = Path.Combine(work, $"page-{rect.Page}.png");
                var render = RenderPage(options, input, png, rect.Page, dpi);

                if (render is not null)
                {
                    failures.Add($"page {rect.Page} could not be rendered: {render}");
                    continue;
                }

                var measurement = MeasureTextRect(png, rect, dpi);
                results.Add(measurement.Report);

                if (measurement.Problem is not null)
                {
                    failures.Add($"page {rect.Page} ({rect.Role}): {measurement.Problem}");
                }
            }
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { /* temp cleanup only */ }
        }

        if (failures.Count > 0)
        {
            throw Failure(
                $"{TextColorIntegrityGate}: the rendered page does not show light text on a dark "
                + "ground where the layout says it should. " + string.Join(" ", failures) + ".");
        }

        return new { gate = TextColorIntegrityGate, verdict = "PASS", render_dpi = dpi, probes = results };
    }

    /// <summary>Renders one page to PNG. Returns null on success, or why it failed.</summary>
    private static string? RenderPage(
        BekiPrintPrepOptions options, string inputPdf, string outputPng, int page, int dpi)
    {
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = options.GhostscriptPath,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };

            foreach (var argument in new[]
            {
                "-dBATCH", "-dNOPAUSE", "-dQUIET", "-dSAFER",
                "-sDEVICE=png16m",
                $"-r{dpi.ToString(CultureInfo.InvariantCulture)}",
                $"-dFirstPage={page.ToString(CultureInfo.InvariantCulture)}",
                $"-dLastPage={page.ToString(CultureInfo.InvariantCulture)}",
                $"-sOutputFile={outputPng}",
                "-f", inputPdf,
            })
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start);
            if (process is null)
            {
                return $"Ghostscript did not start ('{options.GhostscriptPath}')";
            }

            var (_, stderr) = Drain(process, TimeSpan.FromMinutes(3), out var finished);

            if (!finished)
            {
                return "Ghostscript did not finish rendering within three minutes";
            }

            return process.ExitCode == 0 && File.Exists(outputPng)
                ? null
                : $"Ghostscript exited {process.ExitCode}: {Truncate(stderr)}";
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return $"Ghostscript is not available as '{options.GhostscriptPath}'";
        }
    }

    /// <summary>The two modes inside one probe rectangle, and whether they are the right way round.</summary>
    private static (object Report, string? Problem) MeasureTextRect(
        string pngPath, BekiTextProbeRect rect, int dpi)
    {
        const int GlyphLuma = 200;
        const int GroundLuma = 90;
        const double MinimumGlyphShare = 0.004d;

        using var image = Image.Load<Rgb24>(pngPath);

        var left = (int)Math.Round(rect.XMm / 25.4d * dpi);
        var top = (int)Math.Round(rect.YMm / 25.4d * dpi);
        var width = (int)Math.Round(rect.WidthMm / 25.4d * dpi);
        var height = (int)Math.Round(rect.HeightMm / 25.4d * dpi);

        left = Math.Clamp(left, 0, Math.Max(0, image.Width - 1));
        top = Math.Clamp(top, 0, Math.Max(0, image.Height - 1));
        width = Math.Clamp(width, 1, image.Width - left);
        height = Math.Clamp(height, 1, image.Height - top);

        var histogram = new long[256];
        long brightCount = 0;
        double brightR = 0, brightG = 0, brightB = 0;

        for (var y = top; y < top + height; y++)
        {
            for (var x = left; x < left + width; x++)
            {
                var pixel = image[x, y];
                var luma = (int)Math.Round(
                    (0.2126d * pixel.R) + (0.7152d * pixel.G) + (0.0722d * pixel.B));
                luma = Math.Clamp(luma, 0, 255);
                histogram[luma]++;

                if (luma >= GlyphLuma)
                {
                    brightCount++;
                    brightR += pixel.R;
                    brightG += pixel.G;
                    brightB += pixel.B;
                }
            }
        }

        long total = (long)width * height;
        var glyphMode = ModeIn(histogram, GlyphLuma, 255);
        var groundMode = ModeIn(histogram, 0, GroundLuma);
        var share = total == 0 ? 0d : (double)brightCount / total;

        var meanR = brightCount == 0 ? 0d : brightR / brightCount;
        var meanG = brightCount == 0 ? 0d : brightG / brightCount;
        var meanB = brightCount == 0 ? 0d : brightB / brightCount;

        // Cream is warm: red at or above blue. A "bright" mode that is bluer than it is red is a
        // highlight in the artwork, not the cream type this rect was pointed at.
        var warm = brightCount > 0 && meanR + 6d >= meanB;

        string? problem = null;
        if (glyphMode is null)
        {
            problem = $"no glyph mode at or above luma {GlyphLuma} inside the text rect";
        }
        else if (groundMode is null)
        {
            problem = $"no dark ground mode at or below luma {GroundLuma} inside the text rect";
        }
        else if (share < MinimumGlyphShare)
        {
            problem =
                $"only {share:P2} of the rect is at glyph brightness, which is less type than the "
                + "layout says is there";
        }
        else if (!warm)
        {
            problem =
                $"the bright pixels average R{meanR:F0} G{meanG:F0} B{meanB:F0}, which is not the "
                + "cream the credits page is authored in";
        }

        return (new
        {
            page = rect.Page,
            role = rect.Role,
            rect_mm = new[] { rect.XMm, rect.YMm, rect.WidthMm, rect.HeightMm },
            rect_px = new[] { left, top, width, height },
            glyph_mode_luma = glyphMode,
            ground_mode_luma = groundMode,
            glyph_pixel_share = Math.Round(share, 5),
            glyph_mean_rgb = new[] { Math.Round(meanR, 1), Math.Round(meanG, 1), Math.Round(meanB, 1) },
            verdict = problem is null ? "PASS" : "FAIL",
            problem,
        }, problem);
    }

    /// <summary>The most populated luma in a band, or null when the band is effectively empty.</summary>
    private static int? ModeIn(long[] histogram, int from, int to)
    {
        var best = -1;
        long bestCount = 0;

        for (var value = from; value <= to; value++)
        {
            if (histogram[value] > bestCount)
            {
                bestCount = histogram[value];
                best = value;
            }
        }

        return bestCount > 0 ? best : null;
    }

    /// <summary>
    /// The colour conversion: every device colour through the locked profile to CMYK, images
    /// re-encoded at a quality factor chosen to stay visually
    /// transparent (spec §5: no recompression that reduces approved source quality — conversion
    /// necessarily re-encodes, so it re-encodes as gently as JPEG allows, with no downsampling
    /// and no chroma subsampling).
    /// </summary>
    /// <returns>The converted bytes and a one-line record for the report.</returns>
    private static (byte[] Pdf, string Record) ConvertToCmyk(
        byte[] pdf, string iccPath, BekiPrintPrepOptions options)
    {
        var work = Path.Combine(
            Path.GetTempPath(), $"beki-print-prep-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);

        var input = Path.Combine(work, "in.pdf");
        var output = Path.Combine(work, "out.pdf");

        try
        {
            File.WriteAllBytes(input, pdf);

            var start = new ProcessStartInfo
            {
                FileName = options.GhostscriptPath,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };

            // ArgumentList, not a joined string: two of these arguments are paths and one is a
            // PostScript fragment, and shell-style quoting inside a single string is exactly how
            // an argument grows quotes it was never supposed to carry.
            foreach (var argument in new[]
            {
                "-dBATCH", "-dNOPAUSE", "-dQUIET", "-dSAFER",
                // SAFER blocks every read outside the input; the output intent profile is the
                // one extra file the conversion is allowed to open, by exact path.
                $"--permit-file-read={iccPath}",
                "-sDEVICE=pdfwrite",
                "-dCompatibilityLevel=1.6",
                "-sColorConversionStrategy=CMYK",
                "-dProcessColorModel=/DeviceCMYK",
                $"-sOutputICCProfile={iccPath}",
                // No -dBlackText. It was here on the theory that black text belongs on the K plate
                // alone, and audit P0-07 found what it actually did: the credits page is authored
                // cream on dark purple, the option coerced its text to 0 g, and the press file
                // shipped a page of near-invisible type. The audit's instruction is exactly one
                // line — "remove global text-to-black coercion" — and the colour a designer chose
                // now converts through the ICC profile like every other colour in the document.
                // TEXT_COLOR_INTEGRITY, below, is what replaces it: measurement instead of a flag.
                "-dDownsampleColorImages=false",
                "-dDownsampleGrayImages=false",
                "-dDownsampleMonoImages=false",
                $"-sOutputFile={output}",
                "-c",
                "<< /ColorACSImageDict << /QFactor 0.15 /Blend 1 /HSamples [1 1 1 1] /VSamples [1 1 1 1] >> >> setdistillerparams",
                "-f",
                input,
            })
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start)
                ?? throw Failure($"Ghostscript did not start ('{options.GhostscriptPath}').");

            // Both pipes are drained at once, never one after the other. Ghostscript talks on both,
            // and a reader that blocks on one while the other's buffer fills stops the process it
            // is waiting for — a deadlock that looks exactly like a slow conversion.
            var (_, stderr) = Drain(process, TimeSpan.FromMinutes(5), out var finished);

            if (!finished)
            {
                throw Failure("Ghostscript did not finish converting within five minutes.");
            }

            if (process.ExitCode != 0 || !File.Exists(output))
            {
                throw Failure(
                    $"Ghostscript conversion failed (exit {process.ExitCode}): "
                    + Truncate(stderr));
            }

            return (
                File.ReadAllBytes(output),
                $"ghostscript pdfwrite, ColorConversionStrategy=CMYK, output profile "
                + $"'{Path.GetFileName(iccPath)}', no BlackText coercion (audit P0-07), "
                + "QFactor 0.15, no downsampling");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception)
        {
            throw Failure(
                $"Ghostscript is not available as '{options.GhostscriptPath}'. Spec §5 requires "
                + "it on the deployment for colour conversion and render validation.");
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { /* temp cleanup only */ }
        }
    }

    /// <summary>
    /// The page boxes, stated on the file that ships. The conversion pass rewrites the document
    /// and boxes do not reliably survive it, so they are re-applied rather than merely checked:
    /// BleedBox and CropBox equal the MediaBox, and the TrimBox sits the given inset inside it —
    /// 5 mm for the interior, 0 for the cover, whose locked spec sets every box equal.
    /// </summary>
    private static void ApplyBoxes(PdfDocument document, float trimInsetMm)
    {
        ApplyBoxes(document, trimInsetMm, canonicalMixedGeometry: false);
    }

    private static void ApplyBoxes(
        PdfDocument document, float trimInsetMm, bool canonicalMixedGeometry)
    {
        var insetPt = trimInsetMm / 25.4f * 72f;

        for (var index = 0; index < document.Pages.Count; index++)
        {
            var page = document.Pages[index];
            var media = page.MediaBox;
            page.CropBox = media;
            page.BleedBox = media;
            var pageInsetPt = canonicalMixedGeometry && index == 0 ? 0f : insetPt;
            page.TrimBox = new PdfRectangle(new PdfSharp.Drawing.XRect(
                media.X1 + pageInsetPt,
                media.Y1 + pageInsetPt,
                media.Width - (2f * pageInsetPt),
                media.Height - (2f * pageInsetPt)));
        }
    }

    /// <summary>
    /// Refuses any drift from the final mixed-geometry contract after Ghostscript conversion and
    /// box reapplication. Merely checking that TrimBox is inside MediaBox is not enough here: a
    /// perfectly nested 451 mm sheet is still the wrong book.
    /// </summary>
    private static void ValidateCanonicalGeometry(PdfDocument document)
    {
        const double toleranceMm = 0.25d;

        for (var index = 0; index < document.Pages.Count; index++)
        {
            var page = document.Pages[index];
            var cover = index == 0;
            var mediaWidthMm = cover ? 512d : 450d;
            var mediaHeightMm = cover ? 245d : 210d;
            var trimWidthMm = cover ? 512d : 440d;
            var trimHeightMm = cover ? 245d : 200d;

            RequireSize(page.MediaBox, mediaWidthMm, mediaHeightMm, index + 1, "MediaBox", toleranceMm);
            RequireSize(page.CropBox, mediaWidthMm, mediaHeightMm, index + 1, "CropBox", toleranceMm);
            RequireSize(page.BleedBox, mediaWidthMm, mediaHeightMm, index + 1, "BleedBox", toleranceMm);
            RequireSize(page.TrimBox, trimWidthMm, trimHeightMm, index + 1, "TrimBox", toleranceMm);

            var expectedInsetMm = cover ? 0d : 5d;
            var leftInsetMm = PointsToMillimetres(page.TrimBox.X1 - page.MediaBox.X1);
            var bottomInsetMm = PointsToMillimetres(page.TrimBox.Y1 - page.MediaBox.Y1);
            if (Math.Abs(leftInsetMm - expectedInsetMm) > toleranceMm
                || Math.Abs(bottomInsetMm - expectedInsetMm) > toleranceMm)
            {
                throw Failure(
                    $"canonical geometry: page {index + 1} TrimBox is offset "
                    + $"{leftInsetMm:F2}×{bottomInsetMm:F2} mm; expected "
                    + $"{expectedInsetMm:F2} mm on every edge.");
            }
        }
    }

    private static void RequireSize(
        PdfRectangle box,
        double expectedWidthMm,
        double expectedHeightMm,
        int page,
        string name,
        double toleranceMm)
    {
        var widthMm = PointsToMillimetres(box.Width);
        var heightMm = PointsToMillimetres(box.Height);
        if (Math.Abs(widthMm - expectedWidthMm) > toleranceMm
            || Math.Abs(heightMm - expectedHeightMm) > toleranceMm)
        {
            throw Failure(
                $"canonical geometry: page {page} {name} is {widthMm:F2}×{heightMm:F2} mm; "
                + $"expected {expectedWidthMm:F2}×{expectedHeightMm:F2} mm.");
        }
    }

    private static double PointsToMillimetres(double points) => points / 72d * 25.4d;

    private static void WriteOutputIntent(
        PdfDocument document, byte[] iccBytes, BekiPrintPrepOptions options)
    {
        var profile = new PdfDictionary(document);
        document.Internals.AddObject(profile);
        profile.CreateStream(iccBytes);
        profile.Elements["/N"] = new PdfInteger(4);

        var intent = new PdfDictionary(document);
        document.Internals.AddObject(intent);
        intent.Elements["/Type"] = new PdfName("/OutputIntent");
        intent.Elements["/S"] = new PdfName("/GTS_PDFX");
        intent.Elements["/OutputConditionIdentifier"] = new PdfString(options.OutputConditionIdentifier);
        intent.Elements["/Info"] = new PdfString(options.OutputConditionInfo);
        intent.Elements["/RegistryName"] = new PdfString(options.RegistryName);
        intent.Elements["/DestOutputProfile"] = profile.Reference;

        var intents = new PdfArray(document);

        // AddObject above is what gives the dictionary its reference; PDFsharp types the property
        // nullable because a dictionary never added to a document has none. Say so rather than
        // suppressing it, so that a future reordering of these two lines fails loudly.
        intents.Elements.Add(
            intent.Reference
            ?? throw new InvalidOperationException(
                "the output intent was not registered with the document."));
        document.Internals.Catalog.Elements["/OutputIntents"] = intents;
    }

    /// <summary>
    /// The XMP packet that carries the PDF/X-4 claim. A conforming reader looks here, not in the
    /// info dictionary, for GTS_PDFXVersion.
    /// </summary>
    private static void WriteXmpMetadata(PdfDocument document, string title)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var documentId = $"uuid:{Guid.NewGuid()}";
        var instanceId = $"uuid:{Guid.NewGuid()}";

        var xmp = $"""
            <?xpacket begin="﻿" id="W5M0MpCehiHzreSzNTczkc9d"?>
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
              <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
                <rdf:Description rdf:about=""
                    xmlns:pdfxid="http://www.npes.org/pdfx/ns/id/"
                    pdfxid:GTS_PDFXVersion="{PdfxVersion}"/>
                <rdf:Description rdf:about="" xmlns:dc="http://purl.org/dc/elements/1.1/">
                  <dc:title>
                    <rdf:Alt>
                      <rdf:li xml:lang="x-default">{new System.Xml.Linq.XText(title)}</rdf:li>
                    </rdf:Alt>
                  </dc:title>
                </rdf:Description>
                <rdf:Description rdf:about="" xmlns:xmp="http://ns.adobe.com/xap/1.0/"
                    xmp:CreateDate="{now}" xmp:ModifyDate="{now}" xmp:MetadataDate="{now}"/>
                <rdf:Description rdf:about="" xmlns:pdf="http://ns.adobe.com/pdf/1.3/"
                    pdf:Trapped="False"/>
                <rdf:Description rdf:about="" xmlns:xmpMM="http://ns.adobe.com/xap/1.0/mm/"
                    xmpMM:DocumentID="{documentId}" xmpMM:InstanceID="{instanceId}"/>
              </rdf:RDF>
            </x:xmpmeta>
            <?xpacket end="w"?>
            """;

        var metadata = new PdfDictionary(document);
        document.Internals.AddObject(metadata);
        metadata.Elements["/Type"] = new PdfName("/Metadata");
        metadata.Elements["/Subtype"] = new PdfName("/XML");
        metadata.CreateStream(Encoding.UTF8.GetBytes(xmp));

        document.Internals.Catalog.Elements["/Metadata"] = metadata.Reference;
    }

    private sealed record FontRecord(string Name, bool Embedded);

    private static List<FontRecord> InspectFonts(PdfDocument document)
    {
        var fonts = new Dictionary<string, bool>(StringComparer.Ordinal);

        foreach (var page in document.Pages)
        {
            var resources = page.Elements.GetDictionary("/Resources");
            var pageFonts = resources?.Elements.GetDictionary("/Font");
            if (pageFonts is null)
            {
                continue;
            }

            foreach (var key in pageFonts.Elements.Keys.ToList())
            {
                if (Resolve(pageFonts.Elements[key]) is not PdfDictionary font)
                {
                    continue;
                }

                var name = font.Elements.GetName("/BaseFont");
                var descriptor = FontDescriptor(font);
                var embedded = descriptor is not null
                               && (descriptor.Elements.ContainsKey("/FontFile")
                                   || descriptor.Elements.ContainsKey("/FontFile2")
                                   || descriptor.Elements.ContainsKey("/FontFile3"));

                // A face used on several pages reports once, and "embedded anywhere" is not
                // good enough — every occurrence descends from the same object, so one answer
                // per name is the truth.
                fonts[string.IsNullOrEmpty(name) ? key : name] = embedded;
            }
        }

        return fonts.Select(pair => new FontRecord(pair.Key, pair.Value)).ToList();
    }

    /// <summary>The descriptor that holds the font file, one level down for composite fonts.</summary>
    private static PdfDictionary? FontDescriptor(PdfDictionary font)
    {
        if (Resolve(font.Elements["/FontDescriptor"]) is PdfDictionary direct)
        {
            return direct;
        }

        if (Resolve(font.Elements["/DescendantFonts"]) is PdfArray descendants
            && descendants.Elements.Count > 0
            && Resolve(descendants.Elements[0]) is PdfDictionary descendant)
        {
            return Resolve(descendant.Elements["/FontDescriptor"]) as PdfDictionary;
        }

        return null;
    }

    private sealed record ImageRecord(string ColourSpace, bool IsRgb, bool IsMask);

    private static List<ImageRecord> InspectImages(PdfDocument document)
    {
        var images = new List<ImageRecord>();

        foreach (var page in document.Pages)
        {
            var resources = page.Elements.GetDictionary("/Resources");
            var xobjects = resources?.Elements.GetDictionary("/XObject");
            if (xobjects is null)
            {
                continue;
            }

            foreach (var key in xobjects.Elements.Keys.ToList())
            {
                if (Resolve(xobjects.Elements[key]) is not PdfDictionary xobject
                    || xobject.Elements.GetName("/Subtype") != "/Image")
                {
                    continue;
                }

                Record(xobject, isMask: false);

                // The image's own soft mask is an image too, referenced rather than listed.
                if (Resolve(xobject.Elements["/SMask"]) is PdfDictionary mask)
                {
                    Record(mask, isMask: true);
                }
            }
        }

        return images;

        void Record(PdfDictionary image, bool isMask)
        {
            var (name, components) = ColourSpaceOf(image);
            var isRgb = components == 3
                        || name is "/DeviceRGB" or "/CalRGB";

            images.Add(new ImageRecord(
                components > 0 ? $"{name}({components})" : name, isRgb, isMask));
        }
    }

    /// <summary>The colour space name and, for ICC-based spaces, its component count.</summary>
    private static (string Name, int Components) ColourSpaceOf(PdfDictionary image)
    {
        if (image.Elements.GetBoolean("/ImageMask"))
        {
            return ("(stencil mask)", 0);
        }

        var element = Resolve(image.Elements["/ColorSpace"]);

        switch (element)
        {
            case PdfName name:
                return (name.Value, 0);

            case PdfArray array when array.Elements.Count > 0 && array.Elements[0] is PdfName kind:
                if (kind.Value == "/ICCBased"
                    && array.Elements.Count > 1
                    && Resolve(array.Elements[1]) is PdfDictionary stream)
                {
                    return (kind.Value, stream.Elements.GetInteger("/N"));
                }

                if (kind.Value == "/Indexed"
                    && array.Elements.Count > 1)
                {
                    // An indexed space is whatever its base space is; a palette of RGB entries
                    // is RGB content in a thin disguise.
                    var (baseName, baseComponents) = element is PdfArray outer
                        && Resolve(outer.Elements[1]) is PdfName baseKind
                            ? (baseKind.Value, 0)
                            : ("(indexed)", 0);
                    return ($"/Indexed {baseName}", baseName == "/DeviceRGB" ? 3 : baseComponents);
                }

                return (kind.Value, 0);

            case null:
                return ("(none)", 0);

            default:
                return (element.GetType().Name, 0);
        }
    }

    private sealed record BoxRecord(
        int Page, string MediaBox, string? TrimBox, string? BleedBox, string? Problem);

    private static List<BoxRecord> InspectBoxes(PdfDocument document)
    {
        var boxes = new List<BoxRecord>();

        for (var index = 0; index < document.Pages.Count; index++)
        {
            var page = document.Pages[index];
            var media = page.MediaBox;
            var trim = page.Elements.ContainsKey("/TrimBox") ? page.TrimBox : null;
            var bleed = page.Elements.ContainsKey("/BleedBox") ? page.BleedBox : null;

            string? problem = null;
            if (trim is null)
            {
                problem = "no TrimBox";
            }
            else if (bleed is null)
            {
                problem = "no BleedBox";
            }
            else if (trim.Width > media.Width || trim.Height > media.Height)
            {
                problem = "TrimBox is not inside the MediaBox";
            }

            boxes.Add(new BoxRecord(
                index + 1,
                Describe(media),
                trim is null ? null : Describe(trim),
                bleed is null ? null : Describe(bleed),
                problem));
        }

        return boxes;
    }

    private static string Describe(PdfRectangle box) =>
        $"{box.Width:F1}x{box.Height:F1}pt";

    private static PdfItem? Resolve(PdfItem? item) =>
        item is PdfReference reference ? reference.Value : item;

    /// <summary>
    /// Reads a child process's two output pipes concurrently and waits for it to exit.
    ///
    /// Sequential <c>ReadToEnd()</c> calls are the classic way to hang on Ghostscript: reading
    /// stderr to its end blocks until the process exits, the process fills the stdout pipe's
    /// buffer, and neither side can move again. Ghostscript is chatty on both streams — a
    /// linearizing run narrates its object groups on stdout — so both are drained from the start.
    /// </summary>
    /// <param name="finished">False when the deadline passed; the process is killed in that case.</param>
    internal static (string StandardOutput, string StandardError) Drain(
        Process process, TimeSpan timeout, out bool finished)
    {
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        finished = process.WaitForExit(timeout);

        if (!finished)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        }

        // Both readers complete once the pipes close, which killing the process guarantees.
        try
        {
            Task.WaitAll([stdout, stderr], TimeSpan.FromSeconds(30));
        }
        catch (AggregateException)
        {
            // A pipe that faulted has nothing to say; the exit code and the timeout flag do.
        }

        return (
            stdout.IsCompletedSuccessfully ? stdout.Result : string.Empty,
            stderr.IsCompletedSuccessfully ? stderr.Result : string.Empty);
    }

    private static string Truncate(string value) =>
        string.IsNullOrEmpty(value) ? "(no stderr)" : value.Length <= 500 ? value : value[..500] + "…";

    private static BekiLayoutException Failure(string message) =>
        new(CompositeFailureCodes.PrintPreflightFailed, $"Print preparation refused: {message}");
}
