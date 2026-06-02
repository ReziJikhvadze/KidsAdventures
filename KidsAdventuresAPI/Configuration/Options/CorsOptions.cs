namespace AdventurePacks.Api.Configuration.Options;

public sealed class CorsOptions
{
    public const string SectionName = "Cors";
    public string[] AllowedOrigins { get; set; } = [];
    /// <summary>
    /// When true and AllowedOrigins is empty, permits common local Vite dev URLs.
    /// Set false in appsettings.Production.json for Azure deployments.
    /// </summary>
    public bool AllowLocalhostFallback { get; set; }
}
