using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using AdventurePacks.Api.Services.Story.Composite;

namespace AdventurePacks.Api.Services.Story;

/// <summary>
/// A named layout failure, carrying the handoff's own failure code.
///
/// Thrown rather than logged, and thrown rather than degraded: the supplier's diagnosis of the
/// shipped book was that "the system silently takes the old alternative" — a missing asset became a
/// drawn placeholder, an unknown theme became a generic ground, an overflowing paragraph became a
/// smaller one that still overflowed. Every one of those is a book that printed wrong instead of a
/// job that stopped.
/// </summary>
public sealed class BekiLayoutException(string failureCode, string message)
    : InvalidOperationException(message)
{
    /// <summary>One of <see cref="CompositeFailureCodes"/> — today TEXT_OVERFLOW or LAYOUT_FAILED.</summary>
    public string FailureCode { get; } = failureCode;
}

/// <summary>
/// One door for every fixed layout asset the Beki book is built from, and the promise that the
/// bytes we print are the bytes the partners approved.
///
/// The poses already worked this way (<see cref="Composite.Poses.BekiPoseRegistry"/>): a registry
/// naming a file and its SHA-256, verified before first use, with a mismatch stopping the book. R11
/// extends exactly that discipline to the rest of the fixed artwork — the approved endpaper
/// pattern, the six approved intro backgrounds, and the four licensed font files — because the
/// alternative was what shipped: a placeholder dot field where the endpaper should have been, a
/// trial font on the cover, and nothing anywhere that could tell the difference.
///
/// **Hashes are not re-stated.** The endpaper and the six backgrounds are described by the
/// supplier's own <c>theme_reference_registry_v1.json</c>, which this registry reads; only the
/// fonts, which that document does not cover, are listed in
/// <see cref="RegistryAssetPath"/>. One asset, one hash, one place to change when a pack revises.
///
/// Nothing here has a fallback. A file that is missing, unreadable or hash-mismatched raises
/// <see cref="BekiLayoutException"/> with <c>LAYOUT_FAILED</c>, and a theme with no approved
/// background is the same failure rather than a generic ground — the handoff's integration rule for
/// the theme table is "do not infer unknown aliases", and inference is the only thing a fallback
/// could be.
/// </summary>
public sealed class BekiLayoutAssets
{
    /// <summary>
    /// Resolved against <see cref="AppContext.BaseDirectory"/> — the published output tree, the way
    /// the pose registry, the pipeline config and the fonts already resolve.
    /// </summary>
    public const string RegistryAssetPath = "Assets/BekiComposite/layout/beki_layout_asset_registry_v1.json";

    /// <summary>Verified bytes, keyed by expected hash plus absolute path — see the pose registry.</summary>
    private static readonly ConcurrentDictionary<string, byte[]> VerifiedBytes = new(StringComparer.Ordinal);

    /// <summary>
    /// The process-wide registry, loaded once. A layout asset set cannot differ between two books
    /// in one deployment, and re-reading nine JSON files per order buys nothing.
    /// </summary>
    private static readonly Lazy<BekiLayoutAssets> Shared = new(() => Load(), isThreadSafe: true);

    private readonly string _baseDirectory;
    private readonly string _endpaperDirectory;
    private readonly string _introBackgroundDirectory;
    private readonly string _fontDirectory;
    private readonly string _logoDirectory;
    private readonly IReadOnlyDictionary<string, BekiLayoutAsset> _introBackgrounds;
    private readonly IReadOnlyDictionary<string, BekiLayoutAsset> _fonts;

