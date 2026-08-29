using System.Text.Json;
using AdventurePacks.Api.Domain.Enums;
using SixLabors.ImageSharp;

namespace AdventurePacks.Api.Services.Story.Composite;

/// <summary>
/// The one word a failed composite book is allowed to fail with.
///
/// The list is not invented here: <c>pipeline_config_v1.json</c> carries it under
/// <c>failure_codes</c>, and the illustration pipeline's operators read those strings in logs and
/// in the admin view. Naming them as constants rather than typing the literal at each throw site
/// is what stops a book stopping with "INVALID_INPUT" on one path and "INVALID_BOOK_INPUT" on
/// another — the same fault, two words, and a support answer that depends on which branch ran.
/// CompositeContractsTests asserts this set equals the config's, so a code the supplier adds
/// later cannot sit in the config unimplemented and unnoticed.
/// </summary>
public static class CompositeFailureCodes
{
    /// <summary>The parent's own details could not be mapped. Nothing paid for has happened yet.</summary>
    public const string InvalidBookInput = "INVALID_BOOK_INPUT";

    /// <summary>The story call failed, or its result could not be mapped to the boundary.</summary>
    public const string StoryFailed = "STORY_FAILED";

    /// <summary>Two Visual Scenario attempts, both invalid.</summary>
    public const string VisualScenarioFailed = "VISUAL_SCENARIO_FAILED";

    /// <summary>
    /// Two attempts at reading the child's four identity attributes from the photograph, both
    /// unusable — so the book stops before its first picture.
    ///
    /// Added by the v1.1 campaign, and deliberately terminal rather than a warning. The alternative
    /// was tried: a book drawn with identity riding on the photograph alone came back with a
    /// visibly different child on every spread, and passed all eight of its own reviews on the way.
    /// A soft-degrade here would restore exactly that book, minus the evidence.
    /// </summary>
    public const string IdentitySpecFailed = "IDENTITY_SPEC_FAILED";

    public const string ImageGenerationFailed = "IMAGE_GENERATION_FAILED";

    public const string ImageQaFailed = "IMAGE_QA_FAILED";

    /*
      Reserved. The layout, print and preflight stages are a later campaign, but the codes are
      declared now because they are already in the supplier's config: a stage added later must
      reuse the agreed word rather than mint a synonym for it.
    */

    public const string TextOverflow = "TEXT_OVERFLOW";

    public const string LayoutFailed = "LAYOUT_FAILED";

    public const string PrintPreflightFailed = "PRINT_PREFLIGHT_FAILED";

    /// <summary>Every code, in the config's own order. Used by the equivalence test.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        InvalidBookInput,
        StoryFailed,
        VisualScenarioFailed,
        IdentitySpecFailed,
        ImageGenerationFailed,
        ImageQaFailed,
        TextOverflow,
        LayoutFailed,
        PrintPreflightFailed
    ];
}

/// <summary>
/// Where the supplied BEKI configuration and contracts live once the build has copied them.
///
/// They are read at runtime rather than transcribed into C#. The age bands, the canonical theme
/// ids, the failure vocabulary and the JSON Schemas are all documents the illustration pipeline's
/// supplier owns and revises; a copy in code is a second source of truth that nobody updates in
/// the same commit, and the symptom of the drift is a book that validates here and is rejected by
/// the people printing it.
///
/// Resolved against <see cref="AppContext.BaseDirectory"/> — the folder the app was published
/// into — exactly as the existing Beki reference asset and the PDF fonts are.
/// </summary>
internal static class CompositeAssets
{
    private static readonly string Root =
        Path.Combine(AppContext.BaseDirectory, "Assets", "BekiComposite");

    public static string PipelineConfigPath => Path.Combine(Root, "pipeline_config_v1.json");

    public static string ThemeRegistryPath => Path.Combine(Root, "theme_reference_registry_v1.json");

    public static string ContractPath(string fileName) => Path.Combine(Root, "contracts", fileName);

