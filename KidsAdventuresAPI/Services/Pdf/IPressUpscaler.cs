using System.Diagnostics;
using System.Globalization;
using AdventurePacks.Api.Configuration.Options;
using SixLabors.ImageSharp;

namespace AdventurePacks.Api.Services.Pdf;

/// <summary>
/// The one lawful way to make a short raster long enough for press.
///
/// Audit P1-01 states the rule this abstraction exists to enforce: "Upscaling changes pixel count,
/// not source detail." The shipped book ran 2528×1180 story art through a Lanczos stretch to
/// 5315×2480 and reported 300 PPI, and the audit named that metadata-passing as the defect. So the
/// resolution gate refuses interpolation-only enlargement outright, and the correction plan (D5c)
/// leaves exactly one door open: an approved super-resolver, run as an external tool, whose name
/// and factor are recorded in the resolution receipt and echoed in the preflight so that a physical
/// proof can be inspected against a claim somebody actually made.
///
/// **Nothing is installed by this build.** The implementation ships disabled — no binary, no
/// registration — which is the intended state: an unconfigured deployment withholds press files
/// with <c>PRESS_RESOLUTION</c> rather than passing thin ones, and the parent's reading copy is
/// unaffected either way.
/// </summary>
public interface IPressUpscaler
{
    /// <summary>Whether a tool is configured at all. False is the shipped default.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Enlarges one PNG to at least the requested pixel size using real super-resolution.
    /// </summary>
    /// <returns>
    /// The enlarged PNG and the provenance a receipt needs. Never throws for a configuration
    /// reason: an unconfigured or failing upscaler answers with
    /// <see cref="PressUpscaleResult.Succeeded"/> false and a reason, because the caller's next
    /// move is to withhold the press file, not to crash the book the parent paid for.
    /// </returns>
    Task<PressUpscaleResult> UpscaleAsync(
        byte[] png, int targetWidth, int targetHeight, CancellationToken cancellationToken);
}

/// <summary>
/// What an upscale attempt produced, and what may be said about it afterwards.
/// </summary>
/// <param name="Succeeded">False means the press path withholds; it does not mean the book failed.</param>
/// <param name="Png">The enlarged image, when there is one.</param>
/// <param name="Tool">The tool as it will appear in the receipt — never "resize", never blank.</param>
/// <param name="Factor">Linear enlargement actually achieved, source width to delivered width.</param>
/// <param name="SourceWidthPx">The source's real pixel width, which is the number the audit cares about.</param>
/// <param name="Reason">Why it did not happen, when it did not.</param>
public sealed record PressUpscaleResult(
    bool Succeeded,
    byte[]? Png,
    string Tool,
    double Factor,
    int SourceWidthPx,
    int SourceHeightPx,
    int DeliveredWidthPx,
    int DeliveredHeightPx,
    string? Reason)
{
    /// <summary>The shipped state: no tool configured, so no enlargement may be claimed.</summary>
    public static PressUpscaleResult NotConfigured(int sourceWidth, int sourceHeight) =>
        new(false, null, "none", 1d, sourceWidth, sourceHeight, sourceWidth, sourceHeight,
            "no press upscaler is configured (Beki:PrintPrep:UpscalerPath is empty); "
            + "interpolation-only enlargement is a PRESS_RESOLUTION failure, so the press file is "
            + "withheld rather than upscaled in software");

    /// <summary>The receipt line this result contributes to the preflight.</summary>
    public BekiResolutionSource ToReceiptSource(string role) => new(
        role, SourceWidthPx, SourceHeightPx, DeliveredWidthPx, DeliveredHeightPx, Tool, Factor,
        InterpolationOnly: false);
}