    private BekiLayoutAssets(
        string baseDirectory,
        string registryVersion,
        string themeRegistryVersion,
        string endpaperDirectory,
        string introBackgroundDirectory,
        string fontDirectory,
        string logoDirectory,
        string bekiMarkPoseId,
        BekiLayoutAsset endpaperPattern,
        BekiLayoutAsset coverLogo,
        IReadOnlyDictionary<string, BekiLayoutAsset> introBackgrounds,
        IReadOnlyDictionary<string, BekiLayoutAsset> fonts,
        IReadOnlyList<string> forbiddenFontFiles)
    {
        _baseDirectory = baseDirectory;
        RegistryVersion = registryVersion;
        ThemeRegistryVersion = themeRegistryVersion;
        _endpaperDirectory = endpaperDirectory;
        _introBackgroundDirectory = introBackgroundDirectory;
        _fontDirectory = fontDirectory;
        _logoDirectory = logoDirectory;
        BekiMarkPoseId = bekiMarkPoseId;
        EndpaperPattern = endpaperPattern;
        CoverLogo = coverLogo;
        _introBackgrounds = introBackgrounds;
        _fonts = fonts;
        ForbiddenFontFiles = forbiddenFontFiles;
        CanonicalThemeIds = [.. introBackgrounds.Keys];
    }

    /// <summary>The registry's own version string, for logs and for the resume contract.</summary>
    public string RegistryVersion { get; }

    /// <summary>
    /// The supplier's theme-reference registry version — the document that actually names the
    /// endpaper and the six intro backgrounds.
    ///
    /// Two versions rather than one because the two files revise independently, and the asset-lock
    /// manifest (audit §10.1) has to record which document approved each asset. A manifest that
    /// stamped every entry with the layout registry's version would be claiming provenance the
    /// layout registry does not have for artwork it does not describe.
    /// </summary>
    public string ThemeRegistryVersion { get; }

    /// <summary>
    /// Which approved pose is the Beki mark on the credits spread and the back cover.
    ///
    /// An id into the pose registry rather than a filename and hash of its own, for the same
    /// reason the endpaper's hash is not re-stated here: one asset, one hash, one place to change
    /// when a pack revises. The supplier's audit found the composer loading a legacy opaque
    /// raster from a hardcoded path with a silent null fallback — resolving through the pose
    /// registry is what makes the mark approved artwork with a receipt, and a missing or
    /// tampered file a stopped book rather than a shipped one.
    /// </summary>
    public string BekiMarkPoseId { get; }

    /// <summary>The approved endpaper pattern — one file, both endpaper spreads.</summary>
    public BekiLayoutAsset EndpaperPattern { get; }

    /// <summary>The approved unchanged vector logo used on dark cover artwork.</summary>
    public BekiLayoutAsset CoverLogo { get; }

    /// <summary>The canonical theme ids that have an approved intro background, in registry order.</summary>
    public IReadOnlyList<string> CanonicalThemeIds { get; }

    /// <summary>
    /// Every approved intro background, by canonical theme id.
    ///
    /// <see cref="IntroBackground(string?)"/> answers the book's question — which background is
    /// this world's — and refuses everything else. The asset lock asks the opposite question: what
    /// is the complete set, so that all of it can be named in the manifest. Exposing the table
    /// rather than making the lock walk <see cref="CanonicalThemeIds"/> and re-resolve each id
    /// keeps one enumeration of the registry instead of two.
    /// </summary>
    public IReadOnlyDictionary<string, BekiLayoutAsset> IntroBackgrounds => _introBackgrounds;

    /// <summary>Every registered font, by id.</summary>
    public IReadOnlyDictionary<string, BekiLayoutAsset> Fonts => _fonts;

    /// <summary>
    /// Files that must not be in the font folder at all — today the evaluation-only Ottia build,
    /// which shipped in a sold book. Named in data rather than in a test so the acceptance check
    /// and the runtime check cannot disagree about what is forbidden.
    /// </summary>
    public IReadOnlyList<string> ForbiddenFontFiles { get; }

    /// <summary>The registry as the running process sees it, loaded and validated once.</summary>
    public static BekiLayoutAssets Current => Shared.Value;

