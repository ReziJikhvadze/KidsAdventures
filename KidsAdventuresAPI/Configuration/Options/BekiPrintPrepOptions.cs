namespace AdventurePacks.Api.Configuration.Options;

/// <summary>
/// How a short raster is made long enough for press.
///
/// Two modes, because there are exactly two lawful answers and the difference between them is a
/// deployment decision rather than a code path somebody discovers at runtime.
/// </summary>
public enum BekiPrintPrepMode
{
    /// <summary>
    /// The shipped default: one local, deterministic Lanczos3 normalization to the exact locked
    /// raster, after an aspect-safe minimal crop. No external process, no network, no cost, and
    /// the same bytes every time the same source is prepared.
    /// </summary>
    DeterministicLanczos,

    /// <summary>
    /// An external super-resolution executable, configured by path and argument template. Optional
    /// and unused by this deployment; kept because the tooling and the receipts for it exist and a
    /// future printer may want it.
    /// </summary>
    ExternalSuperResolution,
}

/// <summary>
/// The configuration spellings of <see cref="BekiPrintPrepMode"/>.
///
/// The strings are the contract: they appear in <c>appsettings</c>, in <c>press-status.json</c>, in
/// the preflight report and in the failure a misconfigured deployment gets at startup. Naming them
/// once here is what keeps those four places from drifting into four different words for one mode.
/// </summary>
public static class BekiPrintPrepModes
{
    /// <inheritdoc cref="BekiPrintPrepMode.DeterministicLanczos"/>
    public const string DeterministicLanczos = "deterministic_lanczos";

    /// <inheritdoc cref="BekiPrintPrepMode.ExternalSuperResolution"/>
    public const string ExternalSuperResolution = "external_super_resolution";

    /// <summary>The configuration id of a mode — the one spelling anything written down uses.</summary>
    public static string Id(BekiPrintPrepMode mode) => mode switch
    {
        BekiPrintPrepMode.ExternalSuperResolution => ExternalSuperResolution,
        _ => DeterministicLanczos,
    };
}

/// <summary>
/// What the print-preparation stage runs with — since the Locked Print Specification v1
/// (contracts/BEKI_Print_Production_Locked_Spec_v1.md), every default is the locked value rather
/// than "not supplied": the exact FOGRA39 profile ships in the asset tree with its hash pinned
/// here, and the all-CMYK ruling is the printer's written answer, not a question.
///
/// Bound under <c>Beki:PrintPrep</c>. Overrides exist for the day the spec's §7 triggers fire —
/// a different printer, a different profile — and for tests; changing them in production without
/// a revised spec is exactly the silent substitution this stage exists to refuse.
/// </summary>
public sealed class BekiPrintPrepOptions
{
    /// <summary>
    /// Path to the locked Coated FOGRA39 ICC profile, absolute or relative to the app base
    /// directory. The default is the profile the spec supplied, extracted byte-for-byte from the
    /// approved benchmark PDFs' own output intent and shipped with the assets.
    /// </summary>
    public string OutputIntentIccPath { get; set; } =
        "Assets/BekiComposite/print/BEKI_Coated_FOGRA39_OutputIntent.icc";

    /// <summary>
    /// The locked profile's SHA-256. Verified against the file's bytes on every load: an output
    /// intent is a statement about what the press will do with the numbers in the file, and a
    /// swapped or truncated profile would change every colour in the book while looking exactly
    /// the same in a directory listing. Empty skips the check (a test pointing at a fake).
    /// </summary>
    public string OutputIntentIccSha256 { get; set; } =
        "b35713ef7eff09349d4c3249e5f377736d06d8a2671c54712971a3546bf17c57";

    /// <summary>The output condition identifier written into the intent — the registry's name for it.</summary>
    public string OutputConditionIdentifier { get; set; } = "FOGRA39";

    /// <summary>The human-readable output condition — the locked profile's own description.</summary>
    public string OutputConditionInfo { get; set; } = "FOGRA39L Coated";

    /// <summary>The characterization registry the identifier is defined in.</summary>
    public string RegistryName { get; set; } = "https://www.color.org";

