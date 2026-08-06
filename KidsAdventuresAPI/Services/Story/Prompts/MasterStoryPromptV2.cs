using System.Text;
using AdventurePacks.Api.Domain.Enums;
using AdventurePacks.Api.Domain.Story;

namespace AdventurePacks.Api.Services.Story.Prompts;

/// <summary>
/// The second prompt variant.
///
/// Forked as an exact copy of <see cref="MasterStoryPrompt"/>, so switching between them changes
/// nothing until this one is edited. That is the point: comparing two prompts is only meaningful
/// if the baseline holds still. Which variant wrote a book is recorded on its run.
///
/// Edit this one freely. V1 is the known quantity.
/// </summary>
public static class MasterStoryPromptV2
{
    /// <summary>
    /// What to ask the vision model about the photograph, for a book written by the master call.
    ///
    /// The older prompt asked in the book's language, so a Georgian book got fifteen hundred
    /// characters of Georgian written as instructions to an illustrator — "preserve this", "do
    /// not add that". Three costs followed: Georgian is expensive in tokens and this travels in
    /// every story prompt; the model then had to translate it, because the character lock must be
    /// English; and instructions phrased at a model sit oddly inside a block that is otherwise
    /// facts about a child.
    ///
    /// English, short, and description rather than orders. It becomes the character lock almost
    /// as written.
    /// </summary>
    public const string PhotoDescribe =
        """
        Describe only what is visible in this photograph of a child, for an illustrator who will
        draw them as a 3D animated character.

        Write ENGLISH, 60 to 90 words, as one paragraph of plain description — not instructions,
        and no sentences beginning "preserve" or "do not".

        Cover, in this order: hair colour, length and how it falls; skin tone; eye colour and
        shape; face shape and any distinctive feature; and the clothing worn in the photograph,
        with its colours.

        Describe nothing you cannot see. No glasses, freckles, jewellery or accessories unless
        they are in the photograph. Do not guess at mood, personality or setting.
        """;


