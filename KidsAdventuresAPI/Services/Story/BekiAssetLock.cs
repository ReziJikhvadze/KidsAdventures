using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using AdventurePacks.Api.Services.Pdf;
using AdventurePacks.Api.Services.Story.Composite.Poses;
using SixLabors.ImageSharp;

namespace AdventurePacks.Api.Services.Story;

/// <summary>
/// Every fixed asset the book is built from could not be proven, and the book stops.
///
/// Carries the whole list rather than the first failure. An operator who is told "the endpaper hash
/// is wrong" fixes the endpaper, re-runs, and is told the ICC profile is missing; the audit's
/// complaint about this system was that it discovered its problems one paid order at a time.
/// </summary>
public sealed class BekiAssetLockException : InvalidOperationException
{
    public BekiAssetLockException(IReadOnlyList<string> failures)
        : base(
            $"{BekiAssetLock.FailureCode}: the Beki asset lock refused this book. "
            + string.Join(" ", failures))
        => Failures = failures;

    /// <summary>Always <see cref="BekiAssetLock.FailureCode"/> — the audit §10.1 code.</summary>
    public string FailureCode => BekiAssetLock.FailureCode;

    /// <summary>One sentence per asset that could not be proven, in manifest order.</summary>
    public IReadOnlyList<string> Failures { get; }
}

/// <summary>
/// What the lock needs from the outside: the ICC profile it cannot find on its own, and the
/// registries a test may want to point somewhere else.
/// </summary>
/// <remarks>
/// The ICC path and hash arrive as parameters rather than being read from
/// <c>BekiPrintPrepOptions</c> here. The options class is the print stage's, the print stage runs
/// long after the lock does, and a service that reached into another stage's configuration would
/// make the lock impossible to run at the front of fulfillment — which is the one place audit
/// §10.1 requires it: <em>before any model call</em>. The caller that owns the options passes them
/// in.
/// </remarks>
public sealed record BekiAssetLockInputs
{
    /// <summary>The locked FOGRA39 profile, absolute or relative to the app base directory.</summary>
    public required string OutputIntentIccPath { get; init; }

    /// <summary>
    /// The profile's approved SHA-256. Required, not optional.
    ///
    /// The print stage treats an empty value as "skip the check", which is how the audit found a
    /// press file whose output intent nobody had verified (P1-02: "empty <c>OutputIntentIccSha256</c>
    /// disables the ICC check"). A lock with a hole in it is not a lock, so blank fails here.
    /// </summary>
    public required string OutputIntentIccSha256 { get; init; }

    /// <summary>Where the published asset tree is. Null means <see cref="AppContext.BaseDirectory"/>.</summary>
    public string? BaseDirectory { get; init; }

    /// <summary>The layout registry to prove. Null takes the process-wide one.</summary>
    public BekiLayoutAssets? LayoutAssets { get; init; }

    /// <summary>The pose registry to prove. Null loads it from <see cref="BaseDirectory"/>.</summary>
    public BekiPoseRegistry? PoseRegistry { get; init; }

    /// <summary>
    /// Font files the bootstrap could not find. Null asks <see cref="PdfFontBootstrap"/> itself,
    /// which is what production does; a test pointing at a doctored tree supplies its own.
    /// </summary>
    public IReadOnlyList<string>? MissingFontFiles { get; init; }

    /// <summary>Font files the bootstrap found and could not prove. Null asks the bootstrap.</summary>
    public IReadOnlyList<string>? FailedFontFiles { get; init; }
}

/// <summary>
/// One row of <c>asset-lock-manifest.json</c>: what an asset is for, which file it is, and the
/// hash somebody else can check it against.
/// </summary>
public sealed record BekiAssetLockEntry
{
    [JsonPropertyName("role")] public required string Role { get; init; }

    [JsonPropertyName("file")] public required string File { get; init; }

    /// <summary>The version of the document that approved it, not of the file.</summary>
    [JsonPropertyName("version")] public required string Version { get; init; }

    [JsonPropertyName("sha256")] public required string Sha256 { get; init; }

