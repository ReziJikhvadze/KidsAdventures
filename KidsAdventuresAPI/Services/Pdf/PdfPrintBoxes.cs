using System.IO;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace AdventurePacks.Api.Services.Pdf;

/// <summary>
/// QuestPDF's Skia backend writes MediaBox only; a print PDF whose TrimBox equals its bleed size
/// fails preflight (spec §26).
///
/// FAILS CLOSED: exceptions propagate — a Beki book without correct print boxes must fail the
/// job (the fulfilment catch marks the pack Failed), never complete as a nonconforming print file.
/// </summary>
public static class PdfPrintBoxes
{
    public static byte[] Apply(byte[] pdf, float bleedMm)
    {
        var bleedPt = bleedMm / 25.4f * 72f;
        using var stream = new MemoryStream(pdf);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);

        foreach (var page in document.Pages)
        {
            var mediaBox = page.MediaBox;
            page.BleedBox = mediaBox;
            page.TrimBox = new PdfRectangle(new PdfSharp.Drawing.XRect(
                mediaBox.X1 + bleedPt,
                mediaBox.Y1 + bleedPt,
                mediaBox.Width - (2f * bleedPt),
                mediaBox.Height - (2f * bleedPt)));
        }

        using var outStream = new MemoryStream();
        document.Save(outStream);
        return outStream.ToArray();
    }
}

/// <summary>
/// The same job for the file a parent downloads, and it is the opposite job.
///
/// Audit P0-08: the reading copy shipped with 230 × 210 and 450 × 210 mm pages carrying printer
/// bleed, and with no CropBox at all — and a viewer with no CropBox displays the MediaBox, so what
/// the parent actually saw was the bleed area, five millimetres of overrun on every edge of every
/// page. The correction is a dedicated trim-size export (<c>BekiPdfComposer.ComposeReading</c>);
/// this is the last step of it, and it states three things a downloadable PDF has to state:
///
/// 1. **A CropBox, equal to the MediaBox.** Present, not merely correct — absence is the defect.
/// 2. **No printer-only boxes.** A BleedBox or TrimBox on a file already at trim would describe an
///    overrun and a trim that are not there; they are removed rather than set equal, because a
///    press box in a customer file is exactly what the audit asked us to stop shipping.
/// 3. **<c>/Lang</c> on the catalog.** Audit P2-2: the book is Georgian, and a reader that does not
///    know that cannot speak it. <c>BekiDigitalPrep</c> re-asserts the same value after Ghostscript
///    rebuilds the catalog, and reads it back as a gate.
///
/// FAILS CLOSED for the same reason its print sibling does.
/// </summary>
public static class PdfReaderBoxes
{
    /// <summary>The document language the audit asks for, and the one the digital gate checks.</summary>
    public const string DocumentLanguage = "ka-GE";

    public static byte[] Apply(byte[] pdf, string language = DocumentLanguage)
    {
        using var stream = new MemoryStream(pdf);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);

        foreach (var page in document.Pages)
        {
            page.CropBox = page.MediaBox;
            page.Elements.Remove("/BleedBox");
            page.Elements.Remove("/TrimBox");
            page.Elements.Remove("/ArtBox");
        }

        document.Internals.Catalog.Elements.SetString("/Lang", language);

        using var outStream = new MemoryStream();
        document.Save(outStream);
        return outStream.ToArray();
    }
}
