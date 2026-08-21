using AdventurePacks.Api.Domain.Story;

namespace AdventurePacks.Api.Services.Story;

/// <summary>
/// Checks a plan the Beki format can actually build a book from.
///
/// The v5 schema enforces shape — every field exists, every type is right — but not the rules
/// that only make sense once the whole plan is read together: that "beki" is spelled the one way
/// everything downstream expects, that Beki is not quietly missing from the spreads the format
/// promises the reader, that a spread does not name a cast member nobody introduced. A plan can
/// satisfy the schema and still fail every one of these, because a schema cannot see across its
/// own fields.
///
/// Static and stateless, like <see cref="BekiBookGenerator.Corrections"/> — a plan is checked the
/// same way wherever it is read: the preview call about to retry once, or the fulfilment job
/// reading a plan the parent already bought.
/// </summary>
public static class BekiPlanValidator
{
    /// <summary>The exact id every spread must use to mean Beki. Never a cast id, never a display name.</summary>
    public const string BekiId = "beki";

    private const string ChildId = "child";

    /// <summary>Spread 1, the final spread, and at least three more — five in total, at minimum.</summary>
    private const int MinimumBekiSpreads = 5;

    public static IReadOnlyList<string> Validate(MasterStory plan, int expectedSpreadCount)
    {
        var problems = new List<string>();

        ValidateSpreadNumbers(plan, expectedSpreadCount, problems);
        var castIds = ValidateCast(plan, problems);
        ValidateCharacterReferences(plan, castIds, problems);
        ValidateBekiPresence(plan, expectedSpreadCount, problems);
        ValidateText(plan, problems);

        return problems;
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

    private static void ValidateText(MasterStory plan, List<string> problems)
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
            }
        }
    }
}
