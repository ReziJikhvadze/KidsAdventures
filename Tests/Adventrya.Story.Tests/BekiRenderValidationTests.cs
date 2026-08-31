using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Services.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SixLabors.ImageSharp;

namespace Adventrya.Story.Tests;

/// <summary>
/// Render validation on the stored artifact — correction plan D15, acceptance gates
/// <c>RENDER_VALIDATION</c> and <c>QR</c>, and the audit's P2-6.
///
/// The gate is written about pixels for a reason. Everything upstream reasons about a document it
/// built itself and can therefore only confirm its own beliefs; the two defects this stage exists
/// for are invisible from there. A QR is one: the audit asks that it "scans from the rendered
/// stored artifact", not that the encoder was called with the right string, so the code is decoded
/// off the rendered page here. A renderer disagreement is the other, which is why the gate names
/// two renderers and amendment A8 refuses to let a missing one count as a pass.
///
/// Ghostscript is required and these tests use it. Poppler may or may not be installed on the
/// machine running them, so the tests that need it are skippable — but the *rule* about its absence
/// is not skippable, and is asserted against a deliberately wrong path so it runs everywhere.
/// </summary>
public class BekiRenderValidationTests
{
    private const string LockedDestination = "https://beki.ge";

    [Fact]
    public void Ghostscript_renders_every_page_and_the_contact_sheet_carries_them_all()
    {
        var result = BekiRenderValidation.Validate(
            BekiRenderFixtures.Pages(3), "press-interior", new BekiPrintPrepOptions());

        Assert.Equal(BekiRendererRun.Ok, result.Ghostscript.Status);
        Assert.Equal(3, result.Pages.Count);
        Assert.Equal([1, 2, 3], result.Pages.Select(page => page.Page));
        Assert.All(result.Pages, page => Assert.True(page.Png.Length > 0));

        Assert.NotNull(result.ContactSheetPng);
        Assert.NotNull(result.ContactSheetSha256);
        Assert.Equal(64, result.ContactSheetSha256!.Length);

        // A real PNG, wide enough to hold three thumbnails — this is the artifact amendment A2
        // makes the human reviewer sign against, so it has to be a picture and not a promise.
        var sheet = SixLabors.ImageSharp.Image.Identify(result.ContactSheetPng!);
        Assert.True(sheet.Width > 400);
        Assert.True(sheet.Height > 100);

        using var report = JsonDocument.Parse(result.ReportJson);
        Assert.Equal("beki-render-validation-v1", report.RootElement.GetProperty("stage").GetString());
        Assert.Equal(
            result.ContactSheetSha256,
            report.RootElement.GetProperty("contact_sheet").GetProperty("sha256").GetString());
        Assert.Equal(3, report.RootElement.GetProperty("pages").GetArrayLength());
    }

    /// <summary>
    /// The QR gate, satisfied the way it is written: exactly one code, decoded from the rendered
    /// credits page, resolving to the locked destination character for character.
    /// </summary>
    [Fact]
    public void The_credits_qr_scans_from_the_rendered_pixels_to_the_locked_destination()
    {
        var result = BekiRenderValidation.Validate(
            BekiRenderFixtures.WithQrCodes(LockedDestination),
            "press-interior",
            new BekiPrintPrepOptions(),
            new BekiRenderValidationRequest(QrPage: 1));

        Assert.Equal(BekiRendererRun.Ok, result.Qr.Status);
        Assert.Equal(1, result.Qr.Count);
        Assert.Equal([LockedDestination], result.Qr.Payloads);
        Assert.DoesNotContain(BekiRenderValidation.QrGate, result.FailedGates);
    }

    /// <summary>
    /// A code that draws perfectly and resolves somewhere else. Nothing short of a scan finds this.
    /// </summary>
    [Fact]
    public void A_qr_that_resolves_elsewhere_fails_the_qr_gate()
    {
        var result = BekiRenderValidation.Validate(
            BekiRenderFixtures.WithQrCodes("https://beki.example.com"),
            "press-interior",
            new BekiPrintPrepOptions(),
            new BekiRenderValidationRequest(QrPage: 1));

        Assert.Equal(BekiRendererRun.Failed, result.Qr.Status);
        Assert.Contains(BekiRenderValidation.QrGate, result.FailedGates);
        Assert.Equal(BekiRenderValidation.NotReleasable, result.Verdict);
        Assert.Contains("beki.example.com", result.Qr.Problem);
    }

