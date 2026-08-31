using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace AdventurePacks.Api.Services.Pdf;

/// <summary>
/// The customer's own copy, finished and proved — the other half of a job that until now had one.
///
/// Everything about print preparation existed because a press rejects what it cannot use. Nothing
/// equivalent existed for the file the parent downloads, and audit P0-08 is the result: the reading
/// copy was built straight through the print path, so it shipped 230×210 mm and 450×210 mm pages
/// carrying printer bleed, no CropBox at all — which means an ordinary viewer displays the bleed
/// area — and 300 PPI press rasters inflating it to 34 MB. P2-1 adds that it was not linearized, so
/// a reader waits for the whole file before seeing page one, and P2-2 that it carried no document
/// language.
///
/// This stage is the answer to all of that, and correction-plan amendment A10c is its contract. It
/// does two things in one pass, in this order and no other:
///
/// 1. **Linearizes.** Ghostscript writes the file for screen delivery — <c>-dFastWebView=true</c> —
///    with <c>ColorConversionStrategy=LeaveColorUnchanged</c> and every downsampler off. This pass
///    exists to reorder objects, not to touch a single colour: a reading copy that came back with
///    shifted colour would be a different book from the one on paper.
/// 2. **Validates the result, not the input.** Fourteen pages, the exact trim geometry, a CropBox
///    equal to the MediaBox, nothing printer-only left anywhere, every raster in a screen colour
///    space, <c>/Lang ka-GE</c>, and the linearization the first step just claimed to perform.
///
/// The composer produces the pages; this stage produces the deliverable. It refuses with the same
/// <c>PRINT_PREFLIGHT_FAILED</c> code the press stage uses, naming the <c>DIGITAL_GEOMETRY</c> gate
/// in the message where the check belongs to it.
/// </summary>
public static class BekiDigitalPrep
{
    /// <summary>The gate this stage answers for, as <c>BEKI_Acceptance_Gates_v1.json</c> names it.</summary>
    public const string DigitalGeometryGate = "DIGITAL_GEOMETRY";

    /// <summary>The document language the audit's P2-2 asks for, on the catalog.</summary>
    public const string DocumentLanguage = "ka-GE";

    /// <summary>
    /// Fourteen: front cover, front endpaper, intro, eight story spreads, credits, rear endpaper,
    /// back cover. Written against <see cref="BookFormat.SpreadCount"/> rather than as a literal so
    /// that a change to the book's length is a compile-time conversation and not a runtime refusal.
    /// </summary>
    public static readonly int ExpectedPageCount = BookFormat.SpreadCount + 6;

    /// <summary>Half a point: the tolerance A10c states for the millimetre geometry.</summary>
    private const double ToleranceP = 0.5d;

    private const string AcceptanceGatesFile = "BEKI_Acceptance_Gates_v1.json";

    /// <summary>
    /// Linearizes one composed trim-size document and proves what came back.
    /// </summary>
    /// <param name="composedPdf">The screen-mode document, straight from the composer.</param>
    /// <param name="options">Ghostscript's path and the shared print-prep configuration.</param>
    /// <param name="baseDirectory">Test override for locating the acceptance-gates document.</param>
    /// <returns>The linearized PDF and its preflight report, JSON, to store beside it.</returns>
    /// <exception cref="BekiLayoutException">
    /// <c>PRINT_PREFLIGHT_FAILED</c>, naming the check that refused.
    /// </exception>
    public static (byte[] Pdf, string ReportJson) Prepare(
        byte[] composedPdf,
        BekiPrintPrepOptions options,
        string? baseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(composedPdf);
        ArgumentNullException.ThrowIfNull(options);

        var root = baseDirectory ?? AppContext.BaseDirectory;
        var locked = ReadLockedGeometry(root);

        // The page count is taken from the input and held over the conversion, for the reason the
        // press stage documents: Ghostscript recovers from a broken input by writing a valid blank
        // document and exiting clean, and every per-page check below would then pass by having
        // nothing to fail on.
        int expectedPages;
        try
        {
            using var composed = PdfReader.Open(
                new MemoryStream(composedPdf), PdfDocumentOpenMode.InformationOnly);
            expectedPages = composed.PageCount;
        }
        catch (Exception ex) when (ex is not BekiLayoutException)
        {
            throw Failure($"the composed reading copy is not a readable PDF ({ex.GetType().Name}).");
        }

        if (expectedPages != ExpectedPageCount)
        {
            throw Failure(
                $"{DigitalGeometryGate}: the reading copy has {expectedPages} page(s) where the "
                + $"book is {ExpectedPageCount} — cover, endpaper, intro, "
                + $"{BookFormat.SpreadCount} spreads, credits, endpaper, back cover.");
        }

        var (linearized, conversion) = Linearize(composedPdf, options);

        using var stream = new MemoryStream(linearized);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.ReadOnly);

