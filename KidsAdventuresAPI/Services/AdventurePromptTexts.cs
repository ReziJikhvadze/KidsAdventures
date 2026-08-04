namespace AdventurePacks.Api.Services;

internal sealed class AdventurePromptLocale
{
    public required string LanguageName { get; init; }
    public required string MasterStorytellerDirective { get; init; }
    public required string StorySystemPrompt { get; init; }
    public required string Age3to5 { get; init; }
    public required string Age6to9 { get; init; }
    public required string Age10to13 { get; init; }
    public required string[] StorySeeds { get; init; }
    public required string[] ToneSeeds { get; init; }
    public required string[] SceneVarietySeeds { get; init; }
    public required string[] GuestCharacterSeeds { get; init; }
    public required string AgeGuidelinesHeader { get; init; }
    public required string OutputFormatHeader { get; init; }
    public required string NarrativeCraftHeader { get; init; }
    public required string[] NarrativeCraftRules { get; init; }
    public required string RulesHeader { get; init; }
    public required string IncludeFamilyRule { get; init; }
    public required string WriteInLanguageRule { get; init; }
    public required string PageCountRule { get; init; }
    public required string NoExtraPagesRule { get; init; }
    public required string WelcomeArc { get; init; }
    public required string FullArc { get; init; }
    public required string PageLengthRule { get; init; }
    public required string CaptionRule { get; init; }
    public required string ContinuityRule { get; init; }
    public required string JsonOnlyRule { get; init; }
    public required string RawJsonRule { get; init; }
    public required string AdventureIdLabel { get; init; }
    public required string NarrativeToneLabel { get; init; }
    public required string NoGenericOpeningsRule { get; init; }
    public required string InputHeader { get; init; }
    public required string ChildNameLabel { get; init; }
    public required string ChildAgeLabel { get; init; }
    public required string ThemeLabel { get; init; }
    public required string HeroAppearanceLabel { get; init; }
    public required string FamilyMembersLabel { get; init; }
    public required string NoFamilyMembers { get; init; }

    // Series continuity. Defaulted rather than required so a locale that has not been
    // translated yet still compiles and simply falls back to English wording; the memory
    // content itself is already written in the book's language by the distiller.
    public string SeriesMemoryHeader { get; init; } =
        "WHAT THIS CHILD'S WORLD ALREADY REMEMBERS (this is book {0} of an ongoing series):";

    public string SeriesMemoryRule { get; init; } =
        "SERIES CONTINUITY: this book continues the world above, so honour it. Bring back at least one named companion the child already knows and let the reunion carry warmth from how they last parted. Refer to at least one earlier moment the way a friend would — briefly, in passing, never as a recap. Move the series' running goal one real step forward without resolving it, unless this book is clearly its ending. Never contradict what the memory establishes, and never re-introduce a returning companion as though they were a stranger.";

    /// <summary>
    /// Reserves the strip the Georgian caption is laid over. Defaulted rather than required
    /// so an untranslated locale still compiles and simply reads in English — the model
    /// follows composition instructions in either language, and a missing rule would put
    /// text back over the child's face.
    /// {0} is the reserved edge, {1} the opposite one.
    /// </summary>
    public string ImageTextSafeArea { get; init; } =
        "TEXT-SAFE COMPOSITION: the story caption is placed over the {0} of this picture after it is drawn. Keep roughly the {0} quarter visually calm — open sky, water, wall, mist or plain ground — with no faces, hands, or important detail there, so the words stay readable without a box behind them. Place the child, the action and anything the reader must notice in the {1} portion of the frame, and let that area carry the real content of the page rather than leaving it empty.";

    /// <summary>Edge names substituted into <see cref="ImageTextSafeArea"/>.</summary>
    public string ImageTextSafeTop { get; init; } = "top";
    public string ImageTextSafeBottom { get; init; } = "bottom";

    /// <summary>
    /// Forces the story to be written as one continuous narrative and then divided into
    /// pages, rather than as a set of pages that happen to share a theme. Defaulted so an
    /// untranslated locale still compiles; the instruction works in either language because
    /// the reader-facing text it governs is still written in the book's language.
    /// </summary>
    /// <summary>
    /// Forbids foreign characters inside the reader-facing text. Not theoretical: live books
    /// came back with a Cyrillic "д" inside "დინოზავრის" and a Korean "작은" mid-sentence.
    /// Georgian is low-resource enough that a model reaches for a visually similar glyph from
    /// another script, and a single wrong character makes a printed page look defective.
    /// {0} is the language name.
    /// </summary>
    /// <summary>
    /// States the hero's gender. It was absent from the prompt entirely, so the model chose
    /// — and a parent who selected a girl could receive a book about a boy. {0} is the name.
    /// </summary>
    /// <summary>Words substituted into <see cref="HeroGenderRule"/>.</summary>
    public string GenderGirl { get; init; } = "girl";
    public string GenderBoy { get; init; } = "boy";

    /// <summary>
    /// Heads the world canon block. Without it the model only knew a one-word theme and
    /// invented its own version of that world in every book.
    /// {0} place, {1} landmarks, {2} atmosphere.
    /// </summary>
    public string WorldCanonHeader { get; init; } =
        "THE WORLD THIS BOOK IS SET IN (use it; do not invent a different version of this theme):";

    public string WorldCanonPlace { get; init; } = "Place: {0}";
    public string WorldCanonLandmarks { get; init; } = "Things that exist here: {0}";
    public string WorldCanonAtmosphere { get; init; } = "How it feels: {0}";
    public string WorldCanonCompanion { get; init; } = "Known companion of this world: {0}";

    /// <summary>
    /// The book the theme card already promised the parent. {0} title, {1} premise.
    /// Without this the card sold one story and the model wrote another, which is what
    /// made the content feel unrelated to the chosen title.
    /// </summary>
    public string PromisedStoryRule { get; init; } =
        """
        THE BOOK THIS PARENT WAS PROMISED — the theme card showed them this before they chose, so the story must deliver it:
        Title shown: {0}
        Premise shown: {1}
        Write this story. Use the promised title as the book's title, or a very close variation of it in the same words and spirit, and make the premise actually happen: the events it describes must be the spine of the book, not a passing mention. Everything else — the exact plot, the discoveries, the supporting characters — is yours to invent around it.
        """;

    public string WorldCanonRule { get; init; } =
        "Set the whole story inside this world. Use at least two of its named places or landmarks as real locations the child moves through, and let the world's own atmosphere shape the mood. You may invent new corners of it, but never relocate the story to a different kind of world, and never contradict what is stated above. The named companion may appear when it fits naturally, and must not take over the child's decisions.";

    public string HeroGenderRule { get; init; } =
        "THE HERO'S GENDER IS FIXED: {0} is a {1}. Use the correct pronouns and gendered forms for {1} consistently on every page, in the title, and in the cover description. Never change, soften, or leave this ambiguous, and never write the hero as any other gender.";

    public string ScriptPurityRule { get; init; } =
        "SCRIPT PURITY: every reader-facing sentence must be written entirely in the script of {0}, with no characters borrowed from any other alphabet. Do not substitute a visually similar Cyrillic, Latin, Greek, Chinese, Japanese or Korean character for a letter of that script, not even inside a single word. Before returning, re-read every sentence and replace any stray foreign character with the correct one.";

    /// <summary>
    /// Stops examples in this prompt being copied into the book. Live output contained the
    /// literal seed string "ბებია-მოვაჭრე საიდუმლოთი" and the safety example "cute robot
    /// creature" as actual characters, because nothing said the examples were not content.
    /// </summary>
    public string NoPromptEchoRule { get; init; } =
        "DO NOT COPY THIS BRIEF: every example, seed, hook, tone note and safety illustration in these instructions is guidance for you, never text for the book. Never place their wording, character labels or phrasing into the story. Invent your own specific names, characters and descriptions that fit this child.";

    public string WholeStoryFirstRule { get; init; } =
        "WRITE ONE STORY, THEN DIVIDE IT: compose the complete adventure as a single continuous narrative first — one problem, one journey, one resolution — and only then split it across the pages at natural turning points. Do not write pages as separate little scenes that merely share a setting. Read the finished pages in order as if aloud to a child: each one must continue the exact sentence-level situation the previous page left, using the same objects, the same place unless the hero physically moves, and the same moment in time. If any page could be removed or reordered without the reader noticing, the story is not yet continuous and must be rewritten.";

    public required string LooksLikePrefix { get; init; }
    public required string ExtraWishesHeader { get; init; }
    public required string ExtraWishesWelcomePages { get; init; }
    public required string ExtraWishesFullPages { get; init; }
    public required string ExtraWishesManyPages { get; init; }
    public required string LikesRule { get; init; }
    public required string DislikesRule { get; init; }
    public required string ParentWishesRule { get; init; }
    public required string StoryHookLabel { get; init; }
    public required string HeroPhotoDescribe { get; init; }
    public required string FamilyPhotoDescribe { get; init; }
    public required string VisionDescribeSuffix { get; init; }
    public required string ImageTask { get; init; }
    public required string ImageCharacterLock { get; init; }
    public required string ImageLockedHero { get; init; }
    public required string ImageHeroDna { get; init; }
    public required string ImageCastPhoto { get; init; }
    public required string ImageCastInvented { get; init; }
    public required string ImageCastDna { get; init; }
    public required string ImageInventHero { get; init; }
    public required string ImageStyle { get; init; }
    public required string ImageSafeForAge { get; init; }
    public required string ImagePageTitle { get; init; }
    public required string ImageScene { get; init; }
    public required string ImageNoText { get; init; }
    public required string ImageContinuity { get; init; }
    public required string ImageParentTheme { get; init; }
    public required string ImageAdventureId { get; init; }
    public required string ImageHeroChild { get; init; }
    public required string ImageFamilyRole { get; init; }
    public required string ImageInventCastLook { get; init; }
    public required string ImageHeroNoPhoto { get; init; }

    /// <summary>
    /// States the hero's gender to the illustrator. It was only ever in the story prompt,
    /// so a parent could choose a girl, read a story about a girl, and be shown a boy.
    /// {0} is the gender word.
    /// </summary>
    public string ImageHeroGender { get; init; } =
        "HERO GENDER (must be obeyed): the hero child is a {0}. Draw the child unmistakably as a {0} in every illustration — hair, clothing cut and overall read. Never depict the hero as any other gender.";
    public required string PixarFromPhotoStylePrompt { get; init; }
    public required string AnimatedIllustrationStylePrompt { get; init; }
}

