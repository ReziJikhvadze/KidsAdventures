namespace AdventurePacks.Api.Configuration.Options;

public sealed class CorsOptions
{
    public const string SectionName = "Cors";
    public string[] AllowedOrigins { get; set; } = [];
    /// <summary>
    /// When true, merges common local dev URLs (localhost:8080, :5173, :3000) with AllowedOrigins.
    /// Set false on Azure if you want production-only origins (optional — localhost entries are harmless).
    /// </summary>
    public bool AllowLocalhostFallback { get; set; }
}
