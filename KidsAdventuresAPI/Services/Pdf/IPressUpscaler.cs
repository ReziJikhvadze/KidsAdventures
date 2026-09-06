using System.Diagnostics;
using System.Globalization;
using AdventurePacks.Api.Configuration.Options;
using SixLabors.ImageSharp;

namespace AdventurePacks.Api.Services.Pdf;

/// <summary>
/// The stage that turns a stored base into the exact raster the press sheet needs.
///
/// Two implementations, one per <see cref="BekiPrintPrepMode"/>:
/// <see cref="DeterministicLanczosNormalizer"/> — the default — reconciles the aspect ratio with a
/// minimal centred crop and performs one local Lanczos3 resize to the locked pixel size, and
/// <see cref="CliPressUpscaler"/> runs an external super-resolution executable for a deployment that
/// configures one. Which one is registered is <see cref="PressRasterPreparerFactory"/>'s decision,
/// taken from configuration; nothing downstream asks.
///
/// What the caller gets back is a measurement, not a claim. The 2026-09-06 decision record
/// (<c>contracts/BEKI_Print_Prep_Deterministic_Normalization_v1.md</c>) settled that the gate judges
/// the output — exact pixels, effective PPI at placement, aspect preserved — and never the name or
/// the provenance of the tool that resized it. The receipt still records the tool, the factor and
/// the crop, because a physical proof is inspected against what somebody actually did.
/// </summary>
public interface IPressUpscaler
{
    /// <summary>
    /// Whether this preparer can run at all. The deterministic normalizer is always configured;
    /// the external tool is configured only when a path was supplied.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Prepares one PNG at the requested pixel size.
    /// </summary>
    /// <returns>
    /// The prepared PNG and the provenance a receipt needs. Never throws for a configuration or a
    /// content reason: a failing preparer answers with <see cref="PressUpscaleResult.Succeeded"/>
    /// false and a reason, because the caller's next move is to record the problem and let the
    /// measurement decide, not to crash the book the parent paid for.
    /// </returns>
    Task<PressUpscaleResult> UpscaleAsync(
        byte[] png, int targetWidth, int targetHeight, CancellationToken cancellationToken);
}

/// <summary>
/// The rectangle a normalization kept out of its source, and how much of each axis that discarded.
///
/// Recorded rather than inferred: "we resized it" and "we resized it after throwing away a fifth of
/// the picture" are different statements about the same delivered pixel count, and only one of them
/// is a book somebody approved.
/// </summary>
/// <param name="CropFractionX">Share of the source width discarded — 0 when nothing was.</param>
public sealed record PressCropGeometry(
    int XPx,
    int YPx,
    int WidthPx,
    int HeightPx,
    double CropFractionX,
    double CropFractionY);