    /// <summary>
    /// What the approving registry says the asset is for — <c>interior_body</c> for a font,
    /// <c>intro</c> for the pose the intro spread is forced to use. Null where the registry states
    /// no usage of its own, which is most of the artwork.
    ///
    /// Separate from <see cref="Role"/> because they answer to different documents: the role is the
    /// audit's vocabulary and is stable, the usage is the registry's and can change without any
    /// file changing. The pose aliases below are exactly that distinction made visible.
    /// </summary>
    [JsonPropertyName("usage")] public string? Usage { get; init; }

    /// <summary>Pixel width, for the rasters. Null for fonts and for the ICC profile.</summary>
    [JsonPropertyName("width_px")] public int? WidthPx { get; init; }

    [JsonPropertyName("height_px")] public int? HeightPx { get; init; }

    /// <summary>What the bytes themselves declare — never what the filename claims.</summary>
    [JsonPropertyName("color_profile")] public string? ColorProfile { get; init; }

    [JsonPropertyName("approval_status")] public required string ApprovalStatus { get; init; }
}

/// <summary>
/// The manifest audit §10.1 asked for: one role, one file, one canonical hash, for every fixed
/// asset in the book.
/// </summary>
public sealed record BekiAssetLockManifest
{
    [JsonPropertyName("manifest_version")]
    public required string ManifestVersion { get; init; }

    [JsonPropertyName("generated_at_utc")]
    public required DateTimeOffset GeneratedAtUtc { get; init; }

    /// <summary>
    /// Which approval documents this lock was built from, by their own version strings — the
    /// three registries the audit found governance split across.
    /// </summary>
    [JsonPropertyName("source_registries")]
    public required IReadOnlyDictionary<string, string> SourceRegistries { get; init; }

    [JsonPropertyName("assets")]
    public required IReadOnlyList<BekiAssetLockEntry> Assets { get; init; }

    /// <summary>The manifest as it is uploaded and exported: indented, snake_case, stable order.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, ManifestJson);

    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        WriteIndented = true,
        // Names come from the attributes; a policy here could only disagree with them.
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };
}

/// <summary>
/// One place that proves every fixed asset a Beki book will print, before the book is started.
///
/// The machinery to do this already existed and ran nowhere. <see cref="BekiLayoutAssets.VerifyAll"/>
/// and <see cref="BekiPoseRegistry.VerifyAllPoses"/> both had zero production callers; the fonts
/// went into the process by hardcoded path with no hash at all; the ICC check turned itself off when
/// its configured hash was blank. Audit P1-02's finding was not that any of these checks was wrong —
/// it was that the delivered book could not be proven to have been built from approved bytes,
/// because nothing had ever asked.
///
/// So this asks, once, at the front of fulfillment, and answers in two forms. A failure is
/// <see cref="FailureCode"/> and stops the book <em>before any model call</em>, which is the point:
/// a book that fails its asset lock after eight image generations has already cost money to be
/// wrong. A success is <c>asset-lock-manifest.json</c> — role, file, approving document's version,
/// SHA-256, pixel dimensions and colour profile read from the bytes, approval status — which travels
/// in the handback so the supplier can re-derive the same book from the same inputs.
///
/// Nothing here selects an asset by filename pattern, directory order or timestamp, and nothing here
/// has a fallback. Every file it names was named first by one of the three registries.
///
/// Not registered for DI by this class's own campaign — it is constructible and stateless, and the
/// fulfillment agent wires it where the ICC options are in scope.
/// </summary>
public sealed class BekiAssetLock
{
    /// <summary>
    /// The audit §10.1 code. Deliberately declared here rather than in
    /// <c>CompositeFailureCodes</c>: the lock is the only thing that raises it, and a code defined
    /// beside its one raiser cannot drift from the thing it names.
    /// </summary>
    public const string FailureCode = "ASSET_LOCK_FAILED";

    /// <summary>The manifest's file name in storage and in the handback package.</summary>
    public const string ManifestFileName = "asset-lock-manifest.json";

    /// <summary>This manifest's own contract version, bumped when the row shape changes.</summary>
    public const string ManifestVersion = "beki-asset-lock-v1";

    /// <summary>The role the FOGRA39 output intent takes in the manifest, per audit §10.1.</summary>
    public const string OutputIntentRole = "fogra39_output_intent";

    private const string Approved = "approved";

