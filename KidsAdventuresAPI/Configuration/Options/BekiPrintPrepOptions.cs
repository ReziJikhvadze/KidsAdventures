namespace AdventurePacks.Api.Configuration.Options;

/// <summary>
/// What the print-preparation stage needs before it may declare a file ready for a press.
///
/// Bound under <c>Beki:PrintPrep</c>. Every default here is deliberately "not supplied": the
/// output intent profile is a licensed file the printer's workflow defines, and the all-CMYK
/// question is the printer's ruling to give — the supplier's audit is explicit that neither may
/// be guessed. Unset values make the stage refuse with <c>PRINT_PREFLIGHT_FAILED</c> and the
/// print artifact is withheld; they never make it quietly skip a step.
/// </summary>
public sealed class BekiPrintPrepOptions
{
    /// <summary>
    /// Path to the Coated FOGRA39 ICC profile, absolute or relative to the app base directory.
    ///
    /// Empty means the file has not been supplied yet — it is owner item 4 on the handoff ledger
    /// — and print prep refuses rather than writing an output intent that names a profile it
    /// does not embed. PDF/X-4 requires the profile bytes in the file, not a reference to a name.
    /// </summary>
    public string OutputIntentIccPath { get; set; } = string.Empty;

    /// <summary>The output condition identifier written into the intent — the registry's name for it.</summary>
    public string OutputConditionIdentifier { get; set; } = "FOGRA39";

    /// <summary>The human-readable output condition, as the audit names it.</summary>
    public string OutputConditionInfo { get; set; } = "Coated FOGRA39";

    /// <summary>The characterization registry the identifier is defined in.</summary>
    public string RegistryName { get; set; } = "https://www.color.org";

    /// <summary>
    /// Whether every raster object must be converted to CMYK — the printer's ruling, not ours.
    ///
    /// Null until the printer confirms (the audit: "must be confirmed with the printer"). True
    /// currently refuses loudly, because the conversion stage does not exist and pretending
    /// otherwise is the exact failure mode this stage replaces; false and null proceed with
    /// ICC-tagged RGB, which PDF/X-4 permits, and the preflight report records the ruling state.
    /// </summary>
    public bool? RequireAllCmyk { get; set; }
}