    /// <summary>
    /// Reads one of the supplied JSON documents, failing loudly and by name.
    ///
    /// A missing asset here is a deployment fault, not a book fault: the alternative — quietly
    /// falling back to hardcoded bands — would let a mis-published build write eight-spread books
    /// against rules nobody approved, and the first evidence would be a printed copy.
    /// </summary>
    public static JsonDocument Read(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"The BEKI composite pipeline needs '{path}', and it is not in the published "
                + "output. Check that Assets/BekiComposite is shipped by the API csproj.");
        }

        return JsonDocument.Parse(File.ReadAllText(path));
    }
}

/// <summary>
/// What the application is handed when a parent buys a book, in the handoff's own field names
/// (§6 Step 0). Nothing here has been mapped yet: the age is a number, and the gender and theme
/// are whatever the existing backend stored.
/// </summary>
public sealed record BookGenerationInput
{
    public required string ChildName { get; init; }

    public required int ChildAge { get; init; }

    /// <summary>Whatever the journey wrote: girl | boy today, and historically other spellings.</summary>
    public required string ChildGender { get; init; }

    /// <summary>
    /// The backend's own theme value — a <see cref="ThemeType"/> name, its numeric value, or a
    /// canonical BEKI theme id. Mapped once, never guessed.
    /// </summary>
    public required string ThemeId { get; init; }

    /// <summary>Where the child's photograph is stored. Read by the image stage, never by Story.</summary>
    public required string ChildPhotoRef { get; init; }

    /// <summary>
    /// The eye colour the parent typed on the order form, when the stored row carries one.
    ///
    /// Beside <see cref="ChildPhotoRef"/> and for the same reason: the image stage needs it and
    /// Story must never see it. It reaches exactly one place — the identity spec, where a parent
    /// looking at their own child overrides a model looking at a photograph in which the eyes may
    /// be forty pixels wide — and it cannot reach the planner, because
    /// <see cref="NormalizedBookInput"/> has nowhere to put it.
    ///
    /// Optional in every sense: absent on most rows, and an absent value simply leaves the derived
    /// attribute standing.
    /// </summary>
    public string? LegacyEyeColor { get; init; }

    /// <summary>
    /// The parent's legacy Extra Wish, if a stored row still carries one.
    ///
    /// Declared and then deliberately never read. The handoff removes Extra Wish from the MVP and
    /// says not to send it to any model, and old rows still hold the text — so the safest shape is
    /// one where the field is visible at the boundary and demonstrably dropped there, rather than
    /// one where its absence depends on every future caller remembering not to pass it on.
    /// </summary>
    public string? LegacyExtraWish { get; init; }
}

/// <summary>
/// The only four things the Story call is allowed to know about the child.
///
/// The photograph, the appearance description, the eye colour and the Extra Wish are all absent
/// by construction and not by discipline: this record has nowhere to put them, so a later edit
/// that wanted to "just pass the photo through" would have to change the type and be seen doing it.
/// </summary>
public sealed record NormalizedBookInput
{
    public required string ChildName { get; init; }

    /// <summary>The number the parent gave, kept for the Story boundary's own input contract.</summary>
    public required int ChildAge { get; init; }

    /// <summary>One of <c>1-2</c>, <c>3-5</c>, <c>6+</c> — mapped once, here, and never again.</summary>
    public required string AgeBand { get; init; }

    /// <summary><c>girl</c> or <c>boy</c>. There is no third value and no default.</summary>
    public required string ChildGender { get; init; }

    /// <summary>The canonical BEKI theme id: clouds | space | forest | ocean | magic | dinosaurs.</summary>
    public required string ThemeId { get; init; }

    /// <summary>
    /// The same theme as the backend's own enum. Not a second input — one value in the two
    /// spellings the pipeline needs: the canonical id addresses the supplied theme reference PNG,
    /// and the enum is what the Georgian world copy in <see cref="StoryWorlds"/> is keyed by.
    /// </summary>
    public required ThemeType Theme { get; init; }
}

/// <summary>
/// The outcome of the boundary. Either four mapped fields and a readable photograph, or a failure
/// code and the reasons — never a half-mapped input that a later stage completes by guessing.
/// </summary>
public sealed record InputNormalizationResult
{
    public required bool IsValid { get; init; }

    /// <summary>Null when <see cref="IsValid"/> is false.</summary>
    public NormalizedBookInput? Story { get; init; }

