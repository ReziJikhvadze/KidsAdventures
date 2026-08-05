using System.Globalization;
using System.Text.RegularExpressions;
using AdventurePacks.Api.Domain.Story;

namespace AdventurePacks.Api.Services.Story.Validation;

/// <summary>
/// R21 — the writer may enrich, but never alter.
///
/// This is what keeps the plan authoritative without a model having to adjudicate. If the plan
/// says dragon and the prose says unicorn, that is decidable here rather than debatable
/// somewhere else. Freedom over how a page is written, none over what happens in it.
/// </summary>
public sealed class WriterMayNotAlterRule : IProseRule
{
    public string Id => "R21";
    public RuleTier Tier => RuleTier.Blocking;

    public IEnumerable<ValidationFinding> Check(ProseContext context)
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var character in context.Casting.Characters)
        {
            known.Add(character.Name);
        }

        foreach (var page in context.Pages)
        {
            var beat = context.Blueprint.Beats.FirstOrDefault(b => b.Page == page.Page);
            if (beat is null)
            {
                yield return new ValidationFinding(Id, Tier, page.Page,
                    "this page has no matching beat",
                    "the writer may not add pages");
                continue;
            }

            // Only characters the plan put on this page may speak or act on it. Names are the
            // one entity we can detect reliably in free prose without guessing.
            var present = beat.CharactersPresent
                .Select(id => context.Casting.Find(id)?.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var name in known.Where(n => !present.Contains(n)))
            {
                if (ContainsWord(page.Content, name) || ContainsWord(page.Caption, name))
                {
                    yield return new ValidationFinding(Id, Tier, page.Page,
                        $"'{name}' appears in the prose but is not on this page in the plan",
                        $"remove {name} from this page, or add them to the beat");
                }
            }
        }
    }

    private static bool ContainsWord(string haystack, string needle) =>
        Regex.IsMatch(haystack, $@"(^|\W){Regex.Escape(needle)}(\W|$)",
            RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
}

/// <summary>
/// R13 — the prose may not hand the hero something they are not carrying.
///
/// The text side of the vanishing key: a page that describes using an object the state says
/// was never picked up.
/// </summary>
public sealed class ProseInventoryRule : IProseRule
{
    public string Id => "R13";
    public RuleTier Tier => RuleTier.Blocking;

    public IEnumerable<ValidationFinding> Check(ProseContext context)
    {
        foreach (var page in context.Pages)
        {
            var state = context.States.FirstOrDefault(s => s.Page == page.Page);
            var beat = context.Blueprint.Beats.FirstOrDefault(b => b.Page == page.Page);
            if (state is null || beat is null)
            {
                continue;
            }

            var available = new HashSet<string>(state.Inventory, StringComparer.OrdinalIgnoreCase);
            foreach (var introduced in beat.ObjectsIntroduced)
            {
                available.Add(introduced);
            }

            foreach (var storyObject in context.Blueprint.Objects)
            {
                if (available.Contains(storyObject.Id))
                {
                    continue;
                }

                var mentioned = Regex.IsMatch(page.Content,
                    $@"(^|\W){Regex.Escape(storyObject.Name)}(\W|$)",
                    RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

                // Objects still in the world but not in hand are fine to look at; the failure is
                // only when the page treats one as present before it exists in the story at all.
                var introducedLater = context.Blueprint.Beats
                    .Where(b => b.Page <= page.Page)
                    .SelectMany(b => b.ObjectsIntroduced)
                    .Contains(storyObject.Id, StringComparer.OrdinalIgnoreCase);

                if (mentioned && !introducedLater)
                {
                    yield return new ValidationFinding(Id, Tier, page.Page,
                        $"the prose mentions '{storyObject.Name}' before the story introduces it",
                        $"remove the mention, or introduce '{storyObject.Id}' on an earlier page");
                }
            }
        }
    }
}

/// <summary>
/// R17 — one script per book.
///
/// This rule already exists as a sentence in the prompt and is ignored roughly as often as it
/// is followed: Cyrillic and Korean characters have shipped inside Georgian words. As an
/// assertion it simply cannot happen again.
/// </summary>
public sealed class ScriptPurityRule : IProseRule
{
    public string Id => "R17";
    public RuleTier Tier => RuleTier.Blocking;

    public IEnumerable<ValidationFinding> Check(ProseContext context)
    {
        var georgian = string.Equals(context.Meta.Language, "ka", StringComparison.OrdinalIgnoreCase);

        foreach (var page in context.Pages)
        {
            var text = page.Title + " " + page.Caption + " " + page.Content;
            var offenders = text
                .Where(c => char.IsLetter(c) && IsForeign(c, georgian))
                .Distinct()
                .Take(8)
                .ToList();

            if (offenders.Count > 0)
            {
                yield return new ValidationFinding(Id, Tier, page.Page,
                    $"characters from another alphabet appear in the text: {string.Join(" ", offenders)}",
                    "rewrite the affected words in the book's own script");
            }
        }
    }

    private static bool IsForeign(char c, bool georgian)
    {
        var isGeorgian = c is >= 'Ⴀ' and <= 'ჿ';
        var isLatin = c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');

        // Latin is tolerated in a Georgian book for brand names like ADVENTRYA; what is never
        // acceptable is a third script appearing mid-word.
        return georgian ? !isGeorgian && !isLatin : !isLatin;
    }
}

/// <summary>R15 — read-aloud length has to suit the age it was written for.</summary>
public sealed class PageLengthRule : IProseRule
{
    public string Id => "R15";
    public RuleTier Tier => RuleTier.Craft;

