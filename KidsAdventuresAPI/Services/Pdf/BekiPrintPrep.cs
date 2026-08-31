using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace AdventurePacks.Api.Services.Pdf;

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
/// pipeline exists for, black text has to stay on the K plate rather than becoming four-colour,
/// and spec §5 requires the Ghostscript binary on the deployment as a render validator anyway.
/// </summary>
public static class BekiPrintPrep
{
    /// <summary>PDF/X-4's version string, as XMP and the preflight report both name it.</summary>
    public const string PdfxVersion = "PDF/X-4";

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
    /// <param name="baseDirectory">Test override for resolving the profile path.</param>
    /// <returns>The prepared PDF and the preflight report, JSON, ready to store beside it.</returns>
    /// <exception cref="BekiLayoutException">
    /// <c>PRINT_PREFLIGHT_FAILED</c> — a required input is missing or a check failed. The message
    /// names the cause, because the log is where somebody finds out what is still owed.
    /// </exception>
    public static (byte[] Pdf, string ReportJson) Prepare(
        byte[] laidOutPdf,
        string title,
        BekiPrintPrepOptions options,
        float trimInsetMm = 5f,
        string? baseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(laidOutPdf);
        ArgumentNullException.ThrowIfNull(options);

        var root = baseDirectory ?? AppContext.BaseDirectory;
        var (iccPath, iccBytes) = ReadOutputIntentProfile(options, root);

        // The input is proven before anything expensive touches it, and its page count becomes
        // the contract the conversion must honour. Ghostscript recovers from a broken input by
        // emitting a valid BLANK document and exiting clean — a torn-off header comes back as
        // one empty page — and a blank page then passes every per-page check by having nothing
        // on it to fail. The count is the only witness the layout leaves behind.
        int expectedPages;
        try
        {
            using var laidOut = PdfReader.Open(
                new MemoryStream(laidOutPdf), PdfDocumentOpenMode.InformationOnly);
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

        ApplyBoxes(document, trimInsetMm);
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

        using var output = new MemoryStream();
        document.Save(output);

        var report = JsonSerializer.Serialize(
            new
            {
                stage = "beki-print-prep-v2",
                spec = "BEKI_Print_Production_Locked_Spec_v1",
                prepared_at_utc = DateTime.UtcNow,
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
                    poppler = "not run in this stage; render the stored artifact with pdftoppm",
                    qr_scan = "run on the rendered artifact in acceptance checks",
                },
            },
            new JsonSerializerOptions { WriteIndented = true });

        return (output.ToArray(), report);
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
    /// The colour conversion: every device colour through the locked profile to CMYK, black text
    /// preserved on the K plate, images re-encoded at a quality factor chosen to stay visually
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
                "-dBlackText=true",
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

            var stderr = process.StandardError.ReadToEnd();
            _ = process.StandardOutput.ReadToEnd();

            if (!process.WaitForExit(TimeSpan.FromMinutes(5)))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
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
                + $"'{Path.GetFileName(iccPath)}', BlackText preserved, QFactor 0.15, no "
                + "downsampling");
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
        var insetPt = trimInsetMm / 25.4f * 72f;

        foreach (var page in document.Pages)
        {
            var media = page.MediaBox;
            page.CropBox = media;
            page.BleedBox = media;
            page.TrimBox = new PdfRectangle(new PdfSharp.Drawing.XRect(
                media.X1 + insetPt,
                media.Y1 + insetPt,
                media.Width - (2f * insetPt),
                media.Height - (2f * insetPt)));
        }
    }

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
        intents.Elements.Add(intent.Reference);
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

    private static string Truncate(string value) =>
        string.IsNullOrEmpty(value) ? "(no stderr)" : value.Length <= 500 ? value : value[..500] + "…";

    private static BekiLayoutException Failure(string message) =>
        new(CompositeFailureCodes.PrintPreflightFailed, $"Print preparation refused: {message}");
}