        if (document.PageCount != expectedPages)
        {
            throw Failure(
                $"the linearization pass returned {document.PageCount} page(s) where the composed "
                + $"reading copy has {expectedPages} — content was dropped rather than reordered.");
        }

        var pages = InspectGeometry(document, locked);
        var problems = pages.Where(page => page.Problem is not null).ToList();
        if (problems.Count > 0)
        {
            throw Failure(
                $"{DigitalGeometryGate}: "
                + string.Join(" ", problems.Select(page => $"page {page.Page}: {page.Problem}.")));
        }

        var printerOnly = InspectPrinterOnlyMarkers(document, linearized);
        if (printerOnly.Count > 0)
        {
            throw Failure(
                $"{DigitalGeometryGate}: the reading copy still carries printer-only structures — "
                + string.Join(" ", printerOnly) + ". Audit P0-08: the downloadable file must not "
                + "carry print bleed, trim metadata or press colour identification.");
        }

        var rasters = InspectRasterColourSpaces(document);
        var offColour = rasters.Where(raster => !raster.IsScreenColour).ToList();
        if (offColour.Count > 0)
        {
            throw Failure(
                $"{DigitalGeometryGate}: {offColour.Count} raster image object(s) are not in a "
                + "screen colour space "
                + $"({string.Join(", ", offColour.Select(raster => raster.ColourSpace).Distinct())}). "
                + "The reading copy is an sRGB deliverable; a CMYK raster in it is a press master "
                + "that escaped.");
        }

        var language = document.Internals.Catalog.Elements.GetString("/Lang");
        if (!string.Equals(language, DocumentLanguage, StringComparison.Ordinal))
        {
            throw Failure(
                $"{DigitalGeometryGate}: the catalog states /Lang '{language}' rather than "
                + $"'{DocumentLanguage}'. Audit P2-2 asks for the document language, and a Georgian "
                + "book that does not say it is Georgian reads wrong to every assistive tool.");
        }

        var linearizedHeader = IsLinearized(linearized);
        if (!linearizedHeader)
        {
            throw Failure(
                "the linearization pass exited clean and the result carries no /Linearized "
                + "dictionary. Audit P2-1 asks for Fast Web View; a file that only claims it is a "
                + "34 MB wait with a promise attached.");
        }

        var report = JsonSerializer.Serialize(
            new
            {
                stage = "beki-digital-prep-v1",
                contract = AcceptanceGatesFile,
                gate = DigitalGeometryGate,
                verdict = "PASS",
                prepared_at_utc = DateTime.UtcNow,
                bytes = linearized.Length,
                linearization = new
                {
                    fast_web_view = true,
                    linearized_dictionary_present = linearizedHeader,
                    conversion,
                },
                geometry = new
                {
                    expected_page_count = ExpectedPageCount,
                    page_count = document.PageCount,
                    single_page_mm = locked.SinglePageMm,
                    spread_mm = locked.SpreadMm,
                    tolerance_pt = ToleranceP,
                    pages = pages
                        .Select(page => new
                        {
                            page = page.Page,
                            kind = page.Kind,
                            media_box_mm = page.MediaBoxMm,
                            crop_box_mm = page.CropBoxMm,
                            has_bleed_box = page.HasBleedBox,
                            has_trim_box = page.HasTrimBox,
                        })
                        .ToList(),
                },
                colour = new
                {
                    document_language = language,
                    rasters = rasters
                        .GroupBy(raster => raster.ColourSpace)
                        .ToDictionary(group => group.Key, group => group.Count()),
                },
                printer_only_markers = "none",
            },
            new JsonSerializerOptions { WriteIndented = true });

