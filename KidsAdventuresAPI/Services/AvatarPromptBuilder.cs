using System.Text.Json;
using AdventurePacks.Api.Domain.Models;

namespace AdventurePacks.Api.Services;

internal static class AvatarPromptBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static AvatarConfig? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AvatarConfig>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static string Serialize(AvatarConfig config) =>
        JsonSerializer.Serialize(config, JsonOptions);

    /// <summary>
    /// Character DNA for OpenAI from DiceBear Adventurer config.
    /// Describes a Pixar-style 3D kids-film hero matching the parent's Adventurer choices.
    /// </summary>
    public static string BuildAppearanceDescription(AvatarConfig config, int age)
    {
        // Support legacy custom configs that still have SkinTone/HairStyle
        if (!string.Equals(config.Library, "adventurer", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(config.SkinTone))
        {
            return BuildLegacyDescription(config, age);
        }

        var gender = DescribeGender(config.Gender);
        var skin = DescribeSkinHex(config.SkinColor);
        var hair = DescribeHair(config.Hair, config.HairColor);
        var eyes = DescribeEyes(config.Eyes);
        var brows = DescribeBrows(config.Eyebrows);
        var mouth = DescribeMouth(config.Mouth);
        var features = DescribeFeatures(config.Features);
        var glasses = DescribeGlasses(config.Glasses);
        var earrings = DescribeEarrings(config.Earrings);
        var outfit = DescribeOutfit(config.Outfit, config.OutfitColor);

        return
            "CHARACTER DNA (lock identity across every scene): " +
            $"A {age}-year-old {gender} child hero in vibrant Pixar-style 3D animated movie art, " +
            "inspired by a friendly Adventurer cartoon face (big expressive eyes, soft rounded features). " +
            $"FULL BODY always visible: head, torso, both arms with hands, both legs with feet/shoes. " +
            $"Skin: {skin}. Hair: {hair}. Eyes: {eyes}. Brows: {brows}. Expression/mouth: {mouth}.{features}{glasses}{earrings} " +
            $"Outfit: {outfit}. " +
            "Proportions: oversized expressive head, short torso, short limbs, visible hands and shoes (classic kids-film silhouette). " +
            "Art direction: soft subsurface skin shading, glossy eye highlights, warm studio lighting, " +
            "rounded friendly shapes, high-end children's animated film production quality. " +
            "Keep the same face, hair, skin, outfit, hands, and shoes in every illustration. Never crop to a floating head.";
    }

    private static string BuildLegacyDescription(AvatarConfig config, int age)
    {
        var gender = DescribeGender(config.Gender);
        return
            $"A cheerful {age}-year-old {gender} child hero in Pixar-style 3D animated movie art, " +
            "big expressive eyes, rounded friendly features, warm confident smile.";
    }

    private static string DescribeGender(string value) => value.ToLowerInvariant() switch
    {
        "boy" => "boy",
        _ => "girl",
    };

    private static string DescribeSkinHex(string hex) => hex.ToLowerInvariant().Replace("#", "") switch
    {
        "f2d3b1" => "fair peach",
        "ecad80" => "light warm",
        "d08b5b" => "warm medium-light",
        "ae5d29" => "medium warm brown",
        "9e5622" => "sun-kissed tan",
        "763900" => "deep brown",
        "614335" => "rich deep brown",
        _ => "warm natural",
    };

    private static string DescribeHair(string style, string colorHex)
    {
        var length = style.StartsWith("long", StringComparison.OrdinalIgnoreCase)
            ? "long"
            : style.StartsWith("short", StringComparison.OrdinalIgnoreCase)
                ? "short"
                : "styled";

        var color = colorHex.ToLowerInvariant().Replace("#", "") switch
        {
            "0e0e0e" => "jet-black",
            "562306" => "dark brown",
            "6a4e35" => "warm brown",
            "ac6511" => "copper",
            "cb6820" => "auburn",
            "ab2a18" => "red",
            "b9a05f" => "dirty-blonde",
            "e5d7a3" => "blonde",
            "afafaf" => "silver",
            "dba3be" => "soft pink",
            _ => "brown",
        };

        return $"{length} {color} hair (Adventurer style {style})";
    }

    private static string DescribeEyes(string variant) => variant.ToLowerInvariant() switch
    {
        "variant02" => "happy bright eyes",
        "variant03" => "curious wide eyes",
        "variant05" => "wide expressive eyes",
        "variant08" => "dreamy soft eyes",
        "variant09" => "bold expressive eyes",
        _ => "big bright friendly eyes",
    };

    private static string DescribeBrows(string variant) => variant.ToLowerInvariant() switch
    {
        "variant03" => "gently arched brows",
        "variant04" => "straight soft brows",
        "variant05" => "bold brows",
        "variant07" => "raised curious brows",
        _ => "soft natural brows",
    };

    private static string DescribeMouth(string variant) => variant.ToLowerInvariant() switch
    {
        "variant02" or "variant03" or "variant05" or "variant08" or "variant10" => "a joyful happy smile",
        "variant06" => "a curious soft smile",
        "variant07" => "a brave confident smile",
        _ => "a warm friendly smile",
    };

    private static string DescribeFeatures(string value) => value.ToLowerInvariant() switch
    {
        "blush" => " Soft rosy blush on the cheeks.",
        "freckles" => " Light freckles across the nose and cheeks.",
        "birthmark" => " A small distinctive birthmark.",
        _ => string.Empty,
    };

    private static string DescribeGlasses(string value) =>
        value is null or "" or "none"
            ? string.Empty
            : " Wearing friendly kid glasses.";

    private static string DescribeEarrings(string value) =>
        value is null or "" or "none"
            ? string.Empty
            : " Small cute earrings.";

    private static string DescribeOutfit(string outfit, string colorHex)
    {
        var color = colorHex.ToLowerInvariant().Replace("#", "") switch
        {
            "4dabf7" => "sky-blue",
            "63e6be" => "mint-green",
            "fcc419" => "sunny yellow",
            "b197fc" => "lavender",
            "364fc7" => "navy",
            "e8590c" => "orange",
            "2f9e44" => "forest green",
            _ => "coral",
        };

        var baseOutfit = outfit.ToLowerInvariant() switch
        {
            "hoodie" => "a cozy hoodie, comfortable pants, and sneakers",
            "astronaut" => "a kid-sized space suit with boots and gloves",
            "captain" => "a tiny captain's coat, dark pants, and boots",
            "superhero" => "a superhero tunic with a cape, pants, and boots",
            "party" => "a festive party outfit with matching shoes",
            _ => "an explorer vest, adventure shorts, and sturdy boots",
        };

        return $"{baseOutfit} with {color} accent colors; both hands and both feet clearly visible";
    }
}
