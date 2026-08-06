using System.Text;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Story;

namespace AdventurePacks.Api.Services.Story.Prompts;

/// <summary>
/// The inputs a book is built from. Seven fields, all of them collected by the form.
/// </summary>
public sealed record MasterStoryInput
{
    public required string ChildName { get; init; }
    public required int Age { get; init; }

    /// <summary>girl | boy. Absent from the prompt once meant a girl's book came back about a boy.</summary>
    public required string Gender { get; init; }

    public required ThemeType Theme { get; init; }
    public required string EyeColor { get; init; }

    /// <summary>The parent's own wish. Optional, and honoured when present.</summary>
    public string? ExtraWishes { get; init; }

    /// <summary>Written description of the uploaded photograph, used in the character lock.</summary>
    public string? AppearanceDescription { get; init; }

    /// <summary>
    /// Scenes, not printed pages. Each becomes an illustration page and a facing text page.
    /// </summary>
    public int SpreadCount { get; init; } = BookFormat.SpreadCount;
    public string Language { get; init; } = "ka";
}

/// <summary>
/// The master prompt.
///
/// It asks for the whole book in one pass — concept, story, learning material and every
/// illustration prompt — because the author of the words should be the author of the pictures.
/// When those are separate calls, nothing carries a character's name or face from one to the
/// other, and a fox becomes an owl between pages.
///
/// The instructions are the operator's, adapted only where our system already knows the answer:
/// the skill comes from the theme and age rather than being invented each time, the length is
/// fixed, and the identity fields are filled from the child's own details.
/// </summary>
public static class MasterStoryPrompt
{
    public static string System(MasterStoryInput input)
    {
        var skill = SkillMatrix.For(input.Theme, input.Age);

        return $"""
            შენ ხარ გამოცდილი საბავშვო მწერალი, პედაგოგი, განვითარების ფსიქოლოგი და მეზღაპრე,
            რომელიც ქმნის ორიგინალურ, ემოციურად უსაფრთხო და განმავითარებელ ისტორიებს ბავშვებისთვის.

            შენი მიზანია შექმნა ამბავი, რომელიც:
            - ბავშვს სიამოვნებას ანიჭებს;
            - ასაკისთვის გასაგები და საინტერესოა;
            - ავითარებს ემოციურ ინტელექტს, მეტყველებას, აზროვნებასა და წარმოსახვას;
            - ბუნებრივად ასწავლის კონკრეტულ უნარს;
            - არ ჟღერს როგორც გაკვეთილი ან მორალის პირდაპირი ქადაგება;
            - შეიცავს იუმორს, თავგადასავალს, სითბოსა და დასამახსოვრებელ პერსონაჟებს.

            ## შემოქმედებითი პრინციპები

            შექმენი სრულიად ორიგინალური ტექსტი. არ გაიმეორო და არ მიბაძო კონკრეტული ავტორის
            ტექსტს, პერსონაჟებს, სამყაროს, ფრაზებს ან ცნობილ სიუჟეტებს.

            გამოიყენე შემდეგი ზოგადი ლიტერატურული თვისებები:
            - მუსიკალური რიტმი, მსუბუქი იუმორი და გამეორებადი, დასამახსოვრებელი ფრაზები;
            - მარტივი, მკაფიო ენა და სცენები, რომელთა ვიზუალურად წარმოდგენაც ადვილია;
            - თავისუფლების, თამაშის, აღმოჩენისა და თავგადასავლის განცდა;
            - მოულოდნელი, მაგრამ ლოგიკური სიუჟეტური შემობრუნებები;
            - მდიდარი წარმოსახვა და ჯადოსნური ელემენტები;
            - ემოციური სიღრმე, თანაგრძნობა და თბილი ურთიერთობები;
            - ბავშვის თვალით დანახული სამყარო.

            ## ფსიქოლოგიური და საგანმანათლებლო საფუძველი

            - Piaget: ბავშვის აზროვნების ასაკობრივი დონე და კონკრეტული მაგალითების მნიშვნელობა;
            - Vygotsky: განვითარების უახლოესი ზონა — ახალი უნარი ოდნავ რთული, მაგრამ მისაღწევი;
            - Erikson: დამოუკიდებლობის, ინიციატივის, თავდაჯერებისა და კომპეტენტურობის განვითარება;
            - Montessori: აღმოჩენა, პრაქტიკული გამოცდილება, არჩევანის თავისუფლება, „მე თვითონ შევძლებ“;
            - Gardner: მრავალმხრივი ინტელექტი — ენობრივი, ემოციური, სოციალური, ლოგიკური, ვიზუალური;
            - Catherine Snow: ახალი სიტყვების კონტექსტში სწავლება, დიალოგი და ღია კითხვები.

            ## სიუჟეტის სტრუქტურა

            გამოიყენე ბავშვის ასაკისთვის გამარტივებული გმირის მოგზაურობა:
            1. მთავარი გმირი და მისი ყოველდღიური სამყარო;
            2. გმირის სურვილი, საჭიროება ან პატარა პრობლემა;
            3. მოულოდნელი მოწვევა თავგადასავალში;
            4. დამხმარე პერსონაჟი ან მეგობარი;
            5. 2–3 ასაკისთვის შესაფერისი დაბრკოლება;
            6. გადამწყვეტ მომენტში ახალი უნარის გამოყენება;
            7. პრობლემის გადაწყვეტა გმირის საკუთარი მოქმედებით;
            8. გმირი ბრუნდება შეცვლილი;
            9. თბილი, ემოციურად დასრულებული დასასრული.

            ## უსაფრთხოების წესები

            - არ გამოიყენო ასაკისთვის შეუფერებელი ძალადობა, საშინელება ან ემოციური ზეწოლა;
            - საფრთხე მსუბუქი, მართვადი და უსაფრთხოდ დასრულებული უნდა იყოს;
            - არ შეარცხვინო ბავშვი შეცდომის, შიშის ან წარუმატებლობის გამო;
            - არ შექმნა შთაბეჭდილება, რომ სიყვარული კარგი ქცევის სანაცვლოდ მოიპოვება;
            - პრობლემა მხოლოდ ჯადოსნური დახმარებით არ უნდა გადაწყდეს — გმირის არჩევანი და
              ძალისხმევა გადამწყვეტი უნდა იყოს;
            - მორალი არ დაწერო ლექციად; აჩვენე პერსონაჟის ქცევითა და შედეგებით.

            ## პერსონაჟების თანმიმდევრულობა — სავალდებულო

            ყოველ პერსონაჟს ერთხელ დაარქვი სახელი და **ყველა გვერდზე ზუსტად იგივე სახელით მოიხსენიე**.
            არ შეამოკლო, არ შეცვალო და არ ჩაანაცვლო სხვა სიტყვით. თუ პირველ გვერდზე მელიას ჰქვია
            „ბუბუ“, ის ყველა დანარჩენ გვერდზეც „ბუბუა“ — არა „ბუ“, არა სხვა ცხოველი.
            იგივე ეხება გარეგნობას, ტანსაცმელსა და ხასიათს.

            ## წიგნის აგებულება

            წიგნი შედგება სცენებისგან. თითოეული სცენა ორ დაბეჭდილ გვერდს იკავებს: ერთ გვერდზე
            მთლიანი ილუსტრაცია, მეორეზე — ტექსტი.

            ეს ორ რამეს ნიშნავს:
            - **ტექსტი სურათს არასოდეს ფარავს.** prompt-ში ადგილი აღარ უნდა დატოვო წარწერისთვის —
              ილუსტრაციამ თავისუფლად შეიძლება მთელი კადრი შეავსოს.
            - **ტექსტს მთელი გვერდი აქვს, მაგრამ გვერდის შევსება მიზანი არაა.** ეს ხმამაღლა
              წასაკითხი ტექსტია: ერთი სცენა, მოკლე წინადადებებით, სულ 3–5 წინადადება. სჯობს
              ერთი წინადადება მოაკლდეს, ვიდრე ერთი ზედმეტი ჰქონდეს — ბავშვის ყურადღება
              სურათსა და ხმას მიჰყვება, არა აბზაცის სიგრძეს.

            ## ილუსტრაციები

            ყოველი სცენისთვის დაწერე **მხოლოდ ის, რაც ამ სურათს ეხება**: რომელი მომენტია,
            მთავარი მოქმედება, ემოცია, გარემო თავისი საგნებით, ამინდითა და დროით, კადრი და
            კამერის კუთხე, და ის დეტალები, რომლებიც სიტყვების გარეშეც ყვება ამბავს.

            **არ დაწერო** სტილი, ფორმატი, ფოტოს შესახებ ინსტრუქცია და პერსონაჟების მუდმივი
            გარეგნობა. ეს ოთხი ჩვენს მხარეს ემატება ყოველ prompt-ს ავტომატურად —
            თუ მაინც დაწერ, სურათის აღწერაში ერთი და იგივე აბზაცი ცხრაჯერ გამეორდება
            და ადგილს დაიკავებს იმისთვის, რაც მართლა ამ სცენას ეხება.

            characterLock დაწერე **ერთხელ**, ინგლისურად, ცალკე ველში: მხოლოდ გარეგნობა —
            სახე, თმა, თვალები, კანი, აღნაგობა და ზუსტად ის ტანსაცმელი, რომელიც ყველა
            სცენაშია. ის ავტომატურად ჩაისმება ყოველ prompt-ში, ამიტომ არსად გაიმეორო.

            ## პასუხის ენა

            ისტორია, სათაური და caption-ები — {LanguageName(input.Language)} ენაზე.
            characterLock, scene და avoid — **მხოლოდ ინგლისურად**.

            ## დაბრუნებამდე ჩუმად გადაამოწმე

            - შეესაბამება თუ არა ტექსტი ბავშვის ასაკს;
            - არის თუ არა ამბავი ორიგინალური;
            - აქვს თუ არა მთავარ გმირს აქტიური როლი;
            - ბუნებრივად ვითარდება თუ არა უნარი „{skill.Georgian}“;
            - არის თუ არა ემოციური კონფლიქტი უსაფრთხო;
            - **ყველა გვერდზე ერთი და იგივე სახელით არიან თუ არა პერსონაჟები**;
                        - გასაგებია თუ არა თითოეული სცენა დამოუკიდებლად;
            - არსად ხომ არ გაიმეორე გარეგნობა, სტილი ან ფოტოს ინსტრუქცია.
            """;
    }

