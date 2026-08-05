using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story;
using AdventurePacks.Api.Services.Story.Validation;

namespace Adventrya.Story.Tests;

/// <summary>
/// A blueprint that passes every rule, and helpers to break it one way at a time.
///
/// Tests are written as "take a good book, introduce exactly one real fault, prove the rule
/// names it". That keeps each test about one rule, and means a passing suite is evidence the
/// rules fire on the faults they were written for rather than on anything at all.
/// </summary>
public static class StoryFixtures
{
    public const string HeroId = "tamar";
    public const string FoxId = "fox";
    public const string KeyId = "golden-key";
    public const string MapId = "glowing-map";
    public const string ForestId = "forest";
    public const string ClearingId = "clearing";

    public static CastingBible Casting() => new()
    {
        Characters =
        [
            new StoryCharacter
            {
                Id = HeroId,
                Name = "Tamar",
                Role = CharacterRole.Hero,
                Age = 6,
                Gender = "girl",
                Appearance = new CharacterAppearance
                {
                    HairColor = "dark brown", HairLength = "shoulder", HairStyle = "two braids",
                    EyeColor = "brown", SkinTone = "warm olive"
                },
                DefaultOutfit = new Outfit { Top = "red coat", Bottom = "blue trousers", Shoes = "yellow boots" },
                Personality = new CharacterPersonality
                {
                    Traits = ["cautious"], Strength = "notices small things",
                    Fear = "the dark", Want = "to find where the map leads"
                },
                Voice = new CharacterVoice { Register = "quiet, asks questions", Vocabulary = "simple" }
            },
            new StoryCharacter
            {
                Id = FoxId,
                Name = "Rust",
                Role = CharacterRole.Companion,
                Appearance = new CharacterAppearance
                {
                    HairColor = "rust orange", HairLength = "short", HairStyle = "bristled",
                    EyeColor = "amber", SkinTone = "orange fur"
                },
                DefaultOutfit = new Outfit { Top = "green scarf", Bottom = "none", Shoes = "none" },
                Personality = new CharacterPersonality
                {
                    Traits = ["boastful"], Strength = "brave when watched",
                    Fear = "butterflies", Want = "to be thought brave"
                },
                Voice = new CharacterVoice { Register = "loud, exaggerates", Vocabulary = "simple", Tics = ["I am the bravest!"] }
            }
        ],
        Visual = new VisualDirection
        {
            Style = "Pixar 3D", Palette = "warm autumn", LightingStyle = "low golden sun", Mood = "adventurous"
        }
    };

    public static BookMeta Meta() => new()
    {
        BookId = Guid.NewGuid(), ChildId = Guid.NewGuid(), Language = "en",
        WorldId = "animals", PageCount = 6, ChildAge = 6, EngineVersion = "v2"
    };

