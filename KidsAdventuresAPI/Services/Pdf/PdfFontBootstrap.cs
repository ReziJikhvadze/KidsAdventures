using QuestPDF.Drawing;

namespace AdventurePacks.Api.Services.Pdf;

/// <summary>
/// Registers the faces QuestPDF uses for storybook text.
///
/// The family names match the web brand aliases (<c>Adventrya Sans</c> /
/// <c>Adventrya Serif</c>), backed by Noto Georgian rather than Nunito/Fredoka —
/// those two have no Georgian glyphs, so every PDF would have rendered tofu.
/// </summary>
internal static class PdfFontBootstrap
{
    private static bool _registered;
    private static readonly object RegisterLock = new();

    public const string BodyFamily = "Adventrya Sans";
    public const string DisplayFamily = "Adventrya Serif";

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

            _registered = true;
        }
    }

    private static void RegisterWithName(string path, string familyName)
    {
        if (!File.Exists(path))
        {
            return;
        }

        using var stream = File.OpenRead(path);
        FontManager.RegisterFontWithCustomName(familyName, stream);
    }
}