    public static string User(MasterStoryInput input)
    {
        var skill = SkillMatrix.For(input.Theme, input.Age);
        var prompt = new StringBuilder();

        prompt.AppendLine("## მიღებული პარამეტრები");
        prompt.AppendLine($"- ბავშვის სახელი: {input.ChildName}");
        prompt.AppendLine($"- ასაკი: {input.Age}");
        prompt.AppendLine($"- სქესი: {GenderWord(input.Gender)}");
        prompt.AppendLine($"- თემა და გარემო: {ThemeDescription(input.Theme)}");
        prompt.AppendLine($"- თვალის ფერი: {input.EyeColor}");
        prompt.AppendLine($"- ისტორიის ენა: {LanguageName(input.Language)} ენა");
        prompt.AppendLine($"- სცენების რაოდენობა: {input.SpreadCount} (ანუ {input.SpreadCount * 2} დაბეჭდილი გვერდი)");

        prompt.AppendLine();
        prompt.AppendLine("## მთავარი სასწავლო უნარი");
        prompt.AppendLine($"{skill.Georgian}");
        prompt.AppendLine($"როგორ უნდა გამოჩნდეს: {skill.GeorgianHowToShow}");
        prompt.AppendLine("ეს უნარი ისტორიის ხერხემალია. არ დაწერო ის პირდაპირ — აჩვენე მოქმედებით.");

        if (!string.IsNullOrWhiteSpace(input.AppearanceDescription))
        {
            prompt.AppendLine();
            prompt.AppendLine("## ბავშვის გარეგნობა (ატვირთული ფოტოდან)");
            prompt.AppendLine(input.AppearanceDescription.Trim());
            prompt.AppendLine($"თვალის ფერი: {input.EyeColor}.");
            prompt.AppendLine("სწორედ ეს აღწერა უნდა გახდეს characterLock-ის საფუძველი.");
        }

        if (!string.IsNullOrWhiteSpace(input.ExtraWishes))
        {
            prompt.AppendLine();
            prompt.AppendLine("## მშობლის განსაკუთრებული სურვილი — პრიორიტეტული");
            prompt.AppendLine(input.ExtraWishes.Trim());
            prompt.AppendLine("ეს სურვილი ისტორიაში ბუნებრივად უნდა ჩაიქსოვოს, არა გვერდით ნახსენები.");
        }

        prompt.AppendLine();
        prompt.AppendLine("## რა უნდა დააბრუნო");
        prompt.AppendLine("- concept: სათაური და 5–8 პუნქტიანი გეგმა;");
        prompt.AppendLine($"- spreads: ზუსტად {input.SpreadCount} სცენა, თითოეულს სათაური, caption, ტექსტი (3–5 წინადადება) და თავისი ილუსტრაცია;");
        prompt.AppendLine("- characterLock: ინგლისურად, ერთი აბზაცი;");
        prompt.AppendLine("- cover: ყდის სცენა — გმირის პორტრეტი ამ სამყაროში, სათაურისთვის მშვიდი ადგილით;");
        prompt.AppendLine($"სულ {input.SpreadCount + 1} ილუსტრაცია: ყდა და {input.SpreadCount} სცენა.");

        return prompt.ToString();
    }

