using System.Net;
using System.Net.Mail;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Services.Interfaces;

namespace AdventurePacks.Api.Services.Implementations;

public sealed class SmtpEmailService(
    IOptions<EmailOptions> options,
    ILogger<SmtpEmailService> logger) : IEmailService
{
    private readonly EmailOptions _options = options.Value;

    public Task SendEmailAsync(string toAddress, string subject, string htmlBody, CancellationToken cancellationToken = default)
        => SendEmailCoreAsync(toAddress, subject, htmlBody, cancellationToken);

    public Task SendAccountActivationAsync(string toAddress, string confirmationUrl, CancellationToken cancellationToken = default)
    {
        var html = $"""
            <p>Hello,</p>
            <p>Thank you for creating an account with <strong>Adventrya Books</strong>.</p>
            <p>Please confirm your email address to start creating personalized storybooks for your child:</p>
            <p><a href="{confirmationUrl}">Confirm my email</a></p>
            <p>If you did not create this account, you can ignore this message.</p>
            <p>— Adventrya Books</p>
            """;
        return SendEmailCoreAsync(toAddress, "Confirm your Adventrya Books account", html, cancellationToken);
    }

    public Task SendStoryReadyAsync(
        string toAddress,
        string childName,
        string theme,
        string packUrl,
        CancellationToken cancellationToken = default)
    {
        var html = $"""
            <p>Hi there,</p>
            <p>Good news — <strong>{childName}'s {theme} story</strong> has been written and is waiting for you.</p>
            <p>We're now painting the picture-book pages. You'll get another note when the slideshow is ready to read together.</p>
            <p><a href="{packUrl}">Open My Books</a></p>
            <p>With warmth,<br/>Adventrya Books</p>
            """;
        return SendEmailCoreAsync(toAddress, $"{childName}'s story is ready — Adventrya Books", html, cancellationToken);
    }

    public Task SendSlideshowReadyAsync(
        string toAddress,
        string childName,
        string theme,
        string packUrl,
        CancellationToken cancellationToken = default)
    {
        var html = $"""
            <p>Hi there,</p>
            <p>Your picture-book slideshow for <strong>{childName}'s {theme} adventure</strong> is ready — every page is illustrated and waiting for bedtime.</p>
            <p>Snuggle up, tap <strong>Read story</strong>, and swipe through the pages together. When you're ready, you can export a printable PDF from My Books.</p>
            <p><a href="{packUrl}">Read the slideshow</a></p>
            <p>With warmth,<br/>Adventrya Books</p>
            """;
        return SendEmailCoreAsync(toAddress, $"{childName}'s picture book is ready to read — Adventrya Books", html, cancellationToken);
    }

    public Task SendPdfReadyAsync(
        string toAddress,
        string childName,
        string theme,
        string packUrl,
        CancellationToken cancellationToken = default)
    {
        var html = $"""
            <p>Hi there,</p>
            <p>Your printable storybook PDF for <strong>{childName}'s {theme} adventure</strong> is ready to download.</p>
            <p>Open My Books and tap <strong>Download storybook PDF</strong> — perfect for printing or sharing with grandparents.</p>
            <p><a href="{packUrl}">Download from My Books</a></p>
            <p>With warmth,<br/>Adventrya Books</p>
            """;
        return SendEmailCoreAsync(toAddress, $"Your storybook PDF is ready — {childName}", html, cancellationToken);
    }

    public Task SendContactFormAsync(
        string senderName,
        string senderEmail,
        string message,
        CancellationToken cancellationToken = default)
    {
        var inbox = string.IsNullOrWhiteSpace(_options.ContactToAddress)
            ? _options.FromAddress
            : _options.ContactToAddress.Trim();

        var safeName = WebUtility.HtmlEncode(senderName);
        var safeEmail = WebUtility.HtmlEncode(senderEmail);
        var safeMessage = WebUtility.HtmlEncode(message).Replace("\r\n", "<br/>").Replace("\n", "<br/>");

        var html = $"""
            <p>You received a message from the Adventrya Books contact form.</p>
            <p><strong>Name:</strong> {safeName}<br/>
            <strong>Email:</strong> <a href="mailto:{safeEmail}">{safeEmail}</a></p>
            <p><strong>Message:</strong></p>
            <p>{safeMessage}</p>
            """;

        return SendEmailCoreAsync(
            inbox,
            $"Contact form — {senderName}",
            html,
            cancellationToken,
            senderEmail);
    }

    public Task SendLeadMagnetAsync(
        string toAddress,
        string? childName,
        string ctaUrl,
        CancellationToken cancellationToken = default)
    {
        var hero = string.IsNullOrWhiteSpace(childName) ? "your child" : WebUtility.HtmlEncode(childName.Trim());
        var html = $"""
            <p>Hi there,</p>
            <p>You're one step away from a magical bedtime — <strong>{hero}'s first storybook is completely free</strong>, with every one of its six pages fully illustrated.</p>
            <p>Pick a theme, add their name, and we'll weave them into their very own adventure. No credit card needed to read and print the first book.</p>
            <p><a href="{ctaUrl}">Create the free storybook</a></p>
            <p>With warmth,<br/>Adventrya Books</p>
            """;
        return SendEmailCoreAsync(toAddress, "Your child's first storybook is free — Adventrya Books", html, cancellationToken);
    }

    private async Task SendEmailCoreAsync(
        string toAddress,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken,
        string? replyTo = null)
    {
        if (!_options.Enabled)
        {
            logger.LogWarning("Email disabled — skipped message to {To}: {Subject}", toAddress, subject);
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.SmtpPassword))
        {
            logger.LogWarning("Email:SmtpPassword is empty — skipped message to {To}: {Subject}", toAddress, subject);
            throw new InvalidOperationException("Email is not configured yet. Please try again later.");
        }

        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        message.To.Add(toAddress);
        if (!string.IsNullOrWhiteSpace(replyTo))
        {
            message.ReplyToList.Add(new MailAddress(replyTo.Trim()));
        }

        using var client = new SmtpClient(_options.SmtpHost, _options.SmtpPort)
        {
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            EnableSsl = true,
            Credentials = new NetworkCredential(_options.SmtpUser, _options.SmtpPassword)
        };

        try
        {
            await client.SendMailAsync(message, cancellationToken);
            logger.LogInformation("Email sent to {To}: {Subject}", toAddress, subject);
        }
        catch (SmtpException ex) when (ex.Message.Contains("Authentication Required", StringComparison.OrdinalIgnoreCase)
                                       || ex.Message.Contains("not authenticated", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError(
                ex,
                "Gmail SMTP authentication failed for {User}. Use a Google App Password (not your normal Gmail password) in Email:SmtpPassword. " +
                "Create one at: Google Account → Security → 2-Step Verification → App passwords.",
                _options.SmtpUser);
            throw new InvalidOperationException(
                "Could not send email: Gmail requires an App Password. Update Email:SmtpPassword in appsettings.Production.json.",
                ex);
        }
    }
}
