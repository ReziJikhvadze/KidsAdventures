using AdventurePacks.Api.Domain.Enums;

namespace AdventurePacks.Api.Domain.Models;

/// <summary>Inputs for the free, no-login single-page teaser generation.</summary>
public sealed class GuestPreviewInput
{
    public required string ChildName { get; init; }
    public required int Age { get; init; }
    public required ThemeType Theme { get; init; }
    public string? StoryLanguage { get; init; }
    public string? OptionalStoryNotes { get; init; }
    public byte[]? PhotoBytes { get; init; }
    public string PhotoContentType { get; init; } = "image/jpeg";

    /// <summary>Best-effort client identity (IP) for abuse analysis on the persisted preview record.</summary>
    public string? ClientKey { get; init; }
}
