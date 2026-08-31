using System.Security.Cryptography;
using AdventurePacks.Api.Services.Story;
using QuestPDF.Drawing;

namespace AdventurePacks.Api.Services.Pdf;

/// <summary>
/// Registers the faces QuestPDF uses for storybook text.
///
/// The family names match the web brand aliases (<c>Adventrya Sans</c> /
/// <c>Adventrya Serif</c>), backed by Noto Georgian rather than Nunito/Fredoka —
/// those two have no Georgian glyphs, so every PDF would have rendered tofu.
///
/// A face that fails to register is reported through <see cref="MissingFontFiles"/> or
/// <see cref="FailedFontFiles"/> rather than throwing: a book must still print if a display face is
/// absent. The caller logs it — silence here once meant a font could quietly stop shipping and the
/// only symptom was a PDF that looked slightly wrong to whoever happened to open it. The Beki
/// composer does not rely on that forgiveness: it proves its licensed files against
/// <see cref="Story.BekiLayoutAssets"/> before it asks for any of them, and stops the book if one
/// is missing or altered.
///
/// **Every file registered here is now hash-checked, and every file registered here is in the
/// layout registry.** Audit P1-02 found the opposite on both counts: five files went into the
/// process by hardcoded path, two of them (the Georgian SemiBolds) appeared in no registry at all,
/// none of the five was hashed, and the licensed Ottia was marked optional — so a deploy that lost
/// it produced a cover set in the body face and told nobody. Optionality is gone; a face that is
/// missing, unregistered or altered is reported and, in the last two cases, not registered at all,
/// because a font nobody approved reaching a printed page is the defect, not the absence of one.
/// <see cref="Story.BekiAssetLock"/> reads both lists and refuses the book.
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

    /// <summary>
    /// Font files that were present and could not be proved: the bytes do not match the layout
    /// registry's hash, the registry does not describe the file at all, or the registry itself
    /// could not be read.
    ///
    /// Separate from <see cref="MissingFontFiles"/> because the two mean different things to
    /// whoever is reading the log. A missing file is a deploy that dropped something. A failed file
    /// is a file that is <em>there</em> and is not what it claims to be, which is the shape of the
    /// trial-Ottia incident and the one audit P1-02 asked to be made detectable.
    /// </summary>
    public static IReadOnlyList<string> FailedFontFiles => _failed;

    private static readonly List<string> _missing = [];
    private static readonly List<string> _failed = [];

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

            // The registry is read once, here, rather than per file. If it cannot be read at all
            // the faces still register — the A5 book is not a Beki book and losing every glyph
            // would be a worse outcome than an unproven one — but the failure is recorded under the
            // registry's own path, so the asset lock refuses the composite book that does care.
            BekiLayoutAssets? registry = null;
            try
            {
                registry = BekiLayoutAssets.Current;
            }
            catch (Exception ex) when (ex is BekiLayoutException or IOException or InvalidOperationException)
            {
                _failed.Add(BekiLayoutAssets.RegistryAssetPath);
            }

            // RegisterWithName is required: the TTF's internal family is "Noto Sans Georgian",
            // and QuestPDF looks up by the name we pass to FontFamily(...).
            RegisterWithName(Path.Combine(fontsDir, "NotoSansGeorgian-Regular.ttf"), BodyFamily, registry);
            RegisterWithName(Path.Combine(fontsDir, "NotoSansGeorgian-SemiBold.ttf"), BodyFamily, registry);
            RegisterWithName(Path.Combine(fontsDir, "NotoSansGeorgian-Bold.ttf"), BodyFamily, registry);
            RegisterWithName(Path.Combine(fontsDir, "NotoSerifGeorgian-SemiBold.ttf"), DisplayFamily, registry);

            // Not optional any more. It used to be, on the argument that an absent display face
            // falls through the family chain onto the body face and the book prints "exactly as it
            // did before" — which is true, and is precisely the silent substitution audit P1-02
            // objected to: the cover title is the one place the book's own face is the picture, and
            // a Noto cover shipping unremarked is not a degradation anybody chose.
            RegisterWithName(Path.Combine(fontsDir, "Ottia-v01-Regular.ttf"), TitleFamily, registry);

            _registered = true;
        }
    }

    private static void RegisterWithName(string path, string familyName, BekiLayoutAssets? registry)
    {
        var fileName = Path.GetFileName(path);

        if (!File.Exists(path))
        {
            _missing.Add(fileName);
            return;
        }

        var bytes = File.ReadAllBytes(path);

        if (registry is not null)
        {
            var expected = registry.ExpectedFontSha256(fileName);

            if (expected is null)
            {
                // The P1-02 finding itself: a face embedded in sold books that no approval document
                // mentions. It does not register — an unapproved font is exactly what must not
                // reach a page — and the name goes where the asset lock will find it.
                _failed.Add(fileName);
                return;
            }

            var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                _failed.Add(fileName);
                return;
            }
        }

        using var stream = new MemoryStream(bytes, writable: false);
        FontManager.RegisterFontWithCustomName(familyName, stream);
    }
}