    /// <summary>
    /// Reads and validates the registry pair. <paramref name="baseDirectory"/> exists for tests
    /// that point at a doctored tree; production passes nothing and gets the published output.
    /// </summary>
    public static BekiLayoutAssets Load(string? baseDirectory = null)
    {
        var root = baseDirectory ?? AppContext.BaseDirectory;
        var path = Path.Combine(root, RegistryAssetPath);

        var document = ReadJson<RegistryDocument>(path, "layout asset registry");

        if (string.IsNullOrWhiteSpace(document.RegistryVersion))
        {
            throw Failure($"Beki layout asset registry at '{path}' has no registry_version.");
        }

        var themeRegistryPath = Path.Combine(
            root,
            Require(document.ThemeReferenceRegistry, path, "theme_reference_registry"));

        var themes = ReadJson<ThemeRegistryDocument>(themeRegistryPath, "theme reference registry");

        var endpaper = themes.Endpaper is { } supplied
            ? ToAsset(supplied.Filename, supplied.Sha256, "endpaper", themeRegistryPath)
            : throw Failure($"Beki theme reference registry at '{themeRegistryPath}' has no endpaper block.");

        if (themes.Themes is null || themes.Themes.Count == 0)
        {
            throw Failure($"Beki theme reference registry at '{themeRegistryPath}' lists no themes.");
        }

        var introBackgrounds = new Dictionary<string, BekiLayoutAsset>(StringComparer.Ordinal);
        foreach (var theme in themes.Themes)
        {
            if (string.IsNullOrWhiteSpace(theme.Id))
            {
                throw Failure($"Beki theme reference registry at '{themeRegistryPath}' has a theme without an id.");
            }

            if (!introBackgrounds.TryAdd(
                    theme.Id,
                    ToAsset(theme.ReferenceFilename, theme.Sha256, $"theme '{theme.Id}'", themeRegistryPath)))
            {
                throw Failure(
                    $"Beki theme reference registry at '{themeRegistryPath}' lists theme '{theme.Id}' twice.");
            }
        }

        // The registry's own declared id list is the contract the application boundary maps onto
        // (handoff §5's integration rule). A themes block that has drifted from it would leave a
        // canonical id addressable by the boundary and unresolvable here — the exact silence R11
        // exists to remove.
        foreach (var id in themes.CanonicalThemeIds ?? [])
        {
            if (!introBackgrounds.ContainsKey(id))
            {
                throw Failure(
                    $"Beki theme reference registry at '{themeRegistryPath}' declares canonical theme "
                    + $"'{id}' but has no entry for it.");
            }
        }

        if (document.Fonts is null || document.Fonts.Count == 0)
        {
            throw Failure($"Beki layout asset registry at '{path}' lists no fonts.");
        }

        var logo = document.CoverLogo is { } suppliedLogo
            ? ToAsset(suppliedLogo.Filename, suppliedLogo.Sha256, "cover logo", path, suppliedLogo.Role)
            : throw Failure($"Beki layout asset registry at '{path}' has no cover_logo block.");

        var fonts = new Dictionary<string, BekiLayoutAsset>(StringComparer.Ordinal);
        foreach (var font in document.Fonts)
        {
            if (string.IsNullOrWhiteSpace(font.Id))
            {
                throw Failure($"Beki layout asset registry at '{path}' has a font without an id.");
            }

            if (!fonts.TryAdd(
                    font.Id,
                    ToAsset(font.Filename, font.Sha256, $"font '{font.Id}'", path, font.Role)))
            {
                throw Failure($"Beki layout asset registry at '{path}' lists font '{font.Id}' twice.");
            }
        }

        return new BekiLayoutAssets(
            root,
            document.RegistryVersion,
            Require(themes.RegistryVersion, themeRegistryPath, "registry_version"),
            Require(document.EndpaperDirectory, path, "endpaper_directory"),
            Require(document.IntroBackgroundDirectory, path, "intro_background_directory"),
            Require(document.FontDirectory, path, "font_directory"),
            Require(document.LogoDirectory, path, "logo_directory"),
            Require(document.BekiMarkPoseId, path, "beki_mark_pose_id"),
            endpaper,
            logo,
            introBackgrounds,
            fonts,
            document.ForbiddenFontFiles ?? []);
    }

