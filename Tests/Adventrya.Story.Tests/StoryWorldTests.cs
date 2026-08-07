using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Services.Story;

namespace Adventrya.Story.Tests;

/// <summary>
/// Pins each world card to the place the book is set in.
///
/// These names drifted once already — the site offered one island and the prompt described
/// another — and the failure is invisible: the book is fine, it is simply about somewhere the
/// parent did not choose. The names must also match wwwroot/src/lib/i18n/ka/worlds.ts.
/// </summary>
public class StoryWorldTests
{
    [Theory]
    [InlineData(ThemeType.Dinosaurs, "დინოზავრები", "დაკარგული ხეობა")]
    [InlineData(ThemeType.Space, "კოსმოსი", "ვარსკვლავების გზა")]
    [InlineData(ThemeType.Pirates, "მეკობრეები", "მბრწყინავი კუნძული")]
    [InlineData(ThemeType.Animals, "ცხოველები", "მოჯადოებული ტყე")]
    [InlineData(ThemeType.Airplanes, "თვითმფრინავები", "ღრუბლების ქალაქი")]
    [InlineData(ThemeType.Magic, "მაგიური სამყარო", "სინათლის ქალაქი")]
    public void Each_theme_names_its_world(ThemeType theme, string subject, string place)
    {
        var world = StoryWorlds.For(theme);

        Assert.Equal(place, world.Place);

        // The subject is what the card says the book is about. Without it a valley is only a
        // valley, and the dinosaurs the parent chose never arrive.
        Assert.Equal(subject, world.Subject);
    }

    /// <summary>
    /// A seventh theme would fall to the generic "თავგადასავალი" and nobody would notice, because
    /// a book set in an adventure still reads like a book.
    /// </summary>
    [Fact]
    public void Every_theme_has_a_world_of_its_own()
    {
        var places = Enum.GetValues<ThemeType>()
            .Select(t => StoryWorlds.For(t).Place)
            .ToList();

        Assert.Equal(places.Count, places.Distinct().Count());
        Assert.DoesNotContain("თავგადასავალი", places);
    }
}
