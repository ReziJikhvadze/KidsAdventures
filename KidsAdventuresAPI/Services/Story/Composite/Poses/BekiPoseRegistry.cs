using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AdventurePacks.Api.Services.Story.Composite.Poses;

/// <summary>
/// The nine approved Beki pose PNGs, and the promise that the bytes we composite are the bytes the
/// partners approved.
///
/// The composite pipeline exists because an image model cannot be trusted to draw Beki twice the
/// same way — so the character is never drawn at all, only pasted from a fixed file. That argument
/// only holds while the file is provably the approved one. A pose PNG that was re-exported by a
/// well-meaning tool, truncated by a bad deploy, or swapped for a v2 that nobody announced would
/// still paste perfectly and still look roughly like Beki, and the first anyone would know is a
/// printed book with the wrong character in it. So every pose is SHA-256 verified against
/// <see cref="RegistryAssetPath"/> before its first use, and a mismatch stops the book rather than
/// shipping it.
///
/// The registry JSON itself is the partner pack's own file, copied byte-for-byte — the keyword
/// table, the priority order and the hashes are all theirs. Nothing here re-states any of it in
/// C#; a pack revision is a content change, not a code change.
/// </summary>
public sealed class BekiPoseRegistry
{
    /// <summary>
    /// Resolved against <see cref="AppContext.BaseDirectory"/>, the same way
    /// <see cref="BekiIdentity.ReferenceAssetPath"/> and the fonts are — the published output tree,
    /// not the source tree, because on App Service there is no source tree.
    /// </summary>
    public const string RegistryAssetPath = "Assets/BekiComposite/beki_pose_registry_v1.json";

    /// <summary>Where the pose PNGs named by the registry live, beside the registry itself.</summary>
    public const string PoseAssetDirectory = "Assets/BekiComposite/poses";

    /// <summary>The registry's own key for the pose the intro spread is forced to use.</summary>
    public const string IntroUsageKey = "intro";

    /// <summary>
    /// Verified pose bytes, cached for the life of the process and keyed by expected hash plus
    /// absolute path.
    ///
    /// Nine 2048×2048 PNGs is roughly 14 MB resident forever, which is the deliberate trade: a
    /// twelve-spread book composites twelve times, and re-reading and re-hashing 1.6 MB per spread
    /// buys nothing once the file has been proven — the hash is in the key, so a file that changed
    /// under us cannot be served from the cache, it simply misses and is verified again. Static
    /// rather than per-instance so that a second registry instance (tests, or a scoped service)
    /// does not pay for the same nine files twice.
    /// </summary>
    private static readonly ConcurrentDictionary<string, byte[]> VerifiedPoseBytes =
        new(StringComparer.Ordinal);

    private readonly string _baseDirectory;
    private readonly Dictionary<string, BekiPose> _byId;

    private BekiPoseRegistry(
        string baseDirectory,
        string registryVersion,
        IReadOnlyList<BekiPose> poses,
        IReadOnlyList<string> priorityOrder,
        string fallbackPoseId,
        IReadOnlyDictionary<string, string> forcedUsage)
    {
        _baseDirectory = baseDirectory;
        RegistryVersion = registryVersion;
        Poses = poses;
        PriorityOrder = priorityOrder;
        FallbackPoseId = fallbackPoseId;
        ForcedUsage = forcedUsage;
        _byId = poses.ToDictionary(p => p.Id, StringComparer.Ordinal);
    }

    /// <summary>The pack's version string, recorded on every composite for traceability.</summary>
    public string RegistryVersion { get; }

    /// <summary>Every approved pose, in the registry's own order.</summary>
    public IReadOnlyList<BekiPose> Poses { get; }

    /// <summary>
    /// The order poses are considered in when matching an action — the registry's, not the pose
    /// list's. Deliberately not every pose: <c>pose_01_neutral_hover</c> carries no keywords and is
    /// reachable only as <see cref="FallbackPoseId"/>.
    /// </summary>
    public IReadOnlyList<string> PriorityOrder { get; }

