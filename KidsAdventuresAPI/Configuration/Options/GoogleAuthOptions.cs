namespace AdventurePacks.Api.Configuration.Options;

public sealed class GoogleAuthOptions
{
    public const string SectionName = "GoogleAuth";

    public bool Enabled { get; set; }

    public string ClientId { get; set; } = string.Empty;
}
