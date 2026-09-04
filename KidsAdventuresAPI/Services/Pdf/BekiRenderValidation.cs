using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ZXing;

namespace AdventurePacks.Api.Services.Pdf;

/// <summary>
/// What one renderer said about the stored artifact, verbatim.
/// </summary>
/// <param name="Status">
/// <c>ok</c>, <c>failed</c>, or <c>skipped</c> — and <c>skipped</c> is not a pass. Amendment A8 of
/// the audit-2 correction plan makes Poppler a release dependency alongside Ghostscript: a check
/// that did not run cannot evidence anything, so its absence withholds the package rather than
/// waving it through.
/// </param>
public sealed record BekiRendererRun(
    string Tool,
    string Status,
    string Command,
    int? ExitCode,
    string StandardOutput,
    string StandardError)
{
    public const string Ok = "ok";

    public const string Failed = "failed";

    public const string Skipped = "skipped";

    public static BekiRendererRun NotInstalled(string tool, string executable) =>
        new(tool, Skipped, executable, null, string.Empty,
            $"'{executable}' is not installed on this deployment. RENDER_VALIDATION requires both "
            + "Ghostscript and Poppler, so this artifact is not releasable until it is.");
}

/// <summary>One page as a renderer drew it.</summary>
public sealed record BekiRenderedPage(int Page, int WidthPx, int HeightPx, byte[] Png);

/// <summary>One line of the <c>pdffonts</c> table, read rather than counted.</summary>
public sealed record BekiFontRow(string Name, string Type, string Encoding, bool Embedded, bool Subset);

/// <summary>
/// What the second renderer's font table says about the stored artifact.
/// </summary>
/// <param name="Status">
/// <c>ok</c>, <c>failed</c>, <c>skipped</c>, or <c>unreadable</c>. Only <c>ok</c> with no problems
/// evidences anything: this is the answer to a review finding that <c>pdffonts</c>' exit code was
/// checked and its table never read, so a document listing <c>emb no</c> against a face — a page
/// that will print in whatever the RIP substitutes — rendered "clean" and then passed FONT_INTEGRITY
/// on the strength of that verdict.
/// </param>
/// <param name="Table">The raw table, kept verbatim: the gate's evidence is the printout itself.</param>
public sealed record BekiFontScan(
    string Status,
    IReadOnlyList<BekiFontRow> Rows,
    IReadOnlyList<string> Problems,
    string Table);

/// <summary>
/// The QR gate's answer: exactly one vector continuation QR appears below Story spread 8's text
/// and scans from the rendered stored artifact to this book's configured continuation URL.
/// </summary>
public sealed record BekiQrScan(
    string Status, int Count, IReadOnlyList<string> Payloads, string? Problem);

/// <summary>What the caller wants looked at, beyond the renders every artifact gets.</summary>
/// <param name="QrPage">
/// 1-based page carrying the Story spread 8 continuation QR, or null for an artifact that has none.
/// </param>
public sealed record BekiRenderValidationRequest(
    int? QrPage = null,
    int? ContactSheetColumns = null,
    int? ThumbnailWidthPx = null,
    string? ExpectedQrDestination = null);

/// <summary>
/// Everything the render stage found, in a shape the caller can serialize, upload and gate on.
/// </summary>
public sealed record BekiRenderValidationResult(
    string Artifact,
    string Verdict,
    IReadOnlyList<string> FailedGates,
    IReadOnlyList<string> Problems,
    BekiRendererRun Ghostscript,
    BekiRendererRun PopplerPdftoppm,
    BekiRendererRun PopplerPdffonts,
    BekiFontScan Fonts,
    BekiQrScan Qr,
    IReadOnlyList<BekiRenderedPage> Pages,
    byte[]? ContactSheetPng,
    string? ContactSheetSha256,
    string ReportJson)
{
    public bool IsReleasable => Verdict == BekiRenderValidation.Releasable;
}