    /// <summary>"Exactly one" is a count, and the deprecated second code is what it is guarding.</summary>
    [Fact]
    public void Two_codes_on_the_credits_page_fail_the_qr_gate()
    {
        var result = BekiRenderValidation.Validate(
            BekiRenderFixtures.WithQrCodes(LockedDestination, "https://beki.ge/rate"),
            "press-interior",
            new BekiPrintPrepOptions(),
            new BekiRenderValidationRequest(QrPage: 1));

        Assert.Equal(BekiRendererRun.Failed, result.Qr.Status);
        Assert.Equal(2, result.Qr.Count);
        Assert.Contains("2 scannable QR", result.Qr.Problem);
        Assert.Contains(BekiRenderValidation.QrGate, result.FailedGates);
    }

    /// <summary>
    /// Two codes that agree about where they point are still two codes.
    ///
    /// The payloads were deduplicated before they were counted, so the deprecated second QR beside
    /// the current one — the exact defect "exactly one vector QR appears on credits" is written
    /// against — counted as one and passed. Counting symbols is the check; counting distinct strings
    /// is a different check that happens to agree most of the time.
    /// </summary>
    [Fact]
    public void Two_identical_codes_on_the_credits_page_fail_the_qr_gate()
    {
        var result = BekiRenderValidation.Validate(
            BekiRenderFixtures.WithQrCodes(LockedDestination, LockedDestination),
            "press-interior",
            new BekiPrintPrepOptions(),
            new BekiRenderValidationRequest(QrPage: 1));

        Assert.Equal(BekiRendererRun.Failed, result.Qr.Status);
        Assert.Equal(2, result.Qr.Count);
        Assert.Contains("2 scannable QR", result.Qr.Problem);
        Assert.Contains(BekiRenderValidation.QrGate, result.FailedGates);
        Assert.Equal(BekiRenderValidation.NotReleasable, result.Verdict);
    }

    /// <summary>An artifact with no QR page named is not asked about one — a press cover, say.</summary>
    [Fact]
    public void An_artifact_with_no_qr_page_named_is_not_asked_about_one()
    {
        var result = BekiRenderValidation.Validate(
            BekiRenderFixtures.Pages(1), "press-cover", new BekiPrintPrepOptions());

        Assert.Equal(BekiRendererRun.Ok, result.Qr.Status);
        Assert.DoesNotContain(BekiRenderValidation.QrGate, result.FailedGates);
    }

    /// <summary>
    /// Amendment A8, which is the whole of the difference between this stage and a log line: a
    /// renderer that is not installed is recorded as <c>skipped</c>, and skipped withholds the
    /// package. Run against a path that certainly does not exist, so the rule is asserted on every
    /// machine whether or not Poppler is on this one.
    /// </summary>
    [Fact]
    public void An_absent_poppler_is_recorded_as_skipped_and_the_package_is_not_releasable()
    {
        var absent = "/definitely/not/poppler-" + Guid.NewGuid().ToString("N");

        var result = BekiRenderValidation.Validate(
            BekiRenderFixtures.WithQrCodes(LockedDestination),
            "press-interior",
            new BekiPrintPrepOptions
            {
                PopplerPdftoppmPath = absent,
                PopplerPdffontsPath = absent,
            },
            new BekiRenderValidationRequest(QrPage: 1));

        Assert.Equal(BekiRendererRun.Ok, result.Ghostscript.Status);
        Assert.Equal(BekiRendererRun.Skipped, result.PopplerPdftoppm.Status);
        Assert.Equal(BekiRendererRun.Skipped, result.PopplerPdffonts.Status);

        // The QR was fine. The package still does not ship.
        Assert.Equal(BekiRendererRun.Ok, result.Qr.Status);
        Assert.Contains(BekiRenderValidation.RenderValidationGate, result.FailedGates);
        Assert.Equal(BekiRenderValidation.NotReleasable, result.Verdict);
        Assert.False(result.IsReleasable);

        using var report = JsonDocument.Parse(result.ReportJson);
        Assert.Contains(
            report.RootElement.GetProperty("renderers").EnumerateArray(),
            run => run.GetProperty("status").GetString() == BekiRendererRun.Skipped);
    }

