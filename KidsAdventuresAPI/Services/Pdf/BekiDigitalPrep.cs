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
/// does three things in one pass, in this order and no other:
///
/// 1. **Restamps ICC profiles Ghostscript will not write.** See
///    <see cref="HarmonizeIccProfiles"/>: <c>pdfwrite</c> silently emits a zero-length colour-space
///    stream for any ICC profile newer than v4.2, and Skia tags every PNG it embeds with a v4.3
///    sRGB. The fix has to happen BEFORE the conversion, because a repair afterwards would have to
///    rewrite the linearized file and destroy the linearization it just paid for.
/// 2. **Linearizes.** Ghostscript writes the file for screen delivery — <c>-dFastWebView=true</c> —
///    with <c>ColorConversionStrategy=LeaveColorUnchanged</c> and every downsampler off. This pass
///    exists to reorder objects, not to touch a single colour: a reading copy that came back with
///    shifted colour would be a different book from the one on paper.
/// 3. **Validates the result, not the input.** Fourteen pages, the exact trim geometry, a CropBox
///    equal to the MediaBox, nothing printer-only left anywhere, every raster in a screen colour
///    space, every ICC profile stream a readable profile, <c>/Lang ka-GE</c>, and the linearization
///    the second step just claimed to perform.
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
        string? baseDirectory = null) =>
        Prepare(composedPdf, options, baseDirectory, harmonizeIccProfiles: true);

    /// <summary>
    /// The same stage with the ICC restamp switchable off — for the regression test that has to
    /// prove the gate at the end of it actually refuses the shipped defect.
    ///
    /// A test seam and nothing else. Turning the restamp off reproduces the empty colour-space
    /// stream exactly as pack 597344af shipped it, which is the only way to demonstrate that the
    /// validation would have caught it; production never calls this overload.
    /// </summary>
    internal static (byte[] Pdf, string ReportJson) Prepare(
        byte[] composedPdf,
        BekiPrintPrepOptions options,
        string? baseDirectory,
        bool harmonizeIccProfiles)
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
            // See the note in BekiPrintPrep: InformationOnly is obsolete as never implemented and
            // promised only the Info dictionary; the count needs the pages.
            using var composed = PdfReader.Open(
                new MemoryStream(composedPdf), PdfDocumentOpenMode.Import);
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

        // Before the conversion, not after: Ghostscript cannot write a post-4.2 ICC profile and
        // does not say so, and a repair applied to its output would mean re-saving the linearized
        // file and throwing away the linearization. See HarmonizeIccProfiles.
        (byte[] ready, IReadOnlyList<string> restamped) = harmonizeIccProfiles
            ? HarmonizeIccProfiles(composedPdf)
            : (composedPdf, Array.Empty<string>());

        var (linearized, conversion) = Linearize(ready, options);

        // Import, because ReadOnly is obsolete in PDFsharp 6.2 as never implemented. Every gate
        // below walks the object graph rather than editing it, and Import carries the whole graph —
        // IccProfileProblems already reads profile streams out of a document opened this way.
        using var stream = new MemoryStream(linearized);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);

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

        /*
          Every ICC profile the file points at is a profile a strict viewer can read.

          The hole this closes cost a paid book. Pack 597344af shipped with two ICCBased colour
          spaces, one valid and one `<</N 3/Length 0>>` — and the empty one was the space eleven of
          its images were drawn in, both covers and all eight spreads among them. This report said
          PASS, because nothing here had ever looked inside a colour-space stream. Poppler complains
          on stderr and renders anyway, which is why every render gate passed too; a stricter viewer
          drops the images, and the parent opened a book of flat coloured pages.

          A zero-length stream and a stream that is not an ICC profile are the same defect from a
          viewer's point of view, so both are read the same way: as bytes, against the profile
          header the ICC specification fixes.
        */
        var profiles = InspectIccProfiles(document);
        var brokenProfiles = profiles.Where(profile => profile.Problem is not null).ToList();
        if (brokenProfiles.Count > 0)
        {
            throw Failure(
                $"{DigitalGeometryGate}: {brokenProfiles.Count} ICCBased colour space(s) point at a "
                + "profile stream that is not a readable ICC profile — "
                + string.Join(" ", brokenProfiles.Select(profile => profile.Problem))
                + " A strict viewer drops every image in such a colour space, which is a page of "
                + "flat colour where the artwork should be.");
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
                    icc_profiles = profiles
                        .Select(profile => new
                        {
                            @object = profile.ObjectNumber,
                            bytes = profile.Length,
                            version = profile.Version,
                            data_colour_space = profile.DataColourSpace,
                            components = profile.Components,
                        })
                        .ToList(),
                    // What the restamp below had to touch on the way in, named so an operator can
                    // see it happened rather than inferring it from a version number.
                    icc_restamped_for_pdfwrite = restamped.Count == 0
                        ? (object)"none; every embedded profile was already one Ghostscript writes"
                        : restamped,
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

    // ==============================================================================================
    // ICC profiles: the empty colour space, and why it was empty
    // ==============================================================================================

    /// <summary>
    /// The last ICC version Ghostscript's <c>pdfwrite</c> will actually write out: 4.2.0.0.
    ///
    /// Established by experiment on the version this project runs, and it is not a PDF-level
    /// question the way it looks: <c>-dCompatibilityLevel=1.7</c> writes a v4.2 profile and drops a
    /// v4.3 one, and <c>-dCompatibilityLevel=2.0</c> drops the v4.3 one too. There is no argument
    /// that makes it write a profile newer than this.
    /// </summary>
    private const uint MaxVersionPdfWriteWrites = 0x04200000u;

    /// <summary>One embedded ICC profile, as the finished file carries it.</summary>
    private sealed record IccProfileRecord(
        int ObjectNumber,
        int Length,
        string Version,
        string DataColourSpace,
        int Components,
        string? Problem);

    /// <summary>
    /// **The fix for a customer PDF that renders as blank pages in Chrome.** Correction: the ICC
    /// version stamp on every embedded profile is clamped to the newest one Ghostscript can write,
    /// before Ghostscript is asked to write it.
    ///
    /// The defect, in full, because it is worth writing down once. A composed reading copy carries
    /// two sRGB profiles: ImageSharp tags the JPEGs it encodes with the approved artwork's own lcms
    /// profile, which is ICC v2.3, and Skia tags every PNG it embeds with its own compact sRGB,
    /// which is ICC **v4.3**. Ghostscript's <c>pdfwrite</c> refuses to write a profile newer than
    /// v4.2 — and its refusal is to emit <c>&lt;&lt;/N 3/Length 0&gt;&gt;</c>, a colour space whose
    /// profile is nothing at all, rather than to fall back to <c>/DeviceRGB</c> or to fail.
    ///
    /// Poppler prints "Couldn't allocate 0 bytes for profile / read ICCBased color space profile
    /// error", renders the page anyway and exits zero, which is exactly why every render gate in
    /// this pipeline passed the broken book: the complaint was only ever on stderr and nobody read
    /// it. A stricter viewer has no such generosity — pack 597344af was reported as pages of flat
    /// colour with no artwork on them, and the empty colour space is what eleven of that book's
    /// images were drawn in: both covers, all eight story spreads, and the credits mark. Whatever a
    /// given viewer chooses to do with it, a colour space with no profile in it is malformed, and a
    /// paid book must not carry one.
    ///
    /// **Why the version stamp and not a substitute profile.** The Skia profile is a perfectly
    /// ordinary matrix/TRC sRGB whose tags — <c>para</c> curves, an <c>mluc</c> description — were
    /// all introduced in ICC v4.0 and are all legal in v4.2. Nothing in it postdates 4.2 except the
    /// four bytes at offset 8 that say so. Restamping those four bytes therefore preserves every
    /// colour number in the profile exactly, which is the promise
    /// <c>ColorConversionStrategy=LeaveColorUnchanged</c> makes two functions below; swapping in a
    /// different sRGB profile would keep the promise only approximately, and dropping the profile
    /// would keep it not at all.
    ///
    /// **Why before the conversion.** The empty stream does not exist until Ghostscript writes it,
    /// so there is nothing to repair on the way in — there is only a cause to remove. Repairing the
    /// output instead would mean re-saving a linearized file with PDFsharp, which rewrites the
    /// object order and destroys the <c>/Linearized</c> dictionary the pass exists to produce.
    ///
    /// The input is returned untouched when nothing needed restamping, so the ordinary path pays for
    /// one read and no rewrite.
    /// </summary>
    /// <returns>The bytes to linearize, and a line per profile that was restamped.</returns>
    internal static (byte[] Pdf, IReadOnlyList<string> Restamped) HarmonizeIccProfiles(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        var restamped = new List<string>();

        try
        {
            using var source = new MemoryStream(pdf);
            using var document = PdfReader.Open(source, PdfDocumentOpenMode.Modify);

            foreach (var profile in IccProfileStreams(document))
            {
                var bytes = ProfileBytes(profile);

                if (bytes.Length < 132 || Version(bytes) <= MaxVersionPdfWriteWrites)
                {
                    continue;
                }

                var was = VersionText(bytes);

                // ProfileBytes above has already decoded this stream — through UnfilteredValue
                // whenever it carries a filter — so the plain bytes are in hand and there is
                // nothing left to unfilter.
                //
                // Deliberately not PdfStream.TryUncompress, which PDFsharp 6.2 offers as the
                // replacement for the obsolete TryUnfilter. It recognises /Filter only as a direct
                // name and answers false for the equally valid array form, /Filter [/FlateDecode];
                // a profile written that way would be passed over instead of restamped, and the
                // gate on the far side of the conversion would then refuse the book. UnfilteredValue
                // reads both forms, which is why the decode already done is the one to trust.
                //
                // A profile this build genuinely cannot decode never reaches here: ProfileBytes
                // returns nothing for it and the length guard above lets it through untouched.
                var raw = bytes.ToArray();
                raw[8] = 0x04;
                raw[9] = 0x20;
                raw[10] = 0x00;
                raw[11] = 0x00;

                // The bytes going back are decoded, so the filter that described them no longer
                // describes them. Leaving it in place would declare compression over plain bytes.
                profile.Stream!.Value = raw;
                profile.Elements.Remove("/Filter");
                profile.Elements.Remove("/DecodeParms");
                profile.Elements.SetInteger("/Length", raw.Length);

                restamped.Add(
                    $"object {ObjectNumberOf(profile)}: ICC {was} restamped as 4.2.0.0 "
                    + $"({raw.Length} bytes, unchanged)");
            }

            if (restamped.Count == 0)
            {
                return (pdf, restamped);
            }

            using var buffer = new MemoryStream();
            document.Save(buffer);
            return (buffer.ToArray(), restamped);
        }
        catch (Exception ex) when (ex is not BekiLayoutException)
        {
            throw Failure(
                $"{DigitalGeometryGate}: the composed reading copy's ICC profiles could not be read "
                + $"before linearization ({ex.GetType().Name}: {ex.Message}). Ghostscript writes a "
                + "zero-length colour space for a profile it will not carry, and this stage will "
                + "not hand it one it has not looked at.");
        }
    }

    /// <summary>
    /// The ICC integrity gate addressed on bytes — the same check <see cref="Prepare"/> runs on its
    /// own output, reachable so that a deliberately doctored file can be shown to fail it.
    ///
    /// It needs its own door because Ghostscript will not produce the inputs this has to be proved
    /// against: handed a profile it cannot parse, pdfwrite drops the colour space entirely rather
    /// than passing the damage through, so a file carrying a truncated or non-ICC profile cannot be
    /// made by running this stage. It has to be built, and then read.
    /// </summary>
    internal static IReadOnlyList<string> IccProfileProblems(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        using var stream = new MemoryStream(pdf);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);

        return InspectIccProfiles(document)
            .Where(profile => profile.Problem is not null)
            .Select(profile => profile.Problem!)
            .ToList();
    }

    /// <summary>
    /// Every ICC profile stream the finished file points at, judged as bytes.
    ///
    /// Judged as bytes and not by asking a colour library, because the failures that matter here are
    /// the ones a library would refuse to describe: a stream of length zero, and a stream that is
    /// not a profile. The header the ICC specification fixes — a size at byte 0, the
    /// <c>acsp</c> signature at byte 36, a version at byte 8, a data colour space at byte 16 — is
    /// enough to tell those apart from a profile, and small enough to read without a dependency.
    /// </summary>
    private static List<IccProfileRecord> InspectIccProfiles(PdfDocument document)
    {
        var records = new List<IccProfileRecord>();

        foreach (var profile in IccProfileStreams(document))
        {
            var number = ObjectNumberOf(profile);
            var declared = profile.Elements.ContainsKey("/N")
                ? profile.Elements.GetInteger("/N")
                : 0;

            var bytes = ProfileBytes(profile);

            if (bytes.Length == 0)
            {
                records.Add(new IccProfileRecord(
                    number, 0, "(none)", "(none)", declared,
                    $"object {number}: the profile stream is empty (0 bytes). This is exactly the "
                    + "defect pack 597344af shipped with."));
                continue;
            }

            if (bytes.Length < 132)
            {
                records.Add(new IccProfileRecord(
                    number, bytes.Length, "(unreadable)", "(unreadable)", declared,
                    $"object {number}: the profile stream is {bytes.Length} bytes, shorter than an "
                    + "ICC header and its tag count."));
                continue;
            }

            var signature = Encoding.Latin1.GetString(bytes, 36, 4);
            if (signature != "acsp")
            {
                records.Add(new IccProfileRecord(
                    number, bytes.Length, "(unreadable)", "(unreadable)", declared,
                    $"object {number}: the profile stream carries '{Printable(signature)}' where "
                    + "the ICC signature 'acsp' belongs, so it is not an ICC profile."));
                continue;
            }

            var size = (int)Math.Min(int.MaxValue, ReadUInt32(bytes, 0));
            var space = Encoding.Latin1.GetString(bytes, 16, 4).TrimEnd();
            var components = space switch
            {
                "GRAY" => 1,
                "RGB" or "Lab" or "XYZ" or "YCbr" => 3,
                "CMYK" => 4,
                _ => 0,
            };

            string? problem = null;

            if (size < 128 || size > bytes.Length)
            {
                problem =
                    $"object {number}: the profile's own header states {size} bytes and the stream "
                    + $"holds {bytes.Length} — it is truncated.";
            }
            else if (declared > 0 && components > 0 && declared != components)
            {
                problem =
                    $"object {number}: the colour space declares /N {declared} and the profile is "
                    + $"{space} ({components} component(s)).";
            }

            records.Add(new IccProfileRecord(
                number, bytes.Length, VersionText(bytes), space, components, problem));
        }

        return records;
    }

    /// <summary>
    /// The profile stream behind every <c>[/ICCBased …]</c> colour space in the document, once each.
    ///
    /// Walked over the object graph rather than over the page resources: a colour-space array is
    /// written as an indirect object by Ghostscript and inline inside the image dictionary by Skia,
    /// and a check that only knew one of those shapes would find nothing in half the files it was
    /// pointed at.
    /// </summary>
    private static List<PdfDictionary> IccProfileStreams(PdfDocument document)
    {
        var found = new List<PdfDictionary>();
        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);

        foreach (var item in document.Internals.GetAllObjects())
        {
            Visit(item, 0);
        }

        return found;

        void Visit(PdfItem? item, int depth)
        {
            // The graph is a tree in practice and cyclic in principle (a page's /Parent points back
            // up), and references are not followed here — GetAllObjects already hands over every
            // indirect object — so the only recursion is into direct children, which cannot be deep.
            if (depth > 24)
            {
                return;
            }

            switch (item)
            {
                case PdfArray array:
                    if (array.Elements.Count > 1
                        && array.Elements[0] is PdfName { Value: "/ICCBased" }
                        && Resolve(array.Elements[1]) is PdfDictionary profile
                        && seen.Add(profile))
                    {
                        found.Add(profile);
                    }

                    foreach (var element in array.Elements)
                    {
                        if (element is not PdfReference) Visit(element, depth + 1);
                    }

                    break;

                case PdfDictionary dictionary:
                    foreach (var key in dictionary.Elements.Keys.ToList())
                    {
                        var value = dictionary.Elements[key];
                        if (value is not PdfReference) Visit(value, depth + 1);
                    }

                    break;
            }
        }
    }

    /// <summary>The profile's bytes as a viewer would read them: unfiltered, or empty if there
    /// are none.</summary>
    private static byte[] ProfileBytes(PdfDictionary profile)
    {
        try
        {
            if (profile.Stream is null)
            {
                return [];
            }

            return profile.Elements.ContainsKey("/Filter")
                ? profile.Stream.UnfilteredValue ?? []
                : profile.Stream.Value ?? [];
        }
        catch (Exception)
        {
            // An undecodable stream is an unreadable profile, which is what the caller is asking.
            return [];
        }
    }

    /// <summary>
    /// The object number a profile stream is stored under, for a message an operator can act on:
    /// "object 108" is something they can find in the file, and "an ICC profile" is not. Zero for a
    /// profile written directly inside the dictionary that uses it, which is legal and rare.
    /// </summary>
    private static int ObjectNumberOf(PdfDictionary profile) =>
        profile.Reference?.ObjectNumber ?? 0;

    private static uint Version(byte[] profile) => ReadUInt32(profile, 8);

    /// <summary>The ICC version as its own specification writes it: 4.3.0.0, packed BCD-ish.</summary>
    private static string VersionText(byte[] profile)
    {
        if (profile.Length < 12)
        {
            return "(unreadable)";
        }

        return $"{profile[8]}.{profile[9] >> 4}.{profile[9] & 0x0F}.{profile[10]}";
    }

    private static uint ReadUInt32(byte[] bytes, int offset) =>
        ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16)
        | ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];

    private static string Printable(string value) =>
        new([.. value.Select(character => char.IsControl(character) ? '.' : character)]);

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