    /// <summary>
    /// Whether every raster image object must be CMYK in the press file. Locked <c>true</c> by
    /// spec §4 — "fail print preflight if any raster image object remains RGB" — and the one RGB
    /// object in the old intro benchmark is named there as a defect, not a precedent. False is
    /// only for a future printer whose written preset says otherwise.
    /// </summary>
    public bool RequireAllCmyk { get; set; } = true;

    /// <summary>
    /// The Ghostscript executable that performs the ICC colour conversion and is one of the two
    /// required render validators. A name resolves through PATH; a path is used as given. The
    /// conversion is Ghostscript's rather than hand-rolled image surgery because a press file's
    /// colour transform is exactly the job a maintained pdfwrite pipeline exists for — and spec
    /// §5 requires the binary on the deployment anyway.
    /// </summary>
    public string GhostscriptPath { get; set; } = "gs";

    /// <summary>
    /// The Poppler page rasteriser used by render validation, alongside Ghostscript.
    ///
    /// Two renderers, not one, because the gate says so: <c>RENDER_VALIDATION</c> in
    /// <c>BEKI_Acceptance_Gates_v1.json</c> reads "the stored final artifacts pass **both**
    /// Ghostscript and Poppler render validation", and the point of a second interpreter is that
    /// it disagrees. A name resolves through PATH. When it is not installed the render stage
    /// records <c>skipped</c> and the package is not releasable — audit-2 amendment A8: a check
    /// that did not run is not a check that passed.
    /// </summary>
    public string PopplerPdftoppmPath { get; set; } = "pdftoppm";

    /// <summary>Poppler's font lister — the second opinion on FONT_INTEGRITY, same rules as above.</summary>
    public string PopplerPdffontsPath { get; set; } = "pdffonts";

    /// <summary>
    /// Dots per inch for the validation renders and the contact sheet. 120 is chosen against what
    /// the renders are for: a human reading a full-spread contact sheet (amendment A2 makes that
    /// review mandatory before release) and a QR decoder reading a 46 mm code — at 120 dpi that
    /// code lands at ~217 px, comfortably above what ZXing needs, while a full press interior
    /// stays small enough to upload with the evidence.
    /// </summary>
    public int RenderDpi { get; set; } = 120;

    /// <summary>
    /// Which print-preparation mode this deployment runs — one of
    /// <see cref="BekiPrintPrepModes.DeterministicLanczos"/> (the default) or
    /// <see cref="BekiPrintPrepModes.ExternalSuperResolution"/>.
    ///
    /// A string rather than an enum on the wire, because this key is typed into an App Service
    /// settings box and the value a reader of that box needs to recognise is the one the decision
    /// record uses. <see cref="ParseMode"/> is forgiving about case and about <c>_</c> versus
    /// <c>-</c>, and refuses everything else by name at startup rather than silently defaulting:
    /// a typo here would otherwise decide how every press raster in the product is produced.
    ///
    /// The default is the 2026-09-06 decision record
    /// (<c>contracts/BEKI_Print_Prep_Deterministic_Normalization_v1.md</c>): the physical proof was
    /// reviewed and its sharpness accepted, so no external tool is required for this phase.
    /// </summary>
    public string Mode { get; set; } = BekiPrintPrepModes.DeterministicLanczos;

    /// <summary>
    /// <see cref="Mode"/> as the pipeline reads it.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The configured value is not a mode. Thrown rather than defaulted, and the message names
    /// <c>Beki:PrintPrep:Mode</c>, because the startup validator turns it into a deploy-time
    /// failure that says which key to fix.
    /// </exception>
    public BekiPrintPrepMode ResolvedMode => ParseMode(Mode);

    /// <summary>
    /// Parses a configured mode, ignoring case and word separators.
    /// </summary>
    /// <exception cref="InvalidOperationException">The value names no mode.</exception>
    public static BekiPrintPrepMode ParseMode(string value)
    {
        var normalized = (value ?? string.Empty)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToLowerInvariant();

        return normalized switch
        {
            "deterministiclanczos" => BekiPrintPrepMode.DeterministicLanczos,
            "externalsuperresolution" => BekiPrintPrepMode.ExternalSuperResolution,
            _ => throw new InvalidOperationException(
                $"Beki:PrintPrep:Mode is '{value}', which is not a print preparation mode. Use "
                + $"'{BekiPrintPrepModes.DeterministicLanczos}' (the default) or "
                + $"'{BekiPrintPrepModes.ExternalSuperResolution}'."),
        };
    }

