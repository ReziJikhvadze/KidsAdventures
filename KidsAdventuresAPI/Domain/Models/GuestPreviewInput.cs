using AdventurePacks.Api.Domain.Enums;

namespace AdventurePacks.Api.Domain.Models;

/// <summary>Inputs for the free, no-login single-page teaser generation.</summary>
public sealed class GuestPreviewInput
{
    public Guid? ReusePreviewId { get; init; }

    public required string ChildName { get; init; }
    public required int Age { get; init; }

    /// <summary>When given, the age is derived from this rather than trusted from the browser.</summary>
    public DateOnly? BirthDate { get; init; }
    public required ThemeType Theme { get; init; }

    /// <summary>girl | boy, when the parent chose. Without it the model decides, and a
    /// parent who picked a girl gets a story about a boy.</summary>
    public string? Gender { get; init; }
    /// <summary>
    /// One of the six inputs a book is built from. The parent picks it on the profile screen, but
    /// until now it only ever reached the saved character — never the story, which is why the
    /// hero's eyes were whatever the model felt like.
    /// </summary>
    public string? EyeColor { get; init; }

    public string? StoryLanguage { get; init; }
    public string? OptionalStoryNotes { get; init; }
    public byte[]? PhotoBytes { get; init; }
    public string PhotoContentType { get; init; } = "image/jpeg";

    /// <summary>
    /// A description already derived from this same portrait, when the child is a saved hero
    /// whose appearance was cached by an earlier book. Given, the vision call is skipped: the
    /// answer would be the same paragraph about the same face, a second time, for money.
    /// </summary>
    public string? AppearanceDescription { get; init; }

    /// <summary>
    /// The account asking for this preview, when the caller signed in first.
    ///
    /// Null for a guest, which is the first book and the reason this route is anonymous at all.
    /// Given, the run is that parent's from the moment it is written rather than from the moment
    /// it is paid for, so their own space can show a preview that is still being made.
    /// </summary>
    public Guid? UserId { get; init; }

    /// <summary>The saved hero it was started for, so the preview files under the same child.</summary>
    public Guid? CharacterId { get; init; }
}
