using AdventurePacks.Api.Domain.Beki;

namespace AdventurePacks.Api.Services.Beki;

/// <summary>
/// Turns an approved story into one scene spec per illustration.
///
/// This is deliberately ordinary code, not a model call. Every field it needs already
/// exists in the approved story — the generator was required to produce
/// <c>sceneSummaryEn</c>, <c>charactersPresent</c>, <c>childAgencyEn</c> and
/// <c>continuityFromPreviousEn</c> precisely so this step could be deterministic. Asking a
/// model to re-derive them would add cost, latency and a chance of inventing a character
/// who is not in the book.
///
/// Composition is varied by rule rather than left to the image model. Twelve pages that
/// all default to the same centred medium shot read as a slideshow, and the text-safe area
/// has to alternate or the Georgian text lands on the child's face on every page.
/// </summary>
public sealed class BekiSceneSpecBuilder
{
    public BekiPageSceneSpec BuildCoverSpec(BekiStoryOutput story) => new()
    {
        SceneId = $"{story.RequestId}-cover",
        PageNumber = null,
        CharactersPresent = story.Cover.FeaturedCharacters.ToList(),
        ChildAction = "Standing as the hero of the story, inviting the reader in",
        SceneSummaryEn = story.Cover.CoverSceneSummaryEn,
        MainEmotionalBeat = "invitation and wonder",
        Environment = "The signature location of this book's world",
        KeyObject = string.Empty,
        ContinuityState = [],
        Composition = new BekiComposition
        {
            ShotType = "full body hero shot",
            CameraAngle = "eye level, slight heroic low angle",
            HeroPlacement = "centre, occupying the lower two thirds",
            // The title needs clean sky, not a busy horizon.
            TextSafeArea = "upper third reserved clear for the Georgian title and subtitle",
            SupportingPlacement = "secondary characters lower and to the side, smaller than the child",
            FocalObjectPlacement = "not required",
        },
    };

    public IReadOnlyList<BekiPageSceneSpec> BuildPageSpecs(BekiStoryOutput story)
    {
        var specs = new List<BekiPageSceneSpec>(BekiStoryConstants.PageCount);
        var ordered = story.StoryPages.OrderBy(p => p.PageNumber).ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            var page = ordered[i];
            var previous = i > 0 ? ordered[i - 1] : null;

            specs.Add(new BekiPageSceneSpec
            {
                SceneId = $"{story.RequestId}-p{page.PageNumber:00}",
                PageNumber = page.PageNumber,
                CharactersPresent = page.CharactersPresent.ToList(),
                ChildAction = page.ChildAgencyEn,
                SceneSummaryEn = page.SceneSummaryEn,
                MainEmotionalBeat = page.NarrativeBeatEn,
                Environment = page.SceneSummaryEn,
                KeyObject = string.Empty,
                ContinuityState = BuildContinuityState(page, previous),
                Composition = BuildComposition(page),
            });
        }

        return specs;
    }

    /// <summary>
    /// What this page must not contradict. The outfit line is repeated on every page on
    /// purpose: costume drift between pages is the single most common way a personalized
    /// book stops looking like one book.
    /// </summary>
    private static Dictionary<string, string> BuildContinuityState(
        BekiStoryPage page,
        BekiStoryPage? previous)
    {
        var state = new Dictionary<string, string>
        {
            ["outfit"] = "Unchanged approved story outfit from the Visual Bible",
            ["heroIdentity"] = "Unchanged from the approved hero anchor",
        };

        if (previous is not null)
        {
            state["previousBeat"] = previous.NarrativeBeatEn;
            state["previousScene"] = previous.SceneSummaryEn;
        }

        if (!string.IsNullOrWhiteSpace(page.ContinuityFromPreviousEn))
        {
            state["causalLink"] = page.ContinuityFromPreviousEn!;
        }

        return state;
    }

    /// <summary>
    /// Composition is chosen from the page's narrative function, so the visual rhythm
    /// follows the story's rhythm: a reveal earns a wide shot, a relationship beat earns a
    /// close one. The text-safe area alternates top/bottom by page parity so consecutive
    /// spreads do not place Georgian text in the same corner twice running.
    /// </summary>
    private static BekiComposition BuildComposition(BekiStoryPage page)
    {
        var (shot, angle) = page.PageTurnFunction switch
        {
            "invitation" => ("wide establishing shot", "eye level"),
            "curiosity" => ("medium shot", "slightly low, looking with the child"),
            "choice" => ("medium close shot", "eye level"),
            "discovery" => ("wide shot", "slightly high, revealing the space"),
            "consequence" => ("medium shot", "eye level"),
            "relationship" => ("close two-shot", "eye level"),
            "humor" => ("medium shot", "eye level"),
            "setback" => ("medium shot", "slightly high"),
            "reveal" => ("wide dramatic shot", "low angle"),
            "resolution" => ("medium wide shot", "eye level"),
            "continuation_reveal" => ("wide shot with depth", "eye level looking outward"),
            _ => ("medium shot", "eye level"),
        };

        var textAtTop = page.PageNumber % 2 == 0;

        return new BekiComposition
        {
            ShotType = shot,
            CameraAngle = angle,
            HeroPlacement = textAtTop
                ? "child in the lower half, clearly the focal figure"
                : "child in the upper-middle, clearly the focal figure",
            TextSafeArea = textAtTop
                ? "upper third kept visually calm for Georgian story text"
                : "lower third kept visually calm for Georgian story text",
            SupportingPlacement = "supporting characters smaller and offset; never in front of the child",
            FocalObjectPlacement = "any key object within the child's reach or eyeline",
        };
    }
}
