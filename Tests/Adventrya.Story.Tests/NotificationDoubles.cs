using AdventurePacks.Api.Domain.Entities;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Repositories.Interfaces;
using AdventurePacks.Api.Services.Interfaces;

namespace Adventrya.Story.Tests;

/// <summary>
/// The two collaborators every failure path now reaches for: something to send a letter with, and
/// somewhere to find out who it goes to.
///
/// Shared rather than re-declared per file because both interfaces are wide — a dozen methods and
/// seventeen — and three harnesses copying the same wall of <c>NotSupportedException</c> is three
/// places to update when either interface grows. Everything except the one call under test still
/// throws, so a path that starts sending a different email fails loudly rather than passing.
/// </summary>
internal sealed class RecordingEmailService : IEmailService
{
    /// <summary>Every "your book could not be made" letter this test run produced.</summary>
    public List<SentFailure> Failures { get; } = [];

    /// <summary>Stands in for a mail server that is not answering.</summary>
    public bool Throw { get; set; }

    public Task SendBookFailedAsync(
        string toAddress,
        string? childName,
        string? bookTitle,
        string parentMessage,
        CancellationToken cancellationToken = default)
    {
        if (Throw)
        {
            throw new InvalidOperationException("the mail server is not answering");
        }

        Failures.Add(new SentFailure(toAddress, childName, bookTitle, parentMessage));
        return Task.CompletedTask;
    }

    public Task SendEmailAsync(string toAddress, string subject, string htmlBody, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task SendAccountActivationAsync(string toAddress, string confirmationUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task SendMagicLinkAsync(string toAddress, string magicLinkUrl, int validForMinutes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task SendStoryReadyAsync(string toAddress, string childName, string theme, string packUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task SendSlideshowReadyAsync(string toAddress, string childName, string theme, string packUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task SendPdfReadyAsync(string toAddress, string childName, string theme, string packUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task SendContactFormAsync(string senderName, string senderEmail, string message, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task SendAdminAlertAsync(string subject, string headline, IReadOnlyList<(string Label, string Value)> lines, string? linkUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task SendPrintOrderPlacedAsync(string toAddress, string bookTitle, string city, string deliveryEstimate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task SendPrintOrderStatusAsync(string toAddress, string bookTitle, PrintOrderStatus status, string? trackingCode, string deliveryEstimate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

/// <summary>One letter, as the parent-facing surfaces asked for it to be sent.</summary>
internal sealed record SentFailure(string To, string? ChildName, string? BookTitle, string ParentMessage);

/// <summary>
/// One account, which every book in a test belongs to. <see cref="HasEmail"/> false is the parent
/// who signed up with a phone code and has no address at all — a real state, and the one that
/// turns a courtesy into an exception if nobody checks for it.
/// </summary>
internal sealed class SingleUserRepository : IUserRepository
{
    public const string Address = "parent@example.ge";

    public bool HasEmail { get; set; } = true;

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult<User?>(new User { Id = id, Email = HasEmail ? Address : string.Empty });

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<User?> GetByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<User?> GetByConfirmationTokenAsync(string token, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<Guid> CreateAsync(User user, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<int> PurgeDemoAccountsAsync(string emailSuffix, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> UpdateSubscriptionTypeAsync(Guid userId, SubscriptionType subscriptionType, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> ConfirmEmailAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> SetAdminAsync(Guid userId, bool isAdmin, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> AttachPhoneNumberAsync(Guid userId, string phoneNumber, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> AttachEmailAsync(Guid userId, string email, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task UpdateProfileAsync(Guid userId, string? displayName, string? preferredLanguage, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task AddBookCreditsAsync(Guid userId, int credits, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> TryConsumeBookCreditAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task RefundBookCreditAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> TryConsumeWelcomeStoryAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task RefundWelcomeStoryAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
}
