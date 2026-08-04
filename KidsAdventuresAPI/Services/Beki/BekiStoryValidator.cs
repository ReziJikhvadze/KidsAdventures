using System.Text.RegularExpressions;
using AdventurePacks.Api.Domain.Beki;

namespace AdventurePacks.Api.Services.Beki;

/// <summary>
/// Deterministic gate between every AI call and the database.
///
/// The point of splitting generation, review and repair is that ordinary code — not a
/// model — decides whether a book is publishable. Everything checkable without judgement
/// is checked here, so a model that quietly drops a page, writes "დასასრული", or claims
/// Beki appears on pages that do not feature Beki cannot reach a paying parent.
///
/// Errors are phrased as instructions, because they are fed verbatim to the repair prompt.
/// </summary>
public sealed partial class BekiStoryValidator
{
    [GeneratedRegex(@"[\p{L}\p{N}''-]+", RegexOptions.Compiled)]
    private static partial Regex WordPattern();

    /// <summary>Checks the generator's draft, before the reviewer sees it.</summary>
    public IReadOnlyList<string> ValidateDraft(BekiStoryOutput? story, BekiStoryInput input)
    {
        var errors = ValidateCommon(story, input);
        if (story is null)
        {
            return errors;
        }

        // The generator must not grade its own work; the reviewer owns this field.
        if (story.ReviewMetadata is not null)
        {
            errors.Add("reviewMetadata must be null in the generator output; the reviewer populates it.");
        }

        return errors;
    }

    /// <summary>Checks the reviewed story, immediately before it is saved.</summary>
    public IReadOnlyList<string> ValidateFinal(BekiStoryOutput? story, BekiStoryInput input)
    {
        var errors = ValidateCommon(story, input);
        if (story is null)
        {
            return errors;
        }

        if (story.ReviewMetadata is null)
        {
            errors.Add("reviewMetadata must be present after review.");
        }
        else if (!BekiStoryConstants.ReviewStatuses.Contains(story.ReviewMetadata.Status))
        {
            errors.Add(
                $"reviewMetadata.status '{story.ReviewMetadata.Status}' is not one of " +
                $"{string.Join(", ", BekiStoryConstants.ReviewStatuses)}.");
        }

        return errors;
    }

    private List<string> ValidateCommon(BekiStoryOutput? story, BekiStoryInput input)
    {
        var errors = new List<string>();
        if (story is null)
        {
            errors.Add("The model returned no parsable JSON object.");
            return errors;
        }

        ValidateIdentity(story, input, errors);
        ValidatePages(story, input, errors);
        ValidateBekiPresence(story, errors);
        ValidateCtaAndEnding(story, errors);
        ValidateCover(story, errors);
        ValidateMemory(story, errors);

        return errors;
    }

    /// <summary>The reviewer may rewrite prose, but must never rewrite who the book is for.</summary>
    private static void ValidateIdentity(BekiStoryOutput story, BekiStoryInput input, List<string> errors)
    {
        if (story.SchemaVersion != BekiStoryConstants.SchemaVersion)
        {
            errors.Add($"schemaVersion must be '{BekiStoryConstants.SchemaVersion}'.");
        }

        if (!string.Equals(story.RequestId, input.RequestId, StringComparison.Ordinal))
        {
            errors.Add($"requestId must stay '{input.RequestId}'.");
        }

        if (!string.Equals(story.ChildName, input.ChildName, StringComparison.Ordinal))
        {
            errors.Add($"childName must stay '{input.ChildName}' exactly as the parent spelled it.");
        }

        if (!string.Equals(story.AgeBand, input.AgeBand, StringComparison.Ordinal))
        {
            errors.Add($"ageBand must stay '{input.AgeBand}'.");
        }

        if (string.IsNullOrWhiteSpace(story.TitleKa))
        {
            errors.Add("titleKa is required.");
        }
    }

