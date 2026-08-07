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
    [InlineData(ThemeType.Dinosaurs, "დაკარგული ხეობა")]
    [InlineData(ThemeType.Space, "ვარსკვლავების გზა")]
    [InlineData(ThemeType.Pirates, "მბრწყინავი კუნძული")]
    [InlineData(ThemeType.Animals, "მოჯადოებული ტყე")]
    [InlineData(ThemeType.Airplanes, "ღრუბლების ქალაქი")]
    [InlineData(ThemeType.Magic, "სინათლის ქალაქი")]
    public void Each_theme_names_its_world(ThemeType theme, string place) =>
        Assert.Equal(place, StoryWorlds.For(theme).Place);

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
