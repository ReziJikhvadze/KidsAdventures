using System.Security.Cryptography;
using System.Text.Json;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Services.Pdf;
using SixLabors.ImageSharp;

// Explicit opt-in diagnostic. No web host, database, order, AI provider, or job queue is started.
// The executable supplied by the operator MAY itself charge money: approval belongs to them.
if (args.Length != 7)
{
    Console.Error.WriteLine("Usage: BekiUpscalerProbe <child-world-base.png> <width> <height> <absolute-tool-path> <argument-template> <approved-tool/model-version> <new-output-directory>");
    return 2;
}
var inputPath = Path.GetFullPath(args[0]);
var width = int.Parse(args[1]);
var height = int.Parse(args[2]);
var executable = args[3];
if ((width, height) is not ((5315, 2480) or (6047, 2894)))
    throw new ArgumentException("Use the scoped interior or cover target dimensions.");
if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable))
    throw new ArgumentException("An existing absolute worker executable path is required.");
if (string.IsNullOrWhiteSpace(args[5])) throw new ArgumentException("Approved tool/model version is required.");
var output = Path.GetFullPath(args[6]);
if (Directory.Exists(output) || File.Exists(output))
    throw new IOException("Use a new output directory; this probe never overwrites existing artwork.");
var source = await File.ReadAllBytesAsync(inputPath);
var sourceSize = Image.Identify(source);
var toolSha = Sha(await File.ReadAllBytesAsync(executable));
var started = DateTimeOffset.UtcNow;
var result = await new CliPressUpscaler(new BekiPrintPrepOptions
{
    UpscalerPath = executable,
    UpscalerArgsTemplate = args[4],
}).UpscaleAsync(source, width, height, CancellationToken.None);
if (!result.Succeeded || result.Png is null)
{
    Console.Error.WriteLine(result.Reason);
    return 1;
}
var final = BekiPressRaster.FinalSize(result.Png, width, height);
Directory.CreateDirectory(output);
await File.WriteAllBytesAsync(Path.Combine(output, "prepared-base.png"), final);
var report = JsonSerializer.Serialize(new
{
    started_at_utc = started, finished_at_utc = DateTimeOffset.UtcNow,
    source_sha256 = Sha(source), source_px = new[] { sourceSize.Width, sourceSize.Height },
    executable, executable_sha256 = toolSha, operator_approved_tool_model_version = args[5],
    result.Tool, result.Factor,
    tool_output_px = new[] { result.DeliveredWidthPx, result.DeliveredHeightPx },
    final_px = new[] { width, height }, final_sha256 = Sha(final),
    final_size_method = "downsample-only after external detail preparation",
    visual_quality_review = "pending human comparison; dimensions alone do not prove quality",
    beki_typography_logo_qr_in_input = "must be absent; operator must verify the supplied base",
}, new JsonSerializerOptions { WriteIndented = true });
await File.WriteAllTextAsync(Path.Combine(output, "probe-evidence.json"), report);
Console.WriteLine(report);
return 0;

static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
