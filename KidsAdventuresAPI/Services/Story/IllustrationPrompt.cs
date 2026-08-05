namespace AdventurePacks.Api.Services.Story;

/// <summary>
/// Puts an illustration brief together into the one string an image model actually reads.
///
/// The image APIs have no separate field for what to avoid, so an exclusion list is only
/// honoured if it is written into the prompt. Until this existed the master call authored a
/// negative prompt, we stored it, and nothing ever sent it — which mattered most for the entry
/// it always contains: no text, no captions. An image model asked to illustrate a scene with a
/// caption in mind will happily letter one onto the picture, in garbled approximations of
/// Georgian, across the child's face.
/// </summary>
public static class IllustrationPrompt
{
    public static string Compose(string prompt, string? negativePrompt)
    {
        var exclusions = string.IsNullOrWhiteSpace(negativePrompt)
            ? Prompts.MasterStorySchema.DefaultNegativePrompt
            : negativePrompt.Trim();

        return $"""
            {prompt.Trim()}

            Do not include: {exclusions}
            """;
    }
}
