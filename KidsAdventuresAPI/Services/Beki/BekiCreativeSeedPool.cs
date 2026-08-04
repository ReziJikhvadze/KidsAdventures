using AdventurePacks.Api.Domain.Beki;

namespace AdventurePacks.Api.Services.Beki;

public interface IBekiCreativeSeedPool
{
    /// <summary>
    /// Picks an approved seed for this book, avoiding anything the child's memory says was
    /// used recently.
    /// </summary>
    BekiCreativeSeed Select(BekiStoryInput input);
}

/// <summary>
/// The creative seed is chosen here, in ordinary code, and never inside the prompt.
///
/// That separation is the whole point: a model asked to "pick something random" reaches for
/// the same handful of ideas every time, which is how a catalogue ends up full of glowing
/// portals and missing crystals. Choosing outside the model makes variety observable — the
/// seed id is stored with the book, so it is possible to learn which seeds produce good
/// stories and retire the ones that do not.
///
/// Seeds are deliberately abstract. They suggest a shape, not a plot, so they can bend
/// around the Extra Wish and the child's memory rather than competing with them; the prompt
/// ranks the seed lowest of all inputs for the same reason.
/// </summary>
public sealed class BekiCreativeSeedPool : IBekiCreativeSeedPool
{
    private static readonly BekiCreativeSeed[] Seeds =
    [
        new() { SeedId = "seed-001", Tone = "warm magical mystery", StoryHook = "A silent bell is waiting for the right listener.", SceneAnchor = "A garden of sleeping blue flowers." },
        new() { SeedId = "seed-002", Tone = "playful and curious", StoryHook = "Something small keeps leaving footprints that do not match any animal.", SceneAnchor = "A workshop where half-finished toys hum quietly." },
        new() { SeedId = "seed-003", Tone = "cozy bedtime adventure", StoryHook = "A lantern only lights when someone tells it the truth.", SceneAnchor = "A harbour where paper boats carry messages." },
        new() { SeedId = "seed-004", Tone = "bright and funny", StoryHook = "A very serious creature has lost something very silly.", SceneAnchor = "A market where the stalls trade in sounds." },
        new() { SeedId = "seed-005", Tone = "gentle wonder", StoryHook = "The wind has been carrying the same unfinished song for days.", SceneAnchor = "A hillside of tall grass that leans toward music." },
        new() { SeedId = "seed-006", Tone = "quiet courage", StoryHook = "A bridge appears only for someone willing to go slowly.", SceneAnchor = "A canyon filled with drifting warm mist." },
        new() { SeedId = "seed-007", Tone = "curious and clever", StoryHook = "Every clock in one place is wrong by exactly the same amount.", SceneAnchor = "A tower room lined with patient brass instruments." },
        new() { SeedId = "seed-008", Tone = "warm friendship", StoryHook = "Someone has been leaving small gifts and refusing to be thanked.", SceneAnchor = "A courtyard with a very old, very talkative tree." },
        new() { SeedId = "seed-009", Tone = "magical problem-solving", StoryHook = "A colour has gone missing from one small corner of the world.", SceneAnchor = "A meadow where the light pools like water." },
        new() { SeedId = "seed-010", Tone = "adventurous and kind", StoryHook = "A map keeps redrawing itself to show where help is needed.", SceneAnchor = "A cliff path lit by slow-blinking glow beetles." },
        new() { SeedId = "seed-011", Tone = "whimsical discovery", StoryHook = "A door has been built with no wall around it, and it still works.", SceneAnchor = "A field of stone arches wrapped in ivy." },
        new() { SeedId = "seed-012", Tone = "tender and hopeful", StoryHook = "A creature is waiting for a friend who is very late.", SceneAnchor = "A station platform where the trains are made of cloud." },
        new() { SeedId = "seed-013", Tone = "bright morning energy", StoryHook = "The tide has left behind something that does not belong to the sea.", SceneAnchor = "A shore of smooth singing pebbles." },
        new() { SeedId = "seed-014", Tone = "mysterious but safe", StoryHook = "Footsteps echo back a moment too early.", SceneAnchor = "A library where the shelves rearrange themselves politely." },
        new() { SeedId = "seed-015", Tone = "joyful and warm", StoryHook = "A celebration cannot begin until one forgotten guest is found.", SceneAnchor = "A valley strung with lanterns between the trees." },
        new() { SeedId = "seed-016", Tone = "gentle suspense", StoryHook = "Something is knocking politely from the wrong side of a window.", SceneAnchor = "A greenhouse warm with sleeping fruit." },
        new() { SeedId = "seed-017", Tone = "inventive and hands-on", StoryHook = "A machine works perfectly except for one small honest mistake.", SceneAnchor = "A hillside windmill made of patched sails." },
        new() { SeedId = "seed-018", Tone = "soft and reassuring", StoryHook = "A shadow has been separated from the one it belongs to.", SceneAnchor = "A lamplit street after gentle rain." },
    ];

    public BekiCreativeSeed Select(BekiStoryInput input)
    {
        var recent = input.PreviousStoryMemory?.RecentPlotPatternsToAvoidEn ?? [];

        var candidates = Seeds
            .Where(seed => !recent.Any(pattern =>
                seed.StoryHook.Contains(pattern, StringComparison.OrdinalIgnoreCase) ||
                pattern.Contains(seed.SeedId, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        // If memory has ruled out everything, fall back to the full pool rather than failing:
        // a repeated seed is a far smaller problem than a book that never gets written.
        if (candidates.Length == 0)
        {
            candidates = Seeds;
        }

        // Deterministic per request, so regenerating the same book twice does not silently
        // produce a different story — and so a bad book can be reproduced while debugging.
        var index = (int)((uint)StableHash(input.RequestId) % (uint)candidates.Length);
        return candidates[index];
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = 23;
            foreach (var c in value)
            {
                hash = (hash * 31) + c;
            }

            return hash;
        }
    }
}