    /// <summary>A short but fully valid book. Six pages keeps the fixtures readable.</summary>
    public static StoryBlueprint Valid() => new()
    {
        Promise = "Where does the glowing map lead?",
        Answer = "To a door only Tamar's own courage can open.",
        EmotionCurve =
        [
            StoryEmotion.Wonder, StoryEmotion.Curiosity, StoryEmotion.Suspense,
            StoryEmotion.Fear, StoryEmotion.Courage, StoryEmotion.Triumph
        ],
        Locations =
        [
            new StoryLocation { Id = ForestId, Name = "Whispering forest", SensoryAnchors = ["moss", "low mist"] },
            new StoryLocation { Id = ClearingId, Name = "Glass clearing", SensoryAnchors = ["glass trees", "chimes"] }
        ],
        Objects =
        [
            new StoryObject { Id = MapId, Name = "glowing map", Significance = "shows the way" },
            new StoryObject { Id = KeyId, Name = "golden key", Significance = "opens the last door" }
        ],
        Cast = [HeroId, FoxId],
        Threads =
        [
            new RunningThread
            {
                Id = "brave-fox", Kind = ThreadKind.Joke,
                Setup = "Rust announces he is the bravest",
                Payoff = "Rust admits he was a little scared",
                SetupPage = 2, PayoffPage = 6
            }
        ],
        Surprises =
        [
            new Surprise { Kind = SurpriseKind.Character, Description = "a fox afraid of butterflies", UsedOnPage = 4 },
            new Surprise { Kind = SurpriseKind.Image, Description = "trees made of glass", UsedOnPage = 3 },
            new Surprise { Kind = SurpriseKind.Solution, Description = "the door opens to a whisper, not a push", UsedOnPage = 6 }
        ],
        Beats =
        [
            Beat(1, "find out what the map shows", "the map only glows in shadow", "the map is alive",
                NarrativePurpose.Hook, StoryEmotion.Wonder, NarrativeEnergy.Wonder, ForestId,
                [HeroId], introduced: [MapId],
                deltas: [Add(MapId), Open("where does the map lead")],
                hook: "what is the map pointing at?"),

            Beat(2, "follow the map into the trees", "Rust insists on leading and goes the wrong way",
                "Rust is louder than he is brave",
                NarrativePurpose.Relationship, StoryEmotion.Curiosity, NarrativeEnergy.Humor, ForestId,
                [HeroId, FoxId],
                deltas: [Join(FoxId), Rel(FoxId, "new friend")],
                hook: "who is following them?", threads: ["brave-fox"]),

            Beat(3, "reach the place the map is pointing to", "the way is a wall of glass trees",
                "the clearing sings when touched",
                NarrativePurpose.Discovery, StoryEmotion.Suspense, NarrativeEnergy.Discovery, ClearingId,
                [HeroId, FoxId], used: [MapId],
                deltas: [Move(ClearingId)],
                hook: "what is buried under the glass?"),

            Beat(4, "dig out what the chimes are hiding", "the light goes and Tamar is afraid of the dark",
                "she finds a golden key",
                NarrativePurpose.Puzzle, StoryEmotion.Fear, NarrativeEnergy.Tension, ClearingId,
                [HeroId, FoxId], introduced: [KeyId],
                deltas: [Add(KeyId)],
                hook: "what does the key open?"),

            Beat(5, "find the door the key belongs to", "the door has no keyhole",
                "the door listens instead of locking",
                NarrativePurpose.Twist, StoryEmotion.Courage, NarrativeEnergy.Reflection, ClearingId,
                [HeroId, FoxId], used: [KeyId],
                deltas: [Shift("brave")],
                hook: "will she dare to speak to it?"),

            Beat(6, "open the door", "she has to say what she is afraid of, out loud",
                "the door opens for the truth, and the key was only ever a promise",
                NarrativePurpose.Resolution, StoryEmotion.Triumph, NarrativeEnergy.Action, ClearingId,
                [HeroId, FoxId], used: [KeyId],
                deltas: [Resolve("where does the map lead")],
                hook: null, threads: ["brave-fox"])
        ]
    };

    public static BlueprintContext Context(StoryBlueprint blueprint, params string[] previousSurprises)
    {
        var casting = Casting();
        return new BlueprintContext
        {
            Blueprint = blueprint,
            Casting = casting,
            States = StateProjector.Project(blueprint, casting),
            Meta = Meta(),
            PreviousSurpriseSignatures = previousSurprises
        };
    }

    public static StoryBlueprint With(this StoryBlueprint blueprint, Func<StoryBeat, StoryBeat> mutate) =>
        new()
        {
            Promise = blueprint.Promise, Answer = blueprint.Answer, EmotionCurve = blueprint.EmotionCurve,
            Locations = blueprint.Locations, Objects = blueprint.Objects, Cast = blueprint.Cast,
            Threads = blueprint.Threads, Surprises = blueprint.Surprises,
            Beats = [.. blueprint.Beats.Select(mutate)]
        };

