using System.Net;
using AdventurePacks.Api.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace Adventrya.Story.Tests;

/// <summary>
/// Which entry of <c>X-Forwarded-For</c> is believed.
///
/// This is the whole of the rate limit on the anonymous endpoints: get it wrong and a caller
/// picks their own limiter key, which on an endpoint that pays a model per call is the same as
/// having no limit. The failure is silent — every request succeeds, and the only sign is the
/// bill — so the rule is asserted here rather than trusted to a reading of the header spec.
/// </summary>
public class ClientIpAddressTests
{
    private static HttpContext Request(string? forwardedFor, string? remoteIp = "10.0.0.1")
    {
        var context = new DefaultHttpContext();
        if (forwardedFor is not null) context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        if (remoteIp is not null) context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        return context;
    }

    [Fact]
    public void The_hop_nearest_us_is_the_client_behind_one_proxy()
    {
        Assert.Equal("203.0.113.5", ClientIpAddress.Resolve(Request("203.0.113.5"), 1));
    }

    /// <summary>The attack: prepending entries must not change the key the limiter sees.</summary>
    [Theory]
    [InlineData("1.1.1.1, 203.0.113.5")]
    [InlineData("9.9.9.9, 8.8.8.8, 203.0.113.5")]
    [InlineData("not-an-ip, 203.0.113.5")]
    public void A_caller_cannot_move_its_own_key_by_prepending_entries(string forwardedFor)
    {
        Assert.Equal("203.0.113.5", ClientIpAddress.Resolve(Request(forwardedFor), 1));
    }

    [Fact]
    public void A_second_hop_is_counted_from_the_right_as_well()
    {
        // CDN in front: the CDN's own address is last, the client it saw sits before it.
        Assert.Equal("203.0.113.5", ClientIpAddress.Resolve(Request("1.1.1.1, 203.0.113.5, 198.51.100.9"), 2));
    }

    /// <summary>App Service writes the port on; the same visitor must not get a key per connection.</summary>
    [Theory]
    [InlineData("203.0.113.5:44321", "203.0.113.5")]
    [InlineData("[2001:db8::1]:44321", "2001:db8::1")]
    [InlineData("::ffff:203.0.113.5", "203.0.113.5")]
    public void A_port_or_a_mapped_form_does_not_make_a_new_key(string entry, string expected)
    {
        Assert.Equal(expected, ClientIpAddress.Resolve(Request(entry), 1));
    }

    /// <summary>
    /// Fewer entries than the configured chain means the request did not come through it, so
    /// nothing in the header is evidence of anything and the connection is all there is.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("203.0.113.5")]
    public void An_unexpected_chain_falls_back_to_the_connection(string? forwardedFor)
    {
        Assert.Equal("10.0.0.1", ClientIpAddress.Resolve(Request(forwardedFor), 2));
    }

    [Fact]
    public void A_request_we_cannot_place_at_all_still_has_a_key()
    {
        Assert.Equal("unknown", ClientIpAddress.Resolve(Request(null, remoteIp: null), 1));
    }
}
