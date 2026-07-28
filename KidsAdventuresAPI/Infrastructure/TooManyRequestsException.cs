namespace AdventurePacks.Api.Infrastructure;

/// <summary>
/// Thrown when a caller trips an application-level throttle, such as asking for a
/// second sign-in code before the resend cooldown has elapsed. The global handler
/// turns this into a 429 with a <c>Retry-After</c> header so the UI can show a
/// countdown instead of a dead button.
/// </summary>
public sealed class TooManyRequestsException(string message, int retryAfterSeconds)
    : Exception(message)
{
    public int RetryAfterSeconds { get; } = retryAfterSeconds < 1 ? 1 : retryAfterSeconds;
}
