namespace AdventurePacks.Api.Configuration.Options;

/// <summary>
/// How to work out who is calling, for the anonymous endpoints that meter by caller.
///
/// Behind App Service the connection address is Azure's load balancer, so every visitor
/// would share one bucket and the first few would spend everybody's allowance. The client
/// address has to come from <c>X-Forwarded-For</c> instead — and that header is written by
/// the caller as much as by the proxies, so the question is not whether to read it but
/// which entry in it can be believed.
///
/// Proxies append, so the trustworthy entry is counted from the right: the last one was
/// written by the hop nearest us, and anything a caller invented sits harmlessly to its
/// left. One hop is App Service on its own. Putting a CDN or WAF in front adds a hop, and
/// that is the only reason this is a setting rather than a constant.
/// </summary>
public sealed class ClientIpOptions
{
    public const string SectionName = "ClientIp";

    /// <summary>
    /// How many proxies append to <c>X-Forwarded-For</c> before a request reaches us.
    ///
    /// Raise it to match the chain if one is added in front — too low reads an entry the
    /// caller controls, too high reads a proxy's own address and collapses every visitor
    /// into one bucket.
    /// </summary>
    public int TrustedProxyHops { get; set; } = 1;
}
