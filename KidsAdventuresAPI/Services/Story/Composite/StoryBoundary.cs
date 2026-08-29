using System.Text.Json.Serialization;
using AdventurePacks.Api.Domain.Story;

namespace AdventurePacks.Api.Services.Story.Composite;

/// <summary>
/// One page of the boundary: a number and the Georgian words on it. Nothing else.
///
/// The wire names are the contract's, not the codebase's, so the object serialises straight into
/// what <c>story_boundary_v1.schema.json</c> describes and what the Visual Scenario call is handed.
/// </summary>
public sealed record StoryBoundaryPage
{
    [JsonPropertyName("page")]
    public required int Page { get; init; }

    [JsonPropertyName("story_text")]
    public required string StoryText { get; init; }
}

/// <summary>
/// Everything downstream of Story is allowed to know about the story.
///
/// A Georgian title and eight numbered Georgian pages. The plan the model returned holds far more
/// than this — an English title, English copy per spread, a character lock describing a real
/// child's face, cast and object descriptions, illustration briefs — and none of it crosses.
///
/// That is the point of the type. The handoff's locked decisions put the child's likeness and the
/// English copy out of the MVP, and a boundary that merely *promised* not to forward them would
/// be one careless field access away from forwarding them: the Visual Scenario prompt is told to
/// invent nothing about the child's face, and the surest way to keep that true is that the object
/// it is built from has no face in it.
/// </summary>
public sealed record StoryBoundaryOutput
{
    [JsonPropertyName("title_ka")]
    public required string TitleKa { get; init; }

    [JsonPropertyName("story_pages")]
    public required IReadOnlyList<StoryBoundaryPage> StoryPages { get; init; }
}

/// <summary>
/// Either a boundary or the reasons there isn't one.
/// </summary>
public sealed record StoryBoundaryResult
{
    public required bool IsValid { get; init; }

    public StoryBoundaryOutput? Boundary { get; init; }

    /// <summary><see cref="CompositeFailureCodes.StoryFailed"/>, or null when valid.</summary>
    public string? FailureCode { get; init; }

    public IReadOnlyList<string> Problems { get; init; } = [];
}

/// <summary>
/// The single mapping from the story engine's own plan to the contract the rest of the pipeline
/// speaks (handoff §6 Step 1).
///
/// "Mapped once" is the whole design. The alternative — every later stage reaching into
/// <see cref="MasterStory"/> for the field it happens to need — is what makes a provider-specific
/// shape permanent: rename a property on the plan and four unrelated stages break, and adding an
/// English field to the plan silently offers it to a stage that was never meant to have one.
///
/// It also rejects rather than repairs. A plan with seven spreads is a plan the model got wrong,
/// and padding it to eight here would hide a story fault inside a mapping function and print the
/// result.
/// </summary>
public static class StoryBoundary
{
    /// <summary>
    /// Maps an accepted plan into the boundary, or explains why it cannot be mapped.
    /// </summary>
    /// <param name="story">The plan as the story model returned it.</param>
    /// <param name="spreadCount">
    /// How many spreads the format demands. Eight, always, for v0 — a parameter only so the
    /// number is stated once at the call site rather than assumed by two files.
    /// </param>
    public static StoryBoundaryResult From(MasterStory story, int spreadCount = BookFormat.SpreadCount)
    {
        ArgumentNullException.ThrowIfNull(story);

        var problems = new List<string>();

        // The Georgian title. Concept.Title is the Georgian one; MasterStory.TitleEn holds the
        // English and is deliberately not read here — Georgian only is a locked decision, and the
        // boundary is where that stops being a policy and becomes a fact about the data.
        var title = story.Concept?.Title?.Trim() ?? string.Empty;
        if (title.Length == 0)
        {
            problems.Add("The story plan has no Georgian title.");
        }

        var spreads = story.Spreads ?? [];
        if (spreads.Count != spreadCount)
        {
            problems.Add($"The story plan has {spreads.Count} spreads; the book format needs exactly {spreadCount}.");
        }

        // Ordered by the model's own numbering rather than by list position: a plan that returned
        // its spreads out of order is still a usable book, and a plan that numbered two spreads
        // the same is not — the check below is what tells those apart.
        var ordered = spreads.OrderBy(spread => spread.Number).ToList();

        var pages = new List<StoryBoundaryPage>(ordered.Count);
        for (var index = 0; index < ordered.Count; index++)
        {
            var spread = ordered[index];
            var expected = index + 1;

            if (spread.Number != expected)
            {
                problems.Add(
                    $"The story plan's spread numbers are not 1..{spreadCount} exactly once; "
                    + $"position {expected} carries number {spread.Number}.");
            }

            // Spread.Text is the Georgian read-aloud copy. Spread.TextEn exists beside it and is
            // never read: see the type's own note on why the boundary has nowhere to put it.
            var text = spread.Text?.Trim() ?? string.Empty;
            if (text.Length == 0)
            {
                problems.Add($"Spread {spread.Number} has no Georgian story text.");
            }

            pages.Add(new StoryBoundaryPage { Page = expected, StoryText = text });
        }

        if (problems.Count > 0)
        {
            return new StoryBoundaryResult
            {
                IsValid = false,
                FailureCode = CompositeFailureCodes.StoryFailed,
                Problems = problems
            };
        }

        return new StoryBoundaryResult
        {
            IsValid = true,
            Boundary = new StoryBoundaryOutput { TitleKa = title, StoryPages = pages }
        };
    }
}
