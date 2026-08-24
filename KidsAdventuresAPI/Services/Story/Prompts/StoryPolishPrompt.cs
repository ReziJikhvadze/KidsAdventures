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
           grammar, spelling and punctuation errors in textEn.
        3. Anything unsafe or inappropriate for readers aged 2–8.

        You must not change anything else. Do not change the plot, the meaning, the characters'
        names, the scene descriptions, the avoid lists, characterLock, the cast, the objects, any
        id, the outline, or the structure of the book. Do not add, remove or reorder spreads.

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
}
