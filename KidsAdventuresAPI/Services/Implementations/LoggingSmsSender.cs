using AdventurePacks.Api.Infrastructure;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Implementations;

/// <summary>
/// Development SMS sender: writes the message to the log instead of paying a gateway.
/// The number is masked so a shared log never becomes a list of customer phone numbers.
/// </summary>
public sealed class LoggingSmsSender(ILogger<LoggingSmsSender> logger) : ISmsSender
{
    public string ProviderName => "log";

    public bool IsLive => false;

    public Task SendAsync(string e164PhoneNumber, string message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "[dev-sms] to {Phone}: {Message}",
            GeorgianPhoneNumber.Mask(e164PhoneNumber),
            message);
        return Task.CompletedTask;
    }
}
