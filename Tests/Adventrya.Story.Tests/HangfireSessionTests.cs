using AdventurePacks.Api.Infrastructure;

namespace Adventrya.Story.Tests;

/// <summary>
/// The ticket that lets a browser tab open the job dashboard.
///
/// It is the one credential in this system that is not a JWT, and it exists because a navigation
/// carries no Authorization header. That makes it worth pinning precisely: a dashboard that can be
/// opened with a forged or expired cookie is a dashboard where anybody can requeue and delete other
/// people's jobs.
///
/// Three properties, and they are the whole contract. What we signed verifies. What somebody else
/// wrote does not. What has run out does not, however well-formed it is.
/// </summary>
public class HangfireSessionTests
{
    private const string Key = "a-jwt-signing-key-that-is-at-least-32-characters-long";

    private static readonly Guid Admin = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static readonly DateTimeOffset Now = new(2026, 9, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_ticket_this_server_issued_verifies_and_names_its_admin()
    {
        var cookie = HangfireSessionCookie.Issue(Admin, Now.AddHours(1), Key);

        Assert.True(HangfireSessionCookie.TryVerify(cookie, Key, Now, out var userId));
        Assert.Equal(Admin, userId);
    }

    [Fact]
    public void An_expired_ticket_does_not_verify_however_well_formed_it_is()
    {
        // The expiry is inside the signed payload, so this cookie's signature is perfectly good.
        // That is the point: a ticket cannot be extended by editing it, only re-issued.
        var cookie = HangfireSessionCookie.Issue(Admin, Now.AddHours(1), Key);

        Assert.False(HangfireSessionCookie.TryVerify(
            cookie, Key, Now.AddHours(1).AddSeconds(1), out var userId));
        Assert.Equal(Guid.Empty, userId);
    }

    [Fact]
    public void A_ticket_that_expires_exactly_now_is_already_over()
    {
        var expiresAt = Now.AddHours(1);
        var cookie = HangfireSessionCookie.Issue(Admin, expiresAt, Key);

        Assert.False(HangfireSessionCookie.TryVerify(cookie, Key, expiresAt, out _));
    }

    [Theory]
    // A different admin's id, a later expiry, a signature from somewhere else, a truncated one, and
    // the two shapes that are not a ticket at all.
    [InlineData("66666666-6666-6666-6666-666666666666|1788000000|{sig}")]
    [InlineData("{id}|1999999999|{sig}")]
    [InlineData("{id}|1788000000|0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("{id}|1788000000|{short}")]
    [InlineData("{id}|1788000000")]
    [InlineData("not-a-ticket")]
    [InlineData("")]
    public void A_tampered_or_malformed_ticket_does_not_verify(string template)
    {
        var issued = HangfireSessionCookie.Issue(Admin, Now.AddHours(1), Key);
        var signature = issued.Split('|')[2];

        var cookie = template
            .Replace("{id}", Admin.ToString("D"))
            .Replace("{short}", signature[..16])
            .Replace("{sig}", signature);

        Assert.False(HangfireSessionCookie.TryVerify(cookie, Key, Now, out var userId));
        Assert.Equal(Guid.Empty, userId);
    }

    [Fact]
    public void A_ticket_signed_with_another_key_does_not_verify()
    {
        // Which is what makes rotating the JWT secret also revoke every open dashboard session.
        var cookie = HangfireSessionCookie.Issue(Admin, Now.AddHours(1), "a-completely-different-key");

        Assert.False(HangfireSessionCookie.TryVerify(cookie, Key, Now, out _));
    }

    [Fact]
    public void No_key_means_no_admission()
    {
        // A deployment with an empty secret must refuse everybody rather than accept a signature
        // that anybody could have computed.
        var cookie = HangfireSessionCookie.Issue(Admin, Now.AddHours(1), Key);

        Assert.False(HangfireSessionCookie.TryVerify(cookie, null, Now, out _));
        Assert.False(HangfireSessionCookie.TryVerify(cookie, "   ", Now, out _));
    }

    [Fact]
    public void The_ticket_lasts_an_hour_and_is_scoped_to_the_dashboard()
    {
        // Long enough to watch a book through a retry; short enough that a laptop left open in a
        // cafe is not a permanent door into the queue. Scoped so it is not attached to every other
        // call the console makes.
        Assert.Equal(TimeSpan.FromHours(1), HangfireSessionCookie.Lifetime);
        Assert.Equal("/hangfire", HangfireSessionCookie.CookiePath);
        Assert.Equal("beki_hangfire", HangfireSessionCookie.Name);
    }
}
