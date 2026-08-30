using QuestPDF.Drawing;

namespace AdventurePacks.Api.Services.Pdf;

/// <summary>
/// Registers the faces QuestPDF uses for storybook text.
///
/// The family names match the web brand aliases (<c>Adventrya Sans</c> /
/// <c>Adventrya Serif</c>), backed by Noto Georgian rather than Nunito/Fredoka —
/// those two have no Georgian glyphs, so every PDF would have rendered tofu.
///
/// A face that fails to register is reported through <see cref="MissingFontFiles"/> rather than
/// throwing: a book must still print if a display face is absent. The caller logs it — silence
/// here once meant a font could quietly stop shipping and the only symptom was a PDF that looked
/// slightly wrong to whoever happened to open it. The Beki composer does not rely on that
/// forgiveness: it proves its four licensed files against
/// <see cref="Story.BekiLayoutAssets"/> before it asks for any of them, and stops the book if one
/// is missing or altered.
///
/// **Two books share this registry, and only one of them has a font whitelist.** The A5 book sets
/// headings in <see cref="DisplayFamily"/> — Noto Serif Georgian — and keeps doing so. The Beki
/// interior may use nothing but Noto Sans Georgian Regular and Bold (handoff §6 Step 8, R10), so it
/// simply never names the serif; the fallback chains inside that composer name the body face and
/// stop there, which is what keeps a face nobody chose from reaching a printed interior.
/// </summary>
internal static class PdfFontBootstrap
{
    private static bool _registered;
    private static readonly object RegisterLock = new();

    public const string BodyFamily = "Adventrya Sans";

    /// <summary>
    /// The A5 book's heading face. Not part of the Beki interior's whitelist and deliberately not
    /// reachable from it: the Beki composer names <see cref="BodyFamily"/> or
    /// <see cref="TitleFamily"/> and never this.
    /// </summary>
    public const string DisplayFamily = "Adventrya Serif";

    /// <summary>
    /// The book's own display face, used where type is the picture rather than the reading: the
    /// cover title. Separate from <see cref="DisplayFamily"/> so a decorative face can be tried,
    /// changed or dropped without touching the headings that must stay readable.
    ///
    /// Registered from the licensed Ottia v0.1 — <c>Ottia-v01-Regular.ttf</c>, the purchased build.
    /// The evaluation-only trial used to be here and reached a sold book; the layout asset registry
    /// now refuses to let that file exist beside this one.
    ///
    /// Ottia carries all thirty-three modern Georgian letters but not the punctuation the rest of
    /// the book uses — no dash, colon, ellipsis or apostrophe — which is why every call that names
    /// this family passes the body family behind it: QuestPDF walks the list per glyph, so a title
    /// keeps Ottia's letters and borrows a dash from Noto instead of printing a box.
    /// </summary>
    public const string TitleFamily = "Adventrya Display";

    /// <summary>
    /// The font files the Beki interior and cover are allowed to be set in, by file name.
    ///
    /// The acceptance tests read this rather than a list of their own, so "what may be embedded"
    /// has one definition. Noto Sans Georgian Regular and Bold for the interior; the licensed Ottia
    /// for the cover title, which handoff §5 keeps outside the interior rules.
    /// </summary>
    public static readonly IReadOnlyList<string> BekiFontWhitelist =
    [
        "NotoSansGeorgian-Regular.ttf",
        "NotoSansGeorgian-Bold.ttf",
        "Ottia-v01-Regular.ttf",
    ];

    /// <summary>Font files that were expected and not found, in registration order.</summary>
    public static IReadOnlyList<string> MissingFontFiles => _missing;

    private static readonly List<string> _missing = [];

    public static void EnsureRegistered()
    {
        if (_registered)
        {
            return;
        }

        lock (RegisterLock)
        {
            if (_registered)
            {
                return;
            }

            var fontsDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");

            // RegisterWithName is required: the TTF's internal family is "Noto Sans Georgian",
            // and QuestPDF looks up by the name we pass to FontFamily(...).
            RegisterWithName(Path.Combine(fontsDir, "NotoSansGeorgian-Regular.ttf"), BodyFamily);
            RegisterWithName(Path.Combine(fontsDir, "NotoSansGeorgian-SemiBold.ttf"), BodyFamily);
            RegisterWithName(Path.Combine(fontsDir, "NotoSansGeorgian-Bold.ttf"), BodyFamily);
            RegisterWithName(Path.Combine(fontsDir, "NotoSerifGeorgian-SemiBold.ttf"), DisplayFamily);

            // The display face is optional by design: absent, the cover title falls through the
            // family chain onto the body face and the book prints exactly as it did before. The
            // Beki composer has already refused to get this far without it.
            RegisterWithName(Path.Combine(fontsDir, "Ottia-v01-Regular.ttf"), TitleFamily, optional: true);

            _registered = true;
        }
    }

    private static void RegisterWithName(string path, string familyName, bool optional = false)
    {
        if (!File.Exists(path))
        {
            if (!optional)
            {
                _missing.Add(Path.GetFileName(path));
            }

            return;
        }

        using var stream = File.OpenRead(path);
        FontManager.RegisterFontWithCustomName(familyName, stream);
    }
}
