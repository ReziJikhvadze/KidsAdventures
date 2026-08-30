using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Services.Pdf;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite;
using Microsoft.Extensions.Options;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The layout asset registry, and the failures it is for.
///
/// R11's diagnosis of the shipped book, in the supplier's words, was that "the system silently takes
/// the old alternative": the approved endpaper sat in the asset tree while books printed a drawn
/// placeholder, the trial font shipped beside the licensed one, and no theme had a background at
/// all. None of that was a bug anybody could see — every path succeeded. So these tests are about
/// the paths that must now fail.
/// </summary>
public class BekiLayoutAssetTests
{
    [Fact]
    public void Every_registered_layout_asset_is_installed_and_matches_its_hash()
    {
        // Fonts, the approved endpaper pattern and all six intro backgrounds, in one pass.
        BekiLayoutAssets.Current.VerifyAll();

        Assert.Equal("beki-layout-assets-v1.1", BekiLayoutAssets.Current.RegistryVersion);
        Assert.Equal(6, BekiLayoutAssets.Current.CanonicalThemeIds.Count);
        Assert.Equal(4, BekiLayoutAssets.Current.Fonts.Count);

        // v1.1: the credits/back-cover mark resolves through the pose registry — approved
        // transparent artwork with a hash, never the legacy opaque raster from a hardcoded path.
        Assert.Equal("pose_01_neutral_hover", BekiLayoutAssets.Current.BekiMarkPoseId);
    }

    /// <summary>
    /// The evaluation-only Ottia is not installed — an acceptance check (R15), because it was, and
    /// it reached a book somebody bought.
    /// </summary>
    [Fact]
    public void The_trial_ottia_is_not_installed_anywhere_the_composer_can_reach()
    {
        var forbidden = BekiLayoutAssets.Current.ForbiddenFontFiles;
        Assert.NotEmpty(forbidden);
        Assert.Contains("Ottia-v01-Trial-Regular.ttf", forbidden);

        var fontsDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
        foreach (var file in forbidden)
        {
            Assert.False(File.Exists(Path.Combine(fontsDirectory, file)),
                $"The evaluation-only font '{file}' is installed and must not be.");
        }
    }

