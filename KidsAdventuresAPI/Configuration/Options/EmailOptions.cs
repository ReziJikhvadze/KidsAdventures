namespace AdventurePacks.Api.Configuration.Options;

public sealed class EmailOptions
{
    public const string SectionName = "Email";
    public bool Enabled { get; set; } = true;
    public string FromAddress { get; set; } = "rezijikhvadze@gmail.com";
    /// <summary>
    /// The name on the envelope, and the name the letters sign themselves with — the email
    /// templates read it from here rather than repeating it. Production overrides it, so this is
    /// only what an unconfigured deployment says.
    /// </summary>
    public string FromName { get; set; } = "Beki";
    public string SmtpHost { get; set; } = "smtp.gmail.com";
    public int SmtpPort { get; set; } = 587;
    public string SmtpUser { get; set; } = "rezijikhvadze@gmail.com";
    public string SmtpPassword { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "http://localhost:5173";
    /// <summary>API origin used in email links (confirm-email hits the API, then redirects to BaseUrl).</summary>
    public string ApiBaseUrl { get; set; } = "http://localhost:5000";

    /// <summary>Inbox for contact form submissions. Defaults to <see cref="FromAddress"/> when empty.</summary>
    public string ContactToAddress { get; set; } = string.Empty;

    /// <summary>
    /// Where operational alerts go — a new paid order, a book that failed to generate, a parcel
    /// to print. Separate from <see cref="ContactToAddress"/> because these are for whoever is
    /// on duty, and customer mail is for whoever answers customers; the same person today is not
    /// a reason to make it the same setting.
    ///
    /// Falls back to <see cref="ContactToAddress"/> and then <see cref="FromAddress"/>, so an
    /// installation that sets nothing still gets its alerts rather than dropping them.
    /// </summary>
    public string AdminNotificationAddress { get; set; } = string.Empty;
}