    /// <summary>
    /// A document that is not one. The stage returns evidence rather than throwing: the caller's
    /// job is to withhold the file, not to fail the book the parent bought.
    ///
    /// The assertion is deliberately about pages rather than exit codes. Ghostscript answers a
    /// torn-off file by interpreting what is left and exiting <c>0</c> having drawn nothing — the
    /// same forgiving behaviour the press stage keeps a page count for — so "clean exit, no pages"
    /// has to be a render failure or this gate would pass an empty book.
    /// </summary>
    [Fact]
    public void A_document_that_renders_to_no_pages_is_not_releasable_and_does_not_throw()
    {
        var result = BekiRenderValidation.Validate(
            System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\nthis is not a document\n"),
            "press-interior",
            new BekiPrintPrepOptions());

        Assert.Empty(result.Pages);
        Assert.Null(result.ContactSheetPng);
        Assert.Contains(BekiRenderValidation.RenderValidationGate, result.FailedGates);
        Assert.Equal(BekiRenderValidation.NotReleasable, result.Verdict);
        Assert.NotEmpty(result.Problems);
    }

    /// <summary>
    /// The releasable case, which needs the second renderer actually present. On a machine without
    /// Poppler this skips — but the rule it would have proved is already pinned above, from the
    /// other direction.
    /// </summary>
    [SkippableFact]
    public void With_both_renderers_installed_a_clean_artifact_is_releasable()
    {
        Skip.IfNot(PopplerInstalled(), "Poppler (pdftoppm/pdffonts) is not installed on this machine.");

        var result = BekiRenderValidation.Validate(
            BekiRenderFixtures.WithQrCodes(LockedDestination),
            "press-interior",
            new BekiPrintPrepOptions(),
            new BekiRenderValidationRequest(QrPage: 1));

        Assert.Equal(BekiRendererRun.Ok, result.Ghostscript.Status);
        Assert.Equal(BekiRendererRun.Ok, result.PopplerPdftoppm.Status);
        Assert.Equal(BekiRendererRun.Ok, result.PopplerPdffonts.Status);
        Assert.Empty(result.FailedGates);
        Assert.Equal(BekiRenderValidation.Releasable, result.Verdict);
        Assert.True(result.IsReleasable);

        // pdffonts lists what it found, and the log is the artifact P2-6 asks to be shipped.
        Assert.False(string.IsNullOrWhiteSpace(result.PopplerPdffonts.StandardOutput));
    }

