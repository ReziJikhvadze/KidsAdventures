using AdventurePacks.Api.Domain.Story;

namespace AdventurePacks.Api.Services.Story;

/// <summary>
/// Checks a plan the Beki format can actually build a book from.
///
/// The v5 schema enforces shape — every field exists, every type is right — but not the rules
/// that only make sense once the whole plan is read together: that "beki" is spelled the one way
/// everything downstream expects, that Beki is not quietly missing from the spreads the format
/// promises the reader, that a spread does not name a cast member nobody introduced, that a scene
/// naming Beki in prose also lists Beki among its characters. A plan can satisfy the schema and
/// still fail every one of these, because a schema cannot see across its own fields.
///
/// Static and stateless, like <see cref="BekiBookGenerator.Corrections"/> — a plan is checked the
/// same way wherever it is read: the preview call about to retry once, or the fulfilment job
/// reading a plan the parent already bought.
/// </summary>
public static class BekiPlanValidator
{
    /// <summary>The exact id every spread must use to mean Beki. Never a cast id, never a display name.</summary>
    public const string BekiId = "beki";

    /// <summary>
    /// Beki's name in Georgian, matched as a stem by <see cref="NamesBeki"/> — Georgian has no
    /// case and inflects by suffix, so the name arrives as ბეკიმ, ბეკის, ბეკისთან and ბეკიც.
    /// </summary>
    private const string GeorgianBekiStem = "ბეკი";

    private const string ChildId = "child";

    /// <summary>Spread 1, the final spread, and at least three more — five in total, at minimum.</summary>
    private const int MinimumBekiSpreads = 5;

    public static IReadOnlyList<string> Validate(MasterStory plan, int expectedSpreadCount, int? age = null)
    {
        var problems = new List<string>();

        ValidateSpreadNumbers(plan, expectedSpreadCount, problems);
        var castIds = ValidateCast(plan, problems);
        var objIds = ValidateObjects(plan, castIds, problems);
        ValidateCharacterReferences(plan, castIds, problems);
        ValidateObjectReferences(plan, objIds, problems);
        ValidateBekiPresence(plan, expectedSpreadCount, problems);
        ValidateScenesThatNameBeki(plan, problems);
        ValidateText(plan, problems, age);
        ValidateWorldLock(plan, problems);

        return problems;
    }

    /// <summary>
    /// A plan that carries a worldLock must carry a real one. Null means an older plan written
    /// before the field existed, and null must keep passing forever — but a present-and-blank
    /// value is today's model skipping the one paragraph that keeps every illustration in the
    /// same universe, and the schema alone cannot refuse an empty string. Reporting it here puts
    /// it on the corrective retry, with the problem spelled out.
    /// </summary>
    private static void ValidateWorldLock(MasterStory plan, List<string> problems)
    {
        if (plan.WorldLock is not null && string.IsNullOrWhiteSpace(plan.WorldLock))
        {
            problems.Add(
                "worldLock is blank. Write 2–3 concrete English sentences fixing the world's "
                + "constant look — palette, light, terrain, one recurring landmark — with no "
                + "characters, story events or camera instructions.");
        }
    }