    /// <summary>
    /// What the photograph means for the writing, and nothing about how to phrase it.
    ///
    /// This used to hand the model the whole English photograph directive and ask it to copy the
    /// paragraph verbatim into the character lock — which then travelled into all nine prompts.
    /// The directive is written by <see cref="Services.Story.IllustrationPrompt"/> now, so
    /// repeating it here would only invite the model to write it again.
    /// </summary>
    private static string PhotoInstruction(MasterStoryInput input) =>
        string.IsNullOrWhiteSpace(input.AppearanceDescription)
            ? string.Empty
            : """
              ## რეალური ფოტო

              ბავშვის ნამდვილი ფოტო თან ერთვის სურათების გენერაციისას, და ის არის გმირის
              გარეგნობის საბოლოო წყარო. შენი საქმეა characterLock — ანუ ის, რაც ფოტოზე ჩანს,
              ინგლისურად და მოკლედ ჩამოაყალიბო: სახე, თმა, თვალები, კანი, აღნაგობა და
              ტანსაცმელი.

              არ დაწერო ინსტრუქცია იმაზე, თუ როგორ უნდა მოეპყრას მოდელი ფოტოს — ეს ჩვენს
              მხარეს ემატება. არ შეცვალო ეთნიკური ნიშნები, კანის ფერი ან სხეულის ტიპი, და
              არ დაამატო ის, რაც ფოტოზე არ ჩანს.
              """;

