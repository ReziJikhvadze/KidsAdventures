using AdventurePacks.Api.Domain.Enums;

namespace AdventurePacks.Api.Services.Story;

/// <summary>
/// Which skill a book teaches, decided by theme and age rather than by the model.
///
/// The master prompt is built on developmental psychology — Vygotsky's zone of proximal
/// development in particular — and that only works if the skill is genuinely just beyond what
/// the child can already do. Left to the model, the skill would be reinvented on every
/// generation: sometimes too easy, sometimes too abstract, never comparable between books, and
/// impossible to measure or to promise a parent.
///
/// Deciding it here makes it visible, stable and changeable. A child who reads four books gets
/// four different skills rather than the same one worded differently, and a parent can be told
/// what a book is for before they buy it.
/// </summary>
public static class SkillMatrix
{
    /// <summary>
    /// Age bands chosen for how children actually change, not for round numbers: 2-4 is
    /// pre-operational and concrete, 5-6 begins to reason about others, 7-8 can hold a plan,
    /// 9-10 can sit with an idea that is not about them.
    /// </summary>
    public enum AgeBand
    {
        Toddler,   // 2-4
        Early,     // 5-6
        Middle,    // 7-8
        Older      // 9-10
    }

    public static AgeBand BandFor(int age) => age switch
    {
        <= 4 => AgeBand.Toddler,
        <= 6 => AgeBand.Early,
        <= 8 => AgeBand.Middle,
        _ => AgeBand.Older
    };

    public sealed record Skill
    {
        /// <summary>What the child practises, in the book's language, for the prompt.</summary>
        public required string Georgian { get; init; }

        /// <summary>The same, in English, for logs and analytics.</summary>
        public required string English { get; init; }

        /// <summary>
        /// How the story should teach it — the behaviour to dramatise. Without this the model
        /// tends to state the lesson rather than show it, which is the one thing the prompt
        /// explicitly forbids.
        /// </summary>
        public required string GeorgianHowToShow { get; init; }
    }

