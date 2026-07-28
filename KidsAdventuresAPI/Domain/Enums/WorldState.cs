namespace AdventurePacks.Api.Domain.Enums;

/// <summary>
/// A world's place on one child's adventure map.
///
/// Only <see cref="Locked"/>, <see cref="Unlocked"/> and <see cref="Completed"/> are
/// persisted; <see cref="Next"/> is derived at read time from the rest of the map, so
/// there is no stored "next" flag to drift out of step with reality.
/// </summary>
public enum WorldState
{
    Locked = 0,
    Unlocked = 1,
    Completed = 2,
    Next = 3
}
