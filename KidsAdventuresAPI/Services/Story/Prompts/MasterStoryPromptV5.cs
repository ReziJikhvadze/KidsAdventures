using System.Text;
using AdventurePacks.Api.Domain.Story;

namespace AdventurePacks.Api.Services.Story.Prompts;

/// <summary>
/// PLAN BOOK — the Beki format's single planning call.
///
/// Written from the Beki developer handoff and kept close to its wording on purpose: it is the
/// artefact the operator reviews, and a paraphrase makes the two impossible to compare. What is
/// added here is only what the handoff leaves to the implementation — the child's own details,
/// and the reminder that our code, not the model, writes the style, the format and the camera.
///
/// It sits beside v1–v4 rather than replacing any of them. Every book in production is written by
/// v1; this one is reached only by asking for it, so nothing that works today goes through here.
///
/// The handoff says the planner needs name, age, gender, theme and extra wish, and nothing else.
/// The photograph and the eye colour belong to image generation, so neither is sent.
/// </summary>
public static class MasterStoryPromptV5
{
    public static string System(MasterStoryInput input) =>
        $"""
        You plan personalized children's picture books.

        The book must contain exactly {input.SpreadCount} story spreads.

        Use this narrative rhythm:
        1. Enter / setup
        2. Discovery
        3. Action
        4. Complication
        5. Journey or clue
        6. Major reveal
        7. Emotional resolution
        8. Satisfying ending with a small hint that another adventure could follow

        Make the child the main hero.

        Beki is the platform's one canonical story character: every book this platform makes gives
        the child the same warm, curious, brave, magical guide and friend. When Beki appears in a
        spread, list exactly the id "beki" in that spread's characters — never as a cast member,
        and never as anything else spelled or capitalised differently. Beki's appearance is fixed
        elsewhere and is not yours to invent: no visual detail about what Beki looks like belongs
        in any scene, in characterLock, or in a cast entry.

        Beki must appear in spread 1 and in spread {input.SpreadCount} (the last one), and
        meaningfully in at least three other spreads — more when the story naturally calls for it.
        The child stays the protagonist throughout: the child makes the important decisions, and
        Beki never solves the main problem for them. Beki needs no costume for the theme — Beki is
        Beki in every world.

        Use the selected theme as the world of the story.

        Integrate the extra wish naturally when it improves the story. If it would bend the story
        out of shape, leave it out rather than forcing it.

        Create only as many recurring supporting characters as the story needs. None is a valid
        answer. For every recurring supporting character, provide one short, concrete visual
        description.

        For every spread:
        - write Georgian story text
        - write an equivalent English version
        - describe one clear visual scene
        - list which characters appear

        Do not create page titles.

        Each spread should contain one clear story moment.

        The visual scene must describe only what should be visible in the illustration.

        For ages 2–4, keep the story very simple and short.
        For ages 5–8, use slightly richer language while remaining concise and easy to read aloud.
        Treat an age under 2 as 2, and an age over 8 as 8, when choosing language complexity.

        Two things are written by the code that calls you, never by you: the house style and the
        camera. Do not put a style, a format, a shot, a camera angle, a lens, an aspect ratio, a
        text-safe area or an instruction about the child's photograph into any scene. A scene that
        contains them will be sent to the image model twice over.

        Return valid JSON only.
        """;

    public static string User(MasterStoryInput input)
    {
        var text = new StringBuilder();

        text.AppendLine("Child:");
        text.AppendLine($"- name: {input.ChildName}");
        text.AppendLine($"- age: {input.Age}");
        text.AppendLine($"- gender: {input.Gender}");
        text.AppendLine();
        text.AppendLine($"Theme: {ThemeName(input)}");

        if (!string.IsNullOrWhiteSpace(input.ExtraWishes))
        {
            text.AppendLine();
            text.AppendLine($"Extra wish: {input.ExtraWishes!.Trim()}");
        }

        // The child's appearance reaches the image model through characterLock, so the planner is
        // given it here rather than being asked to invent a face it cannot see.
        if (!string.IsNullOrWhiteSpace(input.AppearanceDescription))
        {
            text.AppendLine();
            text.AppendLine("The child looks like this — use it for characterLock, not for the story:");
            text.AppendLine(input.AppearanceDescription!.Trim());
        }

        return text.ToString();
    }

    /// <summary>The theme as a word the model can build a world from, not an enum member.</summary>
    private static string ThemeName(MasterStoryInput input) =>
        input.Theme.ToString().ToLowerInvariant();
}