    private static readonly Dictionary<(ThemeType, AgeBand), Skill> Matrix = new()
    {
        // --- Dinosaurs: size, courage, and being small among large things ------------------
        [(ThemeType.Dinosaurs, AgeBand.Toddler)] = new Skill
        {
            Georgian = "შიშის დასახელება და მისი დაძლევა პატარა ნაბიჯებით",
            English = "naming fear and taking one small step anyway",
            GeorgianHowToShow = "გმირი ხმამაღლა ამბობს რისი ეშინია და მაინც აკეთებს ერთ პატარა ნაბიჯს"
        },
        [(ThemeType.Dinosaurs, AgeBand.Early)] = new Skill
        {
            Georgian = "დაკვირვება და კვალის მიხედვით დასკვნის გამოტანა",
            English = "observing carefully and reasoning from evidence",
            GeorgianHowToShow = "გმირი პატარა ნიშნებს ამჩნევს და მათგან ასკვნის, სად უნდა წავიდეს"
        },
        [(ThemeType.Dinosaurs, AgeBand.Middle)] = new Skill
        {
            Georgian = "სხვისი პერსპექტივის გაგება — დიდიც შეიძლება შეშინებული იყოს",
            English = "perspective-taking — even something big can be frightened",
            GeorgianHowToShow = "გმირი ხვდება, რომ დიდი დინოზავრი მისივე მსგავსად შეშინებულია"
        },
        [(ThemeType.Dinosaurs, AgeBand.Older)] = new Skill
        {
            Georgian = "პასუხისმგებლობა საკუთარ შეცდომაზე და მისი გამოსწორება",
            English = "owning a mistake and repairing it",
            GeorgianHowToShow = "გმირის შეცდომა რაღაცას აფუჭებს და თვითონვე ასწორებს"
        },

        // --- Space: wonder, patience, and questions that have no quick answer --------------
        [(ThemeType.Space, AgeBand.Toddler)] = new Skill
        {
            Georgian = "ცნობისმოყვარეობა და კითხვის დასმა",
            English = "curiosity and asking questions",
            GeorgianHowToShow = "გმირი „რატომ?“ ეკითხება და პასუხი მოგზაურობას იწყებს"
        },
        [(ThemeType.Space, AgeBand.Early)] = new Skill
        {
            Georgian = "მოთმინება — ზოგი რამ დროს საჭიროებს",
            English = "patience — some things take time",
            GeorgianHowToShow = "გმირს უწევს ლოდინი და ლოდინის დროს რაღაცას ამჩნევს"
        },
        [(ThemeType.Space, AgeBand.Middle)] = new Skill
        {
            Georgian = "თანმიმდევრობით ფიქრი და გეგმის შედგენა",
            English = "sequencing and making a plan",
            GeorgianHowToShow = "გმირი ნაბიჯებად ყოფს დიდ ამოცანას და რიგრიგობით ასრულებს"
        },
        [(ThemeType.Space, AgeBand.Older)] = new Skill
        {
            Georgian = "იმის მიღება, რომ ყველა კითხვას პასუხი მაშინვე არ აქვს",
            English = "sitting with an unanswered question",
            GeorgianHowToShow = "გმირი ერთ საიდუმლოს ვერ ხსნის და ეს კარგია — სხვას ხსნის სამაგიეროდ"
        },

        // --- Pirates: sharing, fairness, and what treasure actually is --------------------
        [(ThemeType.Pirates, AgeBand.Toddler)] = new Skill
        {
            Georgian = "გაზიარება და რიგის დაცვა",
            English = "sharing and taking turns",
            GeorgianHowToShow = "გმირი საგანძურს ინაწილებს და ამით უკეთესი რამ ხდება"
        },
        [(ThemeType.Pirates, AgeBand.Early)] = new Skill
        {
            Georgian = "სამართლიანობა — რა არის თანაბარი განაწილება",
            English = "fairness — what an equal share means",
            GeorgianHowToShow = "გმირი ხედავს უსამართლობას და თვითონ ასწორებს"
        },
        [(ThemeType.Pirates, AgeBand.Middle)] = new Skill
        {
            Georgian = "პირობის შესრულება, მაშინაც კი, როცა ძნელია",
            English = "keeping a promise when it costs something",
            GeorgianHowToShow = "გმირს პირობის დარღვევა აჯობებდა, მაგრამ მაინც ასრულებს"
        },
        [(ThemeType.Pirates, AgeBand.Older)] = new Skill
        {
            Georgian = "ღირებულებების გარჩევა — ყველაზე ძვირფასი ყოველთვის ოქრო არაა",
            English = "telling worth from value",
            GeorgianHowToShow = "გმირი ოქროსა და მეგობრობას შორის ირჩევს და მიზეზსაც ხედავს"
        },

        // --- Animals: empathy, care, and listening ---------------------------------------
        [(ThemeType.Animals, AgeBand.Toddler)] = new Skill
        {
            Georgian = "სიკეთე და პატარაზე ზრუნვა",
            English = "kindness and caring for someone smaller",
            GeorgianHowToShow = "გმირი პატარა ცხოველს ეხმარება, თუმცა არავინ სთხოვს"
        },
        [(ThemeType.Animals, AgeBand.Early)] = new Skill
        {
            Georgian = "ემოციების ამოცნობა საკუთარ თავსა და სხვაში",
            English = "recognising emotions in yourself and others",
            GeorgianHowToShow = "გმირი ამჩნევს, რომ მეგობარი მოწყენილია და კითხულობს რატომ"
        },
        [(ThemeType.Animals, AgeBand.Middle)] = new Skill
        {
            Georgian = "მოსმენა — სანამ დაასკვნი, ჯერ მოისმინე",
            English = "listening before concluding",
            GeorgianHowToShow = "გმირი ჯერ არასწორად ხვდება, მერე მოისმენს და აზრს იცვლის"
        },
        [(ThemeType.Animals, AgeBand.Older)] = new Skill
        {
            Georgian = "განსხვავებულობის მიღება — ყველა ერთნაირად არ უნდა იყოს",
            English = "accepting difference",
            GeorgianHowToShow = "გმირი ხვდება, რომ განსხვავება სისუსტე კი არა, უპირატესობაა"
        },

        // --- Airplanes: independence, height, and trying again ----------------------------
        [(ThemeType.Airplanes, AgeBand.Toddler)] = new Skill
        {
            Georgian = "გამბედაობა ახლის ცდაში",
            English = "daring to try something new",
            GeorgianHowToShow = "გმირი პირველად ცდის და ეშინია, მაგრამ მაინც ცდის"
        },
        [(ThemeType.Airplanes, AgeBand.Early)] = new Skill
        {
            Georgian = "დამოუკიდებლობა — „მე თვითონ შევძლებ“",
            English = "independence — doing it myself",
            GeorgianHowToShow = "გმირი დახმარებაზე ამბობს უარს და თვითონ ახერხებს"
        },
        [(ThemeType.Airplanes, AgeBand.Middle)] = new Skill
        {
            Georgian = "წარუმატებლობის შემდეგ ხელახლა ცდა",
            English = "trying again after failing",
            GeorgianHowToShow = "გმირს პირველად არ გამოსდის და მეორედ სხვანაირად ცდილობს"
        },
        [(ThemeType.Airplanes, AgeBand.Older)] = new Skill
        {
            Georgian = "დახმარების თხოვნა — ეს სისუსტე არაა",
            English = "asking for help is not weakness",
            GeorgianHowToShow = "გმირი მარტო ვერ ახერხებს და თხოვნის შემდეგ ახერხებს"
        },

        // --- Magic: choice, consequence, and honesty --------------------------------------
        [(ThemeType.Magic, AgeBand.Toddler)] = new Skill
        {
            Georgian = "თავაზიანობა და კეთილი სიტყვა",
            English = "kind words and courtesy",
            GeorgianHowToShow = "გმირის კეთილი სიტყვა კარს ხსნის, უხეში კი ვერა"
        },
        [(ThemeType.Magic, AgeBand.Early)] = new Skill
        {
            Georgian = "არჩევანს შედეგი აქვს",
            English = "choices have consequences",
            GeorgianHowToShow = "გმირის პატარა არჩევანი მოგვიანებით დიდ განსხვავებას ქმნის"
        },
        [(ThemeType.Magic, AgeBand.Middle)] = new Skill
        {
            Georgian = "სიმართლის თქმა მაშინაც, როცა რთულია",
            English = "telling the truth when it is hard",
            GeorgianHowToShow = "გმირს დამალვა აჯობებდა, მაგრამ სიმართლეს ამბობს და ეს შველის"
        },
        [(ThemeType.Magic, AgeBand.Older)] = new Skill
        {
            Georgian = "ძალაუფლებასთან ერთად პასუხისმგებლობაც მოდის",
            English = "power comes with responsibility",
            GeorgianHowToShow = "გმირს ჯადოსნური შესაძლებლობა აქვს და თავად ირჩევს, არ გამოიყენოს"
        }
    };

    /// <summary>
    /// The skill for this theme and age. Every combination is present, so the fallback exists
    /// only to keep a future theme from throwing before its skills are written.
    /// </summary>
    public static Skill For(ThemeType theme, int age)
    {
        var band = BandFor(age);
        if (Matrix.TryGetValue((theme, band), out var skill))
        {
            return skill;
        }

        return new Skill
        {
            Georgian = "სიმამაცე და მეგობრობა",
            English = "courage and friendship",
            GeorgianHowToShow = "გმირი მეგობრისთვის რაღაც რთულს აკეთებს"
        };
    }
}