    /// <summary>The approved endpaper pattern's bytes, hash-verified on first use.</summary>
    public byte[] EndpaperPatternBytes() => VerifiedAssetBytes(EndpaperPattern, _endpaperDirectory);

    /// <summary>Approved logo SVG bytes, hash-verified before cover composition.</summary>
    public byte[] CoverLogoBytes() => VerifiedAssetBytes(CoverLogo, _logoDirectory);

    /// <summary>
    /// The approved intro background for one canonical theme id.
    ///
    /// An unknown id is a hard failure and not a default background: the six worlds are a closed
    /// set, and a book printed on the wrong world's intro is worse than a book that stopped.
    /// </summary>
    public byte[] IntroBackgroundBytes(string canonicalThemeId)
        => VerifiedAssetBytes(IntroBackground(canonicalThemeId), _introBackgroundDirectory);

    /// <summary>The registry entry for one canonical theme id, or a throw naming it.</summary>
    public BekiLayoutAsset IntroBackground(string? canonicalThemeId)
    {
        if (string.IsNullOrWhiteSpace(canonicalThemeId))
        {
            throw Failure(
                "The Beki intro spread needs a canonical theme id and was given none. The approved "
                + $"themes are {string.Join(", ", _introBackgrounds.Keys)}.");
        }

        return _introBackgrounds.TryGetValue(canonicalThemeId, out var asset)
            ? asset
            : throw Failure(
                $"No approved Beki intro background for theme '{canonicalThemeId}'. The registry "
                + $"'{RegistryVersion}' knows {string.Join(", ", _introBackgrounds.Keys)}.");
    }

    /// <summary>
    /// The absolute path of one registered font file, hash-verified on first use.
    ///
    /// A path rather than bytes because QuestPDF registers faces from a stream over the file; the
    /// verification still happens here, so a font is proven before it is ever registered.
    /// </summary>
    public string VerifiedFontPath(string fontId)
    {
        var font = _fonts.TryGetValue(fontId, out var asset)
            ? asset
            : throw Failure(
                $"Font '{fontId}' is not in Beki layout asset registry '{RegistryVersion}'.");

        _ = VerifiedAssetBytes(font, _fontDirectory);
        return AbsolutePath(font, _fontDirectory);
    }

    /// <summary>
    /// The approved hash for a font <em>file name</em>, or null if this registry does not describe
    /// that file at all.
    ///
    /// By file name rather than by registry id because the caller is
    /// <see cref="Pdf.PdfFontBootstrap"/>, which knows only the five paths it registers. The null
    /// case is the interesting one and is not a convenience: audit P1-02's finding was that two of
    /// those five files were in no registry, so "the registry has never heard of this face" is a
    /// governance failure the bootstrap has to be able to report, not a lookup miss it can shrug at.
    /// </summary>
    public string? ExpectedFontSha256(string fileName)
    {
        foreach (var font in _fonts.Values)
        {
            if (string.Equals(font.FileName, fileName, StringComparison.Ordinal))
            {
                return font.Sha256;
            }
        }

        return null;
    }

    /// <summary>The approved endpaper pattern's absolute path, hash-verified on first use.</summary>
    public string VerifiedEndpaperPatternPath()
    {
        _ = EndpaperPatternBytes();
        return AbsolutePath(EndpaperPattern, _endpaperDirectory);
    }

    /// <summary>One theme's approved intro background as an absolute path, hash-verified first.</summary>
    public string VerifiedIntroBackgroundPath(string canonicalThemeId)
    {
        var asset = IntroBackground(canonicalThemeId);
        _ = VerifiedAssetBytes(asset, _introBackgroundDirectory);
        return AbsolutePath(asset, _introBackgroundDirectory);
    }

