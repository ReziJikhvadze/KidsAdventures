namespace AdventurePacks.Api.Domain;

/// <summary>
/// Bridges the world slugs the map and the frontend speak to the <see cref="ThemeType"/>
/// the story generator was built around. The two vocabularies are one-to-one, but they
/// are stored differently — a slug in <c>Worlds.Id</c>, an enum name in
/// <c>AdventurePacks.Theme</c> — so the mapping needs a home.
/// </summary>
public static class WorldThemes
{
    private static readonly Dictionary<string, ThemeType> ByWorldId = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dinosaurs"] = ThemeType.Dinosaurs,
        ["space"] = ThemeType.Space,
        ["pirates"] = ThemeType.Pirates,
        ["animals"] = ThemeType.Animals,
        ["airplanes"] = ThemeType.Airplanes,
        ["magic"] = ThemeType.Magic
    };

    public static bool TryGetTheme(string? worldId, out ThemeType theme)
    {
        theme = default;
        return worldId is not null && ByWorldId.TryGetValue(worldId, out theme);
    }

    public static ThemeType ThemeFor(string? worldId) =>
        TryGetTheme(worldId, out var theme)
            ? theme
            : throw new InvalidOperationException("ასეთი სამყარო არ არსებობს.");

    /// <summary>The slug for a theme, for legacy rows that only carry the enum.</summary>
    public static string WorldIdFor(ThemeType theme) => theme.ToString().ToLowerInvariant();
}
