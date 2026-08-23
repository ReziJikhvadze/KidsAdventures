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
