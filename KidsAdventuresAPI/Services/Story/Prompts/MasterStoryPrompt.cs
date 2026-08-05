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
            - **ტექსტს მთელი გვერდი აქვს.** შეიძლება ცოტა უფრო გრძელი იყოს, ვიდრე სურათზე
              დასაწერი წარწერა — ოღონდ ასაკს მაინც უნდა შეესაბამებოდეს.

            ## ილუსტრაციების prompt-ები

            characterLock დაწერე ერთხელ, ინგლისურად, და **სიტყვასიტყვით ჩასვი ყოველი სცენის
            prompt-ის დასაწყისში**. ის არასოდეს შეცვალო გვერდიდან გვერდზე.

            ვიზუალური მიმართულება ყოველ prompt-ში:
            "High-quality cinematic 3D animated family-film aesthetic, expressive characters,
            rounded and appealing forms, detailed environments, warm emotional storytelling,
            soft global illumination, polished textures, vibrant but harmonious colors,
            cinematic composition, child-friendly atmosphere."

            prompt-ის სტრუქტურა: პერსონაჟის აღწერა → სცენა და მოქმედება → ემოცია →
            გარემო → კომპოზიცია და კამერა → განათება და ფერები → სტილი → ფორმატი → შეზღუდვები.

            {PhotoInstruction(input)}

            ## პასუხის ენა

            ისტორია, სათაური, caption-ები და განმავითარებელი ნაწილი — {LanguageName(input.Language)} ენაზე.
            characterLock, prompt და negativePrompt — **მხოლოდ ინგლისურად**.

            ## დაბრუნებამდე ჩუმად გადაამოწმე

            - შეესაბამება თუ არა ტექსტი ბავშვის ასაკს;
            - არის თუ არა ამბავი ორიგინალური;
            - აქვს თუ არა მთავარ გმირს აქტიური როლი;
            - ბუნებრივად ვითარდება თუ არა უნარი „{skill.Georgian}“;
            - არის თუ არა ემოციური კონფლიქტი უსაფრთხო;
            - **ყველა გვერდზე ერთი და იგივე სახელით არიან თუ არა პერსონაჟები**;
            - არის თუ არა characterLock ყოველ prompt-ში სიტყვასიტყვით;
            - გასაგებია თუ არა თითოეული prompt დამოუკიდებლად.
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
        prompt.AppendLine($"- ისტორიის ენა: {LanguageName(input.Language)}");
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
        prompt.AppendLine($"- concept: სათაური, ერთწინადადებიანი იდეა, სასწავლო მიზანი, გმირის აღწერა, 5–8 პუნქტიანი გეგმა, ასაკობრივი დასაბუთება;");
        prompt.AppendLine($"- spreads: ზუსტად {input.SpreadCount} სცენა, თითოეულს სათაური, caption, ტექსტი და თავისი ილუსტრაცია;");
        prompt.AppendLine("- characterLock: ინგლისურად, ერთი აბზაცი;");
        prompt.AppendLine($"- cover: ყდის ილუსტრაცია — გმირის პორტრეტი ამ სამყაროში, სათაურისთვის მშვიდი ადგილით;");
        prompt.AppendLine($"სულ {input.SpreadCount + 1} ილუსტრაცია: ყდა და {input.SpreadCount} სცენა. ყოველ prompt-ში characterLock სიტყვასიტყვით.");

        return prompt.ToString();
    }

    /// <summary>
    /// The photograph instruction, only when a photograph exists. Identity accuracy is stated as
    /// taking priority over stylisation, because the failure a parent notices first is a child
    /// who does not look like their child.
    /// </summary>
    private static string PhotoInstruction(MasterStoryInput input) =>
        string.IsNullOrWhiteSpace(input.AppearanceDescription)
            ? string.Empty
            : """
              ## რეალური ფოტო

              ბავშვის ფოტო თან ერთვის ილუსტრაციის გენერაციისას. characterLock-ში ჩადე ეს ფრაზა
              სიტყვასიტყვით, prompt-ის დასაწყისში:

              "Use the attached reference photograph as the primary and authoritative identity
              reference. Preserve the person's recognizable facial identity, facial geometry,
              eye shape and spacing, eyebrows, nose, lips, smile, cheeks, jawline, skin tone,
              hairstyle, hair color, age appearance, body build and natural body proportions as
              accurately as possible while translating them into a polished cinematic 3D
              animated character. Apply moderate stylization only; identity accuracy has
              priority over exaggerated cartoon features."

              არ შეცვალო ეთნიკური ნიშნები, კანის ფერი ან სხეულის ტიპი. არ დაამატო მაკიაჟი ან
              აქსესუარები, რომლებიც ფოტოზე არ ჩანს.
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
