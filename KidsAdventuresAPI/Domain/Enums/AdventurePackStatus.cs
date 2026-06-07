namespace AdventurePacks.Api.Domain.Enums;

public enum AdventurePackStatus
{
    Pending = 1,
    /// <summary>Legacy single-phase job; treat like GeneratingStory in UI.</summary>
    Generating = 2,
    Completed = 3,
    Failed = 4,
    GeneratingStory = 5,
    StoryReady = 6,
    GeneratingPdf = 7
}