    /// <summary>
    /// Proves everything one book will place, before any of it is laid out: the licensed fonts, the
    /// absence of the forbidden ones, the approved endpaper pattern, and the approved background for
    /// this book's own world.
    ///
    /// Not <see cref="VerifyAll"/>, which would hash five backgrounds this book will never open — a
    /// book that stops for a corrupt file it was not going to use is a book that stopped for the
    /// wrong reason. Everything it does touch is proven here, once, at the front.
    /// </summary>
    public void VerifyForBook(string canonicalThemeId)
    {
        VerifyFonts();
        _ = EndpaperPatternBytes();
        _ = IntroBackgroundBytes(canonicalThemeId);
        _ = CoverLogoBytes();
    }

    /// <summary>
    /// Proves the whole set in one pass: every font, the endpaper, all six backgrounds, and the
    /// absence of every forbidden file. For startup and for the acceptance tests — a deployment
    /// that lost an asset should fail where it can be read from a log, not inside a paid order.
    /// </summary>
    public void VerifyAll()
    {
        VerifyFonts();

        _ = EndpaperPatternBytes();
        _ = CoverLogoBytes();

        foreach (var themeId in _introBackgrounds.Keys)
        {
            _ = IntroBackgroundBytes(themeId);
        }
    }

    /// <summary>
    /// The four licensed faces are the approved bytes, and the evaluation-only build is not there
    /// at all.
    ///
    /// Both halves are the point. A trial Ottia sitting beside the licensed one would register
    /// under whichever name the bootstrap happened to ask for first, and the shipped book proved
    /// that a font nobody chose can reach a printed page without anything noticing.
    ///
    /// Public since the press cover: the wrap composer places no registry artwork of its own —
    /// its picture arrives pre-composited — but its Ottia title deserves the same proof as
    /// everything else that prints.
    /// </summary>
    public void VerifyFonts()
    {
        foreach (var font in _fonts.Values)
        {
            _ = VerifiedAssetBytes(font, _fontDirectory);
        }

        foreach (var forbidden in ForbiddenFontFiles)
        {
            var path = Path.Combine(_baseDirectory, _fontDirectory, forbidden);
            if (File.Exists(path))
            {
                throw Failure(
                    $"The forbidden font file '{forbidden}' is installed at '{path}'. It is licensed "
                    + "for evaluation only and must not reach a sold book.");
            }
        }
    }

    private byte[] VerifiedAssetBytes(BekiLayoutAsset asset, string directory)
    {
        var path = AbsolutePath(asset, directory);
        var cacheKey = string.Concat(asset.Sha256, "|", path);

        return VerifiedBytes.GetOrAdd(cacheKey, _ =>
        {
            if (!File.Exists(path))
            {
                throw Failure(
                    $"Approved Beki layout asset '{asset.FileName}' is missing at '{path}'.");
            }

            var bytes = File.ReadAllBytes(path);
            var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

            if (!string.Equals(actual, asset.Sha256, StringComparison.Ordinal))
            {
                throw Failure(
                    $"Approved Beki layout asset hash mismatch for '{asset.FileName}': the registry "
                    + $"expects {asset.Sha256}, the file at '{path}' is {actual}. Refusing to print "
                    + "an unapproved asset.");
            }

            return bytes;
        });
    }

    private string AbsolutePath(BekiLayoutAsset asset, string directory)
        => Path.GetFullPath(Path.Combine(_baseDirectory, directory, asset.FileName));

    private static BekiLayoutAsset ToAsset(
        string? fileName,
        string? sha256,
        string where,
        string path,
        string? role = null)
    {
        if (string.IsNullOrWhiteSpace(fileName) || !IsSha256Hex(sha256))
        {
            throw Failure(
                $"The entry for {where} in '{path}' needs a filename and a lower-case 64-character sha256.");
        }

        // The filename is spliced into a path and comes from a document on disk, so it is checked
        // rather than trusted: a name carrying a separator or a parent hop would let a registry
        // edit read a file from outside the asset tree.
        if (fileName.Contains('/') || fileName.Contains('\\') || fileName.Contains(".."))
        {
            throw Failure($"The entry for {where} in '{path}' names a path rather than a file: '{fileName}'.");
        }

        return new BekiLayoutAsset(fileName, sha256, role);
    }

