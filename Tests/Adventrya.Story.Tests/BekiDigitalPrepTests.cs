using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Pdf;
using AdventurePacks.Api.Services.Story;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Adventrya.Story.Tests;

/// <summary>
/// The customer's copy, prepared and proved — correction plan D3 and amendment A10c, against audit
/// P0-08 (the reading copy shipped with printer bleed and no CropBox, so a parent opening it saw
/// the bleed area), P2-1 (34 MB and not linearized) and P2-2 (no document language).
///
/// What these pin is a stage that does two things in one pass and in one order: Ghostscript writes
/// the file for Fast Web View while changing no colour, and then every check runs against what came
/// back rather than against what went in. The distinction matters — a validator that judges its own
/// input can pass a file the linearizer then breaks.
/// </summary>
public class BekiDigitalPrepTests
{
    [Fact]
    public void A_prepared_reading_copy_is_linearized_trim_sized_and_georgian()
    {
        var (pdf, reportJson) = BekiDigitalPrep.Prepare(
            BekiDigitalFixtures.ReadingCopy(), new BekiPrintPrepOptions());

        var header = Encoding.Latin1.GetString(pdf, 0, Math.Min(pdf.Length, 2048));
        Assert.Contains("/Linearized", header);

        var text = Encoding.Latin1.GetString(pdf);
        Assert.Contains("/Lang", text);
        Assert.Contains("ka-GE", text);

        // Nothing press-only survived into the download.
        Assert.DoesNotContain("/GTS_PDFX", text);
        Assert.DoesNotContain("/OutputIntents", text);

        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.ReadOnly);
        Assert.Equal(14, document.PageCount);
        Assert.Equal(BekiDigitalPrep.ExpectedPageCount, document.PageCount);

        // Page one is a single leaf; page two is a spread; every page states a CropBox.
        Assert.Equal(220d, Mm(document.Pages[0].MediaBox.Width), 1);
        Assert.Equal(200d, Mm(document.Pages[0].MediaBox.Height), 1);
        Assert.Equal(440d, Mm(document.Pages[1].MediaBox.Width), 1);
        Assert.Equal(220d, Mm(document.Pages[13].MediaBox.Width), 1);

        foreach (var page in document.Pages)
        {
            Assert.True(page.Elements.ContainsKey("/CropBox"), "a page shipped without a CropBox");
            Assert.Equal(page.MediaBox.Width, page.CropBox.Width, 1);
            Assert.False(page.Elements.ContainsKey("/BleedBox"));
            Assert.False(page.Elements.ContainsKey("/TrimBox"));
        }

        using var report = JsonDocument.Parse(reportJson);
        var root = report.RootElement;

        Assert.Equal("beki-digital-prep-v1", root.GetProperty("stage").GetString());
        Assert.Equal("DIGITAL_GEOMETRY", root.GetProperty("gate").GetString());
        Assert.Equal("PASS", root.GetProperty("verdict").GetString());
        Assert.True(root.GetProperty("linearization")
            .GetProperty("linearized_dictionary_present").GetBoolean());
        Assert.Contains(
            "LeaveColorUnchanged",
            root.GetProperty("linearization").GetProperty("conversion").GetString());
        Assert.Equal("ka-GE", root.GetProperty("colour").GetProperty("document_language").GetString());
        Assert.Equal(14, root.GetProperty("geometry").GetProperty("pages").GetArrayLength());

