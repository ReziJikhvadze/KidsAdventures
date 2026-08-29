using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AdventurePacks.Api.Services.Story.Composite.Poses;

/// <summary>
/// The record of one exact Beki composite, in the partner pack's own manifest shape
/// (<c>contracts/composition_manifest_v1.schema.json</c>).
///
/// This is the receipt for the claim the whole pipeline rests on: that the character on this page
/// is the approved PNG, pasted, at these coordinates, and not something a model drew. It names the
/// pose and its hash, the pixels that were visible in the source, the size and place they were put,
/// the anchor that decided that, and the hash of what came out — enough for anyone, months later
/// and without the pipeline, to re-derive the same page and compare.
///
/// The schema sets <c>additionalProperties: false</c>, so the serialized document carries exactly
/// the schema's fields and no more. Property names are the schema's, spelled out rather than
/// produced by a naming policy: these strings are a contract with a partner, and a policy that
/// changed its mind about <c>sha256</c> would break it silently.
/// </summary>
public sealed record BekiCompositionManifest
{
    /// <summary>The schema's <c>const</c>. Bumped by the partners, never by us.</summary>
    public const string Version = "beki-exact-composite-v1";

    [JsonPropertyName("composition_version")]
    public string CompositionVersion { get; init; } = Version;

    [JsonPropertyName("canvas")]
    public required BekiCompositionSize Canvas { get; init; }

    [JsonPropertyName("base_image")]
    public required BekiCompositionFile BaseImage { get; init; }

    [JsonPropertyName("beki_layer")]
    public required BekiCompositionLayer BekiLayer { get; init; }

    [JsonPropertyName("output")]
    public required BekiCompositionFile Output { get; init; }

    /// <summary>
    /// The resampler the composite was produced with — carried on the record, deliberately absent
    /// from the JSON.
    ///
    /// The handoff (§6 step 6.4) asks the port to "lock and log one equivalent deterministic
    /// resampler", and §14 explains why it matters: the historic approved composite recorded its
    /// geometry but not its resampling engine, and that missing line is the reason a byte-for-byte
    /// match to the old proof is not required. So the name has to survive into the logs. It cannot
    /// go into the document, because the v1 schema is closed and adding a tenth field would make
    /// every manifest we write fail validation against the partners' own contract. Logged, not
    /// serialized, until the schema gains a home for it.
    ///
    /// Defaulted rather than required: there is exactly one locked resampler, so a manifest built
    /// without naming it is still telling the truth — and a required property the serializer has
    /// been told to ignore is a shape System.Text.Json refuses outright.
    /// </summary>
    [JsonIgnore]
    public string Resampler { get; init; } = BekiCompositeEngine.ResamplerName;

    /// <summary>
    /// The manifest as it is stored beside the book's other artifacts: UTF-8, two-space indent, a
    /// trailing newline — matching the reference implementation's output so the two are diffable.
    /// </summary>
    public string ToJson() => JsonSerializer.Serialize(this, ManifestJson) + "\n";

    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        WriteIndented = true,
        // The reference writer uses ensure_ascii=False; nothing in a manifest is non-ASCII today
        // (filenames and hashes only), but a Georgian filename should round-trip as itself rather
        // than as a run of \u escapes.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

/// <summary>A pixel size — the schema's <c>size</c> and <c>canvas</c> shape.</summary>
public sealed record BekiCompositionSize
{
    [JsonPropertyName("width_px")] public required int WidthPx { get; init; }
    [JsonPropertyName("height_px")] public required int HeightPx { get; init; }
}

/// <summary>A pixel position — the schema's <c>point</c>.</summary>
public sealed record BekiCompositionPoint
{
    [JsonPropertyName("x_px")] public required int XPx { get; init; }
    [JsonPropertyName("y_px")] public required int YPx { get; init; }
}

/// <summary>A pixel rectangle — the schema's <c>rect</c>, used for the source alpha box.</summary>
public sealed record BekiCompositionRect
{
    [JsonPropertyName("x_px")] public required int XPx { get; init; }
    [JsonPropertyName("y_px")] public required int YPx { get; init; }
    [JsonPropertyName("width_px")] public required int WidthPx { get; init; }
    [JsonPropertyName("height_px")] public required int HeightPx { get; init; }
}

/// <summary>A file and the hash that identifies it — the schema's <c>fileHash</c>.</summary>
public sealed record BekiCompositionFile
{
    [JsonPropertyName("file")] public required string File { get; init; }
    [JsonPropertyName("sha256")] public required string Sha256 { get; init; }
}

/// <summary>The normalized anchor the placement was derived from, as fractions of the canvas.</summary>
public sealed record BekiCompositionAnchor
{
    [JsonPropertyName("visible_center_x")] public required double VisibleCenterX { get; init; }
    [JsonPropertyName("visible_center_y")] public required double VisibleCenterY { get; init; }
    [JsonPropertyName("visible_height")] public required double VisibleHeight { get; init; }
}

/// <summary>
/// Everything about the pasted Beki layer.
///
/// The four false flags are not defensive padding. They are the manifest asserting, per page, that
/// the approved artwork was not mirrored, rotated, warped or redrawn — the four things the handoff
/// forbids and the four things a compositing library makes trivially easy to do by accident. They
/// are constants here because the engine has no code that could set them true; the manifest simply
/// says so out loud where a printer or a partner can read it.
/// </summary>
public sealed record BekiCompositionLayer
{
    [JsonPropertyName("pose_id")] public required string PoseId { get; init; }
    [JsonPropertyName("file")] public required string File { get; init; }
    [JsonPropertyName("sha256")] public required string Sha256 { get; init; }
    [JsonPropertyName("source_alpha_bbox")] public required BekiCompositionRect SourceAlphaBbox { get; init; }
    [JsonPropertyName("rendered_size_px")] public required BekiCompositionSize RenderedSizePx { get; init; }
    [JsonPropertyName("placement_px")] public required BekiCompositionPoint PlacementPx { get; init; }
    [JsonPropertyName("normalized_anchor")] public required BekiCompositionAnchor NormalizedAnchor { get; init; }

    /// <summary>
    /// Always 1.0, and written as <c>1.0</c> rather than <c>1</c>.
    ///
    /// Both satisfy the schema — JSON Schema compares numbers by value — but every manifest the
    /// partners produced writes <c>1.0</c>, and a receipt that is diffed by hand against theirs
    /// should not differ in a place that means nothing.
    /// </summary>
    [JsonPropertyName("opacity")]
    [JsonConverter(typeof(DecimalPointDoubleConverter))]
    public double Opacity { get; init; } = 1.0;

    [JsonPropertyName("mirrored")] public bool Mirrored { get; init; }
    [JsonPropertyName("rotated")] public bool Rotated { get; init; }
    [JsonPropertyName("warped")] public bool Warped { get; init; }
    [JsonPropertyName("redrawn")] public bool Redrawn { get; init; }
}

/// <summary>
/// Writes a whole-numbered double as <c>1.0</c> instead of <c>1</c>, so the manifest reads like the
/// reference implementation's. Values with a fraction are written normally.
/// </summary>
internal sealed class DecimalPointDoubleConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetDouble();

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
    {
        if (double.IsFinite(value) && value == Math.Floor(value))
        {
            writer.WriteRawValue(value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
            return;
        }

        writer.WriteNumberValue(value);
    }
}