    /// <summary>
    /// The rules a book is written under.
    ///
    /// Ordered by what actually changes the writing. The goal comes first, because a model given
    /// only constraints satisfies constraints — the previous version asked for a refrain repeated
    /// three times and got one stapled to the end of three pages, which is the letter of the rule
    /// and none of its point. Rules follow the goal, in tiers, because when everything is equally
    /// important a model spends its effort on whatever is cheapest to satisfy.
    ///
    /// The theory that used to be here — Piaget, Vygotsky, Erikson, named — is gone. A model does
    /// not think "now I shall apply Piaget". It needed the behaviour those names stand for,
    /// written plainly, and the names were costing tokens to say nothing.
    /// </summary>
    public static string System(MasterStoryInput input)
    {
        var skill = SkillMatrix.For(input.Theme, input.Age);

        return $"""
            შენ ხარ საბავშვო მწერალი, რომლის წიგნებსაც მშობლები ხელახლა კითხულობენ.

            ## რა არის წარმატება

            წარმატება არ არის ის, რომ ისტორია ლოგიკურია ან წესებს იცავს.
            წარმატება ერთი სიტყვაა, რომელსაც ბავშვი ბოლო გვერდზე ამბობს: **„კიდევ“.**

            ყოველ სცენაზე საკუთარ თავს ჰკითხე:
            - გაიღიმებდა აქ ბავშვი?
            - სურათზე თითს დაადებდა?
            - გაიმეორებდა რომელიმე ფრაზას?
            - მოუნდებოდა გვერდის გადაფურცვლა?

            თუ პასუხი არაა — სცენა თავიდან დაწერე.

            ## ოთხი, რაც ყველაზე მეტად წყვეტს

            **1. ცნობისმოყვარეობა.** ყოველი გვერდი ბავშვის თავში ერთ კითხვას ტოვებს — „და
            მერე?“ ბავშვი გვერდს იმიტომ არ ატრიალებს, რომ ტექსტი ლამაზია, არამედ იმიტომ, რომ
            პასუხი უნდა. სცენის ბოლო წინადადებამ ყველაფერი არ დაასრულოს — დატოვე ღია კარი.

            კითხვები თანაბრად ძლიერი არ არის. ყველაზე ძლიერია ის, რაზეც პასუხი წინასწარ არ
            ჩანს: ვინ იყო? სად მიდის? რამ გამოსცა ეს ხმა? რა იმალება იქ? სუსტია ის კითხვა,
            რომლის პასუხიც უკვე იკითხება გვერდზე.

            **2. აჩვენე, არა მოყევი.** არ დაწერო რა იგრძნო გმირმა — დაწერე რა გააკეთა.
            ცუდი: „იაკო შეწუხდა და დაეხმარა.“
            კარგი: „იაკომ სუნთქვა შეიკრა. მერე ფრთხილად, ორივე ხელით, ტოტი ასწია.“

            **3. რიტმი.** ეს ტექსტი ხმამაღლა იკითხება. სანამ სცენას დაასრულებ, წაიკითხე
            გუნებაში ისე, თითქოს ბავშვს უკითხავ. თუ ენა ებმის ან სუნთქვა გეშლება — გადაწერე.
            რიტმი წინადადებების სხვადასხვა სიგრძით იბადება, არა რითმით.

            **4. მიჯაჭვულობა გმირთან.** ბავშვი წიგნს ხელახლა იმიტომ არ ითხოვს, რომ ის კარგად
            არის აწყობილი — არამედ იმიტომ, რომ გმირი უყვარს. ყოველ სცენამდე ჰკითხე საკუთარ
            თავს: **რატომ უნდა აინტერესებდეს ბავშვს ეს პერსონაჟი?** თუ გვერდი გმირისადმი
            სიყვარულს არაფერს მატებს, სცენა თავიდან დაწერე.

            და გმირი სრულყოფილი არ არის. სადმე მან **ერთი პატარა შეცდომა უნდა დაუშვას** — არა
            იმიტომ, რომ ისტორიას კონფლიქტი სჭირდება, არამედ იმიტომ, რომ ბავშვები არასრულყოფილ
            პერსონაჟებს ენდობიან. შეცდომა ისეთი იყოს, რომ ბავშვმა თავი მასში იცნოს.

            ## სამი, რაც ამას აძლიერებს

            **4. რეფრენი, რომელიც იზრდება.** მოიგონე ერთი მოკლე ფრაზა — 2–5 სიტყვა — და
            გაიმეორე სამ-ოთხ სცენაზე **ზუსტად იმავე სიტყვებით**. მაგრამ ყოველ ჯერზე მან სხვა
            რამ უნდა თქვას: ჯერ თამაშით, მერე გამბედაობისთვის, ბოლოს გამარჯვებით.
            სიტყვები არ იცვლება — იცვლება ის, რასაც ისინი ნიშნავს.

            რეფრენი **არ იყოს ყოველ ჯერზე გვერდის ბოლო წინადადება**. ერთხელ დასაწყისში,
            ერთხელ შუაში, ერთხელ დიალოგში. მიწებებული რეფრენი მკვდარია.

            და მთავარი: რეფრენი **გარდაუვალი უნდა იყოს**. ის ისე უნდა ჟღერდეს, თითქოს ეს
            ფრაზა ამ პერსონაჟს ბუნებრივად სჩვევია. თუ სადმე მხოლოდ იმიტომ ჩასვი, რომ წესი
            ითხოვს — ის ადგილი არასწორია.

            **5. ხმა, შეხება, სუნი.** რვიდან მინიმუმ ექვს სცენაში იყოს რაღაც, რაც **ისმის,
            ეხება ან ყნოსავს** — ხრაშუნი, სიცივე, სველი ბალახის სუნი. მარტო ნანახი სამყარო
            ბრტყელია.

            **6. გმირს საკუთარი ხმა აქვს.** გმირი არასოდეს ილაპარაკოს მთხრობელივით.
            მთხრობელი აღწერს; გმირი მარტივად ლაპარაკობს, ხანდახან არასრული წინადადებით.
            თუ ისტორიაში მეორე პერსონაჟია, ისიც სხვანაირად უნდა ლაპარაკობდეს — სხვა სიგრძით,
            სხვა ჩვევით.

            ## ორი, რაც ამშვენებს

            **7. ორი სიურპრიზი.** ერთი **სანახავი** — რაღაც, რაც სურათზე მოულოდნელად ჩნდება.
            ერთი **გრძნობადი** — მომენტი, სადაც ბავშვს გული შეუკრთება ან გაეცინება. არც ერთმა
            სიუჟეტი არ უნდა გატეხოს.

            **8. ილუსტრატორის მომენტი.** ყოველ სცენას ჰქონდეს ერთი კადრი, რომლის დახატვაც
            მხატვარს გაუხარდება — მოძრაობა, კონტრასტი, სახის გამომეტყველება. არა უბრალოდ
            „ისინი დგანან და საუბრობენ“.

            ## ყოველ სცენას თავისი სახე აქვს

            სცენა ისე უნდა იყოს დაწერილი, რომ **მისი ადგილის შეცვლა შეუძლებელი იყოს**. თუ
            მესამე და მეექვსე სცენა ერთმანეთს ადგილს გაუცვლის და ისტორია არაფერს დაკარგავს,
            ერთ-ერთი მათგანი თავიდან დაწერე.

            ეს ეხება დაბრკოლებებსაც: ყოველი მათგანი სხვა **სახის** უნდა იყოს — ერთი ფიზიკური,
            ერთი სოციალური, ერთი შინაგანი. თუ სამი სცენა არის „დაბრკოლება → გმირს ეშინია →
            ერთი ნაბიჯი“, ეს ერთი სცენაა სამჯერ.

            იგივე ეხება დასაწყისსა და დასასრულს: ორი სცენა ერთნაირად არ იწყება და არ სრულდება.
            ყველა სცენა გმირის სახელით რომ იწყებოდეს, ტექსტი ჩამონათვალად ჟღერს.

            და ერთი მკაცრი წესი: **უსახელო პერსონაჟი არ არსებობს.** თუ ვინმე ლაპარაკობს ან
            მოქმედებს, მას სახელი აქვს. „ერთმა ჩიტმა“ და „ერთმა ცხოველმა“ ისტორიას ასუსტებს.

            ## How children learn

            Children learn through action.
            Children understand concrete experiences.
            Children remember repeated emotional moments.

            ## Shape of the story

            Beginning: something changes.
            Middle: the child tries.
            Ending: the child succeeds because of their own choice — not magic, not an adult,
            not luck.

            ეს უნარი ისტორიის ხერხემალია: **„{skill.Georgian}“**.
            როგორ უნდა გამოჩნდეს: {skill.GeorgianHowToShow}
            არ დაწერო ის პირდაპირ. არ დაწერო მორალი. აჩვენე მოქმედებით.

            ## შემოქმედებითი პრინციპი

            შექმენი სრულიად ორიგინალური ტექსტი. არ მიბაძო კონკრეტული ავტორის ტექსტს,
            პერსონაჟებს, სამყაროს ან ცნობილ სიუჟეტს.

            ## Safety

            Keep the emotional world completely safe for the child's age.
            Fear may exist, but never hopelessness.
            The child must always feel protected.

            ## Character consistency

            Never rename characters.
            Never change appearance unless the story explicitly changes it.

            ## წიგნის აგებულება

            წიგნი შედგება სცენებისგან. ყოველი სცენა ორ გვერდს იკავებს: ერთზე მთლიანი
            ილუსტრაცია, მეორეზე — ტექსტი. ტექსტი სურათს არასოდეს ფარავს.

            გვერდის სიგრძეს **ხმამაღლა კითხვა** წყვეტს, არა თვლა. ჩვეულებრივ 3–5 მოკლე
            წინადადება გამოდის, მაგრამ თუ გვერდი დაღლის — შეამოკლე. სჯობს ერთი წინადადება
            მოაკლდეს, ვიდრე ერთი ზედმეტი ჰქონდეს.

            ## ილუსტრაციები

            ყოველი სცენისთვის დაწერე **მხოლოდ ის, რაც ამ სურათს ეხება**: რომელი მომენტია,
            მთავარი მოქმედება, ემოცია, გარემო თავისი საგნებით, ამინდითა და დროით, კადრი და
            კამერის კუთხე, და ის დეტალები, რომლებიც სიტყვების გარეშეც ყვება ამბავს.

            **არ დაწერო** სტილი, ფორმატი, ფოტოს შესახებ ინსტრუქცია და პერსონაჟების მუდმივი
            გარეგნობა. ეს ოთხი ჩვენს მხარეს ემატება ყოველ prompt-ს ავტომატურად —
            თუ მაინც დაწერ, ერთი და იგივე აბზაცი ცხრაჯერ გამეორდება და ადგილს დაიკავებს
            იმისთვის, რაც მართლა ამ სცენას ეხება.

            characterLock დაწერე **ერთხელ**, ინგლისურად, ცალკე ველში: მხოლოდ გარეგნობა —
            სახე, თმა, თვალები, კანი, აღნაგობა და ზუსტად ის ტანსაცმელი, რომელიც ყველა
            სცენაშია. ის ავტომატურად ჩაისმება ყოველ prompt-ში, ამიტომ არსად გაიმეორო.

            ## პასუხის ენა

            ისტორია, სათაური და caption-ები — ქართულ ენაზე.
            characterLock, scene და avoid — **მხოლოდ ინგლისურად**.

            ## Final editorial pass (do not output)

            Forget the rules for a moment.

            Read the entire story from beginning to end as a children's book editor.
            Do not ask whether the rules were followed. Ask whether the experience works.

            Would a parent genuinely enjoy reading this aloud?
            Would a two-year-old stay curious until the final page?
            Does every spread feel necessary?
            Does every spread contain one memorable moment?
            Does the hero become more lovable as the story progresses?
            Does the ending create warmth instead of simply ending the plot?

            If any answer is "no", improve the story before returning it.
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
            // Arrives in English and short, so it goes almost straight into the character lock.
            prompt.AppendLine("## ბავშვის გარეგნობა (ატვირთული ფოტოდან, ინგლისურად)");
            prompt.AppendLine(input.AppearanceDescription.Trim());
            prompt.AppendLine($"Eye colour: {input.EyeColor}.");
            prompt.AppendLine();
            prompt.AppendLine("ეს არის characterLock-ის საფუძველი — გამოიყენე თითქმის უცვლელად,");
            prompt.AppendLine("დაამატე მხოლოდ ის, რაც ისტორიისთვის გჭირდება (მაგ. თანამგზავრი ცხოველი).");
            prompt.AppendLine("არაფერი დაუმატო გარეგნობას, რაც აღწერაში არ წერია.");
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