/// <summary>
/// What a preparation attempt produced, and what may be said about it afterwards.
/// </summary>
/// <param name="Succeeded">False means this raster was not prepared; it does not mean the book failed.</param>
/// <param name="Png">The prepared image, when there is one.</param>
/// <param name="Tool">The tool as it will appear in the receipt — never "resize", never blank.</param>
/// <param name="Factor">Linear scale actually applied, cropped source width to delivered width.</param>
/// <param name="SourceWidthPx">The source's real pixel width, which is the number the audit cares about.</param>
/// <param name="Reason">Why it did not happen, when it did not.</param>
/// <param name="Crop">What the aspect reconciliation discarded; null when nothing was resized.</param>
/// <param name="Mode">The print preparation mode this result came out of.</param>
public sealed record PressUpscaleResult(
    bool Succeeded,
    byte[]? Png,
    string Tool,
    double Factor,
    int SourceWidthPx,
    int SourceHeightPx,
    int DeliveredWidthPx,
    int DeliveredHeightPx,
    string? Reason,
    PressCropGeometry? Crop = null,
    string Mode = BekiPrintPrepModes.DeterministicLanczos)
{
    /// <summary>
    /// External mode was selected and no tool was named. The startup validator makes this
    /// unreachable on a healthy deployment; it exists so a test double and a hand-built options
    /// object get the same sentence a misconfiguration would.
    /// </summary>
    public static PressUpscaleResult ExternalToolNotConfigured(int sourceWidth, int sourceHeight) =>
        new(false, null, "none", 1d, sourceWidth, sourceHeight, sourceWidth, sourceHeight,
            "external_super_resolution mode is enabled but Beki:PrintPrep:UpscalerPath is empty; "
            + "configure the tool or switch Beki:PrintPrep:Mode to deterministic_lanczos",
            null, BekiPrintPrepModes.ExternalSuperResolution);

    /// <summary>
    /// The former name of <see cref="ExternalToolNotConfigured"/>, kept as a forwarding alias so the
    /// callers written against it keep compiling. Not marked obsolete on purpose: CI builds with
    /// <c>-warnaserror</c>, and a rename is not worth breaking the build over.
    /// </summary>
    public static PressUpscaleResult NotConfigured(int sourceWidth, int sourceHeight) =>
        ExternalToolNotConfigured(sourceWidth, sourceHeight);

    /// <summary>The receipt line this result contributes to the preflight.</summary>
    public BekiResolutionSource ToReceiptSource(string role) => new(
        role, SourceWidthPx, SourceHeightPx, DeliveredWidthPx, DeliveredHeightPx, Tool, Factor,
        InterpolationOnly: false, Crop, Mode);
}

/// <summary>
/// The default preparer: one deterministic Lanczos3 normalization, locally, for free.
///
/// It is always "configured" because there is nothing to configure — no binary, no path, no
/// network — which is the whole reason the 2026-09-06 decision record makes it the default: a
/// deployment cannot forget to install it, and the same source produces the same bytes on every
/// machine that prepares it.
/// </summary>
public sealed class DeterministicLanczosNormalizer : IPressUpscaler
{
    /// <inheritdoc />
    public bool IsConfigured => true;

    /// <inheritdoc />
    public Task<PressUpscaleResult> UpscaleAsync(
        byte[] png, int targetWidth, int targetHeight, CancellationToken cancellationToken) =>
        Task.FromResult(BekiPressRaster.NormalizeDeterministic(png, targetWidth, targetHeight));
}

/// <summary>
/// Chooses the preparer the configured mode asks for.
///
/// One factory rather than a conditional at each construction site: DI resolves it, and so does the
/// fulfilment stage's constructor fallback, and those two answering differently is exactly the
/// class of bug where a job prepares press rasters with a tool the report names wrongly.
/// </summary>
public static class PressRasterPreparerFactory
{
    /// <summary>The preparer for these options' <see cref="BekiPrintPrepOptions.ResolvedMode"/>.</summary>
    public static IPressUpscaler Create(BekiPrintPrepOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.ResolvedMode == BekiPrintPrepMode.ExternalSuperResolution
            ? new CliPressUpscaler(options)
            : new DeterministicLanczosNormalizer();
    }
}

/// <summary>
/// The <see cref="BekiPrintPrepMode.ExternalSuperResolution"/> implementation: a process, an
/// argument list, and no shell. Only reached when a deployment selects that mode.
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
            return PressUpscaleResult.ExternalToolNotConfigured(sourceWidth, sourceHeight);
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
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(Timeout);

            try
            {
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                cancellationToken.ThrowIfCancellationRequested();
                return Failed(
                    sourceWidth, sourceHeight,
                    $"'{_options.UpscalerPath}' did not finish within {Timeout.TotalMinutes:F0} minutes.");
            }

            // Drain both pipes before accepting output; cancellation must not leave a resolver running.
            await stdout.ConfigureAwait(false);

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
                null,
                null,
                BekiPrintPrepModes.ExternalSuperResolution);
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
            1d, width, height, width, height, reason,
            null, BekiPrintPrepModes.ExternalSuperResolution);

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