    /// <summary>
    /// How many press rasters — and how many render validations — are worked on at once.
    ///
    /// Zero or less means "decide from the box", which is the shipped answer:
    /// <see cref="ResolvedParallelism"/> is <c>min(ProcessorCount, 3)</c>. Three was a constant in
    /// the fulfilment stage and it was the wrong shape of number, because each slot is a
    /// multi-megapixel resample holding a decoded canvas and its PNG buffer: on a one-vCPU App
    /// Service three of those do not run three times faster, they run at the same speed with three
    /// times the resident memory, and a plan that then swaps turns a two-minute stage into an hour.
    /// The ceiling stays at three even on a large box, because the stage is bounded by memory long
    /// before it is bounded by cores.
    ///
    /// An explicit value is honoured as given — including one above the core count — for a
    /// deployment that knows its own machine, and is capped only by
    /// <see cref="MaximumParallelism"/> so a typo cannot ask for a thousand.
    /// </summary>
    public int Parallelism { get; set; }

    /// <summary>The most slots any configuration may ask for.</summary>
    public const int MaximumParallelism = 16;

    /// <summary>The most slots this stage takes for itself when nothing is configured.</summary>
    public const int DefaultParallelismCeiling = 3;

    /// <summary><see cref="Parallelism"/> as the stage reads it — never zero, never above the cap.</summary>
    public int ResolvedParallelism => Parallelism > 0
        ? Math.Min(Parallelism, MaximumParallelism)
        : Math.Max(1, Math.Min(Environment.ProcessorCount, DefaultParallelismCeiling));

    /// <summary>
    /// The ceiling, in megabytes, on the pool ImageSharp keeps its pixel buffers in.
    ///
    /// ImageSharp's default allocator sizes its pool from the machine and holds on to what it has
    /// allocated, which is right for a long-lived image service and wrong for this one: a press
    /// stage touches ten thirteen-megapixel canvases in a burst, a few times a day, and then wants
    /// the memory back for the web application it shares a process with. On a small App Service the
    /// retained pool is the difference between a resident set that fits and one that swaps.
    ///
    /// 256 MB is enough for the three concurrent canvases the default parallelism allows without
    /// pooling every buffer the burst ever touched. Zero or less leaves ImageSharp's own default in
    /// place, for a deployment that would rather tune the runtime than this key.
    /// </summary>
    public int MaxImagePoolMegabytes { get; set; } = 256;

    /// <summary>
    /// The external super-resolution executable. Optional, and empty by default.
    ///
    /// Ignored entirely in <see cref="BekiPrintPrepModes.DeterministicLanczos"/> mode, which is
    /// what this deployment runs: nothing reads it, and nothing fails for its absence. It is
    /// required — with <see cref="UpscalerArgsTemplate"/> — only when <see cref="Mode"/> is
    /// <see cref="BekiPrintPrepModes.ExternalSuperResolution"/>, and then startup validation
    /// refuses to boot without it, because a mode that names a tool and has none is a deployment
    /// that would silently prepare nothing.
    /// </summary>
    public string UpscalerPath { get; set; } = string.Empty;

    /// <summary>
    /// The external tool's argument template, whitespace-separated, with <c>{in}</c>, <c>{out}</c>
    /// and <c>{scale}</c> substituted per invocation — for example
    /// <c>-i {in} -o {out} -s {scale} -n realesrgan-x4plus</c>. Optional and ignored on the default
    /// mode; required alongside <see cref="UpscalerPath"/> in external mode.
    ///
    /// A template rather than a command line: the tokens are expanded into
    /// <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/> one argument at a time, so a
    /// path containing a space stays one argument instead of becoming two the way it would if this
    /// were ever joined back into a string and handed to a shell.
    /// </summary>
    public string UpscalerArgsTemplate { get; set; } = string.Empty;
}
