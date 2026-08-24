using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story.Prompts;

namespace AdventurePacks.Api.Services.Story;

/// <summary>
/// The two things about a book that a prompt is allowed to ask for and code has to guarantee.
///
/// Both were asked for politely first and both came back wrong in a book a parent read: the eye
/// colour the parent chose was missing from the character lock, so all nine illustrations invented
/// one; and the companion's Georgian name printed as „ბექი“ instead of „ბეკი“. A prompt rule is
/// how a model is told; this is how the book is made true whatever the model did.
/// </summary>
public static class BekiIdentityRules
{
    /// <summary>
    /// Writes the parent's chosen eye colour into the character lock, which is the one string
    /// quoted verbatim into every illustration prompt.
    ///
    /// Appended unconditionally rather than only when the lock looks like it is missing: a lock
    /// that already says "green jumper" contains "green", and a book about a brown-eyed child
    /// would pass a check like that and lose the colour in all nine pictures. Saying it twice
    /// costs a sentence; saying it never costs the book.
    /// </summary>
    public static MasterStory EnforceCharacterLock(MasterStory story, MasterStoryInput input)
    {
        if (string.IsNullOrWhiteSpace(input.EyeColor)) return story;

        var sentence =
            $"The child's eyes are {input.EyeColor.Trim()}. This is the parent's explicit choice "
            + "and overrides anything the photograph or the description above suggests.";

        var characterLock = (story.CharacterLock ?? string.Empty).TrimEnd();

        return story with
        {
            CharacterLock = characterLock.Length == 0 ? sentence : characterLock + " " + sentence
        };
    }

    /// <summary>
    /// The companion's name, in the only spelling the brand has: „ბეკი“, with კ.
    ///
    /// One replacement covers every declension, because every one of them — ბეკიმ, ბეკის,
    /// ბეკისთან, ბეკიდან — is built on the same stem plus ი. Only the Georgian prose that gets
    /// printed is touched: the book's title and each spread's text. The English, the scenes and
    /// the ids are left alone, and this runs last, after both models have had their say.
    ///
    /// Unless the book legitimately contains that spelling as a person: ბექი is a real Georgian
    /// name, and a child or a cast member who carries it must not be renamed by a brand rule.
    /// When the hero or any cast member's own name contains the ბექ stem, the correction stands
    /// down entirely — the prompt and the polish pass still ask for the right spelling of Beki,
    /// and a rare mixed page is a smaller wrong than rewriting a child's name in their own book.
    /// </summary>
    public static MasterStory EnforceBrandSpelling(MasterStory story, string childName)
    {
        var stemBelongsToAPerson =
            (childName ?? string.Empty).Contains("ბექ", StringComparison.Ordinal)
            || (story.Cast ?? []).Any(member =>
                (member.Name ?? string.Empty).Contains("ბექ", StringComparison.Ordinal));

        if (stemBelongsToAPerson) return story;

        return story with
        {
            Concept = story.Concept with { Title = Corrected(story.Concept.Title) },
            Spreads = story.Spreads
                .Select(spread => spread with { Text = Corrected(spread.Text) })
                .ToList()
        };
    }

    private static string Corrected(string text) =>
        string.IsNullOrEmpty(text) ? text : text.Replace("ბექი", "ბეკი", StringComparison.Ordinal);
}