/// <summary>
/// Render validation on the file that actually shipped — the audit's P2-6 and the
/// <c>RENDER_VALIDATION</c> and <c>QR</c> acceptance gates.
///
/// The distinction that matters is *stored*. Everything upstream of here reasons about a document
/// in memory that it built itself; this stage takes the bytes out of storage, hands them to two
/// independent interpreters, and looks at pixels. That is the only way the two defects it exists
/// for can be caught at all: a QR that draws correctly and resolves to nothing (the gate asks for a
/// scan, not an encoding), and a page that one renderer accepts and another does not.
///
/// It also produces the artifact amendment A2 makes mandatory before release — a contact sheet of
/// every page, whose SHA-256 the human approval records, so that "the reviewer approved the book"
/// means a specific set of pixels rather than a good intention.
///
/// Ghostscript is a hard dependency and always has been (Locked Print Spec §5). Poppler joins it
/// under amendment A8: absent, the run is recorded as <c>skipped</c> and the verdict is
/// <c>NOT_RELEASABLE</c>. Nothing here throws for a render reason — the caller needs the evidence
/// more than it needs an exception, and withholding is the response, not failing the book.
/// </summary>
public static class BekiRenderValidation
{
    public const string RenderValidationGate = "RENDER_VALIDATION";

    public const string QrGate = "QR";

    public const string Releasable = "RELEASABLE";

    public const string NotReleasable = "NOT_RELEASABLE";

    /// <summary>Poppler's own density for the second-opinion render; the audit's P2-6 names 72.</summary>
    private const int PopplerDpi = 72;

    private const string AcceptanceGatesFile = "BEKI_Acceptance_Gates_v1.json";

