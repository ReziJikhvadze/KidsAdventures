namespace AdventurePacks.Api.Configuration.Options;

/// <summary>
/// The physical shape of a printed book.
///
/// Every number here belongs to the print shop, not to us: trim size, how much bleed they
/// want, how much of the inner margin the binding swallows. They are settings because the
/// first vendor's spec sheet will not be the last, and a layout that hard-codes A4 has to
/// be rewritten the first time somebody quotes for a square picture book.
///
/// Defaults are A5 portrait with 3mm bleed — the safest guess for a Georgian print shop,
/// since A-series stock is universal and portrait matches the "Portrait format, full-frame
/// illustration" the story prompt asks the image model for. Replace them with the real spec
/// the moment you have it; nothing in the layout code needs to change.
/// </summary>
public sealed class PrintLayoutOptions
{
    public const string SectionName = "PrintLayout";

    /// <summary>Finished page width after cutting, in millimetres.</summary>
    public float TrimWidthMm { get; set; } = 148f;

    /// <summary>Finished page height after cutting, in millimetres.</summary>
    public float TrimHeightMm { get; set; } = 210f;

    /// <summary>
    /// How far a full-bleed picture runs past the trim line. Three millimetres is the common
    /// ask. Set to zero for a home printer, which cannot print to the edge anyway.
    /// </summary>
    public float BleedMm { get; set; } = 3f;

    /// <summary>
    /// How far text stays clear of the trim line. Guillotines drift by a millimetre or two, so
    /// anything closer than this risks being cut off.
    /// </summary>
    public float SafeMarginMm { get; set; } = 10f;

    /// <summary>
    /// Extra margin on the bound edge, on top of <see cref="SafeMarginMm"/>. Saddle-stitch needs
    /// little; perfect binding swallows a good deal more, because the inner edge curves into
    /// the spine and text set too close disappears into it.
    /// </summary>
    public float GutterMm { get; set; } = 8f;

    /// <summary>
    /// Sheets are folded, so the interior has to divide by this. Four for saddle-stitch. Blank
    /// pages are appended at the back until the count fits, which is what a printer would
    /// otherwise do for you — badly, and after quoting.
    /// </summary>
    public int BindingMultiple { get; set; } = 4;

    /// <summary>
    /// Whether the cover rides in the same file as the interior. Many vendors want the cover
    /// as a separate spread with a spine, so this can be turned off to emit the interior alone.
    /// </summary>
    public bool IncludeCoverInInterior { get; set; } = true;
}
