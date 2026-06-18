namespace AdventurePacks.Api.Configuration.Options;

public sealed class RecaptchaOptions
{
    public const string SectionName = "Recaptcha";

    /// <summary>When false, registration does not require or verify a reCAPTCHA token.</summary>
    public bool Enabled { get; set; }

    /// <summary>Public site key handed to the browser widget.</summary>
    public string SiteKey { get; set; } = string.Empty;

    /// <summary>Server secret used to verify tokens against Google.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Minimum score (reCAPTCHA v3) required to pass. Ignored for v2 checkbox.</summary>
    public double MinimumScore { get; set; } = 0.5;
}