    /// <summary>
    /// Proves every fixed asset and returns the manifest describing them.
    /// </summary>
    /// <exception cref="BekiAssetLockException">
    /// Any asset missing, unreadable, hash-mismatched, unregistered or unprovable. Every failure is
    /// collected before the throw.
    /// </exception>
    public BekiAssetLockManifest Verify(BekiAssetLockInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var root = inputs.BaseDirectory ?? AppContext.BaseDirectory;
        var failures = new List<string>();
        var entries = new List<BekiAssetLockEntry>();

        var layout = TryLoadLayout(inputs, failures);
        var poses = TryLoadPoses(inputs, root, failures);

        var registries = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["layout"] = layout?.RegistryVersion ?? "unresolved",
            ["theme_reference"] = layout?.ThemeRegistryVersion ?? "unresolved",
            ["pose"] = poses?.RegistryVersion ?? "unresolved"
        };

        if (layout is not null)
        {
            // The productionization audit P1-02 asked for, verbatim: the whole-set check that had no
            // caller now has one. It is called for its own sake rather than for the manifest — it is
            // the only thing that proves the *absence* of the forbidden trial font, which no
            // per-asset walk can see — and it stops at the first bad file, so the walk below is what
            // makes every other failure visible in the same run.
            Attempt(failures, () => layout.VerifyAll());

            AppendEndpaper(layout, entries, failures);
            AppendLogo(layout, entries, failures);
            AppendIntroBackgrounds(layout, entries, failures);
            AppendFonts(layout, entries, failures);
        }

        if (poses is not null)
        {
            Attempt(failures, () => poses.VerifyAllPoses());
            AppendPoses(poses, layout, entries, failures);
        }

        CheckFontRegistration(inputs, failures);
        AppendOutputIntent(inputs, root, entries, failures);

        if (failures.Count > 0)
        {
            throw new BekiAssetLockException(failures);
        }