    /// <summary>
    /// Kept beside the story input rather than inside it. The image stage needs it; Story must
    /// never see it.
    /// </summary>
    public string? ChildPhotoRef { get; init; }

    /// <summary><see cref="CompositeFailureCodes.InvalidBookInput"/>, or null when valid.</summary>
    public string? FailureCode { get; init; }

    /// <summary>Every reason, not the first — a parent's row is usually wrong in one way, but a bad deploy is wrong in several.</summary>
    public IReadOnlyList<string> Problems { get; init; } = [];
}

/// <summary>
/// Step 0 of the composite pipeline: validate and normalize, before anything is paid for.
///
/// Three separate jobs happen here and nowhere else.
///
/// The numeric age becomes one of three bands, once. Every later stage — the Story prompt, the
/// Visual Scenario input — takes the band, so there is exactly one place where "is five a 3-5 or a
/// 6+" is answered, and it is answered from the supplier's config rather than from a switch
/// statement somebody would have to remember to update.
///
/// The gender and the theme are mapped from the values the existing backend actually holds onto
/// the canonical values the contracts name. The handoff is explicit that an unknown value is
/// rejected rather than guessed: a book written for the wrong child is worse than a book that was
/// never written, and "not_specified" cannot be silently read as a girl.
///
/// The photograph is decoded, not merely looked up. A zero-byte upload, a HEIC the pipeline cannot
/// read or an HTML error page saved under a .jpg name all pass an existence check and all fail
/// three model calls later, after the money is spent.
/// </summary>
public static class InputNormalization
{
    /// <summary>
    /// The backend's six worlds, in the canonical ids the BEKI theme registry uses.
    ///
    /// This map is the integration decision and belongs in code; the id *set* it produces is
    /// checked against <c>theme_reference_registry_v1.json</c> at runtime, so a registry rename
    /// stops the pipeline here instead of producing a book with no theme reference image.
    ///
    /// Two pairings are worth stating because the words differ: the airplane world is the city
    /// above the clouds, so it is <c>clouds</c>; the pirate world is the shining island in the sea,
    /// so it is <c>ocean</c>.
    /// </summary>
    private static readonly IReadOnlyDictionary<ThemeType, string> CanonicalThemeIds =
        new Dictionary<ThemeType, string>
        {
            [ThemeType.Airplanes] = "clouds",
            [ThemeType.Space] = "space",
            [ThemeType.Animals] = "forest",
            [ThemeType.Pirates] = "ocean",
            [ThemeType.Magic] = "magic",
            [ThemeType.Dinosaurs] = "dinosaurs"
        };

