namespace AdventurePacks.Api.Domain.Models;

/// <summary>
/// DiceBear Adventurer head + full-body outfit choices — stored as JSON on the child.
/// Re-rendered into OpenAI Character DNA for consistent story illustrations.
/// </summary>
public sealed class AvatarConfig
{
    public string Library { get; set; } = "adventurer";

    /// <summary>girl | boy</summary>
    public string Gender { get; set; } = "girl";

    public string SkinColor { get; set; } = "f2d3b1";
    public string Hair { get; set; } = "long01";
    public string HairColor { get; set; } = "6a4e35";
    public string Eyes { get; set; } = "variant01";
    public string Eyebrows { get; set; } = "variant01";
    public string Mouth { get; set; } = "variant01";
    public string Features { get; set; } = "none";
    public string Glasses { get; set; } = "none";
    public string Earrings { get; set; } = "none";

    /// <summary>explorer | hoodie | astronaut | captain | superhero | party</summary>
    public string Outfit { get; set; } = "explorer";

    /// <summary>Hex without #</summary>
    public string OutfitColor { get; set; } = "f07167";

    // Legacy deserialize tolerance
    public string? SkinTone { get; set; }
    public string? FaceShape { get; set; }
    public string? HairStyle { get; set; }
    public string? Accessory { get; set; }
    public string? Companion { get; set; }
}