        return (linearized, report);
    }

    // ---------------------------------------------------------------------------------------

    private sealed record LockedGeometry(double[] SinglePageMm, double[] SpreadMm);

    /// <summary>
    /// The digital geometry, read from the supplier's gates document. Same reasoning as the press
    /// side: these millimetres belong to the people who accept the delivery, and a copy in C# is a
    /// second source of truth that drifts silently.
    /// </summary>
    private static LockedGeometry ReadLockedGeometry(string baseDirectory)
    {
        var path = Path.Combine(
            baseDirectory, "Assets", "BekiComposite", "contracts", AcceptanceGatesFile);

        if (!File.Exists(path))
        {
            throw Failure(
                $"the acceptance gates document is missing at '{path}'. The digital geometry gate "
                + "reads its millimetres from the supplier's own file and will not guess them.");
        }

        try
        {
            using var gates = JsonDocument.Parse(File.ReadAllText(path));
            var locked = gates.RootElement.GetProperty("locked_values");

            return new LockedGeometry(
                Pair(locked.GetProperty("digital_single_page_mm")),
                Pair(locked.GetProperty("digital_spread_mm")));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException
                                       or InvalidOperationException and not BekiLayoutException)
        {
            throw Failure(
                $"'{AcceptanceGatesFile}' does not state the digital page geometry "
                + $"({ex.GetType().Name}).");
        }

        static double[] Pair(JsonElement element) =>
            [element[0].GetDouble(), element[1].GetDouble()];
    }

    /// <summary>
    /// The linearization pass. Same rails as the press conversion — argument list rather than a
    /// joined command line, a bounded wait, a named refusal when the binary is absent — because the
    /// failure modes of running Ghostscript are the same whatever it is being asked to do.
    /// </summary>
    private static (byte[] Pdf, string Record) Linearize(byte[] pdf, BekiPrintPrepOptions options)
    {
        var work = Path.Combine(Path.GetTempPath(), $"beki-digital-prep-{Guid.NewGuid():N}");
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

            foreach (var argument in new[]
            {
                "-dBATCH", "-dNOPAUSE", "-dQUIET", "-dSAFER",
                "-sDEVICE=pdfwrite",
                "-dCompatibilityLevel=1.7",
                // Fast Web View: the whole reason this pass exists (audit P2-1).
                "-dFastWebView=true",
                // And the whole reason it is allowed to touch nothing else. The reading copy has to
                // be the same book as the printed one; a colour transform here would quietly make
                // it a different one.
                "-sColorConversionStrategy=LeaveColorUnchanged",
                "-dPassThroughJPEGImages=true",
                "-dAutoRotatePages=/None",
                "-dDownsampleColorImages=false",
                "-dDownsampleGrayImages=false",
                "-dDownsampleMonoImages=false",
                $"-sOutputFile={output}",
                // pdfwrite rebuilds the document catalog and does not carry /Lang across on its
                // own, so the pass re-asserts what the composer authored rather than letting a
                // reordering step silently drop the document language it is not allowed to change.
                "-c",
                $"[{{Catalog}} <</Lang ({DocumentLanguage})>> /PUT pdfmark",
                "-f",
                input,
            })
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start)
                ?? throw Failure($"Ghostscript did not start ('{options.GhostscriptPath}').");

            // Both pipes at once. A linearizing run narrates every shared object group on stdout,
            // which is far more than a pipe buffer holds — draining stderr first would stop
            // Ghostscript mid-sentence and hang this thread behind it.
            var (_, stderr) = BekiPrintPrep.Drain(process, TimeSpan.FromMinutes(5), out var finished);

            if (!finished)
            {
                throw Failure("Ghostscript did not finish linearizing within five minutes.");
            }

            if (process.ExitCode != 0 || !File.Exists(output))
            {
                throw Failure(
                    $"Ghostscript linearization failed (exit {process.ExitCode}): {Truncate(stderr)}");
            }

            return (
                File.ReadAllBytes(output),
                "ghostscript pdfwrite, FastWebView=true, ColorConversionStrategy="
                + "LeaveColorUnchanged, JPEG pass-through, no downsampling, /Lang re-asserted");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception)
        {
            throw Failure(
                $"Ghostscript is not available as '{options.GhostscriptPath}'. The reading copy is "
                + "linearized with it, as the press file is converted with it.");
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { /* temp cleanup only */ }
        }
    }

    private sealed record PageGeometry(
        int Page,
        string Kind,
        double[] MediaBoxMm,
        double[]? CropBoxMm,
        bool HasBleedBox,
        bool HasTrimBox,
        string? Problem);

    /// <summary>
    /// The page boxes, judged against A10c: the two single leaves at 220×200 mm, the twelve spreads
    /// at 440×200 mm, a CropBox present and equal to the MediaBox on every one of them, and no
    /// printer box reaching past it.
    ///
    /// CropBox *present* is the part audit P0-08 turns on: the shipped file had none, and a viewer
    /// with no CropBox displays the MediaBox — which is why a parent opening the reading copy saw
    /// the bleed.
    /// </summary>
    private static List<PageGeometry> InspectGeometry(PdfDocument document, LockedGeometry locked)
    {
        var pages = new List<PageGeometry>();

        for (var index = 0; index < document.PageCount; index++)
        {
            var page = document.Pages[index];
            var number = index + 1;

            // First and last are the covers, single leaves; everything between is a spread.
            var isLeaf = number == 1 || number == document.PageCount;
            var expected = isLeaf ? locked.SinglePageMm : locked.SpreadMm;
            var kind = isLeaf ? "single" : "spread";

            var media = page.MediaBox;
            var hasCrop = page.Elements.ContainsKey("/CropBox");
            var crop = hasCrop ? page.CropBox : null;
            var hasBleed = page.Elements.ContainsKey("/BleedBox");
            var hasTrim = page.Elements.ContainsKey("/TrimBox");

            string? problem = null;

            if (!Matches(media.Width, expected[0]) || !Matches(media.Height, expected[1]))
            {
                problem =
                    $"the MediaBox is {Mm(media.Width):F2}×{Mm(media.Height):F2} mm where a "
                    + $"{kind} page is {expected[0]}×{expected[1]} mm";
            }
            else if (!hasCrop || crop is null)
            {
                problem =
                    "there is no CropBox, so a viewer displays the MediaBox — which is exactly how "
                    + "the audited file showed printer bleed to a parent";
            }
            else if (Math.Abs(crop.Width - media.Width) > ToleranceP
                     || Math.Abs(crop.Height - media.Height) > ToleranceP
                     || Math.Abs(crop.X1 - media.X1) > ToleranceP
                     || Math.Abs(crop.Y1 - media.Y1) > ToleranceP)
            {
                problem =
                    $"the CropBox is {Mm(crop.Width):F2}×{Mm(crop.Height):F2} mm and does not "
                    + "coincide with the MediaBox";
            }
            else if (hasBleed && !SameBox(page.BleedBox, media))
            {
                problem = "a BleedBox reaches beyond the trim; the reading copy carries no bleed";
            }
            else if (hasTrim && !SameBox(page.TrimBox, media))
            {
                problem = "a TrimBox states a trim inside the page; this file is already at trim";
            }

            pages.Add(new PageGeometry(
                number,
                kind,
                [Math.Round(Mm(media.Width), 2), Math.Round(Mm(media.Height), 2)],
                crop is null
                    ? null
                    : [Math.Round(Mm(crop.Width), 2), Math.Round(Mm(crop.Height), 2)],
                hasBleed,
                hasTrim,
                problem));
        }

        return pages;
    }

    /// <summary>
    /// Press identification that has no business in a download: an output intent, a PDF/X version
    /// claim in the XMP, or a <c>GTS_PDFX</c> subtype anywhere in the file.
    /// </summary>
    private static List<string> InspectPrinterOnlyMarkers(PdfDocument document, byte[] bytes)
    {
        var found = new List<string>();

        if (document.Internals.Catalog.Elements.ContainsKey("/OutputIntents"))
        {
            found.Add("the catalog carries /OutputIntents");
        }

        var text = Encoding.Latin1.GetString(bytes);

        if (text.Contains("/GTS_PDFX", StringComparison.Ordinal))
        {
            found.Add("a /GTS_PDFX output-intent subtype is present");
        }

        if (text.Contains("GTS_PDFXVersion", StringComparison.Ordinal))
        {
            found.Add("the XMP packet claims a PDF/X version");
        }

        return found;
    }

    private sealed record RasterRecord(string ColourSpace, bool IsScreenColour);

    /// <summary>
    /// Every raster's colour space, judged by the one rule a screen deliverable has: RGB, whether
    /// stated as a device space or through an sRGB-class ICC profile. Deliberately implemented here
    /// rather than shared with the press preflight — the two stages ask opposite questions of the
    /// same dictionary, and a shared helper would be one edit away from letting one of them answer
    /// with the other's rule.
    /// </summary>
    private static List<RasterRecord> InspectRasterColourSpaces(PdfDocument document)
    {
        var rasters = new List<RasterRecord>();

        for (var index = 0; index < document.PageCount; index++)
        {
            var resources = document.Pages[index].Elements.GetDictionary("/Resources");
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

                rasters.Add(Classify(xobject));
            }
        }

        return rasters;

        static RasterRecord Classify(PdfDictionary image)
        {
            if (image.Elements.GetBoolean("/ImageMask"))
            {
                // A stencil takes the fill colour; it carries none of its own.
                return new RasterRecord("(stencil mask)", true);
            }

            var element = Resolve(image.Elements["/ColorSpace"]);

            switch (element)
            {
                case PdfName name:
                    // Grey is a screen space too: a greyscale photograph and an alpha channel are
                    // both perfectly at home in an sRGB deliverable.
                    return new RasterRecord(
                        name.Value,
                        name.Value is "/DeviceRGB" or "/CalRGB" or "/DeviceGray" or "/CalGray");

                case PdfArray array when array.Elements.Count > 0 && array.Elements[0] is PdfName kind:
                    if (kind.Value == "/ICCBased"
                        && array.Elements.Count > 1
                        && Resolve(array.Elements[1]) is PdfDictionary stream)
                    {
                        var components = stream.Elements.GetInteger("/N");
                        return new RasterRecord(
                            $"/ICCBased({components})", components is 1 or 3);
                    }

                    if (kind.Value == "/Indexed" && array.Elements.Count > 1)
                    {
                        var baseName = Resolve(array.Elements[1]) is PdfName palette
                            ? palette.Value
                            : "(indexed)";
                        return new RasterRecord(
                            $"/Indexed {baseName}",
                            baseName is "/DeviceRGB" or "/CalRGB" or "/DeviceGray" or "/CalGray");
                    }

                    return new RasterRecord(kind.Value, false);

                case null:
                    return new RasterRecord("(none)", false);

                default:
                    return new RasterRecord(element.GetType().Name, false);
            }
        }
    }

    /// <summary>
    /// Whether the file opens with a linearization dictionary. It is the first object in a
    /// linearized file by definition, so the header region is the whole of where to look.
    /// </summary>
    private static bool IsLinearized(byte[] pdf)
    {
        var window = Math.Min(pdf.Length, 2048);
        return Encoding.Latin1.GetString(pdf, 0, window)
            .Contains("/Linearized", StringComparison.Ordinal);
    }

    private static bool SameBox(PdfRectangle box, PdfRectangle media) =>
        Math.Abs(box.X1 - media.X1) <= ToleranceP
        && Math.Abs(box.Y1 - media.Y1) <= ToleranceP
        && Math.Abs(box.Width - media.Width) <= ToleranceP
        && Math.Abs(box.Height - media.Height) <= ToleranceP;

    private static bool Matches(double pt, double mm) => Math.Abs(pt - (mm / 25.4d * 72d)) <= ToleranceP;

    private static double Mm(double pt) => pt / 72d * 25.4d;

    private static PdfItem? Resolve(PdfItem? item) =>
        item is PdfReference reference ? reference.Value : item;

    private static string Truncate(string value) =>
        string.IsNullOrEmpty(value) ? "(no stderr)"
        : value.Length <= 500 ? value
        : value[..500] + "…";

    private static BekiLayoutException Failure(string message) =>
        new(CompositeFailureCodes.PrintPreflightFailed, $"Digital preparation refused: {message}");
}
