using System.Text.Json;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using PdfSharp.Pdf.IO;

namespace AdventurePacks.Api.Services.Pdf;

/// <summary>Customer structure is mandatory even when manufacturing is withheld.</summary>
public static class BekiCustomerPdfValidation
{
    public static byte[] Validate(byte[] pdf, bool testingFlow = false)
    {
        try
        {
            using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
            var expectedPages = testingFlow ? 3 : 12;
            if (document.PageCount != expectedPages)
                throw new InvalidOperationException($"Expected {expectedPages} pages including the cover wrap.");
            for (var index = 0; index < document.PageCount; index++)
            {
                var page = document.Pages[index];
                var width = index == 0 ? 512d : 450d;
                var height = index == 0 ? 245d : 210d;
                if (Math.Abs(page.MediaBox.Width * 25.4 / 72 - width) > 0.15
                    || Math.Abs(page.MediaBox.Height * 25.4 / 72 - height) > 0.15
                    || page.Contents.Elements.Count == 0)
                    throw new InvalidOperationException($"Invalid or empty canonical page {index + 1}.");
            }
            // Rendering and QR decoding are separate mandatory checks over these same bytes.
            return JsonSerializer.SerializeToUtf8Bytes(new
            {
                stage = testingFlow ? "beki-testing-sample-v1" : "beki-customer-canonical-v1",
                verdict = "PASS", page_count = expectedPages,
                story_spreads = testingFlow ? 2 : 8, testing_flow = testingFlow, print_approval = "See press-status.json; this is not print approval."
            });
        }
        catch (Exception ex) when (ex is not BekiLayoutException)
        {
            throw new BekiLayoutException(CompositeFailureCodes.PrintPreflightFailed,
                "CUSTOMER_PDF_INVALID: " + ex.Message);
        }
    }
}