        // The rasters are recorded by colour space, and every one of them is a screen space.
        var rasters = root.GetProperty("colour").GetProperty("rasters");
        Assert.NotEqual(0, rasters.EnumerateObject().Count());
        Assert.DoesNotContain(
            rasters.EnumerateObject().Select(entry => entry.Name),
            space => space.Contains("CMYK") || space.Contains("(4)"));
    }

    /// <summary>
    /// The page order is the book: cover, endpaper, intro, eight spreads, credits, endpaper, back
    /// cover. Anything else is a different document, and the stage says so before it looks at boxes.
    /// </summary>
    [Fact]
    public void A_reading_copy_with_the_wrong_page_count_is_refused()
    {
        var failure = Assert.Throws<BekiLayoutException>(() =>
            BekiDigitalPrep.Prepare(
                BekiDigitalFixtures.ReadingCopy(pages: 12), new BekiPrintPrepOptions()));

        Assert.Equal("PRINT_PREFLIGHT_FAILED", failure.FailureCode);
        Assert.Contains("DIGITAL_GEOMETRY", failure.Message);
        Assert.Contains("12 page(s)", failure.Message);
        Assert.Contains($"{BookFormat.SpreadCount} spreads", failure.Message);
    }

    /// <summary>
    /// P0-08 exactly: 230×210 and 450×210 mm pages — the press geometry — in the file a parent
    /// downloads.
    /// </summary>
    [Fact]
    public void A_reading_copy_still_carrying_printer_bleed_is_refused()
    {
        var failure = Assert.Throws<BekiLayoutException>(() =>
            BekiDigitalPrep.Prepare(
                BekiDigitalFixtures.ReadingCopy(bleed: true), new BekiPrintPrepOptions()));

        Assert.Contains("DIGITAL_GEOMETRY", failure.Message);
        Assert.Contains("MediaBox", failure.Message);
        Assert.Contains("220x200 mm".Replace("x", "×"), failure.Message);
    }

    /// <summary>
    /// The audited file had no CropBox at all, which is why the bleed was visible: a viewer with no
    /// CropBox displays the MediaBox.
    /// </summary>
    [Fact]
    public void A_reading_copy_without_a_cropbox_is_refused()
    {
        var failure = Assert.Throws<BekiLayoutException>(() =>
            BekiDigitalPrep.Prepare(
                BekiDigitalFixtures.ReadingCopy(cropBox: false), new BekiPrintPrepOptions()));

        Assert.Contains("DIGITAL_GEOMETRY", failure.Message);
        Assert.Contains("no CropBox", failure.Message);
    }

    /// <summary>
    /// A press master handed to the digital stage. The geometry can be right and the file still be
    /// the wrong one: CMYK rasters belong on paper, and a download is an sRGB deliverable.
    /// </summary>
    [Fact]
    public void A_reading_copy_whose_rasters_are_cmyk_is_refused()
    {
        var cmyk = BekiDigitalFixtures.ConvertedToCmyk(BekiDigitalFixtures.ReadingCopy());

        var failure = Assert.Throws<BekiLayoutException>(() =>
            BekiDigitalPrep.Prepare(cmyk, new BekiPrintPrepOptions()));

        Assert.Contains("DIGITAL_GEOMETRY", failure.Message);
        Assert.Contains("screen colour space", failure.Message);
    }

    /// <summary>
    /// Press identification in a download: the output intent and the PDF/X claim have no meaning to
    /// a reader and every meaning to a printer, and the audit's §9 wants the two files told apart.
    ///
    /// Ghostscript's <c>pdfwrite</c> does not carry an output intent across a rewrite unless it is
    /// asked to produce PDF/X, so the linearization pass removes one as a side effect of doing its
    /// job — and the check that follows it proves that rather than assuming it. The check is kept
    /// as a check, not deleted as redundant: it is what would catch a future Ghostscript that
    /// preserves intents, or a <c>-dPDFX</c> that ever creeps into these arguments.
    /// </summary>
    [Fact]
    public void An_output_intent_on_the_input_does_not_survive_into_the_download()
    {
        var stamped = BekiDigitalFixtures.WithOutputIntent(BekiDigitalFixtures.ReadingCopy());
        Assert.Contains("/OutputIntents", Encoding.Latin1.GetString(stamped));

        var (pdf, reportJson) = BekiDigitalPrep.Prepare(stamped, new BekiPrintPrepOptions());

        var text = Encoding.Latin1.GetString(pdf);
        Assert.DoesNotContain("/OutputIntents", text);
        Assert.DoesNotContain("/GTS_PDFX", text);

        using var report = JsonDocument.Parse(reportJson);
        Assert.Equal("none", report.RootElement.GetProperty("printer_only_markers").GetString());
    }

    [Fact]
    public void A_missing_ghostscript_is_refused_by_name()
    {
        var failure = Assert.Throws<BekiLayoutException>(() =>
            BekiDigitalPrep.Prepare(
                BekiDigitalFixtures.ReadingCopy(),
                new BekiPrintPrepOptions
                {
                    GhostscriptPath = "/definitely/not/gs-" + Guid.NewGuid().ToString("N"),
                }));

        Assert.Equal("PRINT_PREFLIGHT_FAILED", failure.FailureCode);
        Assert.Contains("Ghostscript", failure.Message);
    }

    [Fact]
    public void A_document_that_is_not_a_pdf_is_refused()
    {
        var failure = Assert.Throws<BekiLayoutException>(() =>
            BekiDigitalPrep.Prepare(
                Encoding.ASCII.GetBytes("not a pdf at all"), new BekiPrintPrepOptions()));

        Assert.Contains("not a readable PDF", failure.Message);
    }

    private static double Mm(double pt) => pt / 72d * 25.4d;
}

/// <summary>
/// The reading copy as the composer will hand it over: fourteen pages at trim size, a CropBox on
/// every one, an sRGB raster on each, and no printer geometry anywhere. Built here rather than
/// borrowed from the composer because the composer's screen mode belongs to another agent's batch —
/// what this suite pins is the preparation stage's contract, stated in the shapes it accepts.
/// </summary>
internal static class BekiDigitalFixtures
{
    private static readonly Dictionary<string, byte[]> Cache = new(StringComparer.Ordinal);