    private void ValidatePages(BekiStoryOutput story, BekiStoryInput input, List<string> errors)
    {
        if (story.StoryPages.Count != BekiStoryConstants.PageCount)
        {
            errors.Add(
                $"Expected exactly {BekiStoryConstants.PageCount} pages in storyPages, received {story.StoryPages.Count}.");
            return;
        }

        var numbers = story.StoryPages.Select(p => p.PageNumber).ToList();
        var expected = Enumerable.Range(1, BekiStoryConstants.PageCount).ToList();
        if (!numbers.OrderBy(n => n).SequenceEqual(expected))
        {
            errors.Add($"storyPages must be numbered 1..{BekiStoryConstants.PageCount} exactly once each; got [{string.Join(", ", numbers)}].");
        }

        var (minWords, maxWords) = BekiStoryConstants.WordRangeFor(story.AgeBand);
        var slackMin = (int)Math.Floor(minWords * (1 - BekiStoryConstants.WordCountTolerance));
        var slackMax = (int)Math.Ceiling(maxWords * (1 + BekiStoryConstants.WordCountTolerance));
        var maxCast = BekiStoryConstants.MaxSupportingCastFor(story.AgeBand);

        foreach (var page in story.StoryPages.OrderBy(p => p.PageNumber))
        {
            var where = $"Page {page.PageNumber}";

            if (string.IsNullOrWhiteSpace(page.StoryTextKa))
            {
                errors.Add($"{where}: storyTextKa is empty.");
                continue;
            }

            if (!BekiStoryConstants.PageTurnFunctions.Contains(page.PageTurnFunction))
            {
                errors.Add($"{where}: pageTurnFunction '{page.PageTurnFunction}' is not a permitted value.");
            }

            if (page.CharactersPresent.Count == 0)
            {
                errors.Add($"{where}: charactersPresent must list at least the child.");
            }

            if (string.IsNullOrWhiteSpace(page.ChildAgencyEn))
            {
                errors.Add($"{where}: childAgencyEn is required — state what the child does that matters.");
            }

            if (string.IsNullOrWhiteSpace(page.SceneSummaryEn))
            {
                errors.Add($"{where}: sceneSummaryEn is required; the visual pipeline reads it.");
            }

            // Page 1 has nothing to follow from; every later page must earn its place.
            if (page.PageNumber > 1 && string.IsNullOrWhiteSpace(page.ContinuityFromPreviousEn))
            {
                errors.Add($"{where}: continuityFromPreviousEn is required so the page follows causally from the previous one.");
            }

            var words = CountWords(page.StoryTextKa);
            if (words < slackMin || words > slackMax)
            {
                errors.Add(
                    $"{where}: {words} Georgian words is far outside the {minWords}–{maxWords} target for age band {story.AgeBand}.");
            }

            // Beki and the child are named cast too, so the supporting limit counts what is left.
            var supporting = page.CharactersPresent
                .Count(c => !IsChild(c, story.ChildName) && !IsBeki(c));
            if (supporting > maxCast)
            {
                errors.Add(
                    $"{where}: {supporting} supporting characters exceeds the limit of {maxCast} for age band {story.AgeBand}.");
            }
        }

        ValidateSupportingCastAllowList(story, input, errors);
    }

