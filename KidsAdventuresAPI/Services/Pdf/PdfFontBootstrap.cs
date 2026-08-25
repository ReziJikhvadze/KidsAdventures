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
/// slightly wrong to whoever happened to open it.
/// </summary>
internal static class PdfFontBootstrap
{
    private static bool _registered;
    private static readonly object RegisterLock = new();

    public const string BodyFamily = "Adventrya Sans";
    public const string DisplayFamily = "Adventrya Serif";

    /// <summary>
    /// The book's own display face, used where type is the picture rather than the reading: the
    /// cover title. Separate from <see cref="DisplayFamily"/> so a decorative face can be tried,
    /// changed or dropped without touching the headings that must stay readable.
    ///
    /// Registered from Ottia when the file is present. Ottia carries all thirty-three modern
    /// Georgian letters but not the punctuation the rest of the book uses — no dash, colon,
    /// ellipsis or apostrophe — which is why every call that names this family passes the Noto
    /// families behind it: QuestPDF walks the list per glyph, so a title keeps Ottia's letters
    /// and borrows a dash from Noto instead of printing a box.
    /// </summary>
    public const string TitleFamily = "Adventrya Display";

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
            // family chain onto the serif and the book prints exactly as it did before.
            RegisterWithName(Path.Combine(fontsDir, "Ottia-v01-Trial-Regular.ttf"), TitleFamily, optional: true);

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