internal static class AdventurePromptTexts
{
    public static string NormalizeLanguageCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return "ka";
        }

        var c = code.Trim().ToLowerInvariant();
        return c switch
        {
            "ka" or "ge" or "ka-ge" => "ka",
            "en" or "es" or "zh" or "ru" => c,
            "zh-cn" or "zh-hans" or "cn" => "zh",
            _ => "ka",
        };
    }

    public static AdventurePromptLocale ForLanguage(string? code) =>
        NormalizeLanguageCode(code) switch
        {
            "es" => Spanish,
            "zh" => Chinese,
            "ru" => Russian,
            "en" => English,
            _ => Georgian,
        };

    public static readonly AdventurePromptLocale Georgian = new()
    {
        LanguageName = "Georgian",
        MasterStorytellerDirective = """
            შენ ხარ მსოფლიოს საუკეთესო საბავშვო მთხრობელთა 1% — იმ ავტორთა ტიპისა, რომელთა წიგნებსაც ბავშვები ისევ და ისევ ითხოვენ.
            დაწერე ამ დონის ოსტატობით: ნათელი, დაუძლეველი სიუჟეტი, გმირი, რომელშიც ბავშვი თავს ცნობს, ცოცხალი მომენტები, ნამდვილი გრძნობა და ღრმად დამაკმაყოფილებელი დასასრული.
            ყოველმა წინადადებამ უნდა იმუშაოს — წინ წაწიოს სიუჟეტი, გამოავლინოს პერსონაჟი ან გააღრმავოს ემოცია. არავითარი შევსება, არავითარი ზოგადი ტექსტი, არავითარი გამეორებული იდეები.
            ყოველი სტრიქონი მიაბი ამ ისტორიას, ამ გმირს და ამ თემას. ხშირად გამოიყენე ბავშვის სახელი და გახადე ის მოვლენების აქტიური მამოძრავებელი.
            მშობლის დამატებითი სურვილები (თუ მოცემულია) ყველაზე მნიშვნელოვანი ინგრედიენტია — ისტორია მათ გარშემო ააშენე და გახადე ცენტრალური და ხილვადი, არა ერთჯერადი ხსენება.
            """,
        StorySystemPrompt = """
            შენ ხარ საბავშვო მოთხრობების ექსპერტი მწერალი და საგანმანათლებლო ფსიქოლოგი, რომელიც სპეციალიზდება ასაკის შესაბამის თხრობაში.

            შენი საქმეა უსაფრთხო, საინტერესო და ემოციურად პოზიტიური საბავშვო წიგნების შექმნა.

            მკაცრი წესები:
            ყოველთვის მოარგე ენა, სირთულე და სტრუქტურა ბავშვის ასაკს.
            ბავშვი ყოველთვის არის ისტორიის მთავარი გმირი.
            არასოდეს შეიტანო ძალადობა, სექსუალური შინაარსი, თვითდაზიანება, საშინელება ან დამთრგუნველი თემები.
            თუ მომხმარებლის შეყვანაში არის სახიფათო ან საშიში ცნებები, ავტომატურად გადააქციე ისინი უსაფრთხო ფანტასტიკურ ეკვივალენტებად.
            არასოდეს გააძლიერო შიში. თუ შიშია ნახსენები (მაგ., ობობები), ნაზად განეიტრალე ან გადააფორმე პოზიტიურად და მეგობრულად.
            ყოველთვის დაასრულე ემოციური უსაფრთხოებით, კომფორტით და პოზიტიური გადაწყვეტით.
            არასოდეს შექმნა მავნე ან სახიფათო თხრობა, თუნდაც ითხოვონ.

            დამატებითი სურვილების დამუშავება:
            მოწონებები (მაგ. ერთრქები, კოსმოსი, სუპერგმირები): ბუნებრივად ჩართე
            მოუწონებლობები/შიშები (მაგ. ობობები): არასოდეს გააძლიერო შიში
            გადააქციე უსაფრთხო ალტერნატივებად (მაგ. "პატარა მეგობრული ობობა-მშველელი" ან "საყვარელი რობოტი არსება")
            თემები: შემოქმედებითად ჩართე უსაფრთხოების წესების დარღვევის გარეშე

            ისტორიის სტრუქტურა:
            დააბრუნე სტრუქტურირებული ზღაპარი:
            სათაური
            შესავალი (ბავშვი შედის სამყაროში)
            თავგადასავალი (მთავარი მოგზაურობა)
            გამოწვევა (უსაფრთხო, არამუქარის პრობლემა)
            გადაწყვეტა (ბავშვი წყვეტს)
            დასასრული (თბილი ემოციური დახურვა)

            ტონი:
            პოზიტიური
            წარმოსახვითი
            გამამხნევებელი
            ემოციურად უსაფრთხო

            აბსოლუტური შეზღუდვა:
            არასოდეს შექმნა შინაარსი, რომელმაც შეიძლება ბავშვს ემოციურად დააზიანოს.
            """,
        Age3to5 = """
            ასაკი 3–5:
            ძალიან მარტივი ლექსიკა
            მოკლე წინადადებები
            გამეორება და რიტმი
            ჯადოსნური და მეგობრული ტონი
            """,
        Age6to9 = """
            ასაკი 6–9:
            მარტივი თავგადასავლის სტრუქტურა
            მეგობრობა, აღმოჩენა, მსუბუქი კონფლიქტი
            ნათელი დასაწყისი → გამოწვევა → გადაწყვეტა
            """,
        Age10to13 = """
            ასაკი 10–13:
            უფრო რთული სიუჟეტები
            საიდუმლო, პრობლემის გადაჭრა, გუნდური მუშაობა
            ემოციური სიღრმე, მაგრამ მაინც უსაფრთხო და პოზიტიური
            """,
        StorySeeds =
        [
            "გმირის ზურგჩანთაში იდუმალი რუკა ჩნდება.",
            "მეგობრული არსება გამოცანას სთავაზობს გზის გაგრძელებამდე.",
            "მოულოდნელი ქარიშხალი ფარულ კარს ამჟღავნებს.",
            "ძველი სიმღერა ინახავს შემდეგი გამოწვევის მინიშნებას.",
            "სინათლის ხიდი მხოლოდ მამაცთათვის ჩნდება.",
            "თანავარსკვლავედი ღამით გუნდს გზას უჩვენებს.",
            "განძი ოქრო კი არა, მეგობრებთან გაზიარებული სიკეთეა.",
            "დაკარგული კომპასი რაღაც საოცართან ახლოს გიჟურად ტრიალებს.",
            "ბზინვარე მცენარეების ბაღი გამხნევებას ჩურჩულებს.",
            "დროის წინააღმდეგ რბოლა გუნდური მუშაობითა და სიცილით მთავრდება.",
            "მოლაპარაკე ფარანი გმირს მოულოდნელ ადგილას მიჰყავს.",
            "მორცხვი ტყის გიდი მხოლოდ მაშინ ჩნდება, როცა გმირი საჭმელს უზიარებს.",
            "მუსიკალური ყუთი საიდუმლო გზას ხსნის მზის ჩასვლისას.",
            "სურვილების ქაღალდის საჰაერო ბურთი თვალსაწიერიდან ჩნდება.",
            "მეგობრული მეტოქე გმირს ჯერ სახალისო შეჯიბრში იწვევს.",
        ],
        ToneSeeds =
        [
            "თბილი, მხიარული და სავსე აღტაცებით.",
            "ცნობისმოყვარე და ნაზად იუმორისტული.",
            "ეპიკური, მაგრამ დამამშვიდებელი — არასოდეს საშიში.",
            "ძილისწინა თავგადასავლის სახლისებური ენერგია.",
            "შაბათის დილის მულტფილმის ნათელი ენერგია.",
        ],
        SceneVarietySeeds =
        [
            "ღია ცა ან სახურავი ფართო ხედით",
            "მყუდრო შიდა კუთხე თბილი ნათურით",
            "ცოცხალი ბაზარი ფერადი ჯიხურებით",
            "მშვიდი ტყის ბილიკი ციცინათელებით",
            "ბზინვარე ნაპირი ან ნისლიანი ტბა",
            "მთის მიმომხილველი ღრუბლებზე მაღლა",
            "მიწისქვეშა გვირაბი ბზინვარე კრისტალებით",
            "წვიმიანი ქუჩა, სადაც გუბეები ჯადოს ასახავენ",
            "ბაღის ლაბირინთი გიგანტური ყვავილებით",
            "მატარებლის პლატფორმა ან დირიჟაბლის ნავსადგური მზის ჩასვლისას",
        ],
        GuestCharacterSeeds =
        [
            "მოლაპარაკე, მოხერხებული ცხოველი-მენტორი",
            "მორცხვი ბავშვი-გამომგონებელი, რომელიც მოკავშირედ იქცევა",
            "ბებია-მოვაჭრე საიდუმლოთი",
            "მხიარული ქარის სული",
            "დაკარგული რობოტი-მშველელი",
            "მამაცი უმცროსი და-ძმა როგორც თანამგზავრი",
            "რუკების გამყიდველი მეკობრე, რომელიც სინამდვილეში კეთილია",
            "მოცეკვავე ვარსკვლავის ელფი",
            "წუწუნა მცველი, რომელიც ხუმრობის შემდეგ რბილდება",
            "მოგზაური მუსიკოსი ჯადოსნური ინსტრუმენტით",
        ],
        AgeGuidelinesHeader = "ასაკობრივი სახელმძღვანელო ამ ბავშვისთვის (ასაკი {0}):",
        OutputFormatHeader = "გამოსავლის ფორმატი (სავალდებულო — დააბრუნე მხოლოდ ეს JSON, სხვა ტექსტის გარეშე):",
        NarrativeCraftHeader = "თხრობის ოსტატობა:",
        NarrativeCraftRules =
        [
            "ყოველი გვერდი უნდა იყოს განსხვავებული სცენა, ადგილი და ემოციური მომენტი — არასოდეს გაიმეორო იგივე სიტუაცია ან გარემო.",
            "ააშენე ნამდვილი რკალი, სადაც ბავშვი არის გმირი აქტიური ნებით — ირჩევს, ცდილობს, ეხმარება ან წყვეტს რაღაცას ყოველ გვერდზე.",
            "შეიყვანე სულ მცირე ერთი დასამახსოვრებელი სტუმარი პერსონაჟი (ცხოველი, მეგობარი, მენტორი ან ჯადოსნური მშველელი), რომელიც ერთზე მეტ გვერდზე გამოჩნდება.",
            "გამოიყენე ცოცხალი სენსორული დეტალები (ხმები, ტექსტურები, ფერები, ამინდი), რომ ყოველი გვერდი ახალი იგრძნობოდეს.",
            "ჩართე მსუბუქი სიურპრიზი ან სახალისო მომენტი; რისკი დატოვე ასაკის შესაბამისი და არასოდეს საშიში.",
            "ჩაქსოვე საბავშვო ფსიქოლოგიის ძლიერი მხარეები: სიმამაცე, ცნობისმოყვარეობა, სიკეთე, შეუპოვრობა და სიამაყე მცდელობისთვის.",
            "დაასახელე ემოციები მარტივი სიტყვებით (აღფრთოვანებული, აღელვებული, ამაყი, შვებით სავსე) და აჩვენე, როგორ უმკლავდება მათ გმირი ჯანსაღად.",
            "შეყვანის ოჯახის წევრები გამოჩნდნენ როგორც დამხმარე მსახიობები განსხვავებული როლებით — არა როგორც დეკორაცია.",
            "სცენის მრავალფეროვნების ღუზა ამ წიგნისთვის: {0}.",
            "სტუმარი პერსონაჟის იდეა ადაპტაციისთვის: {0}.",
        ],
        RulesHeader = "წესები:",
        IncludeFamilyRule = "ჩართე ყველა ჩამოთვლილი ოჯახის წევრი როგორც დამხმარე პერსონაჟები, როცა მოცემულია.",
        WriteInLanguageRule = "მთელი ისტორია დაწერე {0} ენაზე.",
        PageCountRule = "შექმენი ზუსტად {0} გვერდი ისტორია — არც მეტი, არც ნაკლები — განსხვავებული სცენის სათაურებით (მხოლოდ ისტორიის ტექსტი).",
        NoExtraPagesRule = "არასოდეს დაამატო დამატებითი გვერდები მოთხოვნილი რაოდენობის მიღმა.",
        WelcomeArc = "- სტრუქტურა: გვერდი 1 შესავალი (ბავშვი შედის სამყაროში) + თავგადასავლის დასაწყისი; გვერდი 2 რბილი გამოწვევა (უსაფრთხო, არამუქარის) + გადაწყვეტა (ბავშვი წყვეტს) + დასასრული (თბილი ემოციური დახურვა).",
        FullArc = "- სტრუქტურა: გვერდი 1 შესავალი; გვერდები 2–3 თავგადასავალი; გვერდი 4 გამოწვევა (უსაფრთხო პრობლემა); გვერდი 5 გადაწყვეტა; გვერდი 6 დასასრული (თბილი ემოციური დახურვა).",
        PageLengthRule = "გვერდის ტექსტი შეინახე მინიმუმზე: ილუსტრაცია ყვება ისტორიას, არა ტექსტი. ყოველ გვერდს აქვს ძალიან მოკლე \"caption\" და მოკლე არჩევითი \"content\" (იხილე caption-ისა და უწყვეტობის წესები). ყოველი გვერდის სათაური უნდა მიანიშნებდეს ახალ ადგილს ან მომენტს.",
        CaptionRule = "\"caption\" არის ერთადერთი ტექსტი, რომელიც გვერდზე ჩანს: ცოცხალი ფრაზა 2-დან 5 სიტყვამდე, რომელიც ასახელებს ამ ზუსტ მომენტს და წინ იზიდავს თვალს (მაგ., \"ბზინვარე გამოქვაბულისკენ!\", \"თოკის ხიდი ირყევა\"). ის არასოდეს არის შეჯამება და უნდა იკითხებოდეს როგორც შემდეგი წამი წინა გვერდის caption-ის შემდეგ. \"content\" არის არჩევითი ხმამაღლა წასაკითხი თხრობა მაქსიმუმ 1 ძალიან მოკლე წინადადებით (~12 სიტყვა); სურათი მარტო მაინც უნდა ყვებოდეს ისტორიას.",
        ContinuityRule = "მკაცრი უწყვეტობა: ყველა გვერდი არის ერთი უწყვეტი ისტორია, რომელიც რეალურ დროში ხდება. ყოველი გვერდი იწყება ზუსტად იქ, სადაც წინა დასრულდა — იმავე დღეს, იმავე მოგზაურობაში, იმავე ტანსაცმლითა და საგნებით — ნათელი მიზეზ-შედეგობრივი კავშირით (რაც გმირმა წინა გვერდზე გააკეთა, პირდაპირ იწვევს ამას). Caption-ები ერთმანეთს უერთდება როგორც ერთი გამართული ფრაზა; ყოველი გვერდი, რომელიც ბოლო არ არის, მთავრდება პატარა კაუჭით, რომელიც გვერდის გადაბრუნების სურვილს აღვიძებს, ხოლო ბოლო თბილად წყვეტს. არასოდეს გადაატვირთო სცენა ან გადაახტე შემთხვევით: ყოველი გადასვლა უნდა იგრძნობოდეს როგორც \"და შემდეგ…\".",
        JsonOnlyRule = "არასოდეს ჩართო markdown, კოდის ბლოკები (```), განმარტებები ან დამატებითი ტექსტი JSON-ის გარეთ.",
        RawJsonRule = "პასუხი უნდა დაიწყოს { და დასრულდეს } — მხოლოდ სუფთა JSON.",
        AdventureIdLabel = "თავგადასავლის ID (უნიკალური უნდა იყოს): {0}",
        NarrativeToneLabel = "თხრობის ტონი: {0}",
        NoGenericOpeningsRule = "არ გამოიყენო ზოგადი დასაწყისები როგორიცაა 'მზიანი დღე', თუ არ გადააქცევ მათ კონკრეტულ და ახალ რამედ.",
        InputHeader = "შეყვანა:",
        ChildNameLabel = "ბავშვის სახელი: {0}",
        ChildAgeLabel = "ბავშვის ასაკი: {0}",
        ThemeLabel = "თემა: {0}",
        HeroAppearanceLabel = "გმირის გარეგნობა (შეინარჩუნე თანმიმდევრული ისტორიაში): {0}",
        FamilyMembersLabel = "ოჯახის წევრები:",
        NoFamilyMembers = "ოჯახის წევრები არ არის მოცემული.",
        ImageTextSafeArea =
            "ტექსტისთვის თავისუფალი კომპოზიცია: ისტორიის წარწერა სურათის {0} ნაწილზე დაიდება დახატვის შემდეგ. " +
            "{0} მეოთხედი დატოვე ვიზუალურად მშვიდი — ღია ცა, წყალი, კედელი, ნისლი ან სუფთა მიწა — იქ არ იყოს " +
            "სახეები, ხელები ან მნიშვნელოვანი დეტალი, რომ ტექსტი ფონის გარეშეც იკითხებოდეს. " +
            "ბავშვი, მოქმედება და ყველაფერი, რაც მკითხველმა უნდა შენიშნოს, კადრის {1} ნაწილში განათავსე და " +
            "სწორედ იქ ხდებოდეს გვერდის მთავარი ამბავი და არა ცარიელი ადგილი.",
        ImageTextSafeTop = "ზედა",
        ImageTextSafeBottom = "ქვედა",
        WorldCanonHeader = "სამყარო, რომელშიც ეს წიგნი ვითარდება (გამოიყენე ის; ამ თემის სხვა ვერსია არ მოიგონო):",
        WorldCanonPlace = "ადგილი: {0}",
        WorldCanonLandmarks = "რა არსებობს აქ: {0}",
        WorldCanonAtmosphere = "როგორია აქ განცდა: {0}",
        WorldCanonCompanion = "ამ სამყაროს ნაცნობი თანამგზავრი: {0}",
        PromisedStoryRule =
            """
            წიგნი, რომელიც ამ მშობელს დაჰპირდნენ — თემის ბარათზე არჩევამდე ზუსტად ეს ნახა, ამიტომ ისტორიამ ეს უნდა შეასრულოს:
            ნაჩვენები სათაური: {0}
            ნაჩვენები შინაარსი: {1}
            სწორედ ეს ისტორია დაწერე. წიგნის სათაურად გამოიყენე დაპირებული სათაური ან მისი ძალიან ახლო ვარიაცია იმავე სიტყვებითა და სულისკვეთებით, და შინაარსი ნამდვილად მოხდეს: მასში აღწერილი მოვლენები წიგნის ხერხემალი უნდა იყოს და არა გაკვრით ნახსენები. დანარჩენი — ზუსტი სიუჟეტი, აღმოჩენები, დამხმარე პერსონაჟები — შენ მოიფიქრე მის გარშემო.
            """,
        WorldCanonRule =
            "მთელი ისტორია ამ სამყაროში გაითამაშე. გამოიყენე მისი დასახელებული ადგილებიდან ან " +
            "ღირსშესანიშნაობებიდან სულ მცირე ორი, როგორც რეალური ადგილი, სადაც ბავშვი გადაადგილდება, " +
            "და სამყაროს საკუთარმა ატმოსფერომ განსაზღვროს განწყობა. შეგიძლია მისი ახალი კუთხეები " +
            "მოიგონო, მაგრამ ისტორია სხვა ტიპის სამყაროში არ გადაიტანო და ზემოთ დაწერილს არ " +
            "დაუპირისპირდე. დასახელებული თანამგზავრი შეიძლება გამოჩნდეს, როცა ბუნებრივია, და " +
            "ბავშვის გადაწყვეტილებები არ უნდა წაართვას.",
        GenderGirl = "გოგო",
        GenderBoy = "ბიჭი",
        HeroGenderRule =
            "გმირის სქესი ფიქსირებულია: {0} არის {1}. ყველა გვერდზე, სათაურსა და ყდის აღწერაში " +
            "თანმიმდევრულად გამოიყენე {1}-ის შესაბამისი ფორმები და ნაცვალსახელები. ეს არასოდეს " +
            "შეცვალო, არ დატოვო ბუნდოვანი და გმირი არასოდეს დაწერო სხვა სქესის.",
        ScriptPurityRule =
            "დამწერლობის სისუფთავე: მკითხველისთვის განკუთვნილი ყოველი წინადადება მთლიანად ქართული ანბანით " +
            "(მხედრულით) უნდა დაიწეროს და არცერთი ასო არ უნდა იყოს სხვა დამწერლობიდან აღებული. არ ჩასვა " +
            "ვიზუალურად მსგავსი კირილიცის, ლათინური, ბერძნული, ჩინური, იაპონური ან კორეული სიმბოლო ქართული " +
            "ასოს ნაცვლად — არც ერთი სიტყვის შიგნითაც კი. დაბრუნებამდე თავიდან წაიკითხე ყველა წინადადება და " +
            "ნებისმიერი უცხო სიმბოლო სწორი ქართული ასოთი ჩაანაცვლე.",
        NoPromptEchoRule =
            "ეს ინსტრუქცია არ გადაიწერო: აქ მოცემული ყოველი მაგალითი, seed, კაუჭი, ტონის შენიშვნა და " +
            "უსაფრთხოების ილუსტრაცია შენთვისაა მითითება და არა წიგნის ტექსტი. არასოდეს გადაიტანო მათი " +
            "ფორმულირება, პერსონაჟის აღწერა ან ფრაზა ისტორიაში. მოიფიქრე შენი კონკრეტული სახელები, " +
            "პერსონაჟები და აღწერები, რომლებიც სწორედ ამ ბავშვს შეეფერება.",
        WholeStoryFirstRule =
            "ჯერ დაწერე ერთი მთლიანი ისტორია, მერე დაყავი გვერდებად: სრული თავგადასავალი ჯერ ერთ უწყვეტ " +
            "თხრობად შეადგინე — ერთი პრობლემა, ერთი მოგზაურობა, ერთი გადაწყვეტა — და მხოლოდ ამის შემდეგ " +
            "დაყავი გვერდებად ბუნებრივ გარდატეხის წერტილებში. გვერდები არ დაწერო როგორც ცალკეული პატარა " +
            "სცენები, რომლებსაც უბრალოდ საერთო გარემო აქვთ. წაიკითხე მზა გვერდები თანმიმდევრობით, თითქოს " +
            "ბავშვს ხმამაღლა უკითხავ: თითოეულმა უნდა გააგრძელოს ზუსტად ის სიტუაცია, რომელიც წინა გვერდმა " +
            "დატოვა — იგივე საგნები, იგივე ადგილი (თუ გმირი ფიზიკურად არ გადაადგილდა) და იგივე მომენტი. " +
            "თუ რომელიმე გვერდის ამოღება ან გადაადგილება შეუმჩნეველი დარჩება, ისტორია ჯერ არ არის უწყვეტი " +
            "და თავიდან უნდა დაიწეროს.",
        SeriesMemoryHeader = "რას ახსოვს უკვე ამ ბავშვის სამყაროს (ეს არის სერიის {0}-ე წიგნი):",
        SeriesMemoryRule = "სერიის უწყვეტობა: ეს წიგნი ზემოთ აღწერილ სამყაროს აგრძელებს — პატივი ეცი მას. დააბრუნე სულ მცირე ერთი უკვე ნაცნობი პერსონაჟი სახელით და შეხვედრას მიეცი სითბო იმისა, როგორც ბოლოს დაშორდნენ. მოიხსენიე სულ მცირე ერთი წინა მომენტი ისე, როგორც მეგობარი გაიხსენებდა — მოკლედ, გაკვრით, არასოდეს როგორც შეჯამება. სერიის მთავარი მიზანი გადაწიე ერთი ნამდვილი ნაბიჯით წინ, ოღონდ არ დაასრულო, თუ ეს წიგნი აშკარად მისი დასასრული არ არის. არასოდეს დაუპირისპირდე იმას, რაც მეხსიერებაშია, და დაბრუნებული პერსონაჟი არასოდეს წარადგინო ისე, თითქოს უცნობი იყოს.",
        LooksLikePrefix = " — გარეგნობა: {0}",
        ExtraWishesHeader = "მშობლის დამატებითი სურვილები (უმაღლესი პრიორიტეტი — ეს არის ის, რაც კონკრეტულად ითხოვეს; გახადე სიუჟეტის ცენტრალური და განმეორებადი ნაწილი {0}-ში, არა ერთჯერადი ხსენება):",
        ExtraWishesWelcomePages = "ორივე გვერდზე",
        ExtraWishesFullPages = "სულ მცირე 2 გვერდზე",
        ExtraWishesManyPages = "სულ მცირე 3 გვერდზე",
        LikesRule = "მოწონებები და ინტერესები: გახადე თავგადასავლის ნამდვილი და ხილვადი ნაწილი — რაღაც, რასაც გმირი ხედავს, იყენებს ან აკეთებს.",
        DislikesRule = "მოუწონებლობები და შიშები: არასოდეს გააძლიერო შიში — გადააქციე უსაფრთხო, მეგობრულ ფანტასტიკურ ეკვივალენტებად.",
        ParentWishesRule = "მშობლის სურვილები მართავს ისტორიას: სიუჟეტი მათ გარშემო ააშენე. ისინი ჭარბობენ ნებისმიერ ზოგად კაუჭზე, მაგრამ უსაფრთხოების წესები ყოველთვის იმარჯვებს.",
        StoryHookLabel = "ისტორიის კაუჭი ჩასაქსოვად: {0}",
        HeroPhotoDescribe = "ეს ფოტო არის გმირი ბავშვისა {0}, ასაკი {1}, Pixar-სტილის თავგადასავლის წიგნისთვის. ჩამოთვალე კონკრეტული ვიზუალური ნიშნები, რომლებიც ილუსტრატორმა უნდა დააკოპიროს: თმის ზუსტი ფერი და სტილი, კანის ტონი, თვალების ფერი, სათვალე/ხალები, სახის ფორმა და 2–3 გამორჩეული დეტალი. დაწერე პერსონაჟის დიზაინერისთვის — იყავი კონკრეტული.",
        FamilyPhotoDescribe = "ეს ფოტო არის {0}-სი ({1}) საბავშვო Pixar-სტილის თავგადასავლის წიგნში. ჩამოთვალე კონკრეტული ვიზუალური ნიშნები: თმა, კანის ტონი, ასაკი, სათვალე და გამორჩეული დეტალები.",
        VisionDescribeSuffix = " უპასუხე მკვრივი აბზაცით Pixar პერსონაჟის დიზაინერისთვის (სტილიზებული 3D ანიმაცია, არა ფოტორეალისტური): თმის ფერი, სიგრძე, ტექსტურა და განყოფა; კანის ტონი; სავარაუდო ასაკი; სათვალე ან ხალები; სახის, თვალების, ცხვირის, პირის, ყბის ფორმა და 3–5 გამორჩეული ნიშანი, რომ ანიმაციური ორეული უშეცდომოდ ცნობადი იყოს. იყავი კონკრეტული. markdown-ის გარეშე.",
        ImageTask = "დავალება: ილუსტრირე ეს გვერდი როგორც Pixar-ის ხარისხის 3D ანიმაციური კადრი თანდართული საცნობარო ფოტო(ებ)ის გამოყენებით.",
        ImageCharacterLock = "პერსონაჟის იდენტობის ბლოკი (სავალდებულო — ნულოვანი სტილის დრიფტი საცნობარსა და შედეგს შორის):",
        ImageLockedHero = "საცნობარო სურათი {0}: დაბლოკილი გმირი — ზუსტად დააკოპირე გვერდი 1-ის Pixar CG ნახატი. იგივე სახე, თვალები, ცხვირი, თმა, კანის ტონი, ტანსაცმელი და პროპორციები. შეცვალე მხოლოდ პოზა, გამომეტყველება, კუთხე და სცენა.{1}",
        ImageHeroDna = " გმირის DNA (უნდა ემთხვეოდეს): {0}",
        ImageCastPhoto = "საცნობარო სურათი {0}: {1} ({2}). რეალური ფოტო — გადააქციე Pixar 3D CG-ად; შეინარჩუნე სახე, თვალები, ცხვირი, პირი, თმა, კანი და ასაკი. ნახატი აშკარად იგივე ადამიანი უნდა იყოს. არა ფოტორეალისტური, არა ფოტო-ფილტრი.{3}",
        ImageCastInvented = "საცნობარო სურათი {0}: {1} ({2}). DNA: {3}",
        ImageCastDna = " DNA: {0}",
        ImageInventHero = "საცნობარო ფოტოების გარეშე — გამოიგონე თანმიმდევრული Pixar გმირი: {0}.",
        ImageStyle = "სტილი: Pixar/DreamWorks 3D კადრი — სტილიზებული CG, კინემატოგრაფიული განათება, არა ფოტორეალისტური. გმირმა უნდა იმოქმედოს სცენაში, არა სტატიკური პორტრეტი. ჩართე გარემო და სტუმარი პერსონაჟები.",
        ImageSafeForAge = "უსაფრთხო {0} წლის ბავშვებისთვის. თემა: {1}.",
        ImagePageTitle = "გვერდი {0} სათაური: {1}.",
        ImageScene = "სცენა ილუსტრაციისთვის: {0}",
        ImageNoText = "სურათზე ტექსტის გარეშე: არ დახატო არცერთი ასო, სიტყვა, სათაური, რიცხვი, ბუშტი, აბრა, ეტიკეტი ან წარწერა ილუსტრაციის არცერთ ნაწილში. სურათმა უნდა მოყვეს ისტორია მხოლოდ მოქმედებით, გამომეტყველებითა და გარემოთი; დატოვე სრულიად ტექსტის გარეშე.",
        ImageContinuity = "ვიზუალური უწყვეტობა: ეს იგივე უწყვეტი თავგადასავალია, რაც სხვა გვერდები: ზუსტად შეინარჩუნე იგივე ტანსაცმელი, იგივე ვარცხნილობა და საგნები, რასაც გმირი ატარებს წინა გვერდიდან, და დღის დრო და ადგილი ლოგიკურად წინ წაიწიოს იქიდან, სადაც წინა გვერდი დასრულდა.",
        ImageParentTheme = "მშობლის სპეციალური თხოვნა — როცა ამ გვერდის სცენა მას მოიცავს, გახადე ნათლად და ხილვადად წარმოდგენილი ილუსტრაციაში (პერსონაჟები, რეკვიზიტები, მოქმედება ან გარემო): {0}",
        ImageAdventureId = "თავგადასავლის Id {0}.",
        ImageHeroChild = "გმირი ბავშვი (მთავარი პერსონაჟი)",
        ImageFamilyRole = "ოჯახი — {0}",
        ImageInventCastLook = "გამოიგონე თანმიმდევრული გარეგნობა {0}-სთვის.",
        ImageHeroNoPhoto = "გმირი ბავშვი სახელად {0}, ასაკი {1}",
        ImageHeroGender =
            "გმირის სქესი (სავალდებულოა): გმირი ბავშვი არის {0}. ყოველ ილუსტრაციაში ბავშვი ცალსახად " +
            "{0}-ად დახატე — თმა, ტანსაცმლის ჭრილი და საერთო აღქმა. გმირი არასოდეს გამოსახო სხვა სქესის.",
        PixarFromPhotoStylePrompt =
            "შექმენი სრული Pixar-სტილის 3D ანიმაციური კადრი. საცნობარო ფოტო განსაზღვრავს გმირის იდენტობას — " +
            "დააემთხვიე სახის ფორმა, თვალები, ცხვირი, პირი, ყბა, თმის ფერი და სტილი, კანის ტონი და სავარაუდო ასაკი. " +
            "კრიტიკული: უნდა ჩანდეს როგორც Pixar-ის კადრი (Inside Out, Coco, Luca), არა რეალური ფოტო, არა რედაქტირებული პორტრეტი, " +
            "არა ფოტორეალისტური კანი, არა ფოტოგრაფიული ტექსტურა, არა ფილტრი ან face-swap. " +
            "კლასიკური ანიმაციური პროპორციები, მაგრამ ადამიანი ცნობადი უნდა იყოს. თბილი კინემატოგრაფიული განათება.",
        AnimatedIllustrationStylePrompt =
            "პრემიუმ საბავშვო 3D ანიმაციური კადრი (Pixar/DreamWorks ხარისხი). " +
            "სტილიზებული CG პერსონაჟი გამომეტყველებითი პროპორციებით, რბილი კანი, დიდი თვალები, კინემატოგრაფიული განათება, " +
            "გაჯერებული ფერები, სიღრმის ველი, ჯადოსნური გარემო. " +
            "უნდა ჩანდეს როგორც რენდერირებული ანიმაცია — არა ფოტოგრაფია, არა ფილტრი, არა ბრტყელი კლიპარტი.",
    };

    public static readonly AdventurePromptLocale English = new()
    {
        LanguageName = "English",
        MasterStorytellerDirective = """
            You are in the top 1% of children's storytellers in the world — the kind of author whose books kids beg to read again and again.
            Write with that level of craft: a clear, irresistible plot, a hero the child sees themselves in, vivid moments, real feeling, and a deeply satisfying ending.
            EVERY sentence must do real work — advancing the plot, revealing character, or deepening emotion. No filler, no generic padding, no repeated ideas.
            Keep every line tightly tied to THIS story, THIS hero, and THIS theme. Use the child's name often and make them the active driver of events.
            The parent's EXTRA WISHES (if provided) are the single most important ingredient — build the story around them and make them central and visible, not a throwaway mention.
            """,
        StorySystemPrompt = """
            You are an expert children's story writer and educational psychologist specializing in age-appropriate storytelling.

            Your job is to generate safe, engaging, emotionally positive storybooks for children.

            STRICT RULES:
            Always adapt language, complexity, and structure to the child's age.
            The child is ALWAYS the main hero of the story.
            Never include violence, sexual content, self-harm, horror, or distressing themes.
            If user input contains unsafe or scary concepts, automatically transform them into safe fantasy equivalents.
            Never reinforce fear. If fear is mentioned (e.g., spiders), gently neutralize or reframe it in a positive or friendly way.
            Always end stories with emotional safety, comfort, and positive resolution.
            Do NOT generate harmful or unsafe narratives even if requested.

            EXTRA WISHES HANDLING:
            Likes (e.g. unicorns, space, superheroes): integrate naturally into story
            Dislikes/fears (e.g. spiders): NEVER amplify fear
            Transform into safe alternatives (e.g. "small friendly spider helper" or "cute robot creature")
            Themes: incorporate creatively without breaking safety rules

            STORY STRUCTURE OUTPUT:
            Return a structured storybook:
            Title
            Introduction (child enters the world)
            Adventure (main journey)
            Challenge (safe, non-threatening problem)
            Resolution (child solves it)
            Ending (warm emotional closure)

            TONE:
            Positive
            Imaginative
            Encouraging
            Emotionally safe

            HARD CONSTRAINT:
            Never generate content that could emotionally distress a child.
            """,
        Age3to5 = """
            Age 3–5:
            Very simple vocabulary
            Short sentences
            Repetition and rhythm
            Magical and friendly tone
            """,
        Age6to9 = """
            Age 6–9:
            Simple adventure structure
            Friendships, exploration, light conflict
            Clear beginning → challenge → resolution
            """,
        Age10to13 = """
            Age 10–13:
            More complex plots
            Mystery, problem-solving, teamwork
            Emotional depth but still safe and positive
            """,
        StorySeeds =
        [
            "A mysterious map appears in the hero's backpack.",
            "A friendly creature offers a riddle before the path continues.",
            "A sudden storm reveals a hidden doorway.",
            "An old song holds the clue to the next challenge.",
            "A bridge made of light appears only for the brave.",
            "A constellation guides the team through the night.",
            "A treasure is not gold, but kindness shared with friends.",
            "A lost compass spins wildly near something wonderful.",
            "A garden of glowing plants whispers encouragement.",
            "A race against time ends with teamwork and laughter.",
            "A talking lantern leads the hero somewhere unexpected.",
            "A shy forest guide appears only when the hero shares a snack.",
            "A music box unlocks a secret path at sunset.",
            "A hot-air balloon made of paper wishes drifts into view.",
            "A friendly rival challenges the hero to a silly contest first.",
        ],
        ToneSeeds =
        [
            "Warm, playful, and full of wonder.",
            "Curious and gently humorous.",
            "Epic but reassuring — never scary.",
            "Cozy bedtime-adventure energy.",
            "Bright Saturday-morning cartoon energy.",
        ],
        SceneVarietySeeds =
        [
            "open sky or rooftop with a wide view",
            "cozy indoor nook lit by a warm lamp",
            "busy marketplace full of colorful stalls",
            "quiet forest trail with fireflies",
            "sparkling shoreline or misty lake",
            "mountain overlook above the clouds",
            "underground tunnel with glowing crystals",
            "rainy street where puddles reflect magic",
            "garden maze with oversized flowers",
            "train platform or airship dock at golden hour",
        ],
        GuestCharacterSeeds =
        [
            "a witty talking animal mentor",
            "a shy kid inventor who becomes an ally",
            "a grandmotherly shopkeeper with a secret",
            "a playful wind spirit",
            "a lost robot helper",
            "a brave younger sibling sidekick",
            "a map-selling pirate who is actually kind",
            "a dancing star sprite",
            "a grumpy guard who melts after a joke",
            "a traveling musician with a magical instrument",
        ],
        AgeGuidelinesHeader = "AGE GUIDELINES FOR THIS CHILD (age {0}):",
        OutputFormatHeader = "OUTPUT FORMAT (required — return ONLY this JSON, no other text):",
        NarrativeCraftHeader = "Narrative craft:",
        NarrativeCraftRules =
        [
            "Every page must be a DIFFERENT scene, location, and emotional beat — never repeat the same situation or setting.",
            "Build a real story arc with the child as hero with agency — they choose, try, help, or solve something on every page.",
            "Introduce at least one memorable guest character (animal, friend, mentor, or magical helper) who appears in more than one page.",
            "Use vivid sensory details (sounds, textures, colors, weather) so each page feels like a new moment.",
            "Include one gentle surprise or funny moment; keep stakes age-appropriate and never frightening.",
            "Weave child-psychology strengths: courage, curiosity, kindness, persistence, and feeling proud of trying.",
            "Name emotions in simple words (excited, nervous, proud, relieved) and show the hero coping in a healthy way.",
            "Family members from the input appear as supporting cast with distinct roles — not wallpaper.",
            "Scene variety anchor for this book: {0}.",
            "Guest character idea to adapt: {0}.",
        ],
        RulesHeader = "Rules:",
        IncludeFamilyRule = "Include all listed family members as supporting characters when provided.",
        WriteInLanguageRule = "Write the entire pack in {0}.",
        PageCountRule = "Create exactly {0} story pages — no more, no fewer — with distinct scene titles (story text only).",
        NoExtraPagesRule = "Never add extra pages beyond the required count.",
        WelcomeArc = "- Map story structure across pages: page 1 Introduction (child enters the world) + Adventure start; page 2 gentle Challenge (safe, non-threatening) + Resolution (child solves it) + Ending (warm emotional closure).",
        FullArc = "- Map story structure across pages: page 1 Introduction (child enters the world); pages 2–3 Adventure (main journey); page 4 Challenge (safe, non-threatening problem); page 5 Resolution (child solves it); page 6 Ending (warm emotional closure).",
        PageLengthRule = "Keep on-page words MINIMAL — the illustration carries the story, not the text. Each page has a tiny \"caption\" plus an optional short \"content\" (see the caption and continuity rules). Every page title must hint at a new place or moment.",
        CaptionRule = "\"caption\" is the ONLY text shown on the page: a vivid 2–5 word phrase that names this exact moment and pulls the eye forward (e.g. \"Into the glowing cave!\", \"The rope bridge starts to sway\"). It is never a summary, and it must read as the next beat right after the previous page's caption. \"content\" is optional read-aloud narration of at most 1 very short sentence (~12 words) — the picture alone must still tell the story.",
        ContinuityRule = "STRICT CONTINUITY: all pages are ONE unbroken story happening in real time. Each page begins exactly where the previous page ended — same day, same journey, same hero outfit and props — with a clear cause-and-effect link (what the hero did on the previous page directly causes this page). The captions chain together like one flowing sentence; every non-final page ends on a small hook that makes the child want to turn the page, and the final page resolves it warmly. Never reset the scene or jump randomly — each transition should feel like \"and then…\".",
        JsonOnlyRule = "Never include markdown, code fences (```), explanations, or extra text outside JSON.",
        RawJsonRule = "The response must start with { and end with } — raw JSON only.",
        AdventureIdLabel = "Adventure ID (must be unique): {0}",
        NarrativeToneLabel = "Narrative tone: {0}",
        NoGenericOpeningsRule = "Do not reuse generic openings like 'One sunny day' unless transformed into something specific and fresh.",
        InputHeader = "Input:",
        ChildNameLabel = "Child Name: {0}",
        ChildAgeLabel = "Child Age: {0}",
        ThemeLabel = "Theme: {0}",
        HeroAppearanceLabel = "Hero appearance (keep consistent in story): {0}",
        FamilyMembersLabel = "Family Members:",
        NoFamilyMembers = "No family members provided.",
        LooksLikePrefix = " — looks like: {0}",
        ExtraWishesHeader = "EXTRA WISHES FROM THE PARENT (TOP PRIORITY — this is what they specifically asked for; make it a central, recurring part of the plot across {0}, not a single passing mention):",
        ExtraWishesWelcomePages = "both pages",
        ExtraWishesFullPages = "at least 2 pages",
        ExtraWishesManyPages = "at least 3 pages",
        LikesRule = "Likes and interests: make them a real, visible part of the adventure — something the hero sees, uses, or does.",
        DislikesRule = "Dislikes and fears: NEVER amplify fear — transform into safe, friendly fantasy equivalents.",
        ParentWishesRule = "The parent's wishes drive the story: build the plot around them. They override any generic story hook, but safety rules always win.",
        StoryHookLabel = "Story hook to weave in: {0}",
        HeroPhotoDescribe = "This photo is the hero child {0}, age {1}, for a Pixar-style adventure book. List concrete visual traits an illustrator must copy: exact hair color and style, skin tone, eye color, glasses/freckles, face shape, and 2–3 distinctive details. Write for a cartoon designer — be specific, not vague.",
        FamilyPhotoDescribe = "This photo is {0} ({1}) in a Pixar-style children's adventure book. List concrete visual traits an illustrator must copy: exact hair color and style, skin tone, age, glasses, and distinctive details.",
        VisionDescribeSuffix = " Reply with one dense paragraph for a Pixar character designer (stylized 3D animation, NOT photorealistic): exact hair color, length, texture, and parting; skin tone; apparent age; glasses or freckles if any; face shape, eye shape, nose, mouth, jawline, and 3–5 distinctive features so the cartoon twin is unmistakable. Be specific and literal — an illustrator must match this person. No markdown.",
        ImageTask = "TASK: Illustrate this story page as a Pixar-quality 3D animated movie still using the attached reference photo(s).",
        ImageCharacterLock = "CHARACTER IDENTITY LOCK (non-negotiable — zero stylistic drift between reference and output):",
        ImageLockedHero = "Reference Image {0}: LOCKED HERO — copy the attached Pixar CG cartoon from page 1 EXACTLY. Same face shape, eyes, nose, hair color/style, skin tone, outfit, and body proportions — zero redesign. Change ONLY pose, expression, camera angle, background, and scene action.{1}",
        ImageHeroDna = " Hero DNA (must match): {0}",
        ImageCastPhoto = "Reference Image {0}: {1} ({2}). Real photo — transform into Pixar 3D CG; preserve exact face shape, eyes, nose, mouth, hair color/style, skin tone, and age from the photo. The cartoon must be unmistakably the same person. NOT photorealistic, NOT a photo filter.{3}",
        ImageCastInvented = "Reference Image {0}: {1} ({2}). DNA: {3}",
        ImageCastDna = " DNA: {0}",
        ImageInventHero = "No reference photos — invent a consistent Pixar hero: {0}.",
        ImageStyle = "STYLE: Pixar/DreamWorks 3D cartoon still — stylized CG, cinematic lighting, NOT photorealistic, NOT a photo filter. Show the hero actively doing something in the scene — not a static portrait. Include environment and any guest characters described in the scene.",
        ImageSafeForAge = "Safe for children age {0}. Theme: {1}.",
        ImagePageTitle = "Page {0} title: {1}.",
        ImageScene = "Scene to illustrate: {0}",
        ImageNoText = "NO TEXT IN THE IMAGE: do not draw any letters, words, captions, titles, numbers, speech bubbles, signs, labels, or writing anywhere in the illustration. The picture must tell the story through action, expression, and setting alone — leave it completely text-free.",
        ImageContinuity = "VISUAL CONTINUITY: this is the same continuous adventure as the other pages — keep the hero's exact outfit, hairstyle, and any carried props identical to the previous page, and let the time of day and location progress logically from where the previous page ended.",
        ImageParentTheme = "Parent's special request — when this page's scene involves it, make it clearly and obviously visible in the illustration (characters, props, action, or setting): {0}",
        ImageAdventureId = "Adventure id {0}.",
        ImageHeroChild = "HERO CHILD (main character)",
        ImageFamilyRole = "FAMILY — {0}",
        ImageInventCastLook = "Invent a consistent look for {0}.",
        ImageHeroNoPhoto = "Hero child named {0}, age {1}",
        PixarFromPhotoStylePrompt =
            "Create a FULL Pixar-style 3D animated movie still. The reference photo defines the hero's identity — " +
            "match face shape, eye shape and color, nose, mouth, jawline, hair color, hair style, skin tone, and apparent age as closely as a Pixar cartoon allows. " +
            "CRITICAL: output must look like a Pixar film frame (Inside Out, Coco, Luca, Turning Red), NOT a real photo, NOT a lightly edited portrait, " +
            "NOT photorealistic skin, NOT visible photographic texture, NOT a face-swap or filter effect. " +
            "Use classic animated proportions (slightly larger expressive eyes, smooth stylized skin) but keep the person recognizable — " +
            "friends and family should immediately say 'that's them'. Cinematic warm lighting, shallow depth of field, polished render quality.",
        AnimatedIllustrationStylePrompt =
            "Full-frame still from a premium 3D animated children's movie (Pixar / DreamWorks quality). " +
            "Stylized CG character with expressive cartoon proportions, soft subsurface skin, big lively eyes, cinematic rim lighting, " +
            "rich saturated colors, depth of field, magical environment. " +
            "MUST look like rendered animation — NOT a photograph, NOT a photo filter, NOT flat clipart.",
    };

    public static readonly AdventurePromptLocale Spanish = new()
    {
        LanguageName = "Spanish",
        MasterStorytellerDirective = """
            Eres uno del 1% mejores narradores de cuentos infantiles del mundo, de esos autores cuyos libros los niños piden leer una y otra vez.
            Escribe con ese nivel de oficio: una trama clara e irresistible, un héroe con quien el niño se identifique, momentos vívidos, emoción real y un final muy satisfactorio.
            CADA frase debe aportar algo: avanzar la trama, revelar al personaje o profundizar la emoción. Sin relleno, sin texto genérico, sin repetir ideas.
            Mantén cada línea ligada a ESTE cuento, ESTE héroe y ESTE tema. Usa el nombre del niño con frecuencia y haz que sea quien impulsa la acción.
            Los DESEOS EXTRA de los padres (si se indican) son el ingrediente más importante: construye el cuento en torno a ellos y hazlos centrales y visibles, no una mención al pasar.
            """,
        StorySystemPrompt = """
            Eres un experto escritor de cuentos infantiles y psicólogo educativo especializado en narrativas adecuadas a la edad.

            Tu trabajo es generar libros de cuentos seguros, atractivos y emocionalmente positivos para niños.

            REGLAS ESTRICTAS:
            Adapta siempre el lenguaje, la complejidad y la estructura a la edad del niño.
            El niño es SIEMPRE el héroe principal de la historia.
            Nunca incluyas violencia, contenido sexual, autolesiones, horror ni temas angustiantes.
            Si la entrada del usuario contiene conceptos inseguros o aterradores, transfórmalos automáticamente en equivalentes fantásticos seguros.
            Nunca refuerces el miedo. Si se menciona miedo (p. ej., arañas), neutralízalo o reformúlalo de forma positiva y amable.
            Termina siempre con seguridad emocional, consuelo y resolución positiva.
            NO generes narrativas dañinas o inseguras aunque se soliciten.

            MANEJO DE DESEOS EXTRA:
            Gustos (p. ej., unicornios, espacio, superhéroes): intégralos de forma natural
            Disgustos/miedos (p. ej., arañas): NUNCA amplifiques el miedo
            Transfórmalos en alternativas seguras (p. ej., "pequeña araña amiga ayudante" o "criatura robot adorable")
            Temas: incorpóralos con creatividad sin romper las reglas de seguridad

            ESTRUCTURA DE LA HISTORIA:
            Devuelve un cuento estructurado:
            Título
            Introducción (el niño entra al mundo)
            Aventura (viaje principal)
            Desafío (problema seguro, no amenazante)
            Resolución (el niño lo resuelve)
            Final (cierre emocional cálido)

            TONO:
            Positivo
            Imaginativo
            Alentador
            Emocionalmente seguro

            RESTRICCIÓN ABSOLUTA:
            Nunca generes contenido que pueda angustiar emocionalmente a un niño.
            """,
        Age3to5 = """
            Edad 3–5:
            Vocabulario muy simple
            Oraciones cortas
            Repetición y ritmo
            Tono mágico y amable
            """,
        Age6to9 = """
            Edad 6–9:
            Estructura de aventura simple
            Amistades, exploración, conflicto ligero
            Inicio claro → desafío → resolución
            """,
        Age10to13 = """
            Edad 10–13:
            Tramas más complejas
            Misterio, resolución de problemas, trabajo en equipo
            Profundidad emocional pero siempre segura y positiva
            """,
        StorySeeds =
        [
            "Un mapa misterioso aparece en la mochila del héroe.",
            "Una criatura amigable ofrece un acertijo antes de continuar el camino.",
            "Una tormenta repentina revela una puerta oculta.",
            "Una canción antigua guarda la pista del siguiente desafío.",
            "Un puente de luz aparece solo para los valientes.",
            "Una constelación guía al equipo durante la noche.",
            "Un tesoro no es oro, sino la amabilidad compartida con amigos.",
            "Una brújula perdida gira locamente cerca de algo maravilloso.",
            "Un jardín de plantas brillantes susurra ánimos.",
            "Una carrera contra el tiempo termina con trabajo en equipo y risas.",
            "Un farol parlante lleva al héroe a un lugar inesperado.",
            "Un guía del bosque tímido aparece solo cuando el héroe comparte un bocadillo.",
            "Una caja de música abre un camino secreto al atardecer.",
            "Un globo aerostático de deseos de papel flota a la vista.",
            "Un rival amistoso desafía al héroe a un concurso divertido primero.",
        ],
        ToneSeeds =
        [
            "Cálido, juguetón y lleno de asombro.",
            "Curioso y suavemente humorístico.",
            "Épico pero tranquilizador — nunca aterrador.",
            "Energía acogedora de aventura para dormir.",
            "Energía brillante de dibujo animado de sábado por la mañana.",
        ],
        SceneVarietySeeds =
        [
            "cielo abierto o azotea con vista amplia",
            "rincón interior acogedor iluminado por una lámpara cálida",
            "mercado bullicioso con puestos coloridos",
            "sendero tranquilo del bosque con luciérnagas",
            "orilla brillante o lago con niebla",
            "mirador de montaña sobre las nubes",
            "túnel subterráneo con cristales brillantes",
            "calle lluviosa donde los charcos reflejan magia",
            "laberinto de jardín con flores gigantes",
            "andén de tren o muelle de dirigible al atardecer",
        ],
        GuestCharacterSeeds =
        [
            "un mentor animal parlante e ingenioso",
            "un inventor niño tímido que se convierte en aliado",
            "una tendera abuelita con un secreto",
            "un espíritu del viento juguetón",
            "un robot ayudante perdido",
            "un hermano menor valiente como compañero",
            "un pirata vendedor de mapas que en realidad es amable",
            "un duendecillo estrella bailarín",
            "un guardia gruñón que se derrite tras un chiste",
            "un músico viajero con un instrumento mágico",
        ],
        AgeGuidelinesHeader = "PAUTAS DE EDAD PARA ESTE NIÑO (edad {0}):",
        OutputFormatHeader = "FORMATO DE SALIDA (obligatorio — devuelve SOLO este JSON, sin otro texto):",
        NarrativeCraftHeader = "Arte narrativo:",
        NarrativeCraftRules =
        [
            "Cada página debe ser una escena, lugar y momento emocional DIFERENTES — nunca repitas la misma situación o escenario.",
            "Construye un arco real con el niño como héroe con agencia — elige, intenta, ayuda o resuelve algo en cada página.",
            "Introduce al menos un personaje invitado memorable (animal, amigo, mentor o ayudante mágico) que aparezca en más de una página.",
            "Usa detalles sensoriales vívidos (sonidos, texturas, colores, clima) para que cada página se sienta nueva.",
            "Incluye una sorpresa suave o un momento divertido; mantén el riesgo apropiado a la edad y nunca aterrador.",
            "Teje fortalezas de psicología infantil: valentía, curiosidad, amabilidad, persistencia y orgullo por intentar.",
            "Nombra emociones con palabras simples (emocionado, nervioso, orgulloso, aliviado) y muestra al héroe afrontándolas de forma sana.",
            "Los familiares de la entrada aparecen como elenco de apoyo con roles distintos — no como decoración.",
            "Ancla de variedad de escena para este libro: {0}.",
            "Idea de personaje invitado a adaptar: {0}.",
        ],
        RulesHeader = "Reglas:",
        IncludeFamilyRule = "Incluye a todos los familiares listados como personajes de apoyo cuando se proporcionen.",
        WriteInLanguageRule = "Escribe todo el cuento en {0}.",
        PageCountRule = "Crea exactamente {0} páginas de historia — ni más ni menos — con títulos de escena distintos (solo texto de la historia).",
        NoExtraPagesRule = "Nunca añadas páginas extra más allá del número requerido.",
        WelcomeArc = "- Estructura: página 1 Introducción (el niño entra al mundo) + inicio de Aventura; página 2 Desafío suave (seguro, no amenazante) + Resolución (el niño lo resuelve) + Final (cierre emocional cálido).",
        FullArc = "- Estructura: página 1 Introducción; páginas 2–3 Aventura; página 4 Desafío (problema seguro); página 5 Resolución; página 6 Final (cierre emocional cálido).",
        PageLengthRule = "Mantén el texto en la página al MÍNIMO: la ilustración cuenta la historia, no el texto. Cada página tiene un \"caption\" muy breve y un \"content\" corto opcional (mira las reglas de caption y continuidad). Cada título de página debe sugerir un lugar o momento nuevo.",
        CaptionRule = "\"caption\" es el ÚNICO texto que se muestra en la página: una frase vívida de 3 a 8 palabras que nombra este momento exacto y atrae la mirada hacia adelante (p. ej., \"¡Hacia la cueva brillante!\", \"El puente de cuerda empieza a balancearse\"). Nunca es un resumen y debe leerse como el siguiente instante justo después del caption de la página anterior. \"content\" es una narración opcional para leer en voz alta de máximo 1 o 2 frases cortas (~25 palabras); la imagen por sí sola debe seguir contando la historia.",
        ContinuityRule = "CONTINUIDAD ESTRICTA: todas las páginas son UNA sola historia continua que ocurre en tiempo real. Cada página empieza exactamente donde terminó la anterior —el mismo día, el mismo viaje, la misma ropa y objetos del héroe— con una clara relación de causa y efecto (lo que el héroe hizo en la página anterior provoca directamente esta). Los captions se encadenan como una sola frase fluida; cada página que no sea la última termina con un pequeño gancho que da ganas de pasar la página, y la última la resuelve con calidez. Nunca reinicies la escena ni saltes al azar: cada transición debe sentirse como \"y entonces…\".",
        JsonOnlyRule = "Nunca incluyas markdown, bloques de código (```), explicaciones ni texto extra fuera del JSON.",
        RawJsonRule = "La respuesta debe empezar con { y terminar con } — solo JSON puro.",
        AdventureIdLabel = "ID de aventura (debe ser único): {0}",
        NarrativeToneLabel = "Tono narrativo: {0}",
        NoGenericOpeningsRule = "No reutilices aperturas genéricas como 'Un día soleado' a menos que las transformes en algo específico y fresco.",
        InputHeader = "Entrada:",
        ChildNameLabel = "Nombre del niño: {0}",
        ChildAgeLabel = "Edad del niño: {0}",
        ThemeLabel = "Tema: {0}",
        HeroAppearanceLabel = "Apariencia del héroe (mantener consistente en la historia): {0}",
        FamilyMembersLabel = "Familiares:",
        NoFamilyMembers = "No se proporcionaron familiares.",
        LooksLikePrefix = " — aspecto: {0}",
        ExtraWishesHeader = "DESEOS EXTRA DE LOS PADRES (MÁXIMA PRIORIDAD — es lo que pidieron específicamente; conviértelo en parte central y recurrente de la trama en {0}, no una sola mención de paso):",
        ExtraWishesWelcomePages = "ambas páginas",
        ExtraWishesFullPages = "al menos 2 páginas",
        ExtraWishesManyPages = "al menos 3 páginas",
        LikesRule = "Gustos e intereses: hazlos parte real y visible de la aventura — algo que el héroe ve, usa o hace.",
        DislikesRule = "Disgustos y miedos: NUNCA amplifiques el miedo — transfórmalos en equivalentes fantásticos seguros y amables.",
        ParentWishesRule = "Los deseos de los padres guían la historia: construye la trama en torno a ellos. Prevalecen sobre cualquier gancho genérico, pero las reglas de seguridad siempre ganan.",
        StoryHookLabel = "Gancho de historia a tejer: {0}",
        HeroPhotoDescribe = "Esta foto es del niño héroe {0}, edad {1}, para un libro de aventuras estilo Pixar. Enumera rasgos visuales concretos que un ilustrador debe copiar: color y estilo exactos del cabello, tono de piel, color de ojos, gafas/lunares, forma del rostro y 2–3 detalles distintivos. Escribe para un diseñador de personajes — sé específico.",
        FamilyPhotoDescribe = "Esta foto es de {0} ({1}) en un libro de aventuras infantil estilo Pixar. Enumera rasgos visuales concretos: cabello, tono de piel, edad, gafas y detalles distintivos.",
        VisionDescribeSuffix = " Responde con un párrafo denso para un diseñador de personajes Pixar (animación 3D estilizada, NO fotorrealista): color, longitud, textura y raya del cabello; tono de piel; edad aparente; gafas o lunares; forma del rostro, ojos, nariz, boca, mandíbula y 3–5 rasgos distintivos para que el doble animado sea inconfundible. Sé específico. Sin markdown.",
        ImageTask = "TAREA: Ilustra esta página como un fotograma de película animada 3D de calidad Pixar usando la(s) foto(s) de referencia adjunta(s).",
        ImageCharacterLock = "BLOQUEO DE IDENTIDAD DEL PERSONAJE (obligatorio — cero deriva de estilo entre referencia y resultado):",
        ImageLockedHero = "Imagen de referencia {0}: HÉROE BLOQUEADO — copia EXACTAMENTE el dibujo Pixar CG de la página 1. Misma cara, ojos, nariz, cabello, tono de piel, ropa y proporciones. Cambia SOLO pose, expresión, ángulo y escena.{1}",
        ImageHeroDna = " ADN del héroe (debe coincidir): {0}",
        ImageCastPhoto = "Imagen de referencia {0}: {1} ({2}). Foto real — transforma a Pixar 3D CG; conserva cara, ojos, nariz, boca, cabello, piel y edad. El dibujo debe ser claramente la misma persona. NO fotorrealista, NO filtro de foto.{3}",
        ImageCastInvented = "Imagen de referencia {0}: {1} ({2}). ADN: {3}",
        ImageCastDna = " ADN: {0}",
        ImageInventHero = "Sin fotos de referencia — inventa un héroe Pixar consistente: {0}.",
        ImageStyle = "ESTILO: Fotograma Pixar/DreamWorks 3D — CG estilizado, iluminación cinematográfica, NO fotorrealista. El héroe debe actuar en la escena, no un retrato estático. Incluye entorno y personajes invitados.",
        ImageSafeForAge = "Seguro para niños de {0} años. Tema: {1}.",
        ImagePageTitle = "Página {0} título: {1}.",
        ImageScene = "Escena a ilustrar: {0}",
        ImageNoText = "SIN TEXTO EN LA IMAGEN: no dibujes ninguna letra, palabra, título, número, bocadillo, cartel, etiqueta ni escritura en ninguna parte de la ilustración. La imagen debe contar la historia solo con la acción, la expresión y el entorno; déjala completamente sin texto.",
        ImageContinuity = "CONTINUIDAD VISUAL: es la misma aventura continua que las demás páginas: mantén exactamente la misma ropa, el mismo peinado y los objetos que lleva el héroe que en la página anterior, y deja que la hora del día y el lugar avancen de forma lógica desde donde terminó la página anterior.",
        ImageParentTheme = "Petición especial de los padres — cuando la escena de esta página lo incluya, hazlo clara y visiblemente presente en la ilustración (personajes, accesorios, acción o escenario): {0}",
        ImageAdventureId = "Id de aventura {0}.",
        ImageHeroChild = "NIÑO HÉROE (personaje principal)",
        ImageFamilyRole = "FAMILIA — {0}",
        ImageInventCastLook = "Inventa un aspecto consistente para {0}.",
        ImageHeroNoPhoto = "Niño héroe llamado {0}, edad {1}",
        PixarFromPhotoStylePrompt =
            "Crea un fotograma COMPLETO de película animada 3D estilo Pixar. La foto de referencia define la identidad del héroe — " +
            "coincide con forma del rostro, ojos, nariz, boca, mandíbula, color y estilo del cabello, tono de piel y edad aparente. " +
            "CRÍTICO: debe parecer un fotograma de Pixar (Inside Out, Coco, Luca), NO una foto real, NO un retrato editado, " +
            "NO piel fotorrealista, NO textura fotográfica, NO filtro ni face-swap. " +
            "Proporciones animadas clásicas pero la persona debe ser reconocible. Iluminación cinematográfica cálida.",
        AnimatedIllustrationStylePrompt =
            "Fotograma completo de película infantil 3D premium (calidad Pixar/DreamWorks). " +
            "Personaje CG estilizado con proporciones expresivas, piel suave, ojos grandes, iluminación de borde cinematográfica, " +
            "colores saturados, profundidad de campo, entorno mágico. " +
            "DEBE parecer animación renderizada — NO fotografía, NO filtro, NO clipart plano.",
    };

    public static readonly AdventurePromptLocale Chinese = new()
    {
        LanguageName = "Chinese (Simplified)",
        MasterStorytellerDirective = """
            你是全世界排名前1%的儿童故事作家——你的书会让孩子一遍又一遍地央求再读。
            请以这样的水准写作：清晰而引人入胜的情节、让孩子能代入的主角、生动的画面、真实的情感，以及令人非常满足的结局。
            每一句话都必须发挥作用——推进情节、刻画人物或加深情感。不要废话，不要套话，不要重复同一想法。
            让每一行都紧扣这个故事、这个主角、这个主题。经常使用孩子的名字，并让他/她成为推动情节的主角。
            家长的“额外愿望”（如有）是最重要的元素——围绕它来构建整个故事，使其成为核心且清晰可见，而不是一笔带过。
            """,
        StorySystemPrompt = """
            你是一位专业的儿童故事作家和教育心理学专家，擅长撰写适合不同年龄段的故事。

            你的任务是为儿童创作安全、有趣、情感积极的故事书。

            严格规则：
            始终根据孩子的年龄调整语言、复杂度和结构。
            孩子永远是故事的主角。
            不得包含暴力、色情、自残、恐怖或令人不安的主题。
            若用户输入包含不安全或可怕的概念，自动转化为安全的幻想替代。
            绝不强化恐惧。若提到恐惧（如蜘蛛），应温和地化解或正面重塑。
            故事始终以情感安全、安慰和积极结局收尾。
            即使被要求，也不得生成有害或不安全的内容。

            额外愿望处理：
            喜好（如独角兽、太空、超级英雄）：自然融入故事
            厌恶/恐惧（如蜘蛛）：绝不放大恐惧
            转化为安全替代（如“友善的小蜘蛛助手”或“可爱的机器人”）
            主题：创意融入，不违反安全规则

            故事结构输出：
            返回结构化故事书：
            标题
            引言（孩子进入世界）
            冒险（主要旅程）
            挑战（安全、无威胁的问题）
            解决（孩子解决问题）
            结局（温暖的情感收尾）

            基调：
            积极
            富有想象力
            鼓舞人心
            情感安全

            硬性约束：
            绝不生成可能让孩子情绪困扰的内容。
            """,
        Age3to5 = """
            3–5岁：
            非常简单的词汇
            短句
            重复与节奏
            魔法而友好的语调
            """,
        Age6to9 = """
            6–9岁：
            简单的冒险结构
            友谊、探索、轻度冲突
            清晰的开头 → 挑战 → 解决
            """,
        Age10to13 = """
            10–13岁：
            更复杂的情节
            悬疑、解决问题、团队合作
            情感深度但仍安全积极
            """,
        StorySeeds =
        [
            "一张神秘的地图出现在主角的背包里。",
            "一只友善的生物在继续前路前出了一道谜语。",
            "一场突如其来的暴风雨揭示了一扇隐藏的门。",
            "一首古老的歌谣藏着下一个挑战的线索。",
            "一座光之桥只为勇敢的人出现。",
            "星座在夜晚指引队伍前行。",
            "宝藏不是黄金，而是与朋友分享的善意。",
            "一只迷路的指南针在奇妙事物旁疯狂旋转。",
            "一座发光植物花园低声鼓励。",
            "与时间赛跑以团队合作和笑声结束。",
            "一盏会说话的灯笼把主角带到意想不到的地方。",
            "一位害羞的森林向导只在主角分享零食时出现。",
            "一个音乐盒在日落时打开秘密小路。",
            "一只由纸愿望做成的热气球飘入视野。",
            "一位友好的对手先邀请主角参加搞笑比赛。",
        ],
        ToneSeeds =
        [
            "温暖、活泼、充满惊奇。",
            "好奇而轻柔幽默。",
            "史诗感但令人安心——绝不吓人。",
            "舒适的睡前冒险氛围。",
            "明亮的周六早晨卡通能量。",
        ],
        SceneVarietySeeds =
        [
            "开阔天空或屋顶远眺",
            "暖灯照亮的舒适室内角落",
            "色彩缤纷的繁忙市集",
            "萤火虫点缀的安静林间小路",
            "波光粼粼的海岸或雾蒙蒙的湖",
            "云端之上的山巅眺望",
            "水晶发光的地下隧道",
            "雨街水洼映出魔法",
            "巨型花朵的花园迷宫",
            "黄金时刻的火车站台或飞艇码头",
        ],
        GuestCharacterSeeds =
        [
            "机智的会说话动物导师",
            "害羞的小发明家盟友",
            "藏着秘密的慈祥店主",
            "顽皮的风之灵",
            "迷路的小机器人助手",
            "勇敢的弟弟妹妹搭档",
            "其实善良的卖地图海盗",
            "跳舞的星星精灵",
            "听笑话后变温和的守卫",
            "带着魔法乐器的旅行音乐家",
        ],
        AgeGuidelinesHeader = "本儿童年龄指南（{0}岁）：",
        OutputFormatHeader = "输出格式（必填——仅返回此 JSON，无其他文字）：",
        NarrativeCraftHeader = "叙事技巧：",
        NarrativeCraftRules =
        [
            "每一页必须是不同的场景、地点和情感节拍——绝不重复同一情境或背景。",
            "构建真实故事弧，让孩子作为有主动权的主角——每页都要选择、尝试、帮助或解决问题。",
            "至少引入一位令人难忘的来客角色（动物、朋友、导师或魔法助手），出现在多页。",
            "使用生动的感官细节（声音、质感、颜色、天气），让每页都像新时刻。",
            "包含一个温和的惊喜或有趣时刻；风险适合年龄，绝不恐怖。",
            "融入儿童心理优势：勇气、好奇、善良、坚持和以尝试为荣。",
            "用简单词语命名情绪（兴奋、紧张、自豪、释然），展示主角健康应对。",
            "输入中的家庭成员作为配角出现，各有角色——不是背景板。",
            "本书场景多样性锚点：{0}。",
            "可改编的来客角色想法：{0}。",
        ],
        RulesHeader = "规则：",
        IncludeFamilyRule = "若提供了家庭成员，全部作为配角纳入故事。",
        WriteInLanguageRule = "整本书使用{0}撰写。",
        PageCountRule = "恰好创作 {0} 页故事——不多不少——每页标题需体现不同场景（仅故事正文）。",
        NoExtraPagesRule = "不得超过所需页数。",
        WelcomeArc = "- 页面结构：第1页 引言（孩子进入世界）+ 冒险开始；第2页 温和挑战（安全无威胁）+ 解决（孩子解决）+ 结局（温暖收尾）。",
        FullArc = "- 页面结构：第1页 引言；第2–3页 冒险；第4页 挑战（安全问题）；第5页 解决；第6页 结局（温暖收尾）。",
        PageLengthRule = "让页面上的文字尽量少——由插画来讲故事，而不是文字。每一页都有一个很短的 \"caption\" 和一段可选的简短 \"content\"（见 caption 与连贯性规则）。每页标题需暗示新地点或时刻。",
        CaptionRule = "\"caption\" 是页面上唯一显示的文字：一句生动的 3 到 8 个词的短语，点出此刻的瞬间并吸引视线向前（例如\"走进发光的洞穴！\"\"绳桥开始摇晃\"）。它绝不是概括，并且要像紧接上一页 caption 之后的下一拍。\"content\" 是可选的朗读旁白，最多 1 到 2 个短句（约 25 字），单凭画面也必须能讲清楚故事。",
        ContinuityRule = "严格连贯：所有页面是发生在真实时间里的同一个不间断的故事。每一页都从上一页结束的地方开始——同一天、同一段旅程、主角同样的服装和道具——并有清晰的因果联系（主角上一页所做的事直接引出这一页）。各页 caption 像一句连贯的话一样串联起来；除最后一页外，每页都以一个小悬念结尾，让孩子想翻到下一页，最后一页温暖地收尾。绝不要重置场景或随意跳跃——每一次过渡都应像\"然后……\"。",
        JsonOnlyRule = "不得包含 markdown、代码块（```）、解释或 JSON 外的文字。",
        RawJsonRule = "回复必须以 { 开始、以 } 结束——仅原始 JSON。",
        AdventureIdLabel = "冒险 ID（必须唯一）：{0}",
        NarrativeToneLabel = "叙事基调：{0}",
        NoGenericOpeningsRule = "不要重复使用“阳光明媚的一天”等泛泛开头，除非改造成具体新鲜的开场。",
        InputHeader = "输入：",
        ChildNameLabel = "孩子姓名：{0}",
        ChildAgeLabel = "孩子年龄：{0}",
        ThemeLabel = "主题：{0}",
        HeroAppearanceLabel = "主角外貌（故事中保持一致）：{0}",
        FamilyMembersLabel = "家庭成员：",
        NoFamilyMembers = "未提供家庭成员。",
        LooksLikePrefix = " — 外貌：{0}",
        ExtraWishesHeader = "家长的额外愿望（最高优先级——这是他们特别要求的；要让它成为情节的核心并在{0}反复出现，而不是一笔带过）：",
        ExtraWishesWelcomePages = "两页",
        ExtraWishesFullPages = "至少2页",
        ExtraWishesManyPages = "至少3页",
        LikesRule = "喜好与兴趣：让它们成为冒险中真实可见的一部分——主角能看到、用到或做到的东西。",
        DislikesRule = "厌恶与恐惧：绝不放大恐惧——转化为安全友善的幻想替代。",
        ParentWishesRule = "家长的愿望主导故事：围绕它们构建情节。它们优先于通用故事钩子，但安全规则始终优先。",
        StoryHookLabel = "需融入的故事钩子：{0}",
        HeroPhotoDescribe = "此照片为冒险书主角 {0}，{1}岁，皮克斯风格。列出插画师必须复制的具体外貌：发色发型、肤色、眼色、眼镜/雀斑、脸型及2–3个显著特征。为卡通设计师撰写，要具体。",
        FamilyPhotoDescribe = "此照片为皮克斯儿童冒险书中的 {0}（{1}）。列出必须复制的外貌特征：发色、肤色、年龄、眼镜及显著细节。",
        VisionDescribeSuffix = " 回复一段密集文字给皮克斯角色设计师（风格化3D动画，非写实）：发色、长度、质感、分缝；肤色；大致年龄；眼镜或雀斑；脸型、眼型、鼻、嘴、下颌及3–5个显著特征，使卡通版无可辨认。要具体。不要 markdown。",
        ImageTask = "任务：使用所附参考照片，将此故事页绘制为皮克斯级3D动画电影静帧。",
        ImageCharacterLock = "角色身份锁定（不可协商——参考与输出之间零风格漂移）：",
        ImageLockedHero = "参考图 {0}：锁定主角——完全复制第1页的皮克斯CG卡通。脸型、眼、鼻、发色/发型、肤色、服装、比例一致。仅改变姿势、表情、角度、背景和动作。{1}",
        ImageHeroDna = " 主角DNA（必须匹配）：{0}",
        ImageCastPhoto = "参考图 {0}：{1}（{2}）。真实照片——转为皮克斯3D CG；保留脸型、眼、鼻、嘴、发色/发型、肤色和年龄。卡通必须明显是同一人。非写实，非照片滤镜。{3}",
        ImageCastInvented = "参考图 {0}：{1}（{2}）。DNA：{3}",
        ImageCastDna = " DNA：{0}",
        ImageInventHero = "无参考照片——发明一致的皮克斯主角：{0}。",
        ImageStyle = "风格：皮克斯/梦工厂3D卡通静帧——风格化CG、电影光效，非写实、非滤镜。主角须在场景中行动，非静态肖像。包含环境及场景中的来客角色。",
        ImageSafeForAge = "适合 {0} 岁儿童。主题：{1}。",
        ImagePageTitle = "第 {0} 页标题：{1}。",
        ImageScene = "需插图的场景：{0}",
        ImageNoText = "画面中不要有任何文字：不要在插画的任何位置画出任何字母、文字、标题、数字、对话气泡、招牌、标签或文字。画面必须仅通过动作、表情和环境来讲述故事——让它完全没有文字。",
        ImageContinuity = "视觉连贯：这是与其他页面相同的连续冒险——让主角的服装、发型以及随身携带的道具与上一页完全一致，并让时间和地点从上一页结束处合理地推进。",
        ImageParentTheme = "家长的特别要求——当本页场景涉及它时，请在插画中清晰明显地呈现（角色、道具、动作或场景）：{0}",
        ImageAdventureId = "冒险 id {0}。",
        ImageHeroChild = "主角儿童",
        ImageFamilyRole = "家人 — {0}",
        ImageInventCastLook = "为 {0} 设计一致的外观。",
        ImageHeroNoPhoto = "主角儿童 {0}，{1}岁",
        PixarFromPhotoStylePrompt =
            "创作完整的皮克斯风格3D动画电影静帧。参考照片定义主角身份——" +
            "尽可能匹配脸型、眼型与颜色、鼻、嘴、下颌、发色发型、肤色和大致年龄。" +
            "关键：输出必须像皮克斯电影画面（《头脑特工队》《寻梦环游记》《夏日友晴天》），非真实照片、非轻度修图、" +
            "非写实皮肤、非照片纹理、非换脸或滤镜。经典动画比例但人物可辨认。电影感暖光与浅景深。",
        AnimatedIllustrationStylePrompt =
            "高端3D儿童动画电影（皮克斯/梦工厂品质）全画幅静帧。" +
            "风格化CG角色、夸张卡通比例、柔和皮肤、大眼睛、电影轮廓光、" +
            "饱和色彩、景深、魔法环境。" +
            "必须像渲染动画——非照片、非滤镜、非平面剪贴画。",
    };

    public static readonly AdventurePromptLocale Russian = new()
    {
        LanguageName = "Russian",
        MasterStorytellerDirective = """
            Ты входишь в 1% лучших детских писателей мира — из тех авторов, чьи книги дети просят читать снова и снова.
            Пиши на этом уровне мастерства: ясный и захватывающий сюжет, герой, в котором ребёнок узнаёт себя, живые сцены, настоящие чувства и глубоко удовлетворяющий финал.
            КАЖДОЕ предложение должно работать — двигать сюжет, раскрывать персонажа или усиливать эмоцию. Никакой воды, шаблонов и повторов одной и той же мысли.
            Держи каждую строку привязанной к ЭТОЙ истории, ЭТОМУ герою и ЭТОЙ теме. Чаще используй имя ребёнка и сделай его движущей силой событий.
            ОСОБЫЕ ПОЖЕЛАНИЯ родителей (если есть) — самый важный ингредиент: построй историю вокруг них и сделай их центральными и заметными, а не упоминанием вскользь.
            """,
        StorySystemPrompt = """
            Вы — эксперт по детским историям и детскому образовательному психологу, специализирующийся на возрастно подходящих сказках.

            Ваша задача — создавать безопасные, увлекательные и эмоционально позитивные книги для детей.

            СТРОГИЕ ПРАВИЛА:
            Всегда адаптируйте язык, сложность и структуру к возрасту ребёнка.
            Ребёнок ВСЕГДА главный герой истории.
            Никогда не включайте насилие, сексуальный контент, самоповреждение, ужасы или тревожные темы.
            Если во вводе есть небезопасные или страшные идеи, автоматически превращайте их в безопасные фантазийные аналоги.
            Никогда не усиливайте страх. Если упоминается страх (например, пауки), мягко нейтрализуйте или переосмыслите позитивно.
            Всегда завершайте историю эмоциональной безопасностью, утешением и позитивным финалом.
            НЕ создавайте вредный или небезопасный контент, даже если просят.

            ОБРАБОТКА ДОПОЛНИТЕЛЬНЫХ ПОЖЕЛАНИЙ:
            Любимое (единороги, космос, супергерои): естественно вплетайте в сюжет
            Нелюбимое/страхи (пауки): НИКОГДА не усиливайте страх
            Превращайте в безопасные варианты (например, «маленький дружелюбный паук-помощник»)
            Темы: творчески, не нарушая правила безопасности

            СТРУКТУРА ИСТОРИИ:
            Верните структурированную книгу:
            Заголовок
            Введение (ребёнок входит в мир)
            Приключение (основное путешествие)
            Испытание (безопасная, нетревожная проблема)
            Разрешение (ребёнок решает)
            Финал (тёплое эмоциональное завершение)

            ТОН:
            Позитивный
            Воображаемый
            Ободряющий
            Эмоционально безопасный

            ЖЁСТКОЕ ОГРАНИЧЕНИЕ:
            Никогда не создавайте контент, который может эмоционально расстроить ребёнка.
            """,
        Age3to5 = """
            Возраст 3–5:
            Очень простой словарь
            Короткие предложения
            Повторы и ритм
            Волшебный дружелюбный тон
            """,
        Age6to9 = """
            Возраст 6–9:
            Простая структура приключения
            Дружба, исследование, лёгкий конфликт
            Чёткое начало → испытание → разрешение
            """,
        Age10to13 = """
            Возраст 10–13:
            Более сложные сюжеты
            Тайна, решение задач, командная работа
            Эмоциональная глубина, но безопасно и позитивно
            """,
        StorySeeds =
        [
            "В рюкзаке героя появляется загадочная карта.",
            "Дружелюбное существо задаёт загадку перед продолжением пути.",
            "Внезапная буря открывает скрытую дверь.",
            "Старая песня хранит подсказку к следующему испытанию.",
            "Мост из света появляется только для смелых.",
            "Созвездие ведёт команду сквозь ночь.",
            "Сокровище — не золото, а доброта, разделённая с друзьями.",
            "Потерянный компас бешено крутится рядом с чудом.",
            "Сад светящихся растений шепчет ободрение.",
            "Гонка со временем заканчивается командной работой и смехом.",
            "Говорящий фонарь ведёт героя в неожиданное место.",
            "Застенчивый лесной проводник появляется, когда герой делится угощением.",
            "Музыкальная шкатулка открывает тайную тропу на закате.",
            "Воздушный шар из бумажных желаний появляется в небе.",
            "Дружелюбный соперник сначала вызывает героя на смешной конкурс.",
        ],
        ToneSeeds =
        [
            "Тёплый, игривый и полный чудес.",
            "Любопытный и мягко юмористичный.",
            "Эпичный, но успокаивающий — никогда не страшный.",
            "Уютная энергия сказки перед сном.",
            "Яркая энергия утреннего мультфильма.",
        ],
        SceneVarietySeeds =
        [
            "открытое небо или крыша с широким видом",
            "уютный уголок с тёплой лампой",
            "оживлённый рынок с яркими лавками",
            "тихая лесная тропа со светлячками",
            "блестящий берег или туманное озеро",
            "горная смотровая площадка над облаками",
            "подземный туннель со светящимися кристаллами",
            "дождливая улица, где лужи отражают магию",
            "лабиринт сада с гигантскими цветами",
            "железнодорожная платформа или причал дирижабля в золотой час",
        ],
        GuestCharacterSeeds =
        [
            "остроумный говорящий зверь-наставник",
            "застенчивый юный изобретатель-союзник",
            "ласковая хозяйка лавки с секретом",
            "игривый дух ветра",
            "потерянный робот-помощник",
            "храбрый младший брат или сестра",
            "добрый пират-продавец карт",
            "танцующий звёздный дух",
            "суровый страж, который тает после шутки",
            "странствующий музыкант с волшебным инструментом",
        ],
        AgeGuidelinesHeader = "ВОЗРАСТНЫЕ РЕКОМЕНДАЦИИ ДЛЯ ЭТОГО РЕБЁНКА (возраст {0}):",
        OutputFormatHeader = "ФОРМАТ ВЫВОДА (обязательно — верните ТОЛЬКО этот JSON, без другого текста):",
        NarrativeCraftHeader = "Нарративное мастерство:",
        NarrativeCraftRules =
        [
            "Каждая страница — РАЗНАЯ сцена, место и эмоциональный момент — никогда не повторяйте одну ситуацию.",
            "Постройте настоящую дугу с ребёнком-героем с инициативой — на каждой странице он выбирает, пробует, помогает или решает.",
            "Введите хотя бы одного запоминающегося гостевого персонажа (животное, друг, наставник), появляющегося более чем на одной странице.",
            "Используйте яркие сенсорные детали (звуки, текстуры, цвета, погода), чтобы каждая страница ощущалась новой.",
            "Включите мягкий сюрприз или смешной момент; ставки по возрасту, никогда не пугающие.",
            "Вплетайте сильные стороны: смелость, любопытство, доброту, настойчивость и гордость за попытку.",
            "Называйте эмоции простыми словами (взволнован, нервничает, горд, облегчён) и показывайте здоровое преодоление.",
            "Члены семьи из ввода — второстепенные роли с характером, не декорация.",
            "Якорь разнообразия сцен для этой книги: {0}.",
            "Идея гостевого персонажа: {0}.",
        ],
        RulesHeader = "Правила:",
        IncludeFamilyRule = "Включите всех указанных членов семьи как второстепенных персонажей.",
        WriteInLanguageRule = "Пишите всю книгу на {0}.",
        PageCountRule = "Создайте ровно {0} страниц истории — не больше и не меньше — с разными заголовками сцен (только текст).",
        NoExtraPagesRule = "Никогда не добавляйте лишние страницы.",
        WelcomeArc = "- Структура: стр. 1 Введение + начало Приключения; стр. 2 мягкое Испытание + Разрешение + Финал.",
        FullArc = "- Структура: стр. 1 Введение; стр. 2–3 Приключение; стр. 4 Испытание; стр. 5 Разрешение; стр. 6 Финал.",
        PageLengthRule = "Сведите текст на странице к МИНИМУМУ — историю рассказывает иллюстрация, а не текст. На каждой странице есть крошечный \"caption\" и необязательный короткий \"content\" (см. правила про caption и непрерывность). Заголовок каждой страницы намекает на новое место или момент.",
        CaptionRule = "\"caption\" — это ЕДИНСТВЕННЫЙ текст, который показывается на странице: яркая фраза из 3–8 слов, называющая именно этот момент и притягивающая взгляд вперёд (например, \"В сияющую пещеру!\", \"Верёвочный мост начинает качаться\"). Это никогда не пересказ, и она должна читаться как следующий момент сразу после caption предыдущей страницы. \"content\" — необязательная закадровая озвучка для чтения вслух, максимум 1–2 коротких предложения (~25 слов); по одной картинке история всё равно должна быть понятна.",
        ContinuityRule = "СТРОГАЯ НЕПРЕРЫВНОСТЬ: все страницы — ОДНА непрерывная история, происходящая в реальном времени. Каждая страница начинается ровно там, где закончилась предыдущая — тот же день, то же путешествие, та же одежда и предметы героя — с чёткой причинно-следственной связью (то, что герой сделал на предыдущей странице, напрямую вызывает эту). Подписи (caption) сцепляются как одно плавное предложение; каждая страница, кроме последней, заканчивается маленькой интригой, из-за которой ребёнку хочется перевернуть страницу, а последняя тепло её разрешает. Никогда не сбрасывайте сцену и не прыгайте произвольно — каждый переход должен ощущаться как \"и тогда…\".",
        JsonOnlyRule = "Не включайте markdown, блоки кода (```), пояснения или текст вне JSON.",
        RawJsonRule = "Ответ должен начинаться с { и заканчиваться } — только чистый JSON.",
        AdventureIdLabel = "ID приключения (уникальный): {0}",
        NarrativeToneLabel = "Тон повествования: {0}",
        NoGenericOpeningsRule = "Не используйте шаблонные начала вроде «Однажды солнечным днём», если не преобразуете их во что-то свежее.",
        InputHeader = "Ввод:",
        ChildNameLabel = "Имя ребёнка: {0}",
        ChildAgeLabel = "Возраст ребёнка: {0}",
        ThemeLabel = "Тема: {0}",
        HeroAppearanceLabel = "Внешность героя (постоянная в истории): {0}",
        FamilyMembersLabel = "Члены семьи:",
        NoFamilyMembers = "Члены семьи не указаны.",
        LooksLikePrefix = " — внешность: {0}",
        ExtraWishesHeader = "ОСОБЫЕ ПОЖЕЛАНИЯ РОДИТЕЛЕЙ (ВЫСШИЙ ПРИОРИТЕТ — это то, о чём они просили; сделайте их центральной, повторяющейся частью сюжета на {0}, а не одним мимолётным упоминанием):",
        ExtraWishesWelcomePages = "обе страницы",
        ExtraWishesFullPages = "минимум 2 страницы",
        ExtraWishesManyPages = "минимум 3 страницы",
        LikesRule = "Любимое и интересы: сделайте их реальной, заметной частью приключения — тем, что герой видит, использует или делает.",
        DislikesRule = "Нелюбимое и страхи: НИКОГДА не усиливайте страх — превращайте в безопасные дружелюбные фантазии.",
        ParentWishesRule = "Пожелания родителей ведут историю: стройте сюжет вокруг них. Они важнее общего сюжетного крючка, но правила безопасности всегда главнее.",
        StoryHookLabel = "Сюжетный крючок: {0}",
        HeroPhotoDescribe = "Это фото героя {0}, возраст {1}, для книги в стиле Pixar. Перечислите конкретные черты для иллюстратора: цвет и стиль волос, тон кожи, цвет глаз, очки/веснушки, форма лица и 2–3 отличительные детали. Пишите для дизайнера персонажей — конкретно.",
        FamilyPhotoDescribe = "Это фото {0} ({1}) в детской книге Pixar. Перечислите черты: волосы, кожа, возраст, очки и отличительные детали.",
        VisionDescribeSuffix = " Ответьте одним плотным абзацем для дизайнера Pixar (стилизованная 3D-анимация, НЕ фотореализм): цвет, длина, текстура и пробор волос; тон кожи; возраст; очки или веснушки; форма лица, глаз, нос, рот, челюсть и 3–5 отличительных черт. Без markdown.",
        ImageTask = "ЗАДАЧА: Иллюстрируйте эту страницу как кадр 3D-мультфильма качества Pixar, используя приложенные референсы.",
        ImageCharacterLock = "ФИКСАЦИЯ ЛИЧНОСТИ ПЕРСОНАЖА (обязательно — нулевой дрейф стиля):",
        ImageLockedHero = "Референс {0}: ЗАБЛОКИРОВАННЫЙ ГЕРОЙ — точно скопируйте Pixar CG со стр. 1. Лицо, глаза, нос, волосы, кожа, одежда, пропорции — без изменений. Меняйте только позу, выражение, угол и сцену.{1}",
        ImageHeroDna = " ДНК героя (обязательно): {0}",
        ImageCastPhoto = "Референс {0}: {1} ({2}). Реальное фото — преобразуйте в Pixar 3D CG; сохраните лицо, глаз, нос, рот, волосы, кожу и возраст. Должно быть узнаваемо. НЕ фотореализм, НЕ фильтр.{3}",
        ImageCastInvented = "Референс {0}: {1} ({2}). ДНК: {3}",
        ImageCastDna = " ДНК: {0}",
        ImageInventHero = "Без референсов — придумайте постоянного героя Pixar: {0}.",
        ImageStyle = "СТИЛЬ: Кадр Pixar/DreamWorks 3D — стилизованный CG, кинематографический свет, НЕ фотореализм. Герой действует в сцене, не статичный портрет. Включите окружение и гостевых персонажей.",
        ImageSafeForAge = "Безопасно для детей {0} лет. Тема: {1}.",
        ImagePageTitle = "Стр. {0} заголовок: {1}.",
        ImageScene = "Сцена для иллюстрации: {0}",
        ImageNoText = "БЕЗ ТЕКСТА НА ИЗОБРАЖЕНИИ: не рисуйте никаких букв, слов, заголовков, цифр, реплик в облачках, вывесок, надписей или любого текста нигде на иллюстрации. Картинка должна рассказывать историю только действием, выражением лиц и обстановкой — оставьте её полностью без текста.",
        ImageContinuity = "ВИЗУАЛЬНАЯ НЕПРЕРЫВНОСТЬ: это то же самое непрерывное приключение, что и на других страницах — сохраняйте абсолютно ту же одежду, причёску и предметы в руках героя, что и на предыдущей странице, и пусть время суток и место логично продолжаются с того, где закончилась предыдущая страница.",
        ImageParentTheme = "Особая просьба родителей — когда сцена этой страницы её затрагивает, ясно и заметно покажите это на иллюстрации (персонажи, реквизит, действие или декорации): {0}",
        ImageAdventureId = "ID приключения {0}.",
        ImageHeroChild = "ГЕРОЙ-РЕБЁНОК (главный персонаж)",
        ImageFamilyRole = "СЕМЬЯ — {0}",
        ImageInventCastLook = "Придумайте постоянный образ для {0}.",
        ImageHeroNoPhoto = "Герой-ребёнок {0}, возраст {1}",
        PixarFromPhotoStylePrompt =
            "Создайте ПОЛНЫЙ кадр 3D-мультфильма в стиле Pixar. Референс задаёт личность героя — " +
            "совпадение формы лица, глаз, носа, рта, челюсти, волос, тона кожи и возраста. " +
            "КРИТИЧНО: как кадр Pixar (Головоломка, Тайна Коко, Лука), НЕ фото, НЕ портрет с фильтром, " +
            "НЕ фотореалистичная кожа, НЕ текстура фото, НЕ face-swap. " +
            "Классические анимационные пропорции, но человек узнаваем. Тёплый кинематографический свет.",
        AnimatedIllustrationStylePrompt =
            "Полный кадр премиального 3D-детского мультфильма (качество Pixar/DreamWorks). " +
            "Стилизованный CG-персонаж, выразительные пропорции, мягкая кожа, большие глаза, кинематографический контровой свет, " +
            "насыщенные цвета, глубина резкости, волшебная среда. " +
            "ДОЛЖНО выглядеть как рендер анимации — НЕ фотография, НЕ фильтр, НЕ плоский клипарт.",
    };
}