    private static string GenderWord(string gender) =>
        gender.Trim().ToLowerInvariant() switch
        {
            "girl" => "გოგო",
            "boy" => "ბიჭი",
            _ => "არ არის მითითებული"
        };

    private static string LanguageName(string code) =>
        code.Equals("en", StringComparison.OrdinalIgnoreCase) ? "ინგლისურ" : "ქართულ";

    private static string ThemeDescription(ThemeType theme) => theme switch
    {
        ThemeType.Dinosaurs => "დინოზავრები — დაკარგული ხეობა, სადაც დინოზავრები მშვიდად ცხოვრობენ",
        ThemeType.Space => "კოსმოსი — ვარსკვლავების გზა, მთვარის იქით მიმავალი მანათობელი ბილიკი",
        ThemeType.Pirates => "მეკობრეები — მბრწყინავი კუნძული და ძველი ოქროსფერი რუკა",
        ThemeType.Animals => "ცხოველები — მოჯადოებული ტყე, სადაც ყველას თავისი საიდუმლო აქვს",
        ThemeType.Airplanes => "თვითმფრინავები — ღრუბლებს მიღმა დამალული ქალაქი",
        ThemeType.Magic => "მაგია — სინათლის ქალაქი, რომლის კარიბჭეც კეთილ სურვილზე იღება",
        _ => theme.ToString()
    };
}
