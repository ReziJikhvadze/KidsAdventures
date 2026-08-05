using AdventurePacks.Api.Domain.Enums;

namespace AdventurePacks.Api.Domain.Models;

/// <summary>Inputs for the free, no-login single-page teaser generation.</summary>
public sealed class GuestPreviewInput
{
    public required string ChildName { get; init; }
    public required int Age { get; init; }
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
}