    /// <summary>
    /// Only people the parent actually chose may appear. A model inventing "Grandma" for a
    /// family that has none is a personalization failure a parent notices immediately.
    /// </summary>
    private static void ValidateSupportingCastAllowList(
        BekiStoryOutput story,
        BekiStoryInput input,
        List<string> errors)
    {
        if (input.SelectedSupportingCharacters.Count == 0)
        {
            return;
        }

        var allowed = new HashSet<string>(
            input.SelectedSupportingCharacters.Select(c => c.Name),
            StringComparer.OrdinalIgnoreCase);

        foreach (var character in story.StoryPages
                     .SelectMany(p => p.CharactersPresent)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (IsChild(character, story.ChildName) || IsBeki(character))
            {
                continue;
            }

            // Story-invented companions are allowed and wanted; named *family* is not, so this
            // only flags a name that collides with a real relative who was not selected.
            if (!allowed.Contains(character) &&
                input.SelectedSupportingCharacters.Any(c =>
                    c.Name.Equals(character, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add($"Character '{character}' is a family member who was not selected for this book.");
            }
        }
    }

    private static void ValidateBekiPresence(BekiStoryOutput story, List<string> errors)
    {
        var declared = story.StoryCustomization.BekiPages;
        var actual = story.StoryPages
            .Where(p => p.BekiPresent)
            .Select(p => p.PageNumber)
            .OrderBy(n => n)
            .ToList();

        if (declared.Count != declared.Distinct().Count())
        {
            errors.Add("storyCustomization.bekiPages contains duplicates.");
        }

        if (actual.Count < BekiStoryConstants.MinBekiPages || actual.Count > BekiStoryConstants.MaxBekiPages)
        {
            errors.Add(
                $"Beki must appear meaningfully on {BekiStoryConstants.MinBekiPages}–{BekiStoryConstants.MaxBekiPages} pages; " +
                $"bekiPresent is true on {actual.Count} pages.");
        }

        if (!declared.OrderBy(n => n).SequenceEqual(actual))
        {
            errors.Add(
                $"storyCustomization.bekiPages [{string.Join(", ", declared.OrderBy(n => n))}] does not match the pages " +
                $"flagged bekiPresent [{string.Join(", ", actual)}].");
        }

        // Beki listed in the cast but not flagged present (or the reverse) breaks the visual
        // pipeline, which attaches the canonical Beki asset strictly from charactersPresent.
        foreach (var page in story.StoryPages)
        {
            var castHasBeki = page.CharactersPresent.Any(IsBeki);
            if (castHasBeki != page.BekiPresent)
            {
                errors.Add(
                    $"Page {page.PageNumber}: bekiPresent is {page.BekiPresent} but charactersPresent " +
                    $"{(castHasBeki ? "includes" : "does not include")} Beki. They must agree.");
            }
        }
    }

    private static void ValidateCtaAndEnding(BekiStoryOutput story, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(story.Page12CtaKa))
        {
            errors.Add("page12CtaKa is required.");
        }

        var lastPage = story.StoryPages.FirstOrDefault(p => p.PageNumber == BekiStoryConstants.PageCount);
        if (lastPage is not null &&
            !string.IsNullOrWhiteSpace(story.Page12CtaKa) &&
            lastPage.StoryTextKa.Contains(story.Page12CtaKa, StringComparison.Ordinal))
        {
            errors.Add("page12CtaKa must not be duplicated inside storyPages[11].storyTextKa; the layout places it separately.");
        }

        foreach (var page in story.StoryPages)
        {
            foreach (var ending in BekiStoryConstants.ForbiddenEndings)
            {
                // Word-boundary-ish check: "ბოლო" is a common word, so only flag it when it
                // closes the final page, where it reads as a sign-off.
                var isSoftWord = ending is "ბოლო";
                var hit = isSoftWord
                    ? page.PageNumber == BekiStoryConstants.PageCount &&
                      page.StoryTextKa.TrimEnd('.', '!', '…', ' ').EndsWith(ending, StringComparison.Ordinal)
                    : page.StoryTextKa.Contains(ending, StringComparison.OrdinalIgnoreCase);

                if (hit)
                {
                    errors.Add(
                        $"Page {page.PageNumber}: remove '{ending}'. The series never signs off — page 12 opens the next chapter.");
                }
            }
        }
    }

    private static void ValidateCover(BekiStoryOutput story, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(story.Cover.CoverSceneSummaryEn))
        {
            errors.Add("cover.coverSceneSummaryEn is required.");
        }

        if (story.Cover.FeaturedCharacters.Count == 0)
        {
            errors.Add("cover.featuredCharacters must name at least the child.");
        }
    }

    private static void ValidateMemory(BekiStoryOutput story, List<string> errors)
    {
        var memory = story.ContinuationMemory;

        if (string.IsNullOrWhiteSpace(memory.NextChapterHookKa))
        {
            errors.Add("continuationMemory.nextChapterHookKa is required.");
        }

        if (string.IsNullOrWhiteSpace(memory.ResolvedThreadKa))
        {
            errors.Add("continuationMemory.resolvedThreadKa is required — this book must close its own problem.");
        }

        if (memory.OpenThreadsKa.Count == 0)
        {
            errors.Add("continuationMemory.openThreadsKa must contain at least one thread so the series continues.");
        }

        if (memory.RecentPlotPatternsToAvoidEn.Count == 0)
        {
            errors.Add(
                "continuationMemory.recentPlotPatternsToAvoidEn must list the formulas this book used, " +
                "so the next one does not repeat them.");
        }
    }

    private static bool IsBeki(string name) =>
        name.Equals("Beki", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("ბეკი", StringComparison.Ordinal);

    private static bool IsChild(string name, string childName) =>
        name.Equals(childName, StringComparison.OrdinalIgnoreCase);

    private int CountWords(string text) => WordPattern().Matches(text).Count;
}