    /// <summary>
    /// Every spelling of a gender this codebase has ever written down, and nothing else.
    ///
    /// The journey sends <c>girl</c>/<c>boy</c> today (AdventurePacksController.NormalizeGender),
    /// and the older prompt builders still accept <c>female</c>/<c>male</c> and the Georgian words,
    /// so stored rows can hold any of them. <c>not_specified</c>, the Beki DTO's default, is
    /// deliberately absent: the Visual Scenario contract admits only girl or boy, and a book has to
    /// be about the child who was bought it.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> CanonicalGenders =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["girl"] = "girl",
            ["female"] = "girl",
            ["გოგო"] = "girl",
            ["boy"] = "boy",
            ["male"] = "boy",
            ["ბიჭი"] = "boy"
        };

    private static readonly Lazy<IReadOnlyList<AgeBand>> Bands = new(ReadAgeBands);

    private static readonly Lazy<IReadOnlySet<string>> RegisteredThemeIds = new(ReadThemeIds);

    /// <summary>
    /// The band a numeric age falls in, or null when no band claims it.
    ///
    /// Null is the honest answer and not a band. The config's lowest band starts at 1, so a zero,
    /// a negative number or a stored default is outside every band — and clamping it upward would
    /// mean writing a book for an age nobody entered.
    /// </summary>
    public static string? AgeBandFor(int childAge) =>
        Bands.Value.FirstOrDefault(band => band.Contains(childAge))?.Id;

    /// <summary>
    /// The canonical BEKI theme id behind one stored theme value, or null when it maps to none.
    ///
    /// The same three spellings <see cref="ResolveTheme"/> accepts — the enum's name, its number,
    /// and the canonical id itself — because a caller that needs only the world should not have to
    /// know which of them a row happens to hold. For the resume contract, which names the theme
    /// reference a book's pages were drawn against and is built before the input is normalized.
    /// </summary>
    public static string? CanonicalThemeId(string? themeValue)
    {
        var theme = ResolveTheme(themeValue, []);
        return theme is null ? null : CanonicalThemeIds[theme.Value];
    }

    /// <summary>
    /// Maps one purchase into the four fields Story may see, or refuses it.
    /// </summary>
    /// <param name="input">The stored row, in the handoff's field names.</param>
    /// <param name="childPhotoBytes">
    /// The photograph itself, read by the caller from wherever
    /// <see cref="BookGenerationInput.ChildPhotoRef"/> points. Bytes rather than a path because
    /// the photograph lives in blob storage in production and on disk in tests, and the decode is
    /// the same question in both cases.
    /// </param>
    public static InputNormalizationResult Normalize(BookGenerationInput input, byte[]? childPhotoBytes) =>
        Map(input, childPhotoBytes, checkPhotograph: true);

    /// <summary>
    /// The same four fields, mapped without the photograph.
    ///
    /// For the one caller that maps before the photograph is in hand: the preview's story call,
    /// which happens while the upload is still only a blob URL on the run and which is not allowed
    /// to see the picture anyway. The photograph is still checked — by
    /// <see cref="Normalize(BookGenerationInput, byte[])"/>, before the first image call, which is
    /// the stage that actually needs it and the stage the handoff puts the check in front of.
    ///
    /// Deliberately a second named method rather than a nullable argument on the first. "Normalize
    /// with no bytes" and "normalize and do not look at the bytes" are the same call with opposite
    /// meanings, and the version that silently accepts a missing photograph is the one nobody
    /// should be able to reach by passing null.
    /// </summary>
    public static InputNormalizationResult NormalizeForStory(BookGenerationInput input) =>
        Map(input, null, checkPhotograph: false);

    private static InputNormalizationResult Map(
        BookGenerationInput input, byte[]? childPhotoBytes, bool checkPhotograph)
    {
        var problems = new List<string>();

        var name = input.ChildName?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            problems.Add("child_name is empty.");
        }

        var band = AgeBandFor(input.ChildAge);
        if (band is null)
        {
            problems.Add(
                $"child_age {input.ChildAge} is outside every configured age band "
                + $"({string.Join(", ", Bands.Value.Select(b => b.Id))}).");
        }

        if (!CanonicalGenders.TryGetValue((input.ChildGender ?? string.Empty).Trim(), out var gender))
        {
            problems.Add($"child_gender '{input.ChildGender}' is not a value this pipeline maps.");
        }

        var theme = ResolveTheme(input.ThemeId, problems);

        if (checkPhotograph)
        {
            problems.AddRange(PhotoProblems(childPhotoBytes));
        }

        if (problems.Count > 0)
        {
            return new InputNormalizationResult
            {
                IsValid = false,
                FailureCode = CompositeFailureCodes.InvalidBookInput,
                Problems = problems
            };
        }

        return new InputNormalizationResult
        {
            IsValid = true,
            ChildPhotoRef = input.ChildPhotoRef,
            Story = new NormalizedBookInput
            {
                ChildName = name,
                ChildAge = input.ChildAge,
                AgeBand = band!,
                ChildGender = gender!,
                ThemeId = CanonicalThemeIds[theme!.Value],
                Theme = theme.Value
                // LegacyExtraWish is not copied. This absence is the whole point of the boundary.
            }
        };
    }

    /// <summary>
    /// Why a photograph cannot be used, in the words a support answer needs.
    ///
    /// Decoding is the check, and it is a full decode rather than a header read. The handoff asks
    /// that the photo be "readable" before paid model work starts, and the only honest reading of
    /// "readable" is that the pixels come out.
    ///
    /// <see cref="Image.Identify(byte[])"/> was here first and was wrong for exactly one case,
    /// which is the case that happens: an upload truncated by a dropped connection keeps its
    /// header, so it reports a perfectly good width and height and fails several thousand
    /// tokens later, inside an image call, after a story has been written and paid for. The header
    /// is not the file. <see cref="Image.Load(byte[])"/> walks the actual scan lines, so a photo
    /// that passes here is a photo the image stage can use — which is the entire question the
    /// boundary exists to answer.
    ///
    /// The cost is a decode of one parent's photograph, once per book, against a story call that
    /// takes minutes. That is not a trade worth thinking about twice.
    /// </summary>
    public static IReadOnlyList<string> PhotoProblems(byte[]? photoBytes)
    {
        if (photoBytes is null || photoBytes.Length == 0)
        {
            return ["child_photo_ref resolved to no bytes; the photograph is missing or empty."];
        }

        try
        {
            using var image = Image.Load(photoBytes);

            // Kept even though a decoded image essentially cannot be empty: the check costs
            // nothing and it is the sentence a support answer needs when it ever does happen.
            if (image.Width <= 0 || image.Height <= 0)
            {
                return [$"child photograph decoded to {image.Width}x{image.Height}, which has no pixels."];
            }

            return [];
        }
        catch (Exception ex)
        {
            // Truncation surfaces here as an InvalidImageContentException or an
            // ArgumentOutOfRangeException out of the decoder, depending on where the bytes stop.
            // The type is named rather than the message, which can carry a file path.
            return [$"child photograph could not be decoded as an image: {ex.GetType().Name}."];
        }
    }

    /// <summary>
    /// The backend theme value as one of the six worlds, or null with a reason.
    ///
    /// Three spellings are accepted because three exist in storage: the enum's name, the enum's
    /// number (rows written straight from the database), and the canonical BEKI id itself (rows
    /// written by the composite path once it is live). Anything else is refused — the registry's
    /// own integration rule is "do not infer unknown aliases".
    /// </summary>
    private static ThemeType? ResolveTheme(string? value, List<string> problems)
    {
        var text = (value ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            problems.Add("theme_id is empty.");
            return null;
        }

        ThemeType? theme = null;

        if (Enum.TryParse<ThemeType>(text, ignoreCase: true, out var byName) && Enum.IsDefined(byName))
        {
            theme = byName;
        }
        else if (int.TryParse(text, out var number) && Enum.IsDefined(typeof(ThemeType), number))
        {
            theme = (ThemeType)number;
        }
        else
        {
            foreach (var pair in CanonicalThemeIds)
            {
                if (string.Equals(pair.Value, text, StringComparison.OrdinalIgnoreCase))
                {
                    theme = pair.Key;
                    break;
                }
            }
        }

        if (theme is null)
        {
            problems.Add($"theme_id '{text}' is not one of this backend's six worlds.");
            return null;
        }

        var canonical = CanonicalThemeIds[theme.Value];
        if (!RegisteredThemeIds.Value.Contains(canonical))
        {
            problems.Add(
                $"theme_id '{text}' maps to '{canonical}', which the supplied theme reference "
                + "registry does not list.");
            return null;
        }

        return theme;
    }

    /// <summary>One configured band. <c>MaxAge</c> is null for the open-ended top band.</summary>
    private sealed record AgeBand(string Id, int MinAge, int? MaxAge)
    {
        public bool Contains(int age) => age >= MinAge && (MaxAge is null || age <= MaxAge);
    }

    private static IReadOnlyList<AgeBand> ReadAgeBands()
    {
        using var config = CompositeAssets.Read(CompositeAssets.PipelineConfigPath);

        return config.RootElement
            .GetProperty("story")
            .GetProperty("age_groups")
            .EnumerateArray()
            .Select(group => new AgeBand(
                group.GetProperty("id").GetString()
                    ?? throw new InvalidOperationException("An age group in pipeline_config_v1.json has no id."),
                group.GetProperty("min_age").GetInt32(),
                group.GetProperty("max_age").ValueKind == JsonValueKind.Null
                    ? null
                    : group.GetProperty("max_age").GetInt32()))
            .ToList();
    }

    private static IReadOnlySet<string> ReadThemeIds()
    {
        using var registry = CompositeAssets.Read(CompositeAssets.ThemeRegistryPath);

        return registry.RootElement
            .GetProperty("canonical_theme_ids")
            .EnumerateArray()
            .Select(id => id.GetString()!)
            .ToHashSet(StringComparer.Ordinal);
    }
}
