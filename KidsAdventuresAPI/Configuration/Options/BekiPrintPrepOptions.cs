namespace AdventurePacks.Api.Configuration.Options;

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
    /// The external super-resolution executable, empty by default — which means disabled.
    ///
    /// Audit P1-01 and P0-04: the shipped book was built on ~143 PPI story art and a ~125 PPI
    /// cover, stretched to 300 PPI targets by a Lanczos pass. "Upscaling changes pixel count, not
    /// source detail", so the resolution gate refuses interpolation-only enlargement outright and
    /// there is exactly one lawful way to make a short source long enough: a real super-resolver,
    /// named here, whose tool and factor are then recorded in the resolution receipt and the
    /// preflight. Nothing is installed with this build; unconfigured is the shipped state, and an
    /// unconfigured deployment withholds press files rather than passing thin ones.
    /// </summary>
    public string UpscalerPath { get; set; } = string.Empty;

    /// <summary>
    /// The upscaler's argument template, whitespace-separated, with <c>{in}</c>, <c>{out}</c> and
    /// <c>{scale}</c> substituted per invocation — for example
    /// <c>-i {in} -o {out} -s {scale} -n realesrgan-x4plus</c>.
    ///
    /// A template rather than a command line: the tokens are expanded into
    /// <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/> one argument at a time, so a
    /// path containing a space stays one argument instead of becoming two the way it would if this
    /// were ever joined back into a string and handed to a shell.
    /// </summary>
    public string UpscalerArgsTemplate { get; set; } = string.Empty;
}
