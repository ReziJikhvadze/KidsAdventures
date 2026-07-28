namespace AdventurePacks.Api.Services.Interfaces;

public interface IEmailService
{
    Task SendEmailAsync(string toAddress, string subject, string htmlBody, CancellationToken cancellationToken = default);

    Task SendAccountActivationAsync(string toAddress, string confirmationUrl, CancellationToken cancellationToken = default);

    /// <summary>One-time sign-in link. Doubles as sign-up: clicking it creates the account.</summary>
    Task SendMagicLinkAsync(
        string toAddress,
        string magicLinkUrl,
        int validForMinutes,
        CancellationToken cancellationToken = default);

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

    /// <summary>Confirms a print order and quotes the delivery window for the given city.</summary>
    Task SendPrintOrderPlacedAsync(
        string toAddress,
        string bookTitle,
        string city,
        string deliveryEstimate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A parcel status change worth telling the parent about. <paramref name="trackingCode"/>
    /// is included only when the courier gave us one.
    /// </summary>
    Task SendPrintOrderStatusAsync(
        string toAddress,
        string bookTitle,
        PrintOrderStatus status,
        string? trackingCode,
        string deliveryEstimate,
        CancellationToken cancellationToken = default);
}
