namespace AdventurePacks.Api.Configuration.Options;

public sealed class FrontendHostingOptions
{
    public const string SectionName = "Frontend";

    /// <summary>When true, starts the Nitro Node SSR server and proxies non-API traffic to it.</summary>
    public bool EnableHostedNode { get; set; }

    public int NodePort { get; set; } = 3099;

    /// <summary>Folder under ContentRoot with Nitro node-server build (server/index.mjs).</summary>
    public string OutputRelativePath { get; set; } = "wwwroot/azure-ssr";
}
