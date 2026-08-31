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
}
