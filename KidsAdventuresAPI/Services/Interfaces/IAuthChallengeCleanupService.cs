namespace AdventurePacks.Api.Services.Interfaces;

/// <summary>
/// Sweeps spent and expired sign-in challenges. Nothing depends on the rows once they
/// lapse, and letting them accumulate turns a small hot table into a large cold one.
/// </summary>
public interface IAuthChallengeCleanupService
{
    Task PurgeExpiredAsync();
}