    private static readonly object Gate = new();

    public static byte[] ReadingCopy(int pages = 14, bool bleed = false, bool cropBox = true) =>
        Cached($"reading-{pages}-{bleed}-{cropBox}", () =>
        {
            QuestPDF.Settings.License = LicenseType.Community;

            // The locked digital geometry, and the press geometry the audit found in its place.
            var leafWidth = bleed ? 230f : 220f;
            var spreadWidth = bleed ? 450f : 440f;
            var height = bleed ? 210f : 200f;

            var art = Raster(600, 300);

            var bytes = Document.Create(document =>
            {
                for (var number = 1; number <= pages; number++)
                {
                    var isLeaf = number == 1 || number == pages;
                    var page = number;

                    document.Page(descriptor =>
                    {
                        descriptor.Size(isLeaf ? leafWidth : spreadWidth, height, Unit.Millimetre);
                        descriptor.Margin(0);
                        descriptor.PageColor("#FFFFFF");

                        descriptor.Content().Layers(layers =>
                        {
                            layers.PrimaryLayer()
                                .Image(art).FitUnproportionally().UseOriginalImage();

                            layers.Layer().AlignCenter().AlignMiddle()
                                .Text($"გვერდი {page}").FontSize(24).FontColor("#281B3F");
                        });
                    });
                }
            }).GeneratePdf();

            return cropBox ? WithCropBoxes(bytes) : bytes;
        });

    /// <summary>
    /// What the composer's screen mode owes every page (D3): a CropBox equal to the MediaBox, so a
    /// viewer shows the trim and nothing else. Applied here with PDFsharp because QuestPDF writes
    /// no CropBox of its own.
    /// </summary>
    private static byte[] WithCropBoxes(byte[] pdf)
    {
        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Modify);

        foreach (var page in document.Pages)
        {
            page.CropBox = page.MediaBox;
        }

        using var buffer = new MemoryStream();
        document.Save(buffer);
        return buffer.ToArray();
    }

    /// <summary>A press master: the same pages, every raster converted to CMYK by Ghostscript.</summary>
    public static byte[] ConvertedToCmyk(byte[] pdf) => Cached("cmyk", () =>
    {
        var work = Path.Combine(Path.GetTempPath(), $"beki-digital-fixture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);

        try
        {
            var input = Path.Combine(work, "in.pdf");
            var output = Path.Combine(work, "out.pdf");
            File.WriteAllBytes(input, pdf);

            var start = new ProcessStartInfo
            {
                FileName = "gs",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };

            foreach (var argument in new[]
            {
                "-dBATCH", "-dNOPAUSE", "-dQUIET", "-dSAFER",
                "-sDEVICE=pdfwrite",
                "-sColorConversionStrategy=CMYK",
                "-dProcessColorModel=/DeviceCMYK",
                $"-sOutputFile={output}",
                "-f", input,
            })
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start)!;

            // Both pipes concurrently: reading one to its end while the other fills is how a
            // Ghostscript call hangs, and a hung fixture is a hung suite.
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            Task.WaitAll([stdout, stderr], TimeSpan.FromSeconds(30));

            return File.ReadAllBytes(output);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { /* temp cleanup only */ }
        }
    });

    /// <summary>A press output intent stamped onto a reading copy — the mix-up the gate refuses.</summary>
    public static byte[] WithOutputIntent(byte[] pdf) => Cached("output-intent", () =>
    {
        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Modify);

        var intent = new PdfDictionary(document);
        document.Internals.AddObject(intent);
        intent.Elements["/Type"] = new PdfName("/OutputIntent");
        intent.Elements["/S"] = new PdfName("/GTS_PDFX");
        intent.Elements["/OutputConditionIdentifier"] = new PdfString("FOGRA39");

        var intents = new PdfArray(document);
        intents.Elements.Add(intent.Reference!);
        document.Internals.Catalog.Elements["/OutputIntents"] = intents;

        using var buffer = new MemoryStream();
        document.Save(buffer);
        return buffer.ToArray();
    });

    private static byte[] Raster(int width, int height)
    {
        using var image = new Image<Rgb24>(width, height);

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = new Rgb24(
                        (byte)(200 - (x * 120 / Math.Max(1, width))),
                        (byte)(210 - (y * 90 / Math.Max(1, height))),
                        (byte)(((x / 16) + (y / 16)) % 2 == 0 ? 235 : 190));
                }
            }
        });

        using var buffer = new MemoryStream();
        image.Save(buffer, new PngEncoder());
        return buffer.ToArray();
    }

    private static byte[] Cached(string key, Func<byte[]> build)
    {
        lock (Gate)
        {
            if (!Cache.TryGetValue(key, out var bytes))
            {
                bytes = build();
                Cache[key] = bytes;
            }

            return bytes.ToArray();
        }
    }
}