/// <summary>
/// The configured-external-tool implementation: a process, an argument list, and no shell.
///
/// The executable and its arguments come from <see cref="BekiPrintPrepOptions.UpscalerPath"/> and
/// <see cref="BekiPrintPrepOptions.UpscalerArgsTemplate"/>. Arguments are expanded token by token
/// into <see cref="ProcessStartInfo.ArgumentList"/> — the same idiom the Ghostscript conversion
/// uses and for the same reason: two of them are paths, and shell-style quoting inside one joined
/// string is exactly how an argument grows quotes it was never supposed to carry.
/// </summary>
public sealed class CliPressUpscaler(BekiPrintPrepOptions options) : IPressUpscaler
{
    /// <summary>How long a super-resolution run may take before it is killed.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    private readonly BekiPrintPrepOptions _options = options
        ?? throw new ArgumentNullException(nameof(options));

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.UpscalerPath);

    public async Task<PressUpscaleResult> UpscaleAsync(
        byte[] png, int targetWidth, int targetHeight, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(png);

        var (sourceWidth, sourceHeight) = Measure(png);

        if (!IsConfigured)
        {
            return PressUpscaleResult.NotConfigured(sourceWidth, sourceHeight);
        }

        if (string.IsNullOrWhiteSpace(_options.UpscalerArgsTemplate))
        {
            return Failed(
                sourceWidth, sourceHeight,
                "Beki:PrintPrep:UpscalerPath names a tool but UpscalerArgsTemplate is empty, so "
                + "there is no way to tell it what to do.");
        }

        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return Failed(sourceWidth, sourceHeight, "the source is not a readable image.");
        }

        var work = Path.Combine(Path.GetTempPath(), $"beki-upscale-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);

        var input = Path.Combine(work, "in.png");
        var output = Path.Combine(work, "out.png");

        try
        {
            await File.WriteAllBytesAsync(input, png, cancellationToken).ConfigureAwait(false);

            // The scale a super-resolver is asked for is a whole factor: these tools ship trained
            // ×2/×3/×4 models, and asking for 2.7 either fails or silently resamples afterwards —
            // which would be the very interpolation this class exists to avoid claiming.
            var scale = Math.Max(
                (int)Math.Ceiling((double)targetWidth / sourceWidth),
                (int)Math.Ceiling((double)targetHeight / sourceHeight));
            scale = Math.Max(2, scale);

            var start = new ProcessStartInfo
            {
                FileName = _options.UpscalerPath,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };

            foreach (var argument in _options.UpscalerArgsTemplate.Split(
                         ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                start.ArgumentList.Add(argument
                    .Replace("{in}", input, StringComparison.Ordinal)
                    .Replace("{out}", output, StringComparison.Ordinal)
                    .Replace("{scale}", scale.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
            }

            using var process = Process.Start(start);
            if (process is null)
            {
                return Failed(sourceWidth, sourceHeight, $"'{_options.UpscalerPath}' did not start.");
            }

            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            _ = process.StandardOutput.ReadToEndAsync(cancellationToken);

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(Timeout);

            try
            {
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                return Failed(
                    sourceWidth, sourceHeight,
                    $"'{_options.UpscalerPath}' did not finish within {Timeout.TotalMinutes:F0} minutes.");
            }

            if (process.ExitCode != 0 || !File.Exists(output))
            {
                var message = await stderr.ConfigureAwait(false);
                return Failed(
                    sourceWidth, sourceHeight,
                    $"'{_options.UpscalerPath}' exited {process.ExitCode}: "
                    + (string.IsNullOrWhiteSpace(message) ? "(no stderr)" : Truncate(message)));
            }

            var enlarged = await File.ReadAllBytesAsync(output, cancellationToken).ConfigureAwait(false);
            var (deliveredWidth, deliveredHeight) = Measure(enlarged);

            if (deliveredWidth < targetWidth || deliveredHeight < targetHeight)
            {
                return Failed(
                    sourceWidth, sourceHeight,
                    $"'{Path.GetFileName(_options.UpscalerPath)}' returned {deliveredWidth}×"
                    + $"{deliveredHeight} px, short of the {targetWidth}×{targetHeight} px the "
                    + "placement needs at 300 PPI.");
            }

            return new PressUpscaleResult(
                true,
                enlarged,
                Path.GetFileNameWithoutExtension(_options.UpscalerPath),
                (double)deliveredWidth / sourceWidth,
                sourceWidth,
                sourceHeight,
                deliveredWidth,
                deliveredHeight,
                null);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return Failed(
                sourceWidth, sourceHeight,
                $"'{_options.UpscalerPath}' is not an executable on this deployment.");
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { /* temp cleanup only */ }
        }
    }

    private PressUpscaleResult Failed(int width, int height, string reason) =>
        new(false, null, string.IsNullOrWhiteSpace(_options.UpscalerPath) ? "none" : _options.UpscalerPath,
            1d, width, height, width, height, reason);

    private static (int Width, int Height) Measure(byte[] png)
    {
        try
        {
            var info = Image.Identify(png);
            return (info.Width, info.Height);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return (0, 0);
        }
    }

    private static string Truncate(string value) =>
        value.Length <= 400 ? value : value[..400] + "…";
}
