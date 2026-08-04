using AdventurePacks.Api.Domain.Enums;

namespace AdventurePacks.Api.Services;

/// <summary>
/// What each Adventrya world actually is.
///
/// The prompt used to pass the theme as a bare enum — "Theme: Space" — so the model
/// invented its own space every time. Meanwhile the product already has six named worlds
/// with places, landmarks and a returning companion, all of which lived only in the
/// frontend copy and never reached the writer. A parent who picks the star path expects
/// the star path, not a generic outer-space story.
///
/// This is the canon, server-side, in the book's language. It is deliberately a sketch
/// rather than a script: enough for the story to belong to the world, not so much that
/// every book in it comes out the same. Recurring elements are named so a series set in
/// one world stays recognisably the same place across books.
/// </summary>
internal static class AdventureWorldCanon
{
    internal sealed class WorldFacts
    {
        public required string Place { get; init; }
        public required string Landmarks { get; init; }
        public required string Atmosphere { get; init; }

        /// <summary>The companion this world is known for. Empty when it has none.</summary>
        public string Companion { get; init; } = string.Empty;
    }

    private static readonly Dictionary<ThemeType, WorldFacts> Georgian = new()
    {
        [ThemeType.Dinosaurs] = new WorldFacts
        {
            Place = "დაკარგული ხეობა — მწვანე, თბილი ხეობა, სადაც დინოზავრები მშვიდად ცხოვრობენ",
            Landmarks = "გიგანტური გვიმრები, თბილი ტალახის გუბეები, ქვის ბილიკები კლდეებში, უძველესი კვალი მიწაზე, ჩანჩქერი ხეობის ბოლოს",
            Atmosphere = "მზიანი, ცოცხალი და უსაფრთხო; დინოზავრები მეგობრული და ცნობისმოყვარეა, არასოდეს საშიში",
            Companion = "რექსი — პატარა, მეგობრული დინოზავრი, რომელიც გმირს ხეობაში ხვდება",
        },
        [ThemeType.Space] = new WorldFacts
        {
            Place = "ვარსკვლავების გზა — მანათობელი ბილიკი, რომელიც მთვარის იქით მიდის",
            Landmarks = "ვარსკვლავური რუკები, მცურავი ქვის კუნძულები, ობსერვატორია მინის გუმბათით, კომეტების ბილიკი, მშვიდი პლანეტები რბილი ფერებით",
            Atmosphere = "მშვიდი, საოცრებით სავსე და უჰაერო სიცივის გარეშე; კოსმოსი აქ თბილი და მისასალმებელია",
            Companion = "რექსი, რომელიც გმირს ამ მოგზაურობაშიც აჰყვება",
        },
        [ThemeType.Pirates] = new WorldFacts
        {
            Place = "მბრწყინავი კუნძული — ზღვაში დამალული კუნძული, რომელსაც ძველი ოქროსფერი რუკა მიუთითებს",
            Landmarks = "ოქროსფერი რუკა, ქვიშის სანაპირო ნიჟარებით, გამოქვაბულები ზღვის მხრიდან, ძველი ხის ხომალდი, შუქურა კლდეზე",
            Atmosphere = "სათავგადასავლო და მხიარული; მეკობრეები კეთილები არიან და საგანძური ყოველთვის რაღაც სასიკეთოა",
            Companion = "კეთილი მეკობრე, რომელიც რუკის კითხვას ასწავლის",
        },
        [ThemeType.Animals] = new WorldFacts
        {
            Place = "მოჯადოებული ტყე — მანათობელი ტყე, სადაც ყველა ბინადარს თავისი პატარა საიდუმლო აქვს",
            Landmarks = "მანათობელი სოკოები, ხის ხიდები ტოტებს შორის, ჩუმი ტბა, ციცინათელების მინდორი, ძველი მოლაპარაკე ხე",
            Atmosphere = "თბილი, ჩურჩულით სავსე და ნაზი; ცხოველები საუბრობენ და მეგობრობა მოულოდნელად იბადება",
            Companion = "ტყის ცხოველები, რომლებიც თანდათან ენდობიან გმირს",
        },
        [ThemeType.Airplanes] = new WorldFacts
        {
            Place = "ღრუბლების ქალაქი — ღრუბლებს მიღმა დამალული ქალაქი",
            Landmarks = "ღრუბლების ბაქნები, ქარის წისქვილები, პატარა თვითმფრინავები ფერადი ფრთებით, ხიდები ღრუბლებს შორის, სიმაღლის შუქურა",
            Atmosphere = "ღია, მსუბუქი და თავისუფალი; სიმაღლე აქ სასიხარულოა და არა საშიში",
            Companion = "პატარა თვითმფრინავი, რომელსაც საკუთარი ხასიათი აქვს",
        },
        [ThemeType.Magic] = new WorldFacts
        {
            Place = "სინათლის ქალაქი — მოჯადოებული ქალაქი, რომლის კარიბჭეც მხოლოდ კეთილ სურვილზე იღება",
            Landmarks = "სინათლის კარიბჭე, ცოცხალი წიგნები, ფარნების ქუჩა, სარკის მოედანი, კოშკი, რომელიც სახელს იმახსოვრებს",
            Atmosphere = "ჯადოსნური, თბილი და ოდნავ საიდუმლოებით მოცული; პატარა არჩევანიც კი სინათლეს ტოვებს",
            Companion = "ქალაქის ფარნების მცველი, რომელიც გამოცანებით ელაპარაკება",
        },
    };