    private static HashSet<string> ValidateObjects(MasterStory plan, HashSet<string> castIds, List<string> problems)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var obj in plan.Objects ?? [])
        {
            if (string.IsNullOrWhiteSpace(obj.Id))
            {
                problems.Add($"Object \"{obj.Name}\" has no id.");
                continue;
            }

            if (obj.Id.Equals(ChildId, StringComparison.OrdinalIgnoreCase) || obj.Id.Equals(BekiId, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"Object id \"{obj.Id}\" is reserved; \"child\" and \"beki\" must never appear in objects.");
                continue;
            }

            if (castIds.Contains(obj.Id))
            {
                problems.Add($"Object id \"{obj.Id}\" is already used by a cast member.");
                continue;
            }

            if (!ids.Add(obj.Id))
            {
                problems.Add($"Object id \"{obj.Id}\" is used more than once.");
            }
        }

        return ids;
    }

    private static void ValidateObjectReferences(MasterStory plan, HashSet<string> objIds, List<string> problems)
    {
        foreach (var spread in plan.Spreads)
        {
            foreach (var id in spread.Objects ?? [])
            {
                if (!objIds.Contains(id))
                {
                    problems.Add($"Spread {spread.Number} lists object \"{id}\", which is not a known object id.");
                }
            }
        }
    }

    /// <summary>
    /// True when a brief names Beki in words — English <c>beki</c> as a whole word, or a Georgian
    /// token beginning with <c>ბეკი</c>.
    ///
    /// A Georgian prefix match covers every inflected form of the name (see
    /// <see cref="GeorgianBekiStem"/>), and there is no Georgian word that merely begins with
    /// ბეკი for it to collide with — ბეკონი, bacon, diverges at the fourth letter. English is
    /// matched whole precisely because a prefix match there would swallow any name starting with
    /// the same four letters.
    ///
    /// Public because the generator asks the same question of the same text: a scene that names
    /// Beki must be drawn with the master reference attached, and both halves of the pipeline
    /// have to agree about which scenes those are. Two implementations of one rule is how a
    /// spread the validator accepts gets drawn with an invented Beki anyway.
    /// </summary>
    /// <summary>
    /// True only for an explicit instruction that Beki be absent — "do not show Beki",
    /// "ბეკის გარეშე". A mere mention of Beki in an Avoid brief is usually the opposite: "Beki
    /// with wings" forbids the wings, not Beki, and reading it as an absence was how a spread
    /// whose scene names Beki could be drawn with no reference attached — an invented Beki, the
    /// exact fault this file exists to prevent. Conservative by design: an unrecognized phrasing
    /// attaches the reference, because a needlessly attached reference costs nothing while a
    /// missing one costs the character.
    /// </summary>
    public static bool ForbidsBeki(string? text)
    {
        if (!NamesBeki(text)) return false;

        var lowered = text!.ToLowerInvariant();
        string[] absences =
        [
            "no beki", "without beki", "do not show beki", "don't show beki",
            "do not include beki", "don't include beki", "do not draw beki", "don't draw beki",
            "not show beki", "exclude beki", "beki must not", "beki should not",
            "beki does not appear", "beki is absent", "beki is not present",
            "ბეკის გარეშე", "ბეკი არ ", "ბეკი არაა", "არ დახატო ბეკი", "არ გამოჩნდეს ბეკი",
            "ნუ დახატავ ბეკის",
        ];

        return absences.Any(lowered.Contains);
    }

    public static bool NamesBeki(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            // A token is a run of letters; everything else — spaces, commas, apostrophes, quotes,
            // digits — ends one. The loop runs one past the end so the last token is closed too.
            var isLetter = i < text.Length && char.IsLetter(text[i]);

            if (isLetter)
            {
                if (start < 0) start = i;
                continue;
            }

            if (start < 0) continue;

            var token = text.AsSpan(start, i - start);
            start = -1;

            if (token.Equals(BekiId, StringComparison.OrdinalIgnoreCase)) return true;
            if (token.StartsWith(GeorgianBekiStem, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    private static void ValidateSpreadNumbers(MasterStory plan, int expectedSpreadCount, List<string> problems)
    {
        if (plan.Spreads.Count != expectedSpreadCount)
        {
            problems.Add($"Expected {expectedSpreadCount} spreads, got {plan.Spreads.Count}.");
        }

        var numbers = plan.Spreads.Select(spread => spread.Number).ToList();
        if (numbers.Distinct().Count() != numbers.Count)
        {
            problems.Add("Spread numbers are not unique.");
        }

        var missing = Enumerable.Range(1, expectedSpreadCount).Except(numbers).ToList();
        if (missing.Count > 0)
        {
            problems.Add(
                $"Spread numbers do not form 1..{expectedSpreadCount}; missing: {string.Join(", ", missing)}.");
        }
    }

    private static HashSet<string> ValidateCast(MasterStory plan, List<string> problems)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var member in plan.Cast ?? [])
        {
            if (string.IsNullOrWhiteSpace(member.Id))
            {
                problems.Add($"Cast member \"{member.Name}\" has no id.");
                continue;
            }

            // "child" and "beki" are reserved: their identities come from the photograph and the
            // master reference, never from a cast description. A cast entry wearing either id
            // would hand the illustrator two competing authorities for the same character.
            if (member.Id.Equals(ChildId, StringComparison.OrdinalIgnoreCase)
                || member.Id.Equals(BekiId, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(
                    $"Cast id \"{member.Id}\" is reserved; \"child\" and \"beki\" must never appear in cast.");
                continue;
            }

            if (!ids.Add(member.Id))
            {
                problems.Add($"Cast id \"{member.Id}\" is used more than once.");
            }
        }

        return ids;
    }

    private static void ValidateCharacterReferences(
        MasterStory plan, HashSet<string> castIds, List<string> problems)
    {
        foreach (var spread in plan.Spreads)
        {
            foreach (var id in spread.Characters ?? [])
            {
                var known = id.Equals(ChildId, StringComparison.OrdinalIgnoreCase)
                    || id.Equals(BekiId, StringComparison.OrdinalIgnoreCase)
                    || castIds.Contains(id);

                if (!known)
                {
                    problems.Add(
                        $"Spread {spread.Number} lists \"{id}\", which is not \"child\", \"beki\", or a cast id.");
                }
            }
        }
    }

    private static void ValidateBekiPresence(MasterStory plan, int expectedSpreadCount, List<string> problems)
    {
        var bekiSpreads = plan.Spreads
            .Where(spread => (spread.Characters ?? []).Any(id => id.Equals(BekiId, StringComparison.OrdinalIgnoreCase)))
            .Select(spread => spread.Number)
            .ToHashSet();

        if (!bekiSpreads.Contains(1))
        {
            problems.Add("Beki does not appear in spread 1.");
        }

        if (!bekiSpreads.Contains(expectedSpreadCount))
        {
            problems.Add($"Beki does not appear in spread {expectedSpreadCount}.");
        }

        if (bekiSpreads.Count < MinimumBekiSpreads)
        {
            problems.Add(
                $"Beki appears in only {bekiSpreads.Count} spread(s); at least {MinimumBekiSpreads} are required "
                + "(spread 1, the final spread, and at least three more).");
        }
    }

    /// <summary>
    /// A scene that names Beki must also list "beki" among its characters.
    ///
    /// The two say the same thing to different readers: the scene is prose the illustrator reads,
    /// the characters list is what decides whether Beki's master reference is attached to the
    /// call. A plan that says "ბეკი მიუთითებს ხესკენ" in the scene and omits the id from
    /// characters asks an image model to draw a character it has never been shown — and it does,
    /// which is how a book gets a Beki that is not Beki.
    ///
    /// It is reported as a plan problem rather than patched over in the generator on purpose. The
    /// planner is one corrective retry away from writing a plan that is right in both places, and
    /// a plan that is right is a plan an operator can read; a generator that silently attaches
    /// references the plan never asked for leaves the stored plan permanently disagreeing with
    /// the book that was printed from it. (The generator does infer it as a backstop, for plans
    /// written before this rule existed and resumed afterwards — see BekiBookGenerator.)
    ///
    /// The spread's own Avoid field outranks this only when it explicitly forbids Beki
    /// (<see cref="ForbidsBeki"/>): "do not show Beki" is consistent, not broken. An Avoid that
    /// merely mentions Beki — "Beki with wings" — forbids a detail, not the character.
    /// </summary>
    private static void ValidateScenesThatNameBeki(MasterStory plan, List<string> problems)
    {
        foreach (var spread in plan.Spreads)
        {
            var listed = (spread.Characters ?? [])
                .Any(id => id.Equals(BekiId, StringComparison.OrdinalIgnoreCase));

            if (listed) continue;
            if (!NamesBeki(spread.Illustration.Scene)) continue;
            if (ForbidsBeki(spread.Illustration.Avoid)) continue;

            problems.Add(
                $"Spread {spread.Number}'s visual scene names Beki, but its characters list omits "
                + $"\"{BekiId}\". Either add \"{BekiId}\" to that spread's characters, or take Beki "
                + "out of the scene.");
        }
    }

    private static void ValidateText(MasterStory plan, List<string> problems, int? age = null)
    {
        if (string.IsNullOrWhiteSpace(plan.Concept.Title))
        {
            problems.Add("The title is empty.");
        }

        foreach (var spread in plan.Spreads)
        {
            if (string.IsNullOrWhiteSpace(spread.Text))
            {
                problems.Add($"Spread {spread.Number} has no story text.");
                continue;
            }

            if (age.HasValue)
            {
                var wordCount = spread.Text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
                if (age.Value <= 4 && wordCount > 45)
                {
                    problems.Add($"Spread {spread.Number} has {wordCount} words; maximum for age {age.Value} is 45.");
                }
                else if (age.Value >= 5 && age.Value <= 8 && wordCount > 68)
                {
                    problems.Add($"Spread {spread.Number} has {wordCount} words; maximum for age {age.Value} is 68.");
                }
            }
        }
    }
}