    public static StoryBeat Replace(this StoryBeat beat,
        IReadOnlyList<string>? charactersPresent = null,
        IReadOnlyList<string>? objectsUsed = null,
        IReadOnlyList<string>? objectsIntroduced = null,
        IReadOnlyList<StateDelta>? deltas = null,
        string? locationId = null) => new()
    {
        Page = beat.Page, Goal = beat.Goal, Obstacle = beat.Obstacle, Discovery = beat.Discovery,
        Action = beat.Action, Purpose = beat.Purpose, Emotion = beat.Emotion, Energy = beat.Energy,
        LocationId = locationId ?? beat.LocationId, TimeOfDay = beat.TimeOfDay, Weather = beat.Weather,
        CharactersPresent = charactersPresent ?? beat.CharactersPresent,
        ObjectsIntroduced = objectsIntroduced ?? beat.ObjectsIntroduced,
        ObjectsUsed = objectsUsed ?? beat.ObjectsUsed,
        Deltas = deltas ?? beat.Deltas, Hook = beat.Hook, ThreadRefs = beat.ThreadRefs
    };

    /// <summary>
    /// A valid book of any length, so the engine's independence from page count can be proved
    /// rather than asserted. The middle is padded with distinct goals, rotating emotions,
    /// purposes and energies, which is what the craft rules are looking for.
    /// </summary>
    public static StoryBlueprint OfLength(int pageCount)
    {
        if (pageCount < 4)
        {
            throw new ArgumentOutOfRangeException(nameof(pageCount), "a book needs at least four pages");
        }

        var emotions = new[]
        {
            StoryEmotion.Wonder, StoryEmotion.Curiosity, StoryEmotion.Suspense, StoryEmotion.Fear,
            StoryEmotion.Hope, StoryEmotion.Relief, StoryEmotion.Joy, StoryEmotion.Courage
        };
        var purposes = new[]
        {
            NarrativePurpose.Discovery, NarrativePurpose.Relationship, NarrativePurpose.Comedy,
            NarrativePurpose.Puzzle, NarrativePurpose.Danger, NarrativePurpose.Reflection,
            NarrativePurpose.Twist
        };
        var energies = new[]
        {
            NarrativeEnergy.Discovery, NarrativeEnergy.Humor, NarrativeEnergy.Tension,
            NarrativeEnergy.Reflection, NarrativeEnergy.Action, NarrativeEnergy.Wonder
        };

        var beats = new List<StoryBeat>
        {
            Beat(1, "find out what the map shows", "the map only glows in shadow", "the map is alive",
                NarrativePurpose.Hook, StoryEmotion.Wonder, NarrativeEnergy.Wonder, ForestId,
                [HeroId], introduced: [MapId],
                deltas: [Add(MapId), Open("where does the map lead")],
                hook: "what is the map pointing at?"),

            Beat(2, "follow the map into the trees", "Rust insists on leading", "Rust is louder than he is brave",
                NarrativePurpose.Relationship, StoryEmotion.Curiosity, NarrativeEnergy.Humor, ForestId,
                [HeroId, FoxId], deltas: [Join(FoxId)],
                hook: "who is following them?", threads: ["brave-fox"])
        };

        for (var page = 3; page <= pageCount - 2; page++)
        {
            var i = page - 3;
            beats.Add(Beat(page,
                $"get past the obstacle at stage {i + 1}",
                $"the way is blocked in a new manner at stage {i + 1}",
                $"something about the clearing becomes clearer at stage {i + 1}",
                purposes[i % purposes.Length],
                emotions[i % emotions.Length],
                energies[i % energies.Length],
                page == 3 ? ClearingId : ClearingId,
                [HeroId, FoxId],
                deltas: page == 3 ? [Move(ClearingId)] : [Open($"riddle {i}"), Resolve($"riddle {i}")],
                hook: $"what waits beyond stage {i + 1}?"));
        }

        beats.Add(Beat(pageCount - 1, "find the door the key belongs to", "the door has no keyhole",
            "the door listens instead of locking",
            NarrativePurpose.Twist, StoryEmotion.Courage, NarrativeEnergy.Reflection, ClearingId,
            [HeroId, FoxId], introduced: [KeyId], deltas: [Add(KeyId), Shift("brave")],
            hook: "will she dare to speak to it?"));

        beats.Add(Beat(pageCount, "open the door", "she must say what she fears, out loud",
            "the door opens for the truth",
            NarrativePurpose.Resolution, StoryEmotion.Triumph, NarrativeEnergy.Action, ClearingId,
            [HeroId, FoxId], used: [KeyId, MapId],
            deltas: [Resolve("where does the map lead")],
            hook: null, threads: ["brave-fox"]));

        var surprises = Enumerable.Range(0, StoryScale.MinimumSurprises(pageCount))
            .Select(i => new Surprise
            {
                Kind = (SurpriseKind)(i % 4),
                Description = $"an unexpected turn number {i + 1}",
                UsedOnPage = Math.Min(3 + i, pageCount)
            })
            .ToList();

        return new StoryBlueprint
        {
            Promise = "Where does the glowing map lead?",
            Answer = "To a door only Tamar's own courage can open.",
            EmotionCurve = [.. beats.Select(b => b.Emotion)],
            Locations =
            [
                new StoryLocation { Id = ForestId, Name = "Whispering forest", SensoryAnchors = ["moss", "low mist"] },
                new StoryLocation { Id = ClearingId, Name = "Glass clearing", SensoryAnchors = ["glass trees", "chimes"] }
            ],
            Objects =
            [
                new StoryObject { Id = MapId, Name = "glowing map", Significance = "shows the way" },
                new StoryObject { Id = KeyId, Name = "golden key", Significance = "opens the last door" }
            ],
            Cast = [HeroId, FoxId],
            Threads =
            [
                new RunningThread
                {
                    Id = "brave-fox", Kind = ThreadKind.Joke,
                    Setup = "Rust announces he is the bravest",
                    Payoff = "Rust admits he was a little scared",
                    SetupPage = 2, PayoffPage = pageCount
                }
            ],
            Surprises = surprises,
            Beats = beats
        };
    }

