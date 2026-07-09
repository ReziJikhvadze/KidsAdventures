namespace AdventurePacks.Api.Services.Interfaces;

public interface IEmailService
{
    Task SendEmailAsync(string toAddress, string subject, string htmlBody, CancellationToken cancellationToken = default);

    Task SendAccountActivationAsync(string toAddress, string confirmationUrl, CancellationToken cancellationToken = default);

    Task SendStoryReadyAsync(
        string toAddress,
        string childName,
        string theme,
        string packUrl,
        CancellationToken cancellationToken = default);

    Task SendSlideshowReadyAsync(
        string toAddress,
        string childName,
        string theme,
        string packUrl,
        CancellationToken cancellationToken = default);

    Task SendPdfReadyAsync(
        string toAddress,
        string childName,
        string theme,
        string packUrl,
        CancellationToken cancellationToken = default);

    Task SendContactFormAsync(
        string senderName,
        string senderEmail,
        string message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Follow-up nudge to an anonymous visitor who left their email (e.g. via the exit-intent offer),
    /// reminding them their first fully illustrated storybook is free.
    /// </summary>
    Task SendLeadMagnetAsync(
        string toAddress,
        string? childName,
        string ctaUrl,
        CancellationToken cancellationToken = default);
}
