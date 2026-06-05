using QuestPDF.Drawing;

namespace AdventurePacks.Api.Services.Pdf;

internal static class PdfFontBootstrap
{
    private static bool _registered;
    private static readonly object RegisterLock = new();

    public const string BodyFamily = "Nunito";
    public const string DisplayFamily = "Fredoka";

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
            Register(Path.Combine(fontsDir, "Nunito-Regular.ttf"), BodyFamily);
            Register(Path.Combine(fontsDir, "Nunito-SemiBold.ttf"), BodyFamily);
            Register(Path.Combine(fontsDir, "Nunito-Bold.ttf"), BodyFamily);
            RegisterWithName(Path.Combine(fontsDir, "Fredoka-SemiBold.ttf"), DisplayFamily);

            _registered = true;
        }
    }

    private static void Register(string path, string family)
    {
        if (!File.Exists(path))
        {
            return;
        }

        using var stream = File.OpenRead(path);
        FontManager.RegisterFont(stream);
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
