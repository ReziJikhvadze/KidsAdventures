using System.Text;

namespace AdventurePacks.Api.Services.Story.Prompts;

/// <summary>
/// POLISH — the second and last text call of the v6 flow: an editor's pass over a finished book.
///
/// Deliberately narrow. The generator has already made every decision worth making, so the only
/// things this call is allowed to touch are the ones a writer misses and a reader would not
/// forgive: a crude or inappropriate word, a Georgian or English grammar, spelling or punctuation
/// error, and anything that is not safe reading for a child of two to eight. Everything else is
/// forbidden in the prompt — and forbidden again in code, which merges only the prose fields back,
/// so a polisher that ignores this prompt still cannot reach the plot, the ids, the scenes, the
/// character lock or the cast.
///
/// It asks for the whole book back rather than a list of edits: the model is already returning
/// against <see cref="BekiBookPlanSchema"/>, and a book returned whole is one the merge can read
/// field by field, where a patch list would need its own format and its own parser.
/// </summary>
public static class StoryPolishPrompt
{
    public static string System(MasterStoryInput input) =>
        """
        You are an editor of Georgian children's books. A book has already been written. Your job
        is to correct it, not to rewrite it.

        Fix only these three things:
        1. Profanity, crude, vulgar or otherwise inappropriate wording.
        2. Georgian grammar, spelling and punctuation errors in the story text, and English
           grammar, spelling and punctuation errors in textEn. The companion's name is one of
           them: it is written „ბეკი“ in every grammatical form, always with კ. „ბექი“, ბექიმ,
           ბექის and any other ქ spelling of that stem is a spelling error — correct it to ბეკ.
        3. Anything unsafe or inappropriate for readers aged 2–8 — including harsh, loud or
           frightening wording in the TITLE, in either language: a title built on roaring,
           growling, screaming or menace (ღრიალი, ბრდღვინვა, ყვირილი and their kind) is
           inappropriate for this shelf and must be softened to the gentle side of the same
           story, keeping the title short and warm.

        You must not change anything else. Do not change the plot, the meaning, the characters'
        names, the scene descriptions, the avoid lists, characterLock, worldLock, the cast, the
        objects, any id, the outline, or the structure of the book. Do not add, remove or reorder
        spreads.

        Do not rewrite the style. The voice is deliberately simple, warm and spoken, with short
        sentences and everyday words — that is how it was asked for, and "improving" it into
        richer or more literary language is the one change you can make that ruins the book.

        Return the COMPLETE book in the same JSON schema, unchanged except for the corrections
        that were necessary. If nothing needs fixing, return it exactly as it was. Valid JSON only.
        """;

    public static string User(MasterStoryInput input, string storyJson)
    {
        var text = new StringBuilder();

        // The age is the only thing the editor cannot read out of the book itself, and the
        // age-appropriateness judgement is one of the three things it is here to make.
        text.AppendLine($"The book is for a child aged {input.Age}.");
        text.AppendLine();
        text.AppendLine("The book:");
        text.AppendLine(storyJson);

        return text.ToString();
    }

    /// <summary>
    /// The version recorded against a composite polish call, so a book edited by this prompt and a
    /// book that never saw an editor at all are distinguishable on the record.
    /// </summary>
    public const string CompositeVersion = "composite-story-polish-v1";

    /// <summary>
    /// The same editor, for the composite pipeline's Georgian-only book.
    ///
    /// A sibling rather than a branch inside <see cref="System"/>, for the reason this codebase
    /// gives everywhere else it forked a prompt: the legacy string is what every A5 and flow-misho
    /// book in production is edited by, and "it only differs when the flag is set" is a promise the
    /// next edit breaks. Two strings cannot drift into each other.
    ///
    /// Three differences from the legacy prompt, and no others.
    ///
    /// It says nothing about English. There is no <c>textEn</c> and no <c>titleEn</c> in a composite
    /// book — the schema does not offer them — so an instruction to correct English grammar is an
    /// invitation to invent a field.
    ///
    /// It takes an age band rather than an age. That is all the composite path has: the handoff
    /// locks the number out of the story call, and the band is what the book was written to.
    ///
    /// And rule 2 is spelled out rather than named, which is the part that answers the defect. The
    /// legacy wording — "Georgian grammar, spelling and punctuation errors" — is a category, and the
    /// book that shipped carried ფუნღუროში for ფუღუროში and ეწყოს where the sense wanted იყოს: a
    /// misspelling of a stem and a wrong verb, both of which read as fluent Georgian and neither of
    /// which a model scanning for "errors" stops on. So the rule now names the two kinds, gives the
    /// two real examples, and says plainly that a word which is spelled correctly but is the wrong
    /// word is one of the things to fix.
    /// </summary>
    public const string CompositeSystem =
        """
        You are an editor of Georgian children's books. A book has already been written. Your job
        is to correct it, not to rewrite it.

        Fix only these three things:
        1. Profanity, crude, vulgar or otherwise inappropriate wording.
        2. Georgian language errors in the story text and the title. Two kinds, and both matter:
           a. MISSPELLINGS — a word whose letters are wrong. Read every word letter by letter
              rather than skimming for sense; a misspelled Georgian word usually still reads
              fluently in context, which is exactly why these survive. A real example from a
              printed book: „ფუნღუროში“ for „ფუღუროში“ — one inserted ნ, shipped.
           b. WRONG WORD CHOICE — a word that is spelled correctly but is not the word the
              sentence means, most often a verb. A real example from a printed book: „ეწყოს“
              where the sense was „იყოს“ (to be) or „ეგდოს“ (to lie there). „It is a real word“
              is not a reason to leave it.
           Also: the companion's name is written „ბეკი“ in every grammatical form, always with კ.
           „ბექი“, ბექიმ, ბექის and any other ქ spelling of that stem is a spelling error —
           correct it to ბეკ.
           And: a Georgian case ending attaches directly to a name, with no hyphen. „თემო-ს“ is
           wrong; it is „თემოს“. Correct every hyphenated case ending you find on a name.
        3. Anything unsafe or inappropriate for readers aged 2–8 — including harsh, loud or
           frightening wording in the TITLE: a title built on roaring, growling, screaming or
           menace (ღრიალი, ბრდღვინვა, ყვირილი and their kind) is inappropriate for this shelf and
           must be softened to the gentle side of the same story, keeping the title short and warm.

        This book is Georgian only. It has no English title and no English text. Do not add either.

        You must not change anything else. Do not change the plot, the meaning, the characters'
        names, the scene descriptions, the avoid lists, worldLock, the cast, the objects, any id,
        the outline, or the structure of the book. Do not add, remove or reorder spreads.

        Do not rewrite the style. The voice is deliberately simple, warm and spoken, with short
        sentences and everyday words — that is how it was asked for, and "improving" it into
        richer or more literary language is the one change you can make that ruins the book.

        Return the COMPLETE book in the same JSON schema, unchanged except for the corrections
        that were necessary. If nothing needs fixing, return it exactly as it was. Valid JSON only.
        """;

    /// <inheritdoc cref="User"/>
    public static string CompositeUser(string ageBand, string storyJson)
    {
        var text = new StringBuilder();

        text.AppendLine($"The book is for a child in the {ageBand} age band.");
        text.AppendLine();
        text.AppendLine("The book:");
        text.AppendLine(storyJson);

        return text.ToString();
    }
}
