namespace AdventurePacks.Api.Configuration.Options;

public sealed class EmailOptions
{
    public const string SectionName = "Email";
    public bool Enabled { get; set; } = true;
    public string FromAddress { get; set; } = "rezijikhvadze@gmail.com";
    public string FromName { get; set; } = "Adventrya Books";
    public string SmtpHost { get; set; } = "smtp.gmail.com";
    public int SmtpPort { get; set; } = 587;
    public string SmtpUser { get; set; } = "rezijikhvadze@gmail.com";
    public string SmtpPassword { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "http://localhost:8080";
    /// <summary>API origin used in email links (confirm-email hits the API, then redirects to BaseUrl).</summary>
    public string ApiBaseUrl { get; set; } = "http://localhost:5000";

    /// <summary>Inbox for contact form submissions. Defaults to <see cref="FromAddress"/> when empty.</summary>
    public string ContactToAddress { get; set; } = string.Empty;
}
