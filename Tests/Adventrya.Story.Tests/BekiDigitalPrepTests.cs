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

    // ==============================================================================================
    // The empty colour space — the customer PDF that Chrome rendered blank
    // ==============================================================================================

    /// <summary>
    /// Every ICC profile in a prepared reading copy is a profile a strict viewer can read.
    ///
    /// The defect this pins shipped in pack 597344af. Skia tags every PNG it embeds with an ICC
    /// **v4.3** sRGB, Ghostscript's pdfwrite will not write a profile newer than v4.2, and its way
    /// of not writing one is to emit <c>&lt;&lt;/N 3/Length 0&gt;&gt;</c> — a colour space with no
    /// profile in it. Poppler renders such a page anyway, with a complaint, which is why every
    /// render gate in this pipeline passed the broken book; Chrome drops every image in that colour
    /// space, and the parent opened a book of flat coloured pages.
    ///
    /// The premise is asserted first and on purpose. If a future Skia stopped tagging its rasters
    /// v4.3 this test would go on passing while proving nothing, so the fixture is made to state
    /// that it still carries the profile that provokes the bug.
    /// </summary>
    [Fact]
    public void Every_icc_profile_in_a_prepared_reading_copy_is_readable()
    {
        var composed = BekiDigitalFixtures.ReadingCopy();

        Assert.True(IccVersions(composed).Any(version => version.StartsWith("4.3", StringComparison.Ordinal)),
            "the fixture no longer embeds an ICC v4.3 profile, so it no longer provokes the "
            + "Ghostscript defect this test exists for. Find a raster source that does, or retire it.");

        var (pdf, reportJson) = BekiDigitalPrep.Prepare(composed, new BekiPrintPrepOptions());

        Assert.Empty(BekiDigitalPrep.IccProfileProblems(pdf));

        // Read straight off the bytes as well, because that is the shape the shipped defect had:
        // an object dictionary saying /N 3 with nothing behind it.
        Assert.DoesNotContain("/N 3/Length 0", Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);

        using var report = JsonDocument.Parse(reportJson);
        var colour = report.RootElement.GetProperty("colour");

        var profiles = colour.GetProperty("icc_profiles");
        Assert.NotEqual(0, profiles.GetArrayLength());
        Assert.All(
            profiles.EnumerateArray().ToList(),
            profile => Assert.True(profile.GetProperty("bytes").GetInt32() > 0));

        // And the report says what had to be restamped on the way in rather than leaving an
        // operator to infer it.
        Assert.Contains(
            "restamped",
            colour.GetProperty("icc_restamped_for_pdfwrite").ToString(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// And without the restamp, the gate refuses — which is the hole that let pack 597344af ship.
    ///
    /// The stage's report said PASS on a file with an empty colour space in it, because nothing had
    /// ever looked inside one. This runs the same book through the same Ghostscript with the fix
    /// switched off, reproduces the empty stream exactly, and requires a refusal.
    /// </summary>
    [Fact]
    public void A_reading_copy_whose_icc_profile_came_back_empty_is_refused()
    {
        var failure = Assert.Throws<BekiLayoutException>(() =>
            BekiDigitalPrep.Prepare(
                BekiDigitalFixtures.ReadingCopy(),
                new BekiPrintPrepOptions(),
                baseDirectory: null,
                harmonizeIccProfiles: false));

        Assert.Equal("PRINT_PREFLIGHT_FAILED", failure.FailureCode);
        Assert.Contains("DIGITAL_GEOMETRY", failure.Message);
        Assert.Contains("empty (0 bytes)", failure.Message);
        Assert.Contains("flat colour", failure.Message);
    }

    /// <summary>
    /// A profile stream that is not a profile is the same defect from a viewer's point of view, and
    /// is read the same way.
    ///
    /// Built rather than produced: Ghostscript drops a malformed profile instead of passing the
    /// damage on, so there is no way to make one of these by running the stage. Three shapes — no
    /// bytes at all, too few bytes to be a header, and enough bytes with no ICC signature in them.
    /// </summary>
    [Theory]
    [InlineData(0, "empty (0 bytes)")]
    [InlineData(40, "shorter than an ICC header")]
    [InlineData(600, "is not an ICC profile")]
    public void An_icc_stream_that_is_not_a_readable_profile_is_named_as_a_problem(
        int bytes, string expected)
    {
        var doctored = BekiDigitalFixtures.WithDoctoredIccProfile(bytes);

        var problem = Assert.Single(BekiDigitalPrep.IccProfileProblems(doctored));
        Assert.Contains(expected, problem, StringComparison.Ordinal);
    }

    /// <summary>
    /// Poppler renders the prepared book with nothing to say about its colour spaces.
    ///
    /// The one check that would have caught the shipped book from outside. Poppler's renderer is
    /// forgiving — it prints "read ICCBased color space profile error" and draws the page — so the
    /// contact sheets and render gates all looked right. Its stderr did not, and nobody was reading
    /// it. This does.
    /// </summary>
    [Fact]
    public void Poppler_renders_the_prepared_copy_with_a_clean_stderr()
    {
        var (pdf, _) = BekiDigitalPrep.Prepare(
            BekiDigitalFixtures.ReadingCopy(), new BekiPrintPrepOptions());

        Poppler.AssertRendersCleanly(pdf);
    }

    private static IReadOnlyList<string> IccVersions(byte[] pdf)
    {
        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);

        var versions = new List<string>();

        foreach (var candidate in document.Internals.GetAllObjects())
        {
            if (candidate is not PdfDictionary dictionary
                || dictionary.Stream is null
                || !dictionary.Elements.ContainsKey("/N"))
            {
                continue;
            }

            var bytes = dictionary.Stream.UnfilteredValue;
            if (bytes.Length < 132 || Encoding.Latin1.GetString(bytes, 36, 4) != "acsp") continue;

            versions.Add($"{bytes[8]}.{bytes[9] >> 4}.{bytes[9] & 0x0F}.{bytes[10]}");
        }

        return versions;
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

    /// <summary>
    /// A document carrying one ICCBased colour space whose profile stream is <paramref name="bytes"/>
    /// bytes of nothing in particular.
    ///
    /// Deliberately not a reading copy: what the ICC gate reads is the object graph, and building
    /// the smallest document that has the defect in it says what the defect is without fourteen
    /// pages of unrelated correctness around it. The image is wired into the page's resources so
    /// that saving cannot prune it as unreachable.
    /// </summary>
    public static byte[] WithDoctoredIccProfile(int bytes)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();

        var profile = new PdfDictionary(document);
        document.Internals.AddObject(profile);
        profile.Elements.SetInteger("/N", 3);
        profile.CreateStream(new byte[bytes]);

        var space = new PdfArray(document);
        space.Elements.Add(new PdfName("/ICCBased"));
        space.Elements.Add(profile.Reference!);

        var image = new PdfDictionary(document);
        document.Internals.AddObject(image);
        image.Elements.SetName("/Type", "/XObject");
        image.Elements.SetName("/Subtype", "/Image");
        image.Elements.SetInteger("/Width", 1);
        image.Elements.SetInteger("/Height", 1);
        image.Elements.SetInteger("/BitsPerComponent", 8);
        image.Elements["/ColorSpace"] = space;
        image.CreateStream([0x80, 0x80, 0x80]);

        var xobjects = new PdfDictionary(document);
        xobjects.Elements["/Im0"] = image.Reference!;

        var resources = new PdfDictionary(document);
        resources.Elements["/XObject"] = xobjects;
        page.Elements["/Resources"] = resources;

        using var buffer = new MemoryStream();
        document.Save(buffer);
        return buffer.ToArray();
    }

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

/// <summary>
/// A second renderer's opinion of a finished book, and — the point of it — everything that renderer
/// muttered while forming it.
///
/// Poppler is where the ICC defect was visible all along. It renders a page whose colour space has
/// no profile in it, prints "read ICCBased color space profile error" on stderr, and returns zero;
/// so a pipeline that checks exit codes and looks at the PNGs sees a healthy book, and a parent
/// opening the same file in Chrome sees flat colour. Reading the complaint is the whole technique.
/// </summary>
internal static class Poppler
{
    /// <summary>
    /// Renders every page and requires that nothing was said about colour profiles or syntax while
    /// it happened.
    /// </summary>
    public static void AssertRendersCleanly(byte[] pdf)
    {
        var work = Path.Combine(Path.GetTempPath(), $"beki-poppler-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);

        try
        {
            var input = Path.Combine(work, "book.pdf");
            File.WriteAllBytes(input, pdf);

            // Small enough to be quick and large enough that every image is actually decoded, which
            // is when a colour space gets read and a broken one gets complained about.
            var render = Run("pdftoppm", ["-r", "18", "-png", input, Path.Combine(work, "page")]);
            var info = Run("pdfinfo", [input]);

            Assert.True(
                render.Exit == 0,
                $"pdftoppm exited {render.Exit}: {render.Stderr}");

            foreach (var (tool, stderr) in new[] { ("pdftoppm", render.Stderr), ("pdfinfo", info.Stderr) })
            {
                Assert.False(
                    stderr.Contains("profile", StringComparison.OrdinalIgnoreCase),
                    $"{tool} complained about a colour profile: {stderr.Trim()}");

                Assert.False(
                    stderr.Contains("Syntax Error", StringComparison.OrdinalIgnoreCase),
                    $"{tool} reported a syntax error: {stderr.Trim()}");
            }

            Assert.NotEmpty(Directory.GetFiles(work, "page-*.png"));
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { /* temp only */ }
        }
    }

    private static (int Exit, string Stdout, string Stderr) Run(string tool, string[] arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = tool,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"'{tool}' did not start.");

        // Both pipes at once, for the reason every other process call in this repo does it: draining
        // one to its end while the other fills is how a render hangs a test run.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit(180_000);
        Task.WaitAll([stdout, stderr], TimeSpan.FromSeconds(30));

        return (process.ExitCode, stdout.Result, stderr.Result);
    }
}
