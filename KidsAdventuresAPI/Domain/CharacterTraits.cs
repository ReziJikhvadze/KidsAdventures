namespace AdventurePacks.Api.Domain;

/// <summary>
/// The small closed vocabularies a character is described with.
///
/// These are plain strings rather than enums on purpose: the database check
/// constraints, the API payloads and the Georgian UI all speak the same lower-case
/// tokens, and routing them through a .NET enum would mean a naming-policy fight
/// with every other enum the API already serialises in PascalCase.
/// </summary>
public static class CharacterTraits
{
    public const string TypeChild = "child";
    public const string TypeAdult = "adult";
    public const string TypeAnimal = "animal";
    public const string TypeFantasy = "fantasy";

    public const string GenderGirl = "girl";
    public const string GenderBoy = "boy";

    /// <summary>Mirrors <c>CK_Characters_CharacterType</c>.</summary>
    public static readonly IReadOnlySet<string> Types =
        new HashSet<string>(StringComparer.Ordinal) { TypeChild, TypeAdult, TypeAnimal, TypeFantasy };

    /// <summary>Mirrors <c>CK_Characters_Gender</c>.</summary>
    public static readonly IReadOnlySet<string> Genders =
        new HashSet<string>(StringComparer.Ordinal) { GenderGirl, GenderBoy };

    /// <summary>The four swatches the character form offers.</summary>
    public static readonly IReadOnlySet<string> EyeColors =
        new HashSet<string>(StringComparer.Ordinal) { "brown", "blue", "green", "grey" };

    /// <summary>Only people have a stated gender; animals and fantasy figures may skip it.</summary>
    public static bool RequiresGender(string characterType) =>
        characterType is TypeChild or TypeAdult;

    public static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}