    /// <summary>
    /// Every font the Beki composer is allowed to set type in is a registered, hash-verified file.
    /// The whitelist and the registry are two lists, and this is what stops them drifting apart.
    /// </summary>
    [Fact]
    public void The_font_whitelist_and_the_registry_name_the_same_files()
    {
        var registered = BekiLayoutAssets.Current.Fonts.Values
            .Select(font => font.FileName)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var whitelisted in PdfFontBootstrap.BekiFontWhitelist)
        {
            Assert.Contains(whitelisted, registered);
        }
    }

    /// <summary>
    /// A file that is not the approved one stops the book, by name, with the handoff's own code.
    ///
    /// Proven against a doctored asset tree rather than by trusting the code path: the registry is
    /// pointed at a temporary folder whose endpaper file is a different picture, which is exactly
    /// what a re-export by a well-meaning tool would look like.
    /// </summary>
    [Fact]
    public void An_asset_whose_bytes_are_not_the_approved_ones_stops_the_book()
    {
        using var tree = new DoctoredAssetTree();
        tree.WriteEndpaper("this is not the approved pattern"u8.ToArray());

        var failure = Assert.Throws<BekiLayoutException>(() => tree.Registry.EndpaperPatternBytes());

        Assert.Equal(CompositeFailureCodes.LayoutFailed, failure.FailureCode);
        Assert.Contains("hash mismatch", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BEKI_Endpaper_Pattern_Approved", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_asset_that_is_not_installed_at_all_stops_the_book()
    {
        using var tree = new DoctoredAssetTree();

        var failure = Assert.Throws<BekiLayoutException>(() => tree.Registry.EndpaperPatternBytes());

        Assert.Equal(CompositeFailureCodes.LayoutFailed, failure.FailureCode);
        Assert.Contains("missing", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A theme with no approved background is refused rather than given somebody else's world. The
    /// registry's own integration rule is "do not infer unknown aliases", and a default background
    /// is an inference.
    /// </summary>
    [Fact]
    public void A_theme_with_no_approved_background_is_refused()
    {
        var failure = Assert.Throws<BekiLayoutException>(
            () => BekiLayoutAssets.Current.IntroBackground("atlantis"));

        Assert.Equal(CompositeFailureCodes.LayoutFailed, failure.FailureCode);
        Assert.Contains("atlantis", failure.Message, StringComparison.Ordinal);

        // And so is no theme at all, which is what an unpersonalized book would be asking for.
        Assert.Throws<BekiLayoutException>(() => BekiLayoutAssets.Current.IntroBackground(null));
    }

    /// <summary>
    /// Every world a parent can buy resolves to an approved background.
    ///
    /// Two maps have to agree for a book to have an intro spread: the application boundary's
    /// theme-value → canonical-id mapping, and the registry's canonical-id → file table. Neither
    /// knows about the other, and a new world added to one and not the other would be discovered by
    /// the first parent who chose it.
    /// </summary>
    [Fact]
    public void Every_world_a_parent_can_choose_has_an_approved_intro_background()
    {
        foreach (var theme in Enum.GetValues<ThemeType>())
        {
            var canonical = InputNormalization.CanonicalThemeId(theme.ToString());
            Assert.NotNull(canonical);

            var asset = BekiLayoutAssets.Current.IntroBackground(canonical);
            Assert.NotNull(asset);
            Assert.EndsWith(".png", asset.FileName, StringComparison.OrdinalIgnoreCase);
        }

        // Six worlds, six backgrounds, no spares: an entry nothing can reach is an entry nobody
        // maintains.
        var reachable = Enum.GetValues<ThemeType>()
            .Select(theme => InputNormalization.CanonicalThemeId(theme.ToString()))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            BekiLayoutAssets.Current.CanonicalThemeIds.OrderBy(id => id, StringComparer.Ordinal),
            reachable.OrderBy(id => id, StringComparer.Ordinal));
    }

    /// <summary>
    /// A book whose theme maps to nothing stops before any page is drawn, and so does a book with no
    /// personalization at all. Both used to produce a page: the first a placeholder background, the
    /// second a drawn dot field with the canonical Beki pasted on the left.
    /// </summary>
    [Fact]
    public void A_book_with_no_resolvable_world_is_refused_before_it_is_drawn()
    {
        var plan = BekiLayoutFixture.EightSpreadPlan();
        var spreads = plan.Spreads
            .Select(spread => new BekiSpreadArtwork(spread.Number, BekiLayoutFixture.SheetPng((0, 200, 120))))
            .ToList();
        var composer = new BekiPdfComposer(Options.Create(BekiLayoutFixture.ScreenProofLayout()));

        var unknown = Assert.Throws<BekiLayoutException>(() => composer.Compose(
            plan, BekiLayoutFixture.LeafPng((200, 60, 60)), spreads,
            BekiLayoutFixture.Personalization(theme: "Atlantis")));
        Assert.Equal(CompositeFailureCodes.LayoutFailed, unknown.FailureCode);

        var anonymous = Assert.Throws<BekiLayoutException>(
            () => composer.Compose(plan, BekiLayoutFixture.LeafPng((200, 60, 60)), spreads));
        Assert.Equal(CompositeFailureCodes.LayoutFailed, anonymous.FailureCode);
    }

    /// <summary>
    /// A copy of the two registry documents in a temporary tree, with no asset files beside them —
    /// so a test can put whatever it likes where an approved asset belongs.
    /// </summary>
    private sealed class DoctoredAssetTree : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("beki-doctored-").FullName;

        public DoctoredAssetTree()
        {
            var layout = Path.Combine(_root, "Assets", "BekiComposite", "layout");
            Directory.CreateDirectory(layout);

            var source = Path.Combine(AppContext.BaseDirectory, "Assets", "BekiComposite");
            File.Copy(
                Path.Combine(source, "layout", "beki_layout_asset_registry_v1.json"),
                Path.Combine(layout, "beki_layout_asset_registry_v1.json"));
            File.Copy(
                Path.Combine(source, "theme_reference_registry_v1.json"),
                Path.Combine(_root, "Assets", "BekiComposite", "theme_reference_registry_v1.json"));

            Registry = BekiLayoutAssets.Load(_root);
        }

        public BekiLayoutAssets Registry { get; }

        public void WriteEndpaper(byte[] bytes) => File.WriteAllBytes(
            Path.Combine(_root, "Assets", "BekiComposite", "layout", Registry.EndpaperPattern.FileName),
            bytes);

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