        return new BekiAssetLockManifest
        {
            ManifestVersion = ManifestVersion,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            SourceRegistries = registries,
            Assets = entries
        };
    }

    private static BekiLayoutAssets? TryLoadLayout(BekiAssetLockInputs inputs, List<string> failures)
    {
        if (inputs.LayoutAssets is not null)
        {
            return inputs.LayoutAssets;
        }

        try
        {
            return BekiLayoutAssets.Current;
        }
        catch (Exception ex) when (ex is BekiLayoutException or IOException or InvalidOperationException)
        {
            failures.Add($"The layout asset registry could not be read: {ex.Message}");
            return null;
        }
    }

    private static BekiPoseRegistry? TryLoadPoses(
        BekiAssetLockInputs inputs,
        string root,
        List<string> failures)
    {
        if (inputs.PoseRegistry is not null)
        {
            return inputs.PoseRegistry;
        }

        try
        {
            return BekiPoseRegistry.Load(root);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            failures.Add($"The pose registry could not be read: {ex.Message}");
            return null;
        }
    }

    private static void AppendEndpaper(
        BekiLayoutAssets layout,
        List<BekiAssetLockEntry> entries,
        List<string> failures)
        => AppendRaster(
            "endpaper_pattern_final",
            layout.EndpaperPattern.FileName,
            layout.ThemeRegistryVersion,
            layout.EndpaperPattern.Sha256,
            layout.EndpaperPatternBytes,
            entries,
            failures);

    private static void AppendLogo(
        BekiLayoutAssets layout,
        List<BekiAssetLockEntry> entries,
        List<string> failures)
    {
        try
        {
            _ = layout.CoverLogoBytes();
            entries.Add(new BekiAssetLockEntry
            {
                Role = "approved_cover_logo",
                File = layout.CoverLogo.FileName,
                Version = layout.RegistryVersion,
                Sha256 = layout.CoverLogo.Sha256,
                Usage = layout.CoverLogo.Role,
                ApprovalStatus = Approved
            });
        }
        catch (BekiLayoutException ex)
        {
            failures.Add($"Cover logo '{layout.CoverLogo.FileName}' is not the approved file: {ex.Message}");
        }
    }

    private static void AppendIntroBackgrounds(
        BekiLayoutAssets layout,
        List<BekiAssetLockEntry> entries,
        List<string> failures)
    {
        // Registry order, not alphabetical: the manifest should read the way the approval document
        // reads, so the two can be compared by eye.
        foreach (var themeId in layout.CanonicalThemeIds)
        {
            var asset = layout.IntroBackgrounds[themeId];

            AppendRaster(
                $"intro_background_{themeId}_final",
                asset.FileName,
                layout.ThemeRegistryVersion,
                asset.Sha256,
                () => layout.IntroBackgroundBytes(themeId),
                entries,
                failures);
        }
    }

    private static void AppendPoses(
        BekiPoseRegistry poses,
        BekiLayoutAssets? layout,
        List<BekiAssetLockEntry> entries,
        List<string> failures)
    {
        foreach (var pose in poses.Poses)
        {
            AppendRaster(
                $"beki_{pose.Id}_final",
                pose.FileName,
                poses.RegistryVersion,
                pose.Sha256,
                () => poses.ApprovedPoseBytes(pose.Id),
                entries,
                failures);
        }

        // The audit's suggested roles include intro_beki_pose_07_final, cover_beki_pose_final and
        // credits_beki_pose_final — the fixed placements, named by what they are for rather than by
        // which pose happens to fill them today. Those are aliases onto the same nine files, and
        // they are worth stating: the pose a placement uses is a decision recorded in a registry,
        // and a manifest that only listed the nine files would not show that the decision was made
        // or that it changed. They are derived from the registries' own usage keys, never from a
        // constant here.
        foreach (var (usage, poseId) in poses.ForcedUsage.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            AppendPoseAlias($"{usage}_beki_pose_final", poseId, usage, poses, entries, failures);
        }

        if (layout is not null)
        {
            // One file, two printed placements: the credits spread and the back cover both carry the
            // mark, and the audit names both roles.
            AppendPoseAlias(
                "credits_beki_pose_final", layout.BekiMarkPoseId, "beki_mark", poses, entries, failures);
            AppendPoseAlias(
                "cover_beki_pose_final", layout.BekiMarkPoseId, "beki_mark", poses, entries, failures);
        }
    }

    private static void AppendPoseAlias(
        string role,
        string poseId,
        string usage,
        BekiPoseRegistry poses,
        List<BekiAssetLockEntry> entries,
        List<string> failures)
    {
        try
        {
            var pose = poses.Pose(poseId);

            AppendRaster(
                role,
                pose.FileName,
                poses.RegistryVersion,
                pose.Sha256,
                () => poses.ApprovedPoseBytes(pose.Id),
                entries,
                failures,
                usage);
        }
        catch (InvalidOperationException ex)
        {
            failures.Add($"Role '{role}' names pose '{poseId}', which the pose registry does not have: {ex.Message}");
        }
    }

    private static void AppendFonts(
        BekiLayoutAssets layout,
        List<BekiAssetLockEntry> entries,
        List<string> failures)
    {
        foreach (var (fontId, font) in layout.Fonts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            try
            {
                _ = layout.VerifiedFontPath(fontId);

                entries.Add(new BekiAssetLockEntry
                {
                    Role = $"{fontId}_licensed",
                    File = font.FileName,
                    Version = layout.RegistryVersion,
                    Sha256 = font.Sha256,
                    Usage = font.Role,
                    // A font has no dimensions and no colour profile. Null rather than an invented
                    // value: a manifest that guessed would be a manifest nobody could check.
                    ApprovalStatus = Approved
                });
            }
            catch (BekiLayoutException ex)
            {
                failures.Add($"Font '{fontId}' ({font.FileName}) is not the approved file: {ex.Message}");
            }
        }

    }

    /// <summary>
    /// What the bootstrap actually managed to put into the process, which is a different question
    /// from what the registry describes on disk.
    ///
    /// Until this campaign it could quietly register a file no registry described, or skip the
    /// licensed Ottia and let the cover title fall through onto the body face. Both are book
    /// failures now, and this is where they surface. Asked unconditionally — a run whose registry
    /// could not even be read still deserves to hear which faces went in unproven.
    /// </summary>
    private static void CheckFontRegistration(BekiAssetLockInputs inputs, List<string> failures)
    {
        if (inputs.MissingFontFiles is null || inputs.FailedFontFiles is null)
        {
            // Registration is idempotent and normally happens later, inside the composer. The lock
            // runs at the front of fulfillment, so it does it here: a report read before anything
            // was registered would be two empty lists and a false clean bill.
            PdfFontBootstrap.EnsureRegistered();
        }

        foreach (var missing in inputs.MissingFontFiles ?? PdfFontBootstrap.MissingFontFiles)
        {
            failures.Add(
                $"The font file '{missing}' is not in the published output; it is registered by the "
                + "PDF bootstrap and a book set in a substitute face is not the approved book.");
        }

        foreach (var failed in inputs.FailedFontFiles ?? PdfFontBootstrap.FailedFontFiles)
        {
            failures.Add(
                $"The font file '{failed}' could not be proven against the layout asset registry and "
                + "was not registered.");
        }
    }

    private static void AppendOutputIntent(
        BekiAssetLockInputs inputs,
        string root,
        List<BekiAssetLockEntry> entries,
        List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(inputs.OutputIntentIccPath))
        {
            failures.Add("No output intent ICC profile is configured; the press colour contract has no file.");
            return;
        }

        if (string.IsNullOrWhiteSpace(inputs.OutputIntentIccSha256))
        {
            failures.Add(
                "The output intent ICC profile has no configured sha256. An unverified profile is a "
                + "statement about what the press will do with every colour in the book, made by a "
                + "file nobody checked.");
            return;
        }

        var path = Path.IsPathRooted(inputs.OutputIntentIccPath)
            ? inputs.OutputIntentIccPath
            : Path.Combine(root, inputs.OutputIntentIccPath);

        if (!File.Exists(path))
        {
            failures.Add($"The output intent ICC profile is missing at '{path}'.");
            return;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (IOException ex)
        {
            failures.Add($"The output intent ICC profile at '{path}' could not be read: {ex.Message}");
            return;
        }

        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(actual, inputs.OutputIntentIccSha256, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(
                $"The output intent ICC profile at '{path}' hashes to {actual}, and the locked "
                + $"profile is {inputs.OutputIntentIccSha256.ToLowerInvariant()}.");
            return;
        }

        entries.Add(new BekiAssetLockEntry
        {
            Role = OutputIntentRole,
            File = Path.GetFileName(path),
            Version = IccVersionOf(bytes),
            Sha256 = actual,
            ColorProfile = $"{IccColorSpaceOf(bytes)} ({IccClassOf(bytes)})",
            ApprovalStatus = Approved
        });
    }

    /// <summary>
    /// Verifies one raster's bytes and records what those bytes say about themselves.
    ///
    /// The dimensions and the colour profile are read from the file rather than from the filename,
    /// which is the whole difference between a manifest and a directory listing: the approved
    /// backgrounds are named <c>..._450x210mm_300ppi_sRGB.png</c>, and a re-export that changed all
    /// three of those things would keep the name.
    /// </summary>
    private static void AppendRaster(
        string role,
        string fileName,
        string version,
        string sha256,
        Func<byte[]> verifiedBytes,
        List<BekiAssetLockEntry> entries,
        List<string> failures,
        string? usage = null)
    {
        byte[] bytes;
        try
        {
            bytes = verifiedBytes();
        }
        catch (Exception ex) when (ex is BekiLayoutException or IOException or InvalidOperationException)
        {
            failures.Add($"Role '{role}' ({fileName}) could not be proven: {ex.Message}");
            return;
        }

        int? width = null;
        int? height = null;
        string? profile = null;

        try
        {
            var info = Image.Identify(bytes);
            width = info.Width;
            height = info.Height;
            profile = PngColorProfile(bytes);
        }
        catch (Exception ex) when (ex is ImageFormatException or NotSupportedException or ArgumentException)
        {
            // The hash matched, so these are the approved bytes — and they are not a readable
            // image. That is a broken approval, not a broken deploy, and it stops the book either
            // way rather than shipping a page nothing could draw.
            failures.Add($"Role '{role}' ({fileName}) hashes correctly and is not a readable image: {ex.Message}");
            return;
        }

        entries.Add(new BekiAssetLockEntry
        {
            Role = role,
            File = fileName,
            Version = version,
            Sha256 = sha256,
            Usage = usage,
            WidthPx = width,
            HeightPx = height,
            ColorProfile = profile,
            ApprovalStatus = Approved
        });
    }

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// How a raster declares its colour, read out of the PNG's own chunks.
    ///
    /// Not from ImageSharp's metadata: its identify pass reads the header for dimensions and skips
    /// <c>iCCP</c>, so asking it would have the manifest report every approved asset as untagged —
    /// which is a false statement in the one document that exists to be authoritative. Decoding the
    /// whole image to find out would mean holding seven 5315×2480 surfaces in memory to read four
    /// bytes.
    /// </summary>
    private static string PngColorProfile(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 8 || !bytes[..8].SequenceEqual(PngSignature))
        {
            return "unknown";
        }

        var offset = 8;
        while (offset + 12 <= bytes.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..]);
            if (length > int.MaxValue - 12 || offset + 12 + (int)length > bytes.Length)
            {
                break;
            }

            var type = bytes.Slice(offset + 4, 4);
            var data = bytes.Slice(offset + 8, (int)length);

            if (type.SequenceEqual("iCCP"u8))
            {
                return $"ICC {EmbeddedIccColorSpace(data)}";
            }

            if (type.SequenceEqual("sRGB"u8))
            {
                return "sRGB";
            }

            // Every colour chunk precedes the pixels; past here there is nothing left to learn.
            if (type.SequenceEqual("IDAT"u8))
            {
                break;
            }

            offset += 12 + (int)length;
        }

        // Said plainly rather than assumed to be sRGB. An untagged raster is exactly what the press
        // conversion has to guess about, and a manifest is the wrong place to guess for it.
        return "untagged";
    }

    /// <summary>
    /// The data colour space of a PNG's embedded profile: a null-terminated name, a compression
    /// byte, then the deflated ICC profile whose header says what it converts from.
    /// </summary>
    private static string EmbeddedIccColorSpace(ReadOnlySpan<byte> chunk)
    {
        var nul = chunk.IndexOf((byte)0);
        if (nul < 0 || nul + 2 >= chunk.Length)
        {
            return "(unreadable)";
        }

        try
        {
            using var deflated = new MemoryStream(chunk[(nul + 2)..].ToArray(), writable: false);
            using var profile = new ZLibStream(deflated, CompressionMode.Decompress);

            var header = new byte[IccHeaderBytes];
            profile.ReadExactly(header, 0, IccHeaderBytes);
            return IccColorSpaceOf(header);
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or IOException)
        {
            return "(unreadable)";
        }
    }

    /// <summary>Enough of an ICC profile to read its header fields, per the ICC spec's table 13.</summary>
    private const int IccHeaderBytes = 24;

    /// <summary>The four-character data colour space at byte 16 of an ICC header — "CMYK", "RGB".</summary>
    private static string IccColorSpaceOf(ReadOnlySpan<byte> bytes) =>
        bytes.Length < IccHeaderBytes ? "(unreadable)" : Signature(bytes.Slice(16, 4));

    /// <summary>The profile/device class at byte 12 — "prtr" for an output profile.</summary>
    private static string IccClassOf(ReadOnlySpan<byte> bytes) =>
        bytes.Length < IccHeaderBytes ? "(unreadable)" : Signature(bytes.Slice(12, 4));

    /// <summary>
    /// The profile's own version, from bytes 8–9: major, then minor and patch as one nibble each.
    ///
    /// The profile's rather than an approval document's, because an ICC file has no approval
    /// document to take one from — its bytes are the approval, and they are pinned by the SHA-256
    /// recorded beside this.
    /// </summary>
    private static string IccVersionOf(ReadOnlySpan<byte> bytes) =>
        bytes.Length < IccHeaderBytes
            ? "unknown"
            : $"{bytes[8]}.{bytes[9] >> 4}.{bytes[9] & 0x0F}";

    private static string Signature(ReadOnlySpan<byte> four)
    {
        Span<char> chars = stackalloc char[4];
        for (var index = 0; index < 4; index++)
        {
            var b = four[index];
            chars[index] = b is >= 0x20 and < 0x7F ? (char)b : '?';
        }

        return new string(chars).Trim();
    }

    private static void Attempt(List<string> failures, Action check)
    {
        try
        {
            check();
        }
        catch (Exception ex) when (ex is BekiLayoutException or IOException or InvalidOperationException)
        {
            failures.Add(ex.Message);
        }
    }
}