    // ---------------------------------------------------------------------------------------
    // The font table, which was run and not read
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A face that is not embedded refuses the artifact, and is named.
    ///
    /// The exit code was checked and the table thrown away, so a document whose credits line prints
    /// in whatever the RIP has lying around came back RELEASABLE — and FONT_INTEGRITY, which reads
    /// embedding back off precisely that verdict, then passed the book. This drives the real
    /// validator with a stubbed pdffonts, because the failure is a property of the table and no
    /// document this suite can build produces one.
    /// </summary>
    [SkippableFact]
    public void A_font_the_stored_artifact_does_not_embed_refuses_the_render_validation()
    {
        Skip.If(OperatingSystem.IsWindows(), "the stubbed renderers are shell scripts.");

        using var poppler = new StubbedPoppler(
            """
            name                                 type              encoding         emb sub uni object ID
            ------------------------------------ ----------------- ---------------- --- --- --- ---------
            ABCDEF+NotoSansGeorgian              TrueType          WinAnsi          yes yes no        9  0
            OttiaRegular                         Type 1            Custom           no  no  no       12  0
            """);

        var result = BekiRenderValidation.Validate(
            BekiRenderFixtures.WithQrCodes(LockedDestination),
            "press-interior",
            poppler.Options,
            new BekiRenderValidationRequest(QrPage: 1));

        // Everything else about this artifact is clean: it rendered, both renderers exited zero,
        // and the QR scans. It is the table that refuses it.
        Assert.Equal(BekiRendererRun.Ok, result.Ghostscript.Status);
        Assert.Equal(BekiRendererRun.Ok, result.PopplerPdffonts.Status);
        Assert.Equal(BekiRendererRun.Ok, result.Qr.Status);

        Assert.Contains(BekiRenderValidation.RenderValidationGate, result.FailedGates);
        Assert.Equal(BekiRenderValidation.NotReleasable, result.Verdict);
        Assert.False(result.IsReleasable);
        Assert.Contains(result.Problems, problem => problem.Contains("OttiaRegular"));

        // Both faces are read out, and the raw table travels with them: the gate's evidence is the
        // printout, not a claim about it.
        Assert.Equal(2, result.Fonts.Rows.Count);
        Assert.True(result.Fonts.Rows[0].Embedded);
        Assert.False(result.Fonts.Rows[1].Embedded);
        Assert.Contains("OttiaRegular", result.Fonts.Table);

        using var report = JsonDocument.Parse(result.ReportJson);
        var fonts = report.RootElement.GetProperty("fonts");
        Assert.Equal(2, fonts.GetProperty("rows").GetArrayLength());
        Assert.Contains("emb", fonts.GetProperty("table").GetString());
    }

    /// <summary>
    /// And a table where every face is embedded says so and refuses nothing — the false positive
    /// this parse must not become.
    /// </summary>
    [SkippableFact]
    public void A_table_whose_faces_are_all_embedded_leaves_the_artifact_releasable()
    {
        Skip.If(OperatingSystem.IsWindows(), "the stubbed renderers are shell scripts.");

        using var poppler = new StubbedPoppler(
            """
            name                                 type              encoding         emb sub uni object ID
            ------------------------------------ ----------------- ---------------- --- --- --- ---------
            ABCDEF+NotoSansGeorgian              CID TrueType      Identity-H       yes yes yes       9  0
            GHIJKL+Ottia Regular                 TrueType          WinAnsi          yes yes no       12  0
            """);

        var result = BekiRenderValidation.Validate(
            BekiRenderFixtures.WithQrCodes(LockedDestination),
            "press-interior",
            poppler.Options,
            new BekiRenderValidationRequest(QrPage: 1));

        Assert.Empty(result.FailedGates);
        Assert.Equal(BekiRenderValidation.Releasable, result.Verdict);

        // The columns are cut from the dashed rule, so a face and a type that contain spaces are
        // read whole rather than tokenized into the wrong column.
        Assert.Equal("CID TrueType", result.Fonts.Rows[0].Type);
        Assert.Equal("GHIJKL+Ottia Regular", result.Fonts.Rows[1].Name);
        Assert.All(result.Fonts.Rows, row => Assert.True(row.Embedded));
    }

    /// <summary>
    /// A clean exit that printed something no parser can read is not a pass. Absence of an answer
    /// withholds everywhere else in this campaign, and a table nobody could read is an absence.
    /// </summary>
    [Fact]
    public void A_table_that_cannot_be_read_is_not_evidence_of_embedding()
    {
        var scan = BekiRenderValidation.ScanFonts(new BekiRendererRun(
            "pdffonts", BekiRendererRun.Ok, "pdffonts in.pdf", 0,
            "Syntax Warning: could not parse the font dictionary", string.Empty));

        Assert.Equal("unreadable", scan.Status);
        Assert.NotEmpty(scan.Problems);

        // A document with no fonts at all — a press cover is one whole image — is a genuine zero
        // and says nothing wrong.
        var empty = BekiRenderValidation.ScanFonts(new BekiRendererRun(
            "pdffonts", BekiRendererRun.Ok, "pdffonts in.pdf", 0, string.Empty, string.Empty));

        Assert.Equal(BekiRendererRun.Ok, empty.Status);
        Assert.Empty(empty.Problems);
        Assert.Empty(empty.Rows);
    }