    public IEnumerable<ValidationFinding> Check(ProseContext context)
    {
        var (min, max) = context.Meta.ChildAge switch
        {
            <= 4 => (6, 28),
            <= 6 => (10, 45),
            <= 8 => (18, 70),
            _ => (25, 95)
        };

        foreach (var page in context.Pages)
        {
            var words = WordCount(page.Content);
            if (words < min)
            {
                yield return new ValidationFinding(Id, Tier, page.Page,
                    $"{words} words is thin for age {context.Meta.ChildAge}",
                    $"aim for {min}-{max}");
            }
            else if (words > max)
            {
                yield return new ValidationFinding(Id, Tier, page.Page,
                    $"{words} words is long for age {context.Meta.ChildAge}",
                    $"trim towards {max} — the picture carries half the page");
            }

            var captionWords = WordCount(page.Caption);
            if (captionWords is < 2 or > 6)
            {
                yield return new ValidationFinding(Id, Tier, page.Page,
                    $"the caption is {captionWords} words",
                    "captions read best at two to five");
            }
        }
    }

    private static int WordCount(string text) =>
        text.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;
}

/// <summary>
/// R16 — no two pages should sound the same.
///
/// Catches the drift where a model settles into a sentence shape and repeats it, which reads
/// as monotony long before a reader can say why.
/// </summary>
public sealed class RepetitionRule : IProseRule
{
    public string Id => "R16";
    public RuleTier Tier => RuleTier.Craft;

    private const double OverlapThreshold = 0.28;

    public IEnumerable<ValidationFinding> Check(ProseContext context)
    {
        var grams = context.Pages.ToDictionary(p => p.Page, p => Trigrams(p.Content));

        foreach (var page in context.Pages)
        {
            foreach (var other in context.Pages.Where(o => o.Page < page.Page))
            {
                var a = grams[page.Page];
                var b = grams[other.Page];
                if (a.Count == 0 || b.Count == 0)
                {
                    continue;
                }

                var shared = a.Intersect(b).Count();
                var overlap = (double)shared / Math.Min(a.Count, b.Count);

                if (overlap >= OverlapThreshold)
                {
                    yield return new ValidationFinding(Id, Tier, page.Page,
                        $"this page reuses {overlap:P0} of the phrasing on page {other.Page}",
                        "say it a different way");
                }
            }
        }
    }

    private static HashSet<string> Trigrams(string text)
    {
        var words = text.ToLower(CultureInfo.InvariantCulture)
            .Split([' ', '\n', '\r', '\t', '.', ',', '!', '?', ';', ':'], StringSplitOptions.RemoveEmptyEntries);

        var set = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i + 2 < words.Length; i++)
        {
            set.Add($"{words[i]} {words[i + 1]} {words[i + 2]}");
        }

        return set;
    }
}

/// <summary>
/// R19 — children's books live on dialogue.
///
/// A book that is entirely narration is the "weak dialogue" complaint at its root: nobody ever
/// speaks, so nobody has a personality worth becoming attached to.
/// </summary>
public sealed class DialogueCoverageRule : IProseRule
{
    public string Id => "R19";
    public RuleTier Tier => RuleTier.Craft;

    private const double MinimumRatio = 0.6;

    public IEnumerable<ValidationFinding> Check(ProseContext context)
    {
        if (context.Pages.Count == 0)
        {
            yield break;
        }

        var withSpeech = context.Pages.Count(HasDialogue);
        var ratio = (double)withSpeech / context.Pages.Count;

        if (ratio < MinimumRatio)
        {
            yield return new ValidationFinding(Id, Tier, null,
                $"only {ratio:P0} of pages have anyone speaking",
                $"get to {MinimumRatio:P0} — children remember voices, not narration");
        }

        foreach (var page in context.Pages.Where(p => !HasDialogue(p)))
        {
            var beat = context.Blueprint.Beats.FirstOrDefault(b => b.Page == page.Page);
            if (beat is not null && beat.CharactersPresent.Count > 1)
            {
                yield return new ValidationFinding(Id, Tier, page.Page,
                    "two characters share this page and neither says anything",
                    "let one of them speak");
            }
        }
    }

    /// <summary>Covers ASCII, typographic and Georgian quotation marks.</summary>
    public static bool HasDialogue(WrittenPage page) =>
        page.Content.Contains('"') || page.Content.Contains('„')
        || page.Content.Contains('“') || page.Content.Contains('«')
        || page.Content.Contains('—');
}

/// <summary>R14 — the openings that make a book sound generated before the first full stop.</summary>
public sealed class NoGenericOpeningRule : IProseRule
{
    public string Id => "R14";
    public RuleTier Tier => RuleTier.Craft;

    private static readonly string[] Banned =
    [
        "once upon a time",
        "one sunny day",
        "one beautiful day",
        "in a land far",
        "ერთხელ ერთ",
        "ერთ მშვენიერ დღეს",
        "დიდი ხნის წინ"
    ];

    public IEnumerable<ValidationFinding> Check(ProseContext context)
    {
        var first = context.Pages.OrderBy(p => p.Page).FirstOrDefault();
        if (first is null)
        {
            yield break;
        }

        var opening = first.Content.TrimStart();

        foreach (var phrase in Banned.Where(p => opening.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            yield return new ValidationFinding(Id, Tier, first.Page,
                $"the book opens with \"{phrase}\"",
                "open in the middle of something happening instead");
        }
    }
}