    private static T ReadJson<T>(string path, string what) where T : class
    {
        if (!File.Exists(path))
        {
            throw Failure(
                $"The Beki {what} is missing at '{path}'. Check that Assets/BekiComposite is shipped "
                + "by the API csproj.");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllBytes(path))
                ?? throw Failure($"The Beki {what} at '{path}' deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new BekiLayoutException(
                CompositeFailureCodes.LayoutFailed,
                $"The Beki {what} at '{path}' is not valid JSON: {ex.Message}");
        }
    }

    private static string Require(string? value, string path, string field)
        => string.IsNullOrWhiteSpace(value)
            ? throw Failure($"Beki layout asset registry at '{path}' has no {field}.")
            : value;

    private static BekiLayoutException Failure(string message)
        => new(CompositeFailureCodes.LayoutFailed, message);

    private static bool IsSha256Hex([NotNullWhen(true)] string? value)
    {
        if (value is not { Length: 64 })
        {
            return false;
        }

        foreach (var c in value)
        {
            if (c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }

    private sealed record RegistryDocument
    {
        [JsonPropertyName("registry_version")] public string? RegistryVersion { get; init; }
        [JsonPropertyName("theme_reference_registry")] public string? ThemeReferenceRegistry { get; init; }
        [JsonPropertyName("endpaper_directory")] public string? EndpaperDirectory { get; init; }
        [JsonPropertyName("intro_background_directory")] public string? IntroBackgroundDirectory { get; init; }
        [JsonPropertyName("font_directory")] public string? FontDirectory { get; init; }
        [JsonPropertyName("logo_directory")] public string? LogoDirectory { get; init; }
        [JsonPropertyName("cover_logo")] public LogoDocument? CoverLogo { get; init; }
        [JsonPropertyName("beki_mark_pose_id")] public string? BekiMarkPoseId { get; init; }
        [JsonPropertyName("fonts")] public List<FontDocument>? Fonts { get; init; }
        [JsonPropertyName("forbidden_font_files")] public List<string>? ForbiddenFontFiles { get; init; }
    }

    private sealed record FontDocument
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("role")] public string? Role { get; init; }
        [JsonPropertyName("filename")] public string? Filename { get; init; }
        [JsonPropertyName("sha256")] public string? Sha256 { get; init; }
    }

    private sealed record LogoDocument
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("role")] public string? Role { get; init; }
        [JsonPropertyName("filename")] public string? Filename { get; init; }
        [JsonPropertyName("sha256")] public string? Sha256 { get; init; }
    }

    private sealed record ThemeRegistryDocument
    {
        [JsonPropertyName("registry_version")] public string? RegistryVersion { get; init; }
        [JsonPropertyName("canonical_theme_ids")] public List<string>? CanonicalThemeIds { get; init; }
        [JsonPropertyName("themes")] public List<ThemeDocument>? Themes { get; init; }
        [JsonPropertyName("endpaper")] public EndpaperDocument? Endpaper { get; init; }
    }

    private sealed record ThemeDocument
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("reference_filename")] public string? ReferenceFilename { get; init; }
        [JsonPropertyName("sha256")] public string? Sha256 { get; init; }
    }

    private sealed record EndpaperDocument
    {
        [JsonPropertyName("filename")] public string? Filename { get; init; }
        [JsonPropertyName("sha256")] public string? Sha256 { get; init; }
    }
}

/// <summary>
/// One approved layout asset: the file it names, and the hash that proves the file.
/// </summary>
/// <param name="Role">
/// What the asset is for, in the registry's own words (<c>interior_body</c>, <c>cover_title</c>).
/// Null for the endpaper and the intro backgrounds, whose role is their position in the
/// theme-reference document rather than a field. Carried since audit P1-02 asked for a manifest
/// keyed by role: a role that lives only in a C# switch is a second opinion about what a file is
/// for, and the registry's is the one that ships.
/// </param>
public sealed record BekiLayoutAsset(string FileName, string Sha256, string? Role = null);