    private static readonly Dictionary<ThemeType, WorldFacts> English = new()
    {
        [ThemeType.Dinosaurs] = new WorldFacts
        {
            Place = "the Lost Valley — a warm green valley where dinosaurs live peacefully",
            Landmarks = "giant ferns, warm mud pools, stone paths in the cliffs, ancient tracks in the earth, a waterfall at the valley's end",
            Atmosphere = "sunlit, alive and safe; the dinosaurs are friendly and curious, never threatening",
            Companion = "Rex, a small friendly dinosaur who meets the hero in the valley",
        },
        [ThemeType.Space] = new WorldFacts
        {
            Place = "the Star Path — a glowing trail that leads out past the moon",
            Landmarks = "star maps, floating stone islands, an observatory under a glass dome, a comet trail, calm planets in soft colours",
            Atmosphere = "calm and full of wonder, without airless cold; space here is warm and welcoming",
            Companion = "Rex, who comes along on this journey too",
        },
        [ThemeType.Pirates] = new WorldFacts
        {
            Place = "the Shining Island — an island hidden at sea, marked on an old golden map",
            Landmarks = "the golden map, a shell-strewn shore, sea caves, an old wooden ship, a lighthouse on the rocks",
            Atmosphere = "adventurous and cheerful; the pirates are kind and the treasure is always something good",
            Companion = "a kind pirate who teaches the hero to read the map",
        },
        [ThemeType.Animals] = new WorldFacts
        {
            Place = "the Enchanted Forest — a glowing forest where every resident keeps a small secret",
            Landmarks = "glowing mushrooms, rope bridges between branches, a quiet lake, a firefly meadow, an old talking tree",
            Atmosphere = "warm, whispering and gentle; the animals speak and friendship arrives unexpectedly",
            Companion = "the forest animals, who come to trust the hero",
        },
        [ThemeType.Airplanes] = new WorldFacts
        {
            Place = "the Cloud City — a city hidden above the clouds",
            Landmarks = "cloud platforms, windmills, small aeroplanes with colourful wings, bridges between clouds, a beacon at altitude",
            Atmosphere = "open, light and free; height here is a joy rather than a danger",
            Companion = "a little aeroplane with a character of its own",
        },
        [ThemeType.Magic] = new WorldFacts
        {
            Place = "the City of Light — an enchanted city whose gate opens only to a kind wish",
            Landmarks = "the gate of light, living books, a street of lanterns, a mirror square, a tower that remembers names",
            Atmosphere = "magical, warm and lightly mysterious; even a small choice leaves light behind",
            Companion = "the keeper of the city's lanterns, who speaks in riddles",
        },
    };

    /// <summary>
    /// The world's facts in the book's language, or null when the theme has no canon —
    /// in which case the prompt simply falls back to naming the theme, as before.
    /// </summary>
    public static WorldFacts? For(ThemeType theme, string languageCode)
    {
        var table = AdventurePromptTexts.NormalizeLanguageCode(languageCode) == "en" ? English : Georgian;
        return table.TryGetValue(theme, out var facts) ? facts : null;
    }
}