    /// <summary>The pose used when no keyword matches.</summary>
    public string FallbackPoseId { get; }

    /// <summary>Contexts whose pose is fixed regardless of any action text — today, the intro.</summary>
    public IReadOnlyDictionary<string, string> ForcedUsage { get; }

    /// <summary>The intro's forced pose, which the handoff locks to the curious lean.</summary>
    public string IntroPoseId => ForcedUsage[IntroUsageKey];

    /// <summary>
    /// Reads and validates the registry. <paramref name="baseDirectory"/> exists for tests that
    /// need to point at a doctored asset tree; production passes nothing and gets the published
    /// output directory.
    /// </summary>
    public static BekiPoseRegistry Load(string? baseDirectory = null)
    {
        var root = baseDirectory ?? AppContext.BaseDirectory;
        var path = Path.Combine(root, RegistryAssetPath);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Beki pose registry missing at '{path}'. The composite pipeline cannot select a "
                + "pose without it.", path);
        }

        RegistryDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<RegistryDocument>(File.ReadAllBytes(path));
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Beki pose registry at '{path}' is not valid JSON.", ex);
        }

        if (document is null)
        {
            throw new InvalidOperationException($"Beki pose registry at '{path}' deserialized to null.");
        }

        if (string.IsNullOrWhiteSpace(document.RegistryVersion))
        {
            throw new InvalidOperationException($"Beki pose registry at '{path}' has no registry_version.");
        }

        if (document.Poses is null || document.Poses.Count == 0)
        {
            throw new InvalidOperationException($"Beki pose registry at '{path}' lists no poses.");
        }

        var poses = new List<BekiPose>(document.Poses.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in document.Poses)
        {
            if (string.IsNullOrWhiteSpace(entry.Id)
                || string.IsNullOrWhiteSpace(entry.Filename)
                || !IsSha256Hex(entry.Sha256))
            {
                throw new InvalidOperationException(
                    $"Beki pose registry at '{path}' has an entry without an id, filename or "
                    + $"lower-case 64-character sha256 (id '{entry.Id}').");
            }

            if (!seen.Add(entry.Id))
            {
                throw new InvalidOperationException(
                    $"Beki pose registry at '{path}' lists pose '{entry.Id}' twice.");
            }

            poses.Add(new BekiPose(entry.Id, entry.Filename, entry.Sha256, entry.Keywords ?? []));
        }

        var matching = document.Matching
            ?? throw new InvalidOperationException($"Beki pose registry at '{path}' has no matching block.");

        var priorityOrder = matching.PriorityOrder ?? [];
        foreach (var id in priorityOrder)
        {
            if (!seen.Contains(id))
            {
                throw new InvalidOperationException(
                    $"Beki pose registry at '{path}' orders unknown pose '{id}' in priority_order.");
            }
        }

        if (string.IsNullOrWhiteSpace(matching.FallbackPoseId) || !seen.Contains(matching.FallbackPoseId))
        {
            throw new InvalidOperationException(
                $"Beki pose registry at '{path}' names an unknown fallback pose "
                + $"'{matching.FallbackPoseId}'.");
        }

        var forcedUsage = document.ForcedUsage ?? new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (context, id) in forcedUsage)
        {
            if (!seen.Contains(id))
            {
                throw new InvalidOperationException(
                    $"Beki pose registry at '{path}' forces unknown pose '{id}' for '{context}'.");
            }
        }

        // The intro's forced pose is not optional: the handoff fixes it, IntroPoseId indexes it
        // directly, and a registry that dropped it would fail later, inside a book run, instead of
        // here at load.
        if (!forcedUsage.ContainsKey(IntroUsageKey))
        {
            throw new InvalidOperationException(
                $"Beki pose registry at '{path}' has no forced_usage entry for '{IntroUsageKey}'.");
        }

        return new BekiPoseRegistry(
            root,
            document.RegistryVersion,
            poses,
            priorityOrder,
            matching.FallbackPoseId,
            forcedUsage);
    }

    /// <summary>The registry entry for <paramref name="poseId"/>, or a throw naming it.</summary>
    public BekiPose Pose(string poseId)
        => _byId.TryGetValue(poseId, out var pose)
            ? pose
            : throw new InvalidOperationException(
                $"Beki pose '{poseId}' is not in registry '{RegistryVersion}'.");

    /// <summary>
    /// The approved bytes for <paramref name="poseId"/>, SHA-256 verified against the registry on
    /// first use and served from the cache after that.
    ///
    /// The mismatch message carries the pose id and both hashes because the only useful next step
    /// is comparing them against the partner pack by hand — an operator who is told only "hash
    /// mismatch" has to reproduce the failure to learn anything.
    /// </summary>
    public byte[] ApprovedPoseBytes(string poseId)
    {
        var pose = Pose(poseId);
        var path = Path.GetFullPath(Path.Combine(_baseDirectory, PoseAssetDirectory, pose.FileName));
        var cacheKey = string.Concat(pose.Sha256, "|", path);

        return VerifiedPoseBytes.GetOrAdd(cacheKey, _ =>
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"Approved Beki pose '{pose.Id}' is missing its asset at '{path}'.", path);
            }

            var bytes = File.ReadAllBytes(path);
            var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!string.Equals(actual, pose.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Approved Beki pose hash mismatch for '{pose.Id}' ({pose.FileName}): registry "
                    + $"expects {pose.Sha256}, file at '{path}' is {actual}. Refusing to composite "
                    + "an unapproved Beki.");
            }

            return bytes;
        });
    }

    /// <summary>
    /// Verifies every registered pose in one pass, for a startup or test check that wants the whole
    /// asset set proven rather than the poses a single book happens to reach.
    /// </summary>
    public void VerifyAllPoses()
    {
        foreach (var pose in Poses)
        {
            _ = ApprovedPoseBytes(pose.Id);
        }
    }

    private static bool IsSha256Hex([NotNullWhen(true)] string? value)
    {
        if (value is not { Length: 64 })
        {
            return false;
        }

        foreach (var c in value)
        {
            var hex = c is >= '0' and <= '9' or >= 'a' and <= 'f';
            if (!hex)
            {
                return false;
            }
        }

        return true;
    }

    private sealed record RegistryDocument
    {
        [JsonPropertyName("registry_version")] public string? RegistryVersion { get; init; }
        [JsonPropertyName("matching")] public MatchingDocument? Matching { get; init; }
        [JsonPropertyName("poses")] public List<PoseDocument>? Poses { get; init; }
        [JsonPropertyName("forced_usage")] public Dictionary<string, string>? ForcedUsage { get; init; }
    }

    private sealed record MatchingDocument
    {
        [JsonPropertyName("normalization")] public string? Normalization { get; init; }
        [JsonPropertyName("strategy")] public string? Strategy { get; init; }
        [JsonPropertyName("priority_order")] public List<string>? PriorityOrder { get; init; }
        [JsonPropertyName("fallback_pose_id")] public string? FallbackPoseId { get; init; }
    }

    private sealed record PoseDocument
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("filename")] public string? Filename { get; init; }
        [JsonPropertyName("sha256")] public string? Sha256 { get; init; }
        [JsonPropertyName("keywords")] public List<string>? Keywords { get; init; }
    }
}

/// <summary>
/// One approved pose: the id the pipeline logs, the file it pastes, the hash that proves the file,
/// and the ordered keywords that select it.
/// </summary>
/// <param name="Keywords">
/// Ordered, and the order is meaningful — the first keyword that hits is the one recorded against
/// the book, so an operator reading a log can see which word in the scenario chose the pose.
/// </param>
public sealed record BekiPose(
    string Id,
    string FileName,
    string Sha256,
    IReadOnlyList<string> Keywords);