    /// <summary>A book state with nothing generated yet, for testing the append-only guarantees.</summary>
    public static BookState EmptyBookState() => new()
    {
        Meta = Meta(),
        Casting = Casting(),
        Inspiration = new Inspiration
        {
            WonderSeed = "a floating whale",
            HumorSeed = "a fox afraid of butterflies",
            VisualSeed = "trees made of glass",
            EmotionalSeed = "helping someone smaller",
            MysterySeed = "a door that only appears at sunset"
        }
    };

    private static StoryBeat Beat(
        int page, string goal, string obstacle, string discovery,
        NarrativePurpose purpose, StoryEmotion emotion, NarrativeEnergy energy,
        string location, IReadOnlyList<string> present,
        IReadOnlyList<string>? introduced = null, IReadOnlyList<string>? used = null,
        IReadOnlyList<StateDelta>? deltas = null, string? hook = null,
        IReadOnlyList<string>? threads = null) => new()
    {
        Page = page, Goal = goal, Obstacle = obstacle, Discovery = discovery,
        Action = goal, Purpose = purpose, Emotion = emotion, Energy = energy,
        LocationId = location, TimeOfDay = TimeOfDay.Afternoon, Weather = Weather.Clear,
        CharactersPresent = present, ObjectsIntroduced = introduced ?? [], ObjectsUsed = used ?? [],
        Deltas = deltas ?? [], Hook = hook, ThreadRefs = threads ?? []
    };

    private static StateDelta Add(string id) => new() { Kind = DeltaKind.AddToInventory, Target = id };
    private static StateDelta Move(string id) => new() { Kind = DeltaKind.MoveToLocation, Target = id };
    private static StateDelta Join(string id) => new() { Kind = DeltaKind.CompanionJoins, Target = id };
    private static StateDelta Open(string q) => new() { Kind = DeltaKind.OpenQuestion, Target = q };
    private static StateDelta Resolve(string q) => new() { Kind = DeltaKind.ResolveQuestion, Target = q };
    private static StateDelta Shift(string trait) => new() { Kind = DeltaKind.HeroTraitShift, Target = trait };
    private static StateDelta Rel(string other, string status) =>
        new() { Kind = DeltaKind.ChangeRelationship, Target = other, Value = status };
}
