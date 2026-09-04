using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Services.Pdf;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Composite.Poses;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The asset lock, and the audit finding that produced it.
///
/// P1-02's complaint was not that a check failed. It was that the delivered book could not be
/// proven to have been built from approved bytes at all: the whole-set verifications existed and
/// had no callers, five font files went into the process by hardcoded path with no hash, and the
/// ICC check silently disabled itself when its configured hash was blank. Every one of those paths
/// succeeded, and a successful path that proves nothing is what the supplier rejected.
///
/// So these tests are about what must now fail, and about the one artifact that has to exist when
/// nothing does.
/// </summary>
public class BekiAssetLockTests
{
    /// <summary>The production ICC inputs, read from the options the print stage actually runs with.</summary>
    private static BekiAssetLockInputs LockedInputs() => new()
    {
        OutputIntentIccPath = new BekiPrintPrepOptions().OutputIntentIccPath,
        OutputIntentIccSha256 = new BekiPrintPrepOptions().OutputIntentIccSha256
    };

    [Fact]
    public void The_lock_proves_the_whole_asset_set_and_writes_the_manifest()
    {
        var manifest = new BekiAssetLock().Verify(LockedInputs());

        Assert.Equal(BekiAssetLock.ManifestVersion, manifest.ManifestVersion);

        // All three approval documents are named by their own version — the audit's "governance
        // split across three registries" turned into a record of which document approved what.
        Assert.Equal("beki-layout-assets-v2.0", manifest.SourceRegistries["layout"]);
        Assert.Equal("beki-theme-references-v1", manifest.SourceRegistries["theme_reference"]);
        Assert.Equal("beki-pose-registry-v1", manifest.SourceRegistries["pose"]);

        // Nothing may be approved-with-reservations: the lock either proved a file or threw.
        Assert.All(manifest.Assets, entry => Assert.Equal("approved", entry.ApprovalStatus));
        Assert.All(manifest.Assets, entry => Assert.Equal(64, entry.Sha256.Length));

        // One role, one canonical hash (audit §10.1) — a role naming two files would be exactly the
        // ambiguity the section forbids.
        var roles = manifest.Assets.Select(entry => entry.Role).ToList();
        Assert.Equal(roles.Count, roles.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// Every role audit §10.1 lists is in the manifest, spelled the way the audit spells it.
    ///
    /// The suggested list is the supplier's vocabulary for the handback, and a manifest that
    /// invented its own names would be a manifest the supplier has to translate before they can
    /// check anything against it.
    /// </summary>
    [Fact]
    public void The_manifest_covers_every_role_the_audit_named()
    {
        var manifest = new BekiAssetLock().Verify(LockedInputs());
        var roles = manifest.Assets.Select(entry => entry.Role).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("endpaper_pattern_final", roles);
        Assert.Contains("intro_background_forest_final", roles);
        Assert.Contains("fogra39_output_intent", roles);

        // The fixed Beki placements, by what they are for rather than by which pose fills them.
        Assert.Contains("intro_beki_pose_final", roles);
        Assert.Contains("cover_beki_pose_final", roles);
        Assert.Contains("credits_beki_pose_final", roles);

        Assert.Contains("noto_sans_georgian_regular_licensed", roles);
        Assert.Contains("ottia_regular_ttf_licensed", roles);

        // The two faces audit P1-02 found in no registry at all are now locked like everything else.
        Assert.Contains("noto_sans_georgian_semibold_licensed", roles);
        Assert.Contains("noto_serif_georgian_semibold_licensed", roles);

        // Every world a book can be set in, and every approved pose — not only the ones a
        // particular book reaches.
        foreach (var themeId in BekiLayoutAssets.Current.CanonicalThemeIds)
        {
            Assert.Contains($"intro_background_{themeId}_final", roles);
        }

        foreach (var pose in BekiPoseRegistry.Load().Poses)
        {
            Assert.Contains($"beki_{pose.Id}_final", roles);
        }
    }

    /// <summary>
    /// Dimensions and colour profile are read out of the bytes, not off the filename.
    ///
    /// The approved backgrounds are called <c>..._450x210mm_300ppi_sRGB.png</c>. A re-export that
    /// halved the resolution and stripped the profile would keep every character of that name,
    /// which is precisely why the audit asked for dimensions and colour profile in the manifest.
    /// </summary>
    [Fact]
    public void The_manifest_records_what_the_bytes_say_rather_than_what_the_filename_claims()
    {
        var manifest = new BekiAssetLock().Verify(LockedInputs());

        var endpaper = manifest.Assets.Single(entry => entry.Role == "endpaper_pattern_final");
        Assert.Equal(5315, endpaper.WidthPx);
        Assert.Equal(2480, endpaper.HeightPx);
        Assert.StartsWith("ICC ", endpaper.ColorProfile, StringComparison.Ordinal);

        var pose = manifest.Assets.Single(entry => entry.Role == "intro_beki_pose_final");
        Assert.Equal(2048, pose.WidthPx);
        Assert.Equal(2048, pose.HeightPx);
        Assert.Equal("intro", pose.Usage);

        // The output intent is the press's colour contract; the manifest states which colour space
        // it actually converts into.
        var icc = manifest.Assets.Single(entry => entry.Role == BekiAssetLock.OutputIntentRole);
        Assert.Contains("Cmyk", icc.ColorProfile, StringComparison.OrdinalIgnoreCase);
        Assert.Null(icc.WidthPx);

        // A font has no pixels, and the manifest says so rather than inventing a number.
        var font = manifest.Assets.Single(entry => entry.Role == "noto_sans_georgian_regular_licensed");
        Assert.Null(font.WidthPx);
        Assert.Equal("interior_body", font.Usage);
    }

    [Fact]
    public void The_manifest_serializes_as_the_supplier_asked_for_it()
    {
        var json = new BekiAssetLock().Verify(LockedInputs()).ToJson();

        Assert.Contains("\"manifest_version\"", json, StringComparison.Ordinal);
        Assert.Contains("\"source_registries\"", json, StringComparison.Ordinal);
        Assert.Contains("\"approval_status\": \"approved\"", json, StringComparison.Ordinal);
        Assert.Contains("\"role\": \"fogra39_output_intent\"", json, StringComparison.Ordinal);
        Assert.Equal("asset-lock-manifest.json", BekiAssetLock.ManifestFileName);
    }

    /// <summary>
    /// A blank configured hash used to mean "skip the check". Now it means the book stops.
    ///
    /// Audit P1-02, exactly: an empty <c>OutputIntentIccSha256</c> disabled the ICC verification,
    /// so a press file could be assembled around an output intent nobody had ever checked — a
    /// statement about what the press will do with every colour in the book, made by a file that
    /// could have been anything.
    /// </summary>
    [Fact]
    public void A_blank_icc_hash_is_a_lock_failure_rather_than_a_skipped_check()
    {
        var failure = Assert.Throws<BekiAssetLockException>(() => new BekiAssetLock().Verify(
            LockedInputs() with { OutputIntentIccSha256 = string.Empty }));

        Assert.Equal("ASSET_LOCK_FAILED", failure.FailureCode);
        Assert.Contains(failure.Failures, detail =>
            detail.Contains("no configured sha256", StringComparison.Ordinal));
    }

    [Fact]
    public void An_icc_profile_that_is_not_the_locked_one_is_refused()
    {
        var swapped = Path.Combine(Directory.CreateTempSubdirectory("beki-icc-").FullName, "swapped.icc");
        File.WriteAllBytes(swapped, "this is not the locked FOGRA39 profile"u8.ToArray());

        try
        {
            var failure = Assert.Throws<BekiAssetLockException>(() => new BekiAssetLock().Verify(
                LockedInputs() with { OutputIntentIccPath = swapped }));

            Assert.Contains(failure.Failures, detail =>
                detail.Contains("hashes to", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(swapped)!, recursive: true);
        }
    }

    [Fact]
    public void A_missing_icc_profile_is_a_lock_failure()
    {
        var failure = Assert.Throws<BekiAssetLockException>(() => new BekiAssetLock().Verify(
            LockedInputs() with { OutputIntentIccPath = "Assets/BekiComposite/print/not-here.icc" }));

        Assert.Contains(failure.Failures, detail => detail.Contains("missing at", StringComparison.Ordinal));
    }

    /// <summary>
    /// A font the bootstrap could not prove fails the book, even though the file on disk hashes
    /// correctly against the registry.
    ///
    /// The two are different questions. The registry proves the bytes in the asset tree; the
    /// bootstrap reports what actually went into the process — and until this campaign it could
    /// register a face no registry described, or skip the licensed Ottia and let the cover title
    /// fall through onto the body face without anybody hearing about it.
    /// </summary>
    [Fact]
    public void A_face_the_bootstrap_could_not_prove_stops_the_book()
    {
        var failure = Assert.Throws<BekiAssetLockException>(() => new BekiAssetLock().Verify(
            LockedInputs() with { FailedFontFiles = ["Ottia-v01-Regular.ttf"] }));

        Assert.Contains(failure.Failures, detail =>
            detail.Contains("Ottia-v01-Regular.ttf", StringComparison.Ordinal)
            && detail.Contains("could not be proven", StringComparison.Ordinal));
    }

    /// <summary>
    /// And so does a face that is simply not there. The licensed Ottia used to be optional on this
    /// path: absent, the cover printed in Noto and the run reported success.
    /// </summary>
    [Fact]
    public void A_missing_face_stops_the_book_instead_of_falling_back_to_another()
    {
        var failure = Assert.Throws<BekiAssetLockException>(() => new BekiAssetLock().Verify(
            LockedInputs() with { MissingFontFiles = ["Ottia-v01-Regular.ttf"] }));

        Assert.Contains(failure.Failures, detail =>
            detail.Contains("not in the published output", StringComparison.Ordinal));
    }

    /// <summary>
    /// An asset whose bytes are not the approved ones stops the book by name, before any model call.
    ///
    /// Proven against a doctored tree rather than by trusting the code path: the layout registry is
    /// pointed at a temporary folder whose endpaper is a different picture, which is what a
    /// re-export by a well-meaning tool looks like from here. The pose registry stays real, so the
    /// failure list is about the one thing that changed.
    /// </summary>
    [Fact]
    public void An_asset_that_is_not_the_approved_one_refuses_the_book()
    {
        using var tree = new DoctoredLayoutTree();
        tree.WriteEndpaper("this is not the approved pattern"u8.ToArray());

        var failure = Assert.Throws<BekiAssetLockException>(() => new BekiAssetLock().Verify(
            LockedInputs() with
            {
                LayoutAssets = tree.Registry,
                PoseRegistry = BekiPoseRegistry.Load(),
                MissingFontFiles = [],
                FailedFontFiles = []
            }));

        Assert.Equal(BekiAssetLock.FailureCode, failure.FailureCode);
        Assert.Contains(failure.Failures, detail =>
            detail.Contains("BEKI_Endpaper_Pattern_Approved", StringComparison.Ordinal)
            && detail.Contains("hash mismatch", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The lock reports everything it found, not the first thing.
    ///
    /// The audit's account of this system was that it discovered its problems one paid order at a
    /// time. An operator who fixes the endpaper, re-runs, and is then told about the backgrounds is
    /// living that story again.
    /// </summary>
    [Fact]
    public void The_failure_carries_every_asset_it_could_not_prove()
    {
        using var tree = new DoctoredLayoutTree();

        var failure = Assert.Throws<BekiAssetLockException>(() => new BekiAssetLock().Verify(
            LockedInputs() with
            {
                LayoutAssets = tree.Registry,
                PoseRegistry = BekiPoseRegistry.Load(),
                MissingFontFiles = [],
                FailedFontFiles = []
            }));

        // Nothing was installed in the doctored tree: the endpaper, all six backgrounds and every
        // registered font are missing, and all of them are named.
        Assert.True(failure.Failures.Count >= 7, failure.Message);
        Assert.Contains(failure.Failures, detail =>
            detail.Contains("intro_background_forest_final", StringComparison.Ordinal));
        Assert.Contains(failure.Failures, detail =>
            detail.Contains("Ottia-v01-Regular.ttf", StringComparison.Ordinal));
    }

    /// <summary>
    /// The bootstrap proves the five faces it registers, and reports nothing wrong with a healthy
    /// tree — the acceptance half of P1-02, because the lock consumes both lists.
    /// </summary>
    [Fact]
    public void The_font_bootstrap_proves_every_face_it_registers()
    {
        PdfFontBootstrap.EnsureRegistered();

        Assert.Empty(PdfFontBootstrap.MissingFontFiles);
        Assert.Empty(PdfFontBootstrap.FailedFontFiles);
    }

    /// <summary>
    /// A copy of the two layout registry documents in a temporary tree with no assets beside them,
    /// so a test can put whatever it likes where an approved asset belongs.
    /// </summary>
    private sealed class DoctoredLayoutTree : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("beki-lock-").FullName;

        public DoctoredLayoutTree()
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
