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
/// identification, no output intent, no preflight, and nothing anywhere recording that those
/// steps had been skipped. This stage makes the skip impossible rather than the steps optional:
/// a print artifact either comes out of here — PDF/X-4 identified, Coated FOGRA39 output intent
/// embedded, preflight report written — or it does not exist, with
/// <c>PRINT_PREFLIGHT_FAILED</c> naming exactly which input is missing. Layout export alone is
/// never again "completed print preparation".
///
/// What it deliberately does not do: convert rasters to CMYK. That is the printer's ruling to
/// give (<see cref="BekiPrintPrepOptions.RequireAllCmyk"/>), the conversion machinery does not
/// exist yet, and a stage that claimed to have done it would be the same lie in a new place.
/// PDF/X-4 permits ICC-based colour, which is what the composer emits; the report says so.
/// </summary>
public static class BekiPrintPrep
{
    /// <summary>PDF/X-4's version string, as XMP and the preflight report both name it.</summary>
    public const string PdfxVersion = "PDF/X-4";

    /// <summary>
    /// Applies print preparation to one laid-out artifact and proves what it did.
    /// </summary>
    /// <returns>The prepared PDF and the preflight report, JSON, ready to store beside it.</returns>
    /// <exception cref="BekiLayoutException">
    /// <c>PRINT_PREFLIGHT_FAILED</c> — a required input is missing or a check failed. The message
    /// names the input, because most of them are owner-side deliverables and the log is where
    /// somebody finds out which one is still owed.
    /// </exception>
    public static (byte[] Pdf, string ReportJson) Prepare(
        byte[] laidOutPdf,
        string title,
        BekiPrintPrepOptions options,
        string? baseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(laidOutPdf);
        ArgumentNullException.ThrowIfNull(options);

        var iccBytes = ReadOutputIntentProfile(options, baseDirectory ?? AppContext.BaseDirectory);

        if (options.RequireAllCmyk == true)
        {
            throw Failure(
                "the printer has ruled that every raster must be CMYK, and the RGB-to-CMYK "
                + "conversion stage is not implemented. Refusing to emit a file that claims a "
                + "conversion nobody performed.");
        }

        using var stream = new MemoryStream(laidOutPdf);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);

        // PDF/X requires a document title and an explicit trapped state; both live in the info
        // dictionary and again in the XMP packet, and the two must agree.
        document.Info.Title = title;
        document.Info.Elements["/Trapped"] = new PdfName("/False");

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

        using var output = new MemoryStream();
        document.Save(output);

        var report = JsonSerializer.Serialize(
            new
            {
                stage = "beki-print-prep-v1",
                prepared_at_utc = DateTime.UtcNow,
                pdfx = new
                {
                    version = PdfxVersion,
                    output_condition_identifier = options.OutputConditionIdentifier,
                    output_condition_info = options.OutputConditionInfo,
                    registry_name = options.RegistryName,
                    icc_profile_bytes = iccBytes.Length,
                },
                colour = new
                {
                    require_all_cmyk = options.RequireAllCmyk,
                    ruling = options.RequireAllCmyk is null
                        ? "unconfirmed by the printer; ICC-based colour emitted, permitted by PDF/X-4"
                        : "printer ruled CMYK not required; ICC-based colour emitted",
                    image_colour_spaces = images
                        .GroupBy(image => image.ColourSpace)
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
                    // Rendering through Poppler and Ghostscript is part of the audit's handback
                    // package, run where those tools exist. This report never claims a check it
                    // did not run.
                    poppler = "not run in this stage; render the stored artifact with pdftoppm",
                    ghostscript = "not run in this stage; render the stored artifact with gs",
                },
            },
            new JsonSerializerOptions { WriteIndented = true });

        return (output.ToArray(), report);
    }

    /// <summary>
    /// The profile the output intent embeds, refused loudly when it is not there to embed.
    /// </summary>
    private static byte[] ReadOutputIntentProfile(BekiPrintPrepOptions options, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(options.OutputIntentIccPath))
        {
            throw Failure(
                "no output intent ICC profile is configured (Beki:PrintPrep:OutputIntentIccPath). "
                + "The Coated FOGRA39 profile is owner item 4 on the handoff ledger; until it is "
                + "supplied, no print artifact can claim PDF/X-4.");
        }

        var path = Path.IsPathRooted(options.OutputIntentIccPath)
            ? options.OutputIntentIccPath
            : Path.Combine(baseDirectory, options.OutputIntentIccPath);

        if (!File.Exists(path))
        {
            throw Failure($"the configured output intent ICC profile is missing at '{path}'.");
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

        return bytes;
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

        // XMP must be readable by tools that do not decode PDF filters; PdfSharp compresses new
        // streams by default only when asked, and CreateStream stores the bytes as given.
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

    private sealed record ImageRecord(string ColourSpace);

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

                images.Add(new ImageRecord(ColourSpaceName(xobject)));
            }
        }

        return images;
    }

    private static string ColourSpaceName(PdfDictionary image)
    {
        var element = Resolve(image.Elements["/ColorSpace"]);

        return element switch
        {
            PdfName name => name.Value,
            PdfArray array when array.Elements.Count > 0 && array.Elements[0] is PdfName kind =>
                kind.Value,
            null => "(none)",
            _ => element.GetType().Name,
        };
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
            else if (trim.Width >= media.Width || trim.Height >= media.Height)
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

    private static BekiLayoutException Failure(string message) =>
        new(CompositeFailureCodes.PrintPreflightFailed, $"Print preparation refused: {message}");
}