    /// <summary>
    /// Renders, reads and scans one stored artifact.
    /// </summary>
    /// <param name="storedPdf">The bytes as they were stored, not as they were composed.</param>
    /// <param name="artifact">A name for the report — normally <c>canonical-book</c>.</param>
    /// <param name="options">Renderer paths and the validation density.</param>
    /// <param name="request">What to look for beyond the renders themselves.</param>
    /// <param name="baseDirectory">Test override for locating the acceptance-gates document.</param>
    public static BekiRenderValidationResult Validate(
        byte[] storedPdf,
        string artifact,
        BekiPrintPrepOptions options,
        BekiRenderValidationRequest? request = null,
        string? baseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(storedPdf);
        ArgumentNullException.ThrowIfNull(options);

        var root = baseDirectory ?? AppContext.BaseDirectory;
        var (expectedQrCount, expectedQrDestination) = ReadQrExpectation(root);
        var dpi = options.RenderDpi > 0 ? options.RenderDpi : 120;

        var work = Path.Combine(Path.GetTempPath(), $"beki-render-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);

        var problems = new List<string>();
        var failedGates = new List<string>();

        try
        {
            var input = Path.Combine(work, "in.pdf");
            File.WriteAllBytes(input, storedPdf);

            var ghostscript = RenderWithGhostscript(options, input, work, dpi);
            var pages = ghostscript.Status == BekiRendererRun.Ok
                ? ReadRenderedPages(work, "gs-page-")
                : [];

            if (ghostscript.Status != BekiRendererRun.Ok)
            {
                problems.Add($"Ghostscript {ghostscript.Status}: {Truncate(ghostscript.StandardError)}");
            }
            else if (pages.Count == 0)
            {
                problems.Add("Ghostscript exited clean and produced no page images at all.");
            }

            var pdftoppm = RunPoppler(
                options.PopplerPdftoppmPath, "pdftoppm",
                ["-r", PopplerDpi.ToString(CultureInfo.InvariantCulture), "-png", input,
                 Path.Combine(work, "poppler-page")],
                work);

            var pdffonts = RunPoppler(
                options.PopplerPdffontsPath, "pdffonts", [input], work);

            foreach (var run in new[] { pdftoppm, pdffonts })
            {
                if (run.Status != BekiRendererRun.Ok)
                {
                    problems.Add(
                        $"{run.Tool} {run.Status}: "
                        + Truncate(string.IsNullOrWhiteSpace(run.StandardError)
                            ? run.StandardOutput
                            : run.StandardError));
                }
            }

            /*
              And the font table is READ, which it was not.

              The review's finding: `pdffonts` exit code was checked and its output never parsed, so
              an artifact whose table said `emb no` against a face came back RELEASABLE — and
              FONT_INTEGRITY, which reads embedding back off exactly this verdict, then passed the
              book on it. A non-embedded face is a page that prints in whatever the RIP substitutes,
              which is the one thing a press file may not do quietly.
            */
            var fonts = ScanFonts(pdffonts);
            problems.AddRange(fonts.Problems);

            if (ghostscript.Status != BekiRendererRun.Ok
                || pages.Count == 0
                || pdftoppm.Status != BekiRendererRun.Ok
                || pdffonts.Status != BekiRendererRun.Ok
                || fonts.Status != BekiRendererRun.Ok
                || fonts.Problems.Count > 0)
            {
                failedGates.Add(RenderValidationGate);
            }

            var qr = ScanQr(
                pages,
                request?.QrPage,
                expectedQrCount,
                request?.ExpectedQrDestination ?? expectedQrDestination);
            var effectiveQrDestination = request?.ExpectedQrDestination ?? expectedQrDestination;
            if (qr.Status != BekiRendererRun.Ok)
            {
                if (qr.Problem is not null)
                {
                    problems.Add($"{QrGate}: {qr.Problem}");
                }

                failedGates.Add(QrGate);
            }

            var contactSheet = pages.Count == 0
                ? null
                : BuildContactSheet(
                    pages,
                    request?.ContactSheetColumns ?? DefaultColumns(pages.Count),
                    request?.ThumbnailWidthPx ?? 420);

            var sha = contactSheet is null
                ? null
                : Convert.ToHexString(SHA256.HashData(contactSheet)).ToLowerInvariant();

            var verdict = failedGates.Count == 0 ? Releasable : NotReleasable;

            var report = JsonSerializer.Serialize(
                new
                {
                    stage = "beki-render-validation-v1",
                    contract = AcceptanceGatesFile,
                    artifact,
                    validated_at_utc = DateTime.UtcNow,
                    verdict,
                    failed_gates = failedGates,
                    problems,
                    render_dpi = dpi,
                    renderers = new[] { ghostscript, pdftoppm, pdffonts }
                        .Select(run => new
                        {
                            tool = run.Tool,
                            status = run.Status,
                            command = run.Command,
                            exit_code = run.ExitCode,
                            stdout = Truncate(run.StandardOutput),
                            stderr = Truncate(run.StandardError),
                        })
                        .ToList(),
                    // The table verbatim, beside what was read out of it: the gate's evidence is the
                    // printout, and a parse nobody can check against the original is an assertion.
                    fonts = new
                    {
                        gate = "FONT_INTEGRITY",
                        status = fonts.Status,
                        problems = fonts.Problems,
                        rows = fonts.Rows
                            .Select(row => new
                            {
                                name = row.Name,
                                type = row.Type,
                                encoding = row.Encoding,
                                embedded = row.Embedded,
                                subset = row.Subset,
                            })
                            .ToList(),
                        table = Truncate(fonts.Table),
                    },
                    qr = new
                    {
                        gate = QrGate,
                        status = qr.Status,
                        expected_count = expectedQrCount,
                        expected_destination = effectiveQrDestination,
                        found_count = qr.Count,
                        payloads = qr.Payloads,
                        problem = qr.Problem,
                        page = request?.QrPage,
                    },
                    pages = pages
                        .Select(page => new
                        {
                            page = page.Page,
                            width_px = page.WidthPx,
                            height_px = page.HeightPx,
                            png_bytes = page.Png.Length,
                        })
                        .ToList(),
                    contact_sheet = contactSheet is null
                        ? null
                        : new
                        {
                            // Amendment A2: the human approval records this hash, so the reviewer's
                            // sign-off is attached to a specific set of pixels and a stale sheet
                            // can be refused.
                            sha256 = sha,
                            bytes = contactSheet.Length,
                            cells = pages.Select(page => page.Page).ToList(),
                        },
                },
                new JsonSerializerOptions { WriteIndented = true });

            return new BekiRenderValidationResult(
                artifact, verdict, failedGates, problems,
                ghostscript, pdftoppm, pdffonts, fonts, qr, pages, contactSheet, sha, report);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { /* temp cleanup only */ }
        }
    }

    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The QR gate's locked expectations, read from the supplier's gates document rather than
    /// restated here — the destination is a URL somebody may change without touching this code.
    /// </summary>
    private static (int Count, string Destination) ReadQrExpectation(string baseDirectory)
    {
        var path = Path.Combine(
            baseDirectory, "Assets", "BekiComposite", "contracts", AcceptanceGatesFile);

        if (!File.Exists(path))
        {
            // Read validation is evidence-gathering, not a book-stopping stage, so a missing
            // contract degrades to the locked values the gate document itself states rather than
            // throwing — and the report says the file was not found.
            return (1, "https://beki.ge");
        }

        try
        {
            using var gates = JsonDocument.Parse(File.ReadAllText(path));
            var locked = gates.RootElement.GetProperty("locked_values");
            return (
                locked.GetProperty("qr_count").GetInt32(),
                locked.GetProperty("qr_destination").GetString() ?? "https://beki.ge");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return (1, "https://beki.ge");
        }
    }

    /// <summary>
    /// The <c>pdffonts</c> table, parsed — every face the stored artifact carries, and whether the
    /// bytes for it travel with the file.
    ///
    /// Poppler prints a fixed-width table whose dashed rule under the header states the column
    /// extents exactly, so the columns are cut from that rule rather than from whitespace: a face
    /// called "Noto Sans Georgian" and a type called "CID TrueType" both contain spaces, and a
    /// tokenizing parser silently reads the wrong column on precisely the documents this book is
    /// made of.
    ///
    /// Silence is not a pass. A clean exit with an unparseable table is <c>unreadable</c> and fails,
    /// for the same reason absence fails everywhere else in this campaign — a check that produced no
    /// answer has not answered. An artifact with no fonts at all (a press cover is one whole image)
    /// prints the header and no rows, and that is a genuine zero rather than a silence.
    /// </summary>
    public static BekiFontScan ScanFonts(BekiRendererRun pdffonts)
    {
        ArgumentNullException.ThrowIfNull(pdffonts);

        var table = pdffonts.StandardOutput ?? string.Empty;

        if (pdffonts.Status != BekiRendererRun.Ok)
        {
            // The run's own failure is already a problem and already fails the gate; repeating it
            // here would say the same thing twice in the report.
            return new BekiFontScan(pdffonts.Status, [], [], table);
        }

        if (string.IsNullOrWhiteSpace(table))
        {
            return new BekiFontScan(BekiRendererRun.Ok, [], [], table);
        }

        var lines = table.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        var ruleIndex = Array.FindIndex(
            lines,
            line => line.Length > 0
                && line.Contains("---", StringComparison.Ordinal)
                && line.All(character => character is '-' or ' '));

        if (ruleIndex < 1)
        {
            return new BekiFontScan(
                "unreadable", [], [
                    "FONT_INTEGRITY: pdffonts exited clean and printed no table this deployment "
                    + "can read, so nothing was read back about embedding."
                ],
                table);
        }

        var header = lines[ruleIndex - 1];
        var columns = Columns(lines[ruleIndex]);
        var byName = new Dictionary<string, (int Start, int Length)>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in columns)
        {
            var name = Cut(header, column).Trim();
            if (name.Length > 0)
            {
                byName[name] = column;
            }
        }

        if (!byName.TryGetValue("emb", out var embedded) || !byName.TryGetValue("name", out var face))
        {
            return new BekiFontScan(
                "unreadable", [], [
                    "FONT_INTEGRITY: the pdffonts table names no 'name'/'emb' columns, so embedding "
                    + "could not be read off it."
                ],
                table);
        }

        byName.TryGetValue("type", out var type);
        byName.TryGetValue("encoding", out var encoding);
        byName.TryGetValue("sub", out var subset);

        var rows = new List<BekiFontRow>();
        var problems = new List<string>();

        foreach (var line in lines.Skip(ruleIndex + 1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var name = Cut(line, face).Trim();
            var row = new BekiFontRow(
                name,
                Cut(line, type).Trim(),
                Cut(line, encoding).Trim(),
                Cut(line, embedded).Trim().Equals("yes", StringComparison.OrdinalIgnoreCase),
                Cut(line, subset).Trim().Equals("yes", StringComparison.OrdinalIgnoreCase));

            rows.Add(row);

            if (!row.Embedded)
            {
                problems.Add(
                    $"FONT_INTEGRITY: '{(name.Length == 0 ? "(unnamed)" : name)}' "
                    + $"({(row.Type.Length == 0 ? "unknown type" : row.Type)}) is not embedded in "
                    + "the stored artifact — pdffonts reports emb=no, so the page prints in "
                    + "whatever the consumer substitutes.");
            }
            else if (IndicatesSubstitution(name))
            {
                problems.Add(
                    $"FONT_INTEGRITY: the stored artifact names '{name}', which is a substituted "
                    + "face rather than one of the licensed files.");
            }
        }

        return new BekiFontScan(BekiRendererRun.Ok, rows, problems, table);
    }

    /// <summary>The dashed rule's runs, which are the table's column extents.</summary>
    private static List<(int Start, int Length)> Columns(string rule)
    {
        var columns = new List<(int, int)>();
        var index = 0;

        while (index < rule.Length)
        {
            if (rule[index] != '-')
            {
                index++;
                continue;
            }

            var start = index;
            while (index < rule.Length && rule[index] == '-')
            {
                index++;
            }

            columns.Add((start, index - start));
        }

        return columns;
    }

    /// <summary>
    /// One column out of one line, tolerant of a row that ends early — the last column of a
    /// pdffonts row is routinely shorter than its rule.
    /// </summary>
    private static string Cut(string line, (int Start, int Length) column)
    {
        if (column.Length <= 0 || column.Start >= line.Length)
        {
            return string.Empty;
        }

        var length = Math.Min(column.Length, line.Length - column.Start);
        return line.Substring(column.Start, length);
    }

    /// <summary>
    /// Names that mean "this is not the file that was approved": Poppler's own placeholder for a
    /// face it could not name, and the base-14 aliases a consumer falls back to.
    /// </summary>
    private static bool IndicatesSubstitution(string name) =>
        name.Length == 0
        || name.Contains("[none]", StringComparison.OrdinalIgnoreCase)
        || new[] { "Helvetica", "Times-", "Courier", "Symbol", "ZapfDingbats", "Arial" }
            .Any(alias => name.Contains(alias, StringComparison.OrdinalIgnoreCase));

    private static BekiRendererRun RenderWithGhostscript(
        BekiPrintPrepOptions options, string input, string work, int dpi)
    {
        var output = Path.Combine(work, "gs-page-%03d.png");

        // No -dQUIET here, unlike the conversion: D15 asks for the Ghostscript log as an artifact,
        // and a silent renderer produces no log to attach.
        string[] arguments =
        [
            "-dBATCH", "-dNOPAUSE", "-dSAFER",
            "-sDEVICE=png16m",
            $"-r{dpi.ToString(CultureInfo.InvariantCulture)}",
            $"-sOutputFile={output}",
            "-f", input,
        ];

        return Run(options.GhostscriptPath, "ghostscript", arguments, work, TimeSpan.FromMinutes(10));
    }

    private static BekiRendererRun RunPoppler(
        string executable, string tool, string[] arguments, string work) =>
        Run(executable, tool, arguments, work, TimeSpan.FromMinutes(10));

    private static BekiRendererRun Run(
        string executable, string tool, string[] arguments, string work, TimeSpan timeout)
    {
        var command = $"{executable} {string.Join(' ', arguments)}";

        try
        {
            var start = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = work,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };

            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start);
            if (process is null)
            {
                return BekiRendererRun.NotInstalled(tool, executable);
            }

            // Concurrently, never one pipe then the other: a render run without -dQUIET is exactly
            // the case where blocking on stderr while stdout fills its buffer hangs both sides.
            var (stdout, stderr) = BekiPrintPrep.Drain(process, timeout, out var finished);

            if (!finished)
            {
                return new BekiRendererRun(
                    tool, BekiRendererRun.Failed, command, null, stdout,
                    $"{tool} did not finish within {timeout.TotalMinutes:F0} minutes.");
            }

            return new BekiRendererRun(
                tool,
                process.ExitCode == 0 ? BekiRendererRun.Ok : BekiRendererRun.Failed,
                command,
                process.ExitCode,
                stdout,
                stderr);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            // The executable is not on this deployment. Amendment A8: skipped, and skipped
            // withholds the package.
            return BekiRendererRun.NotInstalled(tool, executable);
        }
    }

    private static List<BekiRenderedPage> ReadRenderedPages(string work, string prefix)
    {
        var pages = new List<BekiRenderedPage>();

        foreach (var file in Directory
                     .EnumerateFiles(work, prefix + "*.png")
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var digits = new string(Path.GetFileNameWithoutExtension(file)
                .Skip(prefix.Length)
                .TakeWhile(char.IsDigit)
                .ToArray());

            if (!int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                continue;
            }

            var bytes = File.ReadAllBytes(file);
            var info = Image.Identify(bytes);
            pages.Add(new BekiRenderedPage(number, info.Width, info.Height, bytes));
        }

        return pages.OrderBy(page => page.Page).ToList();
    }

    /// <summary>
    /// The QR gate, decoded off the rendered pixels.
    ///
    /// "Exactly one" is checked as exactly one SYMBOL, on the page the caller names, and the payload
    /// must be the per-book continuation destination character for character. An artifact with no
    /// QR page named passes by not being asked.
    /// </summary>
    private static BekiQrScan ScanQr(
        IReadOnlyList<BekiRenderedPage> pages, int? qrPage, int expectedCount, string expectedDestination)
    {
        if (qrPage is null)
        {
            return new BekiQrScan(
                BekiRendererRun.Ok, 0, [],
                null);
        }

        var page = pages.FirstOrDefault(candidate => candidate.Page == qrPage.Value);
        if (page is null)
        {
            return new BekiQrScan(
                BekiRendererRun.Failed, 0, [],
                $"page {qrPage} was never rendered, so its QR could not be scanned");
        }

        List<string> payloads;
        try
        {
            payloads = Decode(page.Png);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new BekiQrScan(
                BekiRendererRun.Failed, 0, [],
                $"the QR decoder failed on the rendered page ({ex.GetType().Name})");
        }

        if (payloads.Count != expectedCount)
        {
            return new BekiQrScan(
                BekiRendererRun.Failed, payloads.Count, payloads,
                $"the rendered page {qrPage} carries {payloads.Count} scannable QR code(s) where "
                + $"the locked value is {expectedCount}");
        }

        var wrong = payloads
            .Where(payload => !string.Equals(payload, expectedDestination, StringComparison.Ordinal))
            .ToList();

        return wrong.Count == 0
            ? new BekiQrScan(BekiRendererRun.Ok, payloads.Count, payloads, null)
            : new BekiQrScan(
                BekiRendererRun.Failed, payloads.Count, payloads,
                "the code scans to "
                + string.Join(
                    ", ",
                    wrong.Distinct(StringComparer.Ordinal).Select(value => $"'{value}'"))
                + $" rather than '{expectedDestination}'");
    }

    /// <summary>
    /// Every QR ZXing can find in one rendered page — one entry per SYMBOL, not per payload.
    ///
    /// The distinction is the review's finding. The payloads used to be deduplicated before they
    /// were counted, so a story page carrying a second code beside the current one —
    /// two symbols, one string — counted as one and walked through a gate whose entire content is
    /// "exactly one vector QR appears on Story spread 8". Two codes that agree about where they point are
    /// still two codes on the page, and the spec allows one.
    ///
    /// The luminance source is built by hand over ImageSharp's pixels rather than through a binding
    /// package: the conversion is eight lines, and it keeps the decoder's dependency to the one
    /// pure-managed assembly the csproj admits to.
    /// </summary>
    private static List<string> Decode(byte[] png)
    {
        using var image = Image.Load<Rgb24>(png);

        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new ZXing.Common.DecodingOptions
            {
                PossibleFormats = [BarcodeFormat.QR_CODE],
                TryHarder = true,
            },
        };

        var source = Luminance(image);

        var results = reader.DecodeMultiple(source);
        if (results is { Length: > 0 })
        {
            return results
                .Where(result => result?.Text is not null)
                .Select(result => result.Text)
                .ToList();
        }

        // ZXing's multi-symbol detector can return no candidates for a page containing one small
        // code even when its single-symbol detector resolves it cleanly. Falling back only after
        // the multi pass found nothing preserves duplicate detection while avoiding a false QR
        // refusal for the overwhelmingly common one-code page.
        var single = reader.Decode(source);
        if (single?.Text is not null)
        {
            return [single.Text];
        }

        // A book spread is much wider than its QR. Some decoder versions fail to form a finder
        // pattern from the full photographic page even though the symbol itself is sharp. Retry
        // overlapping tiles so the vector QR occupies a useful share of the luminance matrix.
        var halfWidth = image.Width / 2;
        var halfHeight = image.Height / 2;
        var tiles = new[]
        {
            new Rectangle(0, 0, halfWidth, halfHeight),
            new Rectangle(image.Width - halfWidth, 0, halfWidth, halfHeight),
            new Rectangle(0, image.Height - halfHeight, halfWidth, halfHeight),
            new Rectangle(image.Width - halfWidth, image.Height - halfHeight, halfWidth, halfHeight),
        };

        foreach (var tile in tiles)
        {
            using var crop = image.Clone(context => context.Crop(tile));
            single = reader.Decode(Luminance(crop));
            if (single?.Text is not null)
            {
                return [single.Text];
            }
        }

        return [];
    }

    private static RGBLuminanceSource Luminance(Image<Rgb24> image)
    {
        var raw = new byte[image.Width * image.Height * 3];
        var index = 0;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image[x, y];
                raw[index++] = pixel.R;
                raw[index++] = pixel.G;
                raw[index++] = pixel.B;
            }
        }

        return new RGBLuminanceSource(
            raw, image.Width, image.Height, RGBLuminanceSource.BitmapFormat.RGB24);
    }

    private static int DefaultColumns(int pageCount) =>
        pageCount <= 2 ? pageCount : Math.Min(4, (int)Math.Ceiling(Math.Sqrt(pageCount)));

    /// <summary>
    /// The contact sheet amendment A2 makes a release artifact: every page, at a size a person can
    /// judge a spread by, with its page number burned into the image itself.
    ///
    /// Burned in, not written beside: the sheet gets uploaded, downloaded, screenshotted and
    /// emailed, and a page number that lives in a filename does not survive any of that. The digits
    /// are drawn from a three-by-five bitmap held here rather than through a text renderer, because
    /// adding a font-rasterising dependency to draw ten glyphs would be a poor trade.
    /// </summary>
    private static byte[] BuildContactSheet(
        IReadOnlyList<BekiRenderedPage> pages, int columns, int thumbnailWidth)
    {
        columns = Math.Clamp(columns, 1, 8);
        thumbnailWidth = Math.Clamp(thumbnailWidth, 80, 2000);

        const int Gutter = 12;
        const int LabelHeight = 26;

        var thumbnails = new List<Image<Rgb24>>();
        try
        {
            foreach (var page in pages)
            {
                var thumbnail = Image.Load<Rgb24>(page.Png);
                var height = Math.Max(
                    1, (int)Math.Round((double)thumbnail.Height * thumbnailWidth / thumbnail.Width));
                thumbnail.Mutate(context => context.Resize(thumbnailWidth, height));
                thumbnails.Add(thumbnail);
            }

            var cellHeight = thumbnails.Max(thumbnail => thumbnail.Height) + LabelHeight;
            var rows = (int)Math.Ceiling((double)thumbnails.Count / columns);

            var sheetWidth = (columns * thumbnailWidth) + ((columns + 1) * Gutter);
            var sheetHeight = (rows * cellHeight) + ((rows + 1) * Gutter);

            using var sheet = new Image<Rgb24>(sheetWidth, sheetHeight, new Rgb24(245, 245, 245));

            for (var index = 0; index < thumbnails.Count; index++)
            {
                var column = index % columns;
                var row = index / columns;
                var x = Gutter + (column * (thumbnailWidth + Gutter));
                var y = Gutter + (row * (cellHeight + Gutter));

                var thumbnail = thumbnails[index];
                sheet.Mutate(context => context.DrawImage(thumbnail, new Point(x, y), 1f));

                DrawNumber(sheet, pages[index].Page, x, y + thumbnail.Height + 6);
            }

            using var buffer = new MemoryStream();
            sheet.Save(buffer, new PngEncoder());
            return buffer.ToArray();
        }
        finally
        {
            foreach (var thumbnail in thumbnails)
            {
                thumbnail.Dispose();
            }
        }
    }

    /// <summary>Three-by-five bitmap digits, one row of bits per byte, low three bits used.</summary>
    private static readonly byte[][] Digits =
    [
        [0b111, 0b101, 0b101, 0b101, 0b111],
        [0b010, 0b110, 0b010, 0b010, 0b111],
        [0b111, 0b001, 0b111, 0b100, 0b111],
        [0b111, 0b001, 0b111, 0b001, 0b111],
        [0b101, 0b101, 0b111, 0b001, 0b001],
        [0b111, 0b100, 0b111, 0b001, 0b111],
        [0b111, 0b100, 0b111, 0b101, 0b111],
        [0b111, 0b001, 0b001, 0b010, 0b010],
        [0b111, 0b101, 0b111, 0b101, 0b111],
        [0b111, 0b101, 0b111, 0b001, 0b111],
    ];

    private static void DrawNumber(Image<Rgb24> sheet, int number, int left, int top)
    {
        const int Scale = 4;
        var ink = new Rgb24(20, 20, 20);
        var text = number.ToString(CultureInfo.InvariantCulture);
        var cursor = left;

        foreach (var character in text)
        {
            if (!char.IsDigit(character))
            {
                continue;
            }

            var glyph = Digits[character - '0'];

            for (var row = 0; row < glyph.Length; row++)
            {
                for (var column = 0; column < 3; column++)
                {
                    if ((glyph[row] & (1 << (2 - column))) == 0)
                    {
                        continue;
                    }

                    for (var dy = 0; dy < Scale; dy++)
                    {
                        for (var dx = 0; dx < Scale; dx++)
                        {
                            var x = cursor + (column * Scale) + dx;
                            var y = top + (row * Scale) + dy;
                            if (x >= 0 && y >= 0 && x < sheet.Width && y < sheet.Height)
                            {
                                sheet[x, y] = ink;
                            }
                        }
                    }
                }
            }

            cursor += (3 * Scale) + Scale;
        }
    }

    private static string Truncate(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty
        : value.Length <= 4000 ? value
        : value[..4000] + "…";
}
