using AdventurePacks.Api.Domain;
using AdventurePacks.Api.Domain.Enums;

namespace Adventrya.Story.Tests;

/// <summary>
/// What a parcel costs and how long it takes, by where it is going.
///
/// Three prices, and the one that matters most is the zero: five working days inside Tbilisi is
/// free, and free is the option a Tbilisi address gets by default. That is a promise on the
/// checkout screen, and it had no test at all - the rule lived in one file, was mirrored by hand
/// in the frontend's own copy, and nothing anywhere asserted that either of them said 0.
/// </summary>
public sealed class GeorgianDeliveryTests
{
    [Fact]
    public void Tbilisi_gets_both_windows_and_the_free_one_by_default()
    {
        var options = GeorgianDelivery.OptionsFor("თბილისი");

        Assert.Equal(
            [DeliveryOption.TbilisiStandard, DeliveryOption.TbilisiExpress],
            options);

        // Free first, and first is what an address that has not chosen yet is quoted.
        var fallback = GeorgianDelivery.Resolve("თბილისი", null, (DeliveryOption?)null);
        Assert.Equal(DeliveryOption.TbilisiStandard, fallback.Option);
        Assert.Equal(0, fallback.PriceMinor);
        Assert.Equal(5, fallback.MinDays);
    }

    [Fact]
    public void The_faster_Tbilisi_window_is_seven_lari_and_three_days()
    {
        var express = GeorgianDelivery.Resolve(
            "თბილისი", null, DeliveryOption.TbilisiExpress);

        Assert.Equal(DeliveryOption.TbilisiExpress, express.Option);
        Assert.Equal(700, express.PriceMinor);
        Assert.Equal(3, express.MinDays);
        Assert.Equal(3, express.MaxDays);
    }

    [Fact]
    public void Everywhere_else_is_eight_lari_in_five_to_seven_days()
    {
        foreach (var address in new[] { "ბათუმი", "ქუთაისი", "Rustavi", "თელავი, ჭავჭავაძის 12" })
        {
            var choice = GeorgianDelivery.Resolve(address, null, (DeliveryOption?)null);

            Assert.Equal(DeliveryOption.Regional, choice.Option);
            Assert.Equal(800, choice.PriceMinor);
            Assert.Equal(5, choice.MinDays);
            Assert.Equal(7, choice.MaxDays);
        }

        Assert.Equal([DeliveryOption.Regional], GeorgianDelivery.OptionsFor("ბათუმი"));
    }

    [Fact]
    public void An_address_nobody_has_typed_yet_is_quoted_as_a_region()
    {
        /*
          The safe direction to guess in, and the reason the checkout shows 8 GEL before an
          address exists: recognising Tbilisi later takes the total *down*, where guessing free
          and correcting upwards would raise a figure the parent had already accepted.
        */
        foreach (var nothing in new string?[] { null, "", "   " })
        {
            var choice = GeorgianDelivery.Resolve(nothing, nothing, (DeliveryOption?)null);
            Assert.Equal(DeliveryOption.Regional, choice.Option);
            Assert.Equal(800, choice.PriceMinor);
        }
    }

    [Fact]
    public void Tbilisi_is_recognised_however_the_address_spells_it()
    {
        // What a parent actually types: the city on its own, the city inside a whole address, the
        // Latin spellings, and any casing of them.
        foreach (var address in new[]
                 {
                     "თბილისი",
                     "თბილისი, გროზნოს 11ა",
                     "გროზნოს 11ა, თბილისი, 0160",
                     "Tbilisi",
                     "TBILISI, Chavchavadze 37",
                     "tiflis",
                 })
        {
            Assert.Equal(
                DeliveryOption.TbilisiStandard,
                GeorgianDelivery.DefaultFor(address));
        }
    }

    [Fact]
    public void An_option_the_address_cannot_have_is_priced_as_the_one_it_can()
    {
        /*
          Not refused. A client asking for free Tbilisi delivery to Batumi is either stale or
          wrong, and failing the checkout on a mismatch the parent can neither see nor fix is the
          worse answer: the order is priced for the delivery that address really gets, and the
          quote says which one that was.
        */
        var toBatumi = GeorgianDelivery.Resolve(
            "ბათუმი", null, DeliveryOption.TbilisiStandard);

        Assert.Equal(DeliveryOption.Regional, toBatumi.Option);
        Assert.Equal(800, toBatumi.PriceMinor);

        // And the other way: a regional option asked for inside Tbilisi becomes the free one.
        var inTbilisi = GeorgianDelivery.Resolve(
            "თბილისი", null, DeliveryOption.Regional);

        Assert.Equal(DeliveryOption.TbilisiStandard, inTbilisi.Option);
        Assert.Equal(0, inTbilisi.PriceMinor);
    }

    [Fact]
    public void The_whole_address_line_decides_the_zone_when_the_city_field_is_empty()
    {
        /*
          The checkout sends the address line as the city - one field, because a parent typing
          their own address does not fill in a separate box for the city they just named. The
          resolver is asked with both, so the zone is found in whichever one carries it.
        */
        var fromLine = GeorgianDelivery.Resolve(
            null, "თბილისი, გროზნოს 11ა", (DeliveryOption?)null);

        Assert.Equal(DeliveryOption.TbilisiStandard, fromLine.Option);
        Assert.Equal(0, fromLine.PriceMinor);
    }

    [Fact]
    public void Only_a_posted_parcel_is_charged_for_delivery()
    {
        var regional = GeorgianDelivery.Resolve("ბათუმი", null, (DeliveryOption?)null);

        Assert.Equal(800, GelPricing.DeliveryFor(OrderPackage.Print, regional));
        // A file is not posted anywhere, whatever the address on the order happens to say.
        Assert.Equal(0, GelPricing.DeliveryFor(OrderPackage.Digital, regional));
    }

    [Fact]
    public void Free_delivery_reaches_the_total_as_nothing()
    {
        /*
          The one that would be worth catching. Every other price here is a number that would look
          wrong on screen if it were mishandled; zero is the one that can be quietly replaced by a
          default and still look like a price.
        */
        var free = GeorgianDelivery.Resolve("თბილისი", null, (DeliveryOption?)null);

        Assert.Equal(0, free.PriceMinor);
        Assert.Equal(0, GelPricing.DeliveryFor(OrderPackage.Print, free));
    }
}