    /// <summary>
    /// Poppler, replaced by two shell scripts: <c>pdftoppm</c> that exits clean, and
    /// <c>pdffonts</c> that prints a table this test chose. The point is to drive the real
    /// validator — its gate arithmetic, its report — against a font table no document the suite can
    /// build would produce.
    /// </summary>
    private sealed class StubbedPoppler : IDisposable
    {
        private readonly string _directory;

        public StubbedPoppler(string table)
        {
            _directory = Path.Combine(
                Path.GetTempPath(), $"beki-poppler-stub-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_directory);

            var tablePath = Path.Combine(_directory, "fonts.txt");
            File.WriteAllText(tablePath, table + Environment.NewLine);

            Options = new BekiPrintPrepOptions
            {
                PopplerPdftoppmPath = Script("pdftoppm", "exit 0"),
                PopplerPdffontsPath = Script("pdffonts", $"cat '{tablePath}'"),
            };
        }

        public BekiPrintPrepOptions Options { get; }

        private string Script(string name, string body)
        {
            var path = Path.Combine(_directory, name);
            File.WriteAllText(path, $"#!/bin/sh\n{body}\n");

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(_directory, recursive: true); } catch { /* temp only */ }
        }
    }

    private static bool PopplerInstalled()
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        return paths.Any(directory =>
            File.Exists(Path.Combine(directory, "pdftoppm")))
            && paths.Any(directory => File.Exists(Path.Combine(directory, "pdffonts")));
    }
}

/// <summary>
/// Small documents built for the renderers to disagree about — page count, and vector QR codes
/// drawn exactly as the credits page draws its own (QRCoder SVG, not a raster), because the gate is
/// about a code that scans and a raster QR would be testing a different thing.
/// </summary>
internal static class BekiRenderFixtures
{
    private static readonly Dictionary<string, byte[]> Cache = new(StringComparer.Ordinal);

    private static readonly object Gate = new();

    public static byte[] Pages(int count) => Cached($"pages-{count}", () =>
    {
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(document =>
        {
            for (var number = 1; number <= count; number++)
            {
                var page = number;
                document.Page(descriptor =>
                {
                    descriptor.Size(120f, 90f, Unit.Millimetre);
                    descriptor.Margin(6, Unit.Millimetre);
                    descriptor.PageColor("#FFFFFF");
                    descriptor.Content().AlignCenter().AlignMiddle()
                        .Text($"page {page}").FontSize(28).FontColor("#281B3F");
                });
            }
        }).GeneratePdf();
    });

    public static byte[] WithQrCodes(params string[] urls) => Cached(
        "qr-" + string.Join("|", urls),
        () =>
        {
            QuestPDF.Settings.License = LicenseType.Community;

            return Document.Create(document =>
            {
                document.Page(descriptor =>
                {
                    descriptor.Size(160f, 100f, Unit.Millimetre);
                    descriptor.Margin(5, Unit.Millimetre);
                    descriptor.PageColor("#FFFFFF");

                    descriptor.Content().Row(row =>
                    {
                        foreach (var url in urls)
                        {
                            row.RelativeItem()
                                .Padding(4, Unit.Millimetre)
                                .Background("#FFFFFF")
                                .Svg(QrSvg(url))
                                .FitArea();
                        }
                    });
                });
            }).GeneratePdf();
        });

    /// <summary>The credits page's own generator, at its own settings.</summary>
    private static string QrSvg(string url)
    {
        using var generator = new QRCoder.QRCodeGenerator();
        using var data = generator.CreateQrCode(url.Trim(), QRCoder.QRCodeGenerator.ECCLevel.Q);
        return new QRCoder.SvgQRCode(data).GetGraphic(
            pixelsPerModule: 16,
            darkColorHex: "#000000",
            lightColorHex: "#FFFFFF",
            drawQuietZones: true);
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
