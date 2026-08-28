export const journey = {
  steps: {
    one: "ნაბიჯი 1 / 3",
    two: "ნაბიჯი 2 / 3",
    three: "ნაბიჯი 3 / 3 · Preview",
    order: "შეკვეთა · პროფილი",
    payment: "შეკვეთა · გადახდა",
    creating: "წიგნის შექმნა",
  },

  profile: {
    eyebrow: " პირველი თავგადასავალი",
    title: "ჯერ გავიცნოთ პატარა გმირი",
    lead: "მხოლოდ ის დეტალები გვჭირდება, რომლებიც ბავშვის ასაკსა და ილუსტრაციებს ბუნებრივად მოარგებს.",
    primaryCharacter: "მთავარი გმირი",
    nthCharacter: (index: number) => `გმირი ${index}`,
    addCharacterTitle: "ხომ არ გინდა ზღაპარში კიდევ ერთი პერსონაჟი დაამატო?",
    addCharacterHint: "კიდევ შეგიძლია დაამატო ",
    addCharacterLimit: "მაქსიმუმ 3 გმირი",
    addAnother: "დაამატე კიდევ ერთი პერსონაჟი",
    privacyNote: "მონაცემები გამოიყენება მხოლოდ პერსონალიზებული წიგნის შესაქმნელად.",
    termsPrefix: "ვეთანხმები ",
    termsLink: "წესებსა და პირობებს",
    /* The one action on this form: it makes the book. */
    continue: "შექმენი წიგნი",
    ready: "პერსონაჟი მზადაა",
    saveCharacter: " პერსონაჟის შენახვა",
    saveChanges: " ცვლილებების შენახვა",
    /* The heading of the dialog that says what the form is still waiting for. */
    missingTitle: "ერთი წუთით",
  },

  characterForm: {
    nameLabel: "სახელი",
    birthDateLabel: "დაბადების თარიღი",
    genderLegend: "გოგოა თუ ბიჭი? · აუცილებელი",
    eyeColorLegend: "თვალის ფერი",
    relationshipLegend: "ვინ არის ეს პერსონაჟი მთავარი გმირისთვის?",
    relationshipCustom: "ჩაწერე ურთიერთობა",
    relationshipPlaceholder: "მაგ. ნათლია ან ჯადოსნური მეგობარი",
    photoRequired: "პორტრეტი აუცილებელია",
    photoReady: "გმირი მზადაა ✓",
    photoPrompt: "დაამატე მკაფიო პორტრეტი",
    photoHint: "სახე სრულად ჩანს · კარგი განათება · მზის სათვალის გარეშე",
    photoUpload: "ფოტოს ატვირთვა",
    photoReplace: "შეცვალე ფოტო",
    /* The two examples. A parent sees what is wanted before choosing, not after being refused. */
    // Advice, not a rule: the check itself only asks whether a person is in the photo, so this
    // must not say "won't work" about a picture the form will happily accept.
    photoGuide: {
      title: "როგორი ფოტო მოგცემს საუკეთესო შედეგს",
      goodLabel: "ასეთი სჯობს",
      goodReason: "სახე კადრს ავსებს, წინიდან განათებულია და კამერას უყურებს.",
      badLabel: "ასეთიც გამოდგება",
      badReason: "შორია, ბნელია და სახე გვერდზეა — წიგნიც გამოვა, ოღონდ ნაკლებად მსგავსი.",
    },
    photoChecking: "ფოტო მზადდება…",
    /*
      Keyed by the code the server returns, so each refusal says what to do differently.
      "This photo will not do" sends a parent back to the picker with nothing to change.
    */
    photoRejected: {
      not_a_person: "ფოტოზე ადამიანი ვერ ვნახეთ — ატვირთე სურათი, სადაც ბავშვი ჩანს.",
      unsuitable: "ეს ფოტო არ გამოდგება — ატვირთე სურათი, სადაც ბავშვი ჩანს.",
      unreadable: "ფაილი ვერ წავიკითხეთ — ატვირთე JPG, PNG ან WEBP ფოტო.",
      too_large: "ფოტო ძალიან დიდია — აირჩიე უფრო პატარა სურათი.",
      unavailable: "ფოტოს შემოწმება ვერ მოხერხდა — სცადე ხელახლა ატვირთვა.",
    },
  },

  bookSettings: {
    title: "წიგნის პარამეტრები",
    languageLabel: "წიგნის ენა · აუცილებელი",
    languageQuestion: "რომელ ენაზე შევქმნათ ეს წიგნი?",
    platformLanguageNote: "პლატფორმის ენა არ შეიცვლება.",
    languageShort: "წიგნის ენა",
    thisBookLanguage: "ამ წიგნის ენა",
    changeableNote: "ყოველი ახალი წიგნისთვის შეგიძლია შეცვალო",
  },

  validation: {
    nameRequired: "მიუთითე პერსონაჟის სახელი.",
    birthDateRequired: "მიუთითე ბავშვის დაბადების თარიღი.",
    genderRequired: "აირჩიე, პერსონაჟი გოგოა თუ ბიჭი.",
    relationshipRequired: "დაამატე მისი თანამგზავრი",
    relationshipTextRequired: "ვინ არის ის?",
    photoRequired: "დაამატე მკაფიო პორტრეტი.",
    termsRequired: "წიგნის შესაქმნელად საჭიროა წესებსა და პირობებზე დათანხმება.",
    /*
      It said "the ADDITIONAL character" even when the open form was the main hero's, so the
      message named something that was not on the screen. It is about whichever character is
      open, which is the only one it can be.
    */
    phoneInvalid: "შეიყვანე სწორი 9-ნიშნა ქართული ნომერი.",
    otpInvalid: "კოდი არასწორია.",
  },

  /*
    The delivered world selector. The island names are not here: they come from the world
    catalogue like every other mention of a world in the product, so a place renamed once is
    renamed everywhere rather than only on the page that happens to show a painting of it.
  */
  worldSelector: {
    eyebrow: "შენი ისტორია აქედან იწყება",
    title: "აირჩიე ჯადოსნური სამყარო",
    lead: "შეეხე კუნძულს და აღმოაჩინე მისი ამბავი",
    stageLabel: "ჯადოსნური სამყაროს არჩევა",
    artLabel: "ექვსი მოფარფატე ჯადოსნური სამყარო, ბექი და მანათობელი წიგნი",
    brandLabel: "Beki — მთავარი",
    backLabel: "უკან, მთავარ გვერდზე",
    /* "Create", not "let's go to this world": the button makes a book, and the world is already
       chosen by the time it can be pressed. */
    create: "შექმნა",
    continueTo: (world: string) => `შექმნა — ${world}`,
    /* Spoken, not shown: the painting says all this in pictures, and a screen reader cannot
       see a star cross it. */
    statusIdle: "სამყარო ჯერ არ არის არჩეული.",
    statusFlying: (world: string) => `${world} არჩეულია. ბექის ვარსკვლავი მიემართება სამყაროსკენ.`,
    statusReady: (world: string) => `${world} არჩეულია. ღილაკი ამ სამყაროში წასასვლელად მზადაა.`,
  },

  firstMap: {
    title: "აირჩიე შენი ზღაპარი",
    letBekiChoose: "ბეკიმ აირჩიოს",
    bekiChoosing: "ბეკი არჩევს…",
    continueTo: (place: string) => `${place} — წავიდეთ!`,
    eyebrow: "ბეკის გზა · პირველი კარიბჭე",
    creating: "პირველ თავგადასავალს ქმნი",
    titlePrefix: "სად იწყება ",
    titleSuffix: "ს პირველი თავგადასავალი?",
    guidance: "დააჭირე შენს საყვარელ სამყაროს — ბეკი გზას გაგინათებს.",
    selectedHeading: "არჩეული სამყარო და სურვილი",
    emptySelection: "დააჭირე ერთ სამყაროს — შენი ამბავი აქ დაიწყება.",
    emptyGlyph: "სამყარო 0",
    selected: "არჩეულია",
    activate: "გააცოცხლე",
    liveCaption: "ეს სამყარო შენს შეხებაზე გაცოცხლდა",
    panCue: " აირჩიე, სად წავიდეთ შემდეგ ",
    storyStarts: "ყველაფერი აქ იწყება",
    /* The world is chosen first now, so this leads to the child's details, not to the preview. */
    continue: "გავიცნოთ პატარა გმირი",
    /* Beki says one short line at a time. A child is listening, not reading. */
    beki: {
      greeting: "გამარჯობა! მე ბეკი ვარ — შენი გზამკვლევი.",
      peek: (theme: string) => `${theme}? კარგი არჩევანია.`,
      chosen: (place: string) => `შესანიშნავია! ${place} გველოდება.`,
      chosenByBeki: (place: string) => `მე ვირჩევ: ${place}! წავიდეთ.`,
      alt: "ბეკი, შენი გზამკვლევი",
    },
  },

  previewLoader: {
    paintingCover: "ზღაპარი დაწერილია — ვხატავთ ყდას…",
    heading: " პერსონალიზებული Preview იქმნება",
    subheading: "ს პირველი გვერდი უკვე მზადდება ✨",
    reassurance: "დარჩი ამ ჯადოსნურ მომენტში — დაახლოებით 30 წამი.",
    atelier: "BEKI BOOK ATELIER · დაახლოებით 30 წამი",
    ariaLabel: (hero: string) => `ნახე ${hero}ს ამბავი უფასოდ`,
    stages: [
      "გმირებისა და მათი დეტალების მომზადება",
      "ისტორიის პირველი მომენტის დაწერა",
      "ყდის ილუსტრაციის მოხატვა",
      "პირველი გვერდის გაცოცხლება",
      "Preview-ს წიგნად აკინძვა",
    ],
  },

  preview: {
    chooseWorldFirst: "ჯერ აირჩიე სამყარო, რომ ზღაპარი შენს არჩევანს დაემთხვეს.",
    failedTitle: "ზღაპარი ვერ დაიწერა",
    failed: "ზღაპრის შექმნისას რაღაც ხარვეზი მოხდა. სცადე თავიდან — შენი მონაცემები შენახულია.",
    expired: "შენი ზღაპრის ვადა ამოიწურა. შექმენი ახალი.",
    tookTooLong:
      "ზღაპარი მოსალოდნელზე დიდხანს გრძელდება. სცადე თავიდან — შენი მონაცემები შენახულია.",
    tryAgain: "თავიდან ცდა",
    eyebrow: " პერსონალიზებული Preview მზადაა",
    titlePrefix: "აი, როგორ იწყება ",
    titleSuffix: "ს ამბავი",
    lead: "ყდა და პირველი გვერდი უფასოა. სრული წიგნი გადახდის შემდეგ შეიქმნება.",
    bookNote: " ყდა და პირველი გვერდი — უფასოდ. სრული ამბავი — როცა მოგეწონება.",
    freeFirstPage: "ნახე პირველი გვერდი უფასოდ",
    wishAcknowledged: "შენი სურვილიც ამბავშია ✨",
    packageHeading: "აირჩიე ფორმატი",
    packageQuestion: "როგორ გინდა მიიღო წიგნი?",
    selectedPackage: "არჩეული პაკეტი",
    continue: "გააგრძელე ამბავი · ",
    changeSelection: " არჩევანის შეცვლა",
    coverAlt: (hero: string) => `${hero}ს წიგნის ყდა`,
  },

  packages: {
    digital: {
      title: "Digital Book",
      features: ["PDF ჩამოტვირთვა", "Online Reader", "Adventure World"],
      upgradeNote: "ბეჭდურზე გადასვლა მოგვიანებით +65 ₾",
    },
    print: {
      title: "პერსონალიზებული Printed Book",
      badge: "ყველაზე ემოციური არჩევანი",
      features: [
        "ყველაფერი Digital პაკეტიდან",
        "მიწოდება მთელ საქართველოში",
        "თბილისი 4–5 · რეგიონები 5–8 დღე",
      ],
    },
  },

  auth: {
    eyebrow: " ერთი პატარა ნაბიჯი",
    titlePrefix: "შეინახე ",
    lead: "წიგნზე, Reader-ზე და მომავალ თავგადასავლებზე უსაფრთხო წვდომისთვის. პაროლი არ დაგჭირდება.",
    previewSaved: " Preview შენახულია და გაგრძელების შემდეგ ზუსტად ეს წიგნი შეიქმნება.",
    google: " გააგრძელე Google-ით",
    apple: " გააგრძელე Apple-ით",
    googleUnavailable: "Google შესვლა ამ გარემოში მიუწვდომელია.",
    appleSoon: "Apple-ით შესვლა მალე დაემატება.",
    tabEmail: "ელფოსტა",
    tabPhone: "ტელეფონის ნომერი",
    sendMagicLink: "გამომიგზავნე Magic Link ",
    magicLinkSent: (email: string) => `ბმული გაიგზავნა — შეამოწმე ${email}.`,
    openMagicLink: "გახსენი Magic Link (დემო)",
    phoneLabel: "ტელეფონის ნომერი",
    phoneDemoNote: "SMS არ გაიგზავნება",
    sendCode: "კოდის მიღება ",
    otpHeading: "შეიყვანე 6-ნიშნა დამადასტურებელი კოდი",
    otpDigitAria: (index: number) => `კოდის ${index}-ე ციფრი`,
    resend: "გამომიგზავნე ახალი კოდი",
    resendIn: (seconds: number) => ` ${seconds} წმ`,
    changeNumber: "ნომრის შეცვლა",
    verify: "კოდის დადასტურება ",
    back: " უკან",
    devDelivery: "დემო რეჟიმი · რეალური შეტყობინება არ იგზავნება.",
    devCode: (secret: string) => `საცდელი კოდი: ${secret}`,

    /** The page the emailed magic link lands on. */
    landing: {
      verifying: "შესვლა მიმდინარეობს…",
      verifyingLead: "ერთი წამი, ბმულს ვამოწმებთ.",
      successTitle: "მოგესალმებით!",
      successLead: "შესვლა წარმატებულია. გადაგიყვანთ თავგადასავალში.",
      failedTitle: "ბმული ვერ დადასტურდა",
      missingToken: "ბმული არასრულია. სცადე ელფოსტიდან ხელახლა გახსნა.",
      retry: "ახალი ბმულის მოთხოვნა",
      goHome: "მთავარ გვერდზე",
    },
  },

  checkout: {
    printTitle: "ბეჭდური ვერსიის შეკვეთა",
    printLead: "მიიღე უკვე შექმნილი წიგნი ბეჭდურად",
    title: "დაასრულე შეკვეთა",
    secure: "უსაფრთხო გადახდა",
    zeroTotal: "გადასახდელი თანხა განულებულია",
    zeroTotalNote: "ბარათის მონაცემები აღარ არის საჭირო.",
    recipient: "მიმღები",
    addressPlaceholder: "ქალაქი, ქუჩა, შენობა და ბინა",
    shippingAddress: "მიმღების მისამართი",
    addAnotherAddress: "სხვა მისამართის დამატება",
    useSavedAddress: "გამოიყენე შენახული მისამართი",
    activateOrder: "შეკვეთის გააქტიურება",
    /* The photo is uploaded and the order created behind this button; it is seconds, not milliseconds. */
    placingOrder: "შეკვეთა მუშავდება…",
    pay: (amount: string) => `გადახდა · ${amount} ₾`,
    summaryHeading: "შეკვეთის შეჯამება",
    summaryAlt: (hero: string) => "შენი შეკვეთა",
    alreadyOwnedDigital: "უკვე შეძენილი Digital ",
    deliveryLine: "მიწოდება საქართველოში ",
    discountLine: "შენი ფასდაკლება ",
    total: "ჯამი ",
    printReuseNote: "უკვე შექმნილი წიგნი დაიბეჭდება — ისტორია ხელახლა არ გენერირდება.",
    payFirstNote: "სრული წიგნი მხოლოდ წარმატებული გადახდის შემდეგ შეიქმნება.",
  },

  generating: {
    heading: "ახლა იქმნება",
    titleSuffix: "ს ამბავში მაგია იწყება...",
    companionPrefix: "რექსმა ",
    companionSuffix: "ს ახალი სამყაროს კარი გაიღო",
    leaveNote: "შეგიძლია თავისუფლად გახვიდე — წიგნის მზადებისას ელფოსტასაც გამოგიგზავნით.",
    softTime: "დაახლოებით ერთი წუთი",
    stageLabel: "ნაბიჯი ",
    ariaLabel: (hero: string) => `${hero}ს ამბავი იბადება`,
    stages: [
      "გმირის სახეს ვამზადებთ",
      "ისტორიის გზას ვწერთ",
      "ილუსტრაციებს ვაცოცხლებთ",
      "შვიდ გვერდს ერთ წიგნად ვკრავთ",
    ],
  },

  generated: {
    ready: " წიგნი მზადაა",
    digitalNote: "ეს არის შენი Digital ვერსია",
    languageNote: "წიგნის ენა: ",
    deliveryNote:
      "ბეჭდურ წიგნს მიიღებ მითითებულ მისამართზე — თბილისში 4–5 დღეში, საქართველოს სხვა რეგიონებში 5–8 დღეში.",
    pageBadge: " 7 გვერდი",
    fullBookAria: (hero: string) => `${hero}ს სრული წიგნი`,
    downloadPdf: "PDF-ის ჩამოტვირთვა",
    openWorld: "ს სამყარო ",
    orderPrint: "გინდა ხელში დაიჭირო? +65 ₾ ",
  },

  continuation: {
    heading: "გააგრძელე ნაცნობ პერსონაჟებთან",
    limit: "მაქს. 2 დამატებითი",
    wishLabel: "რისი დამატება გინდა ახალ თავგადასავალში? · არასავალდებულო",
    wishPlaceholder: "მაგ. რექსმა თან წაიღოს ძველი რუკა...",
    wishHint: "არასავალდებულო. ძველი მეგობრები და მოგონებები ავტომატურად გაგრძელდება.",
    createNext: "შექმენი შემდეგი თავის Preview",
  },
};
