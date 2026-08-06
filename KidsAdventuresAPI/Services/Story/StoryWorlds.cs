using AdventurePacks.Api.Domain.Enums;

namespace AdventurePacks.Api.Services.Story;

/// <summary>
/// The six worlds, as the parent was shown them.
///
/// The names here must match what the site puts on the world card, because the parent chooses
/// from that card and the book has to be about the place they picked. They had drifted: the site
/// offered „მბრწყინავი კუნძულის საიდუმლო“ and the prompt described warm seas and maps, and
/// nothing compared the two. This is a milder version of the fault that once produced a dinosaur
/// book for somebody who chose the star path.
///
/// The names live here rather than arriving from the browser because the preview is anonymous:
/// anything a client can put into a prompt, anyone can put into a prompt.
///
/// **If you rename a world, change it in both places.** The site copy is in
/// wwwroot/src/lib/i18n/{ka,en}/worlds.ts.
///
/// The sensory lines are not decoration. The prompt asks for something heard, touched or smelled
/// in most scenes, and a model given only "a forest" will write one that is only looked at.
/// </summary>
public static class StoryWorlds
{
    public sealed record World
    {
        /// <summary>The place, named as the site names it.</summary>
        public required string Place { get; init; }

        /// <summary>What it is made of — the raw material for sound, texture and smell.</summary>
        public required string Environment { get; init; }
    }

    public static World For(ThemeType theme) => theme switch
    {
        ThemeType.Dinosaurs => new World
        {
            Place = "დაკარგული ხეობა",
            Environment =
                "ხშირი უძველესი ხეობა: გიგანტური გვიმრები, თბილი ნისლი დილით, მშვიდი "
                + "მცენარეჭამია დინოზავრები, სველ ქვიშაში დარჩენილი ნაკვალევი, მოციმციმე "
                + "ნაკადულები და გლუვი ქვები ბილიკზე."
        },

        ThemeType.Space => new World
        {
            Place = "ვარსკვლავების გზა",
            Environment =
                "რბილად მბზინავი ნისლეულები, მეგობრული მოლივლივე ვარსკვლავის მტვერი, ჩუმი "
                + "ვარსკვლავური ბილიკები, მანათობელი თანავარსკვლავედები, პატარა წყნარი "
                + "რაკეტები და მთვარის ფხვნილი."
        },

        ThemeType.Pirates => new World
        {
            Place = "მბრწყინავი კუნძული",
            Environment =
                "ოქროსფერი ქვიშიანი სანაპირო, ნიჟარების კვალი, ნაზი ტალღები, პალმებით "
                + "დაფარული მიმალული გამოქვაბულები, მბზინავი კომპასები და მეგობრული ზღვის "
                + "ბინადრები."
        },

        ThemeType.Animals => new World
        {
            Place = "მოჯადოებული ტყე",
            Environment =
                "ხავსიანი ბილიკები, მჩურჩულე მუხები, ცვარი ფოთლებზე, ტყის რბილი ხმები, "
                + "მეგობრული ტყის ცხოველები და მანათობელი სოკოები."
        },

        ThemeType.Airplanes => new World
        {
            Place = "ღრუბლების ქალაქი",
            Environment =
                "ბამბისებრი ღრუბლები, თბილი ცისარტყელები, ნაზი ცის ნიავი, მეგობრული "
                + "ცაში მოლივლივე პლანერები და მოტივტივე ღრუბლის კუნძულები."
        },

        ThemeType.Magic => new World
        {
            Place = "სინათლის ქალაქი",
            Environment =
                "მბზინავი ბროლის ფარნები, მოციმციმე ქვაფენილი, ნაზი ჯადოსნური ბუშტები, "
                + "მეგობრული სინათლის ფერიები და თბილი თაღები."
        },

        _ => new World
        {
            Place = "თავგადასავალი",
            Environment = "ღია, მეგობრული ადგილი, სადაც ყოველ კუთხეს თავისი ხმა აქვს."
        }
    };
}
