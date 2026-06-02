namespace AdventurePacks.Api.Configuration.Options;

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>
    /// When true, creates demo users on startup if they do not exist (idempotent).
    /// Set to true once in Azure App Settings, then turn off after first deploy.
    /// </summary>
    public bool Enabled { get; set; }

    public string DemoEmail { get; set; } = "demo@adventurepacks.com";
    public string DemoPassword { get; set; } = "Adventure123!";
    public bool CreatePremiumDemoUser { get; set; } = true;
    public string PremiumDemoEmail { get; set; } = "premium@adventurepacks.com";
}
