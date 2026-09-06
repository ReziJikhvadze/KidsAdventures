using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// The image-frame validator is wired into startup, not only unit-tested in isolation: a
/// deployment that asks the configured model for a frame it cannot draw must refuse to come up.
/// </summary>
public class BekiImageRequestStartupValidationTests
{
    private static IOptions<BekiOptions> Resolve(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

        return new ServiceCollection()
            .AddAdventurePacksOptions(configuration)
            .BuildServiceProvider()
            .GetRequiredService<IOptions<BekiOptions>>();
    }

    [Fact]
    public void The_shipped_frames_pass_startup_validation()
    {
        var options = Resolve().Value;

        Assert.Equal("1536x1024", options.SpreadImageSize);
        Assert.Equal("1536x1024", options.CoverWrapImageSize);
        Assert.False(options.AllowExperimentalImageSizes);
    }

    [Fact]
    public void An_experimental_frame_refuses_startup_unless_opted_in()
    {
        var refused = Assert.Throws<OptionsValidationException>(
            () => Resolve(("Beki:SpreadImageSize", "3840x2160")).Value);

        Assert.Contains("Beki:SpreadImageSize", refused.Message, StringComparison.Ordinal);

        var accepted = Resolve(
            ("Beki:SpreadImageSize", "3840x2160"),
            ("Beki:AllowExperimentalImageSizes", "true")).Value;

        Assert.Equal("3840x2160", accepted.SpreadImageSize);
    }

    [Fact]
    public void A_portrait_frame_refuses_startup()
    {
        var refused = Assert.Throws<OptionsValidationException>(
            () => Resolve(("Beki:CoverWrapImageSize", "1024x1536")).Value);

        Assert.Contains("Beki:CoverWrapImageSize", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The accepted string is sent to the provider verbatim, so a spelling the API would reject must
    /// be refused at startup rather than waved through by a lenient parser (sol review, 2026-09-06).
    /// </summary>
    [Theory]
    [InlineData("1536 x 1024 ")]
    [InlineData("01536x1024")]
    [InlineData("1536X1024")]
    [InlineData("auto")]
    public void A_non_canonical_frame_spelling_refuses_startup(string value)
    {
        var refused = Assert.Throws<OptionsValidationException>(
            () => Resolve(("Beki:SpreadImageSize", value)).Value);

        Assert.Contains("Beki:SpreadImageSize", refused.Message, StringComparison.Ordinal);
        Assert.False(BekiImageRequestValidation.TryParse(value, out _, out _));
    }
}
