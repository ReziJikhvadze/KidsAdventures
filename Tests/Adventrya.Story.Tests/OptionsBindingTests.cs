using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Extensions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Adventrya.Story.Tests;

/// <summary>
/// Where a bad App Service setting is allowed to hurt.
///
/// Written after `Beki__PortraitGateEnabled` was set to something that is not a boolean. The
/// binder threw, as it should — but options bind on first resolution, so nothing threw at
/// deployment. The site came up, the deploy went green, and the failure surfaced later as a 500
/// on the parent who happened to upload a photo, naming a config key nobody was looking at.
///
/// These pin the corrected behaviour: the same bad value stops the host from starting.
/// </summary>
public class OptionsBindingTests
{
    [Theory]
    [InlineData("Beki:PortraitGateEnabled")]
    [InlineData("Stripe:Enabled")]
    [InlineData("Bog:Enabled")]
    public async Task A_flag_that_is_not_a_boolean_stops_the_host_starting(string key)
    {
        // "1" is the value a person reaches for when a portal asks them to switch something on,
        // and the one .NET will not take.
        using var host = BuildHost(new Dictionary<string, string?> { [key] = "1" });

        var exception = await Record.ExceptionAsync(() => host.StartAsync());

        Assert.NotNull(exception);

        // The message has to name the setting, or a green deploy is replaced by a red one that
        // says nothing more useful than "it broke".
        Assert.Contains(key, FlattenMessages(exception));
    }

    [Fact]
    public async Task An_empty_flag_stops_the_host_starting_too()
    {
        // The Azure portal has two saves. Miss the second and the name is stored with no value,
        // which reaches the binder as an empty string and fails exactly like a typo.
        using var host = BuildHost(new Dictionary<string, string?> { ["Beki:PortraitGateEnabled"] = "" });

        Assert.NotNull(await Record.ExceptionAsync(() => host.StartAsync()));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("False")]
    public async Task A_well_formed_flag_starts_and_binds(string value)
    {
        using var host = BuildHost(new Dictionary<string, string?> { ["Beki:PortraitGateEnabled"] = value });

        await host.StartAsync();

        var beki = host.Services.GetRequiredService<IOptions<BekiOptions>>().Value;
        Assert.Equal(bool.Parse(value), beki.PortraitGateEnabled);

        await host.StopAsync();
    }

    /// <summary>
    /// Only the options registration, not the whole application: this is about configuration
    /// binding, and a host that also wanted a database would fail for reasons of its own.
    /// </summary>
    private static IHost BuildHost(Dictionary<string, string?> settings)
    {
        return new HostBuilder()
            .ConfigureAppConfiguration(builder => builder.AddInMemoryCollection(settings))
            .ConfigureServices((context, services) =>
                services.AddAdventurePacksOptions(context.Configuration))
            .Build();
    }

    private static string FlattenMessages(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }
}
