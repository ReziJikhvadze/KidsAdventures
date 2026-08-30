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

    /// <summary>
    /// The letter that was never written: this book could not be made.
    ///
    /// Every other outcome of a purchase has an email behind it — the story is ready, the pictures
    /// are ready, the PDF is ready, the parcel has shipped. A failure had none, so the only person
    /// told was whoever was on duty, and the family who paid learned about it by refreshing a
    /// screen that never changed.
    ///
    /// <paramref name="parentMessage"/> comes from
    /// <see cref="Story.ParentFacingFailure.ToParentMessage"/> and is the entire explanation the
    /// letter carries. The stored failure — code, stage, spread number — must never reach it: the
    /// same string goes to the admin alert, which is where it belongs.
    ///
    /// <paramref name="childName"/> and <paramref name="bookTitle"/> are both optional because a
    /// failure is exactly the moment they may be missing: a book that stopped before its title was
    /// written still needs its parent told.
    /// </summary>
    Task SendBookFailedAsync(
        string toAddress,
        string? childName,
        string? bookTitle,
        string parentMessage,
        CancellationToken cancellationToken = default);

    Task SendContactFormAsync(
        string senderName,
        string senderEmail,
        string message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// An operational alert to whoever is on duty — not to a customer.
    ///
    /// <paramref name="lines"/> are label/value pairs rendered as a small table, because every
    /// one of these alerts is the same shape: something happened, here are the four facts you
    /// need to decide whether to do anything about it.
    /// </summary>
    Task SendAdminAlertAsync(
        string subject,
        string headline,
        IReadOnlyList<(string Label, string Value)> lines,
        string? linkUrl,
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
