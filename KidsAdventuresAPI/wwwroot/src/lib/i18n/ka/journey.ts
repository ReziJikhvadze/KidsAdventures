export const journey = {
  steps: {
    one: "ნაბიჯი 1 / 3",
    two: "ნაბიჯი 2 / 3",
    three: "ნაბიჯი 3 / 3 · Preview",
    order: "შეკვეთა",
    payment: "შეკვეთა · გადახდა",
    creating: "წიგნის შექმნა",
  },

  profile: {
    eyebrow: " პირველი თავგადასავალი",
    title: "ჯერ გავიცნოთ პატარა გმირი",
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
    /*
      A signed-in parent's saved children, offered before the empty form. A second book used to
      begin with the same questions as the first — name, date, eyes, photo — for a child the
      account already knew.
    */
    /* The card of a supporting character, above their name. */
    additionalLabel: "დამატებითი",
    /* The banner over a saved hero's form: whose book this is, and the way out for another child. */
    knownHero: {
      newBook: (name: string) =>
        `ქმნი ახალ წიგნს ${/[აეიოუ]$/.test(name) ? `${name}სთვის` : `${name}ისთვის`}`,
      newHero: (name: string) => `შეიქმნება ახალი გმირი: ${name}`,
      otherChild: "სხვა ბავშვისთვის? დაიწყე ახალი გმირით",
    },
    heroPicker: {
      title: "ვისთვის ვქმნით წიგნს?",
      hint: "შენახული გმირის მონაცემები და ფოტო ავტომატურად შეივსება.",
      newChild: "ახალი ბავშვი",
      loading: "გმირები იტვირთება…",
    },
  },

  characterForm: {
    nameLabel: "სახელი",
    birthDateLabel: "დაბადების თარიღი",
    genderLegend: "გოგოა თუ ბიჭი? · აუცილებელი",
    eyeColorLegend: "თვალის ფერი",
    relationshipLegend: "ვინ არის ეს პერსონაჟი მთავარი გმირისთვის?",
    relationshipCustom: "ჩაწერე ურთიერთობა",
    relationshipPlaceholder: "მაგ. ნათლია ან ჯადოსნური მეგობარი",
    photoGuideAlt:
      "მარცხნივ სწორი ფოტო — ბავშვის სახე ახლოდან და პირდაპირ; მარჯვნივ არასწორი — შორიდან და გვერდულად.",
    photoUpload: "ფოტოს ატვირთვა",
    photoReplace: "შეცვალე ფოტო",
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
    termsRequired: "წიგნის შესაქმნელად საჭიროა წესებსა და პირობებზე დათანხმება.",
    /*
      It said "the ADDITIONAL character" even when the open form was the main hero's, so the
      message named something that was not on the screen. It is about whichever character is
      open, which is the only one it can be.
    */
    photoRequired: "დაამატე მკაფიო ფოტო.",
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
    /* The arrow no longer always leads home — it leads back to wherever the parent came from. */
    backLabel: "უკან დაბრუნება",
    /* "Create", not "let's go to this world": the button makes a book, and the world is already
       chosen by the time it can be pressed. */
    create: "შექმნა",
    continueTo: (world: string) => `შექმნა — ${world}`,
    /* Spoken, not shown: the painting says all this in pictures, and a screen reader cannot
       see a star cross it. */
    statusIdle: "სამყარო ჯერ არ არის არჩეული.",
    statusFlying: (world: string) => `${world} არჩეულია. ბექის ვარსკვლავი მიემართება სამყაროსკენ.`,
    statusReady: (world: string) => `${world} არჩეულია. ღილაკი ამ სამყაროში წასასვლელად მზადაა.`,

    /* Shown only when the map is opened for a child who already has books: which worlds they
       have been to, and which are still shut. */
    visited: "უკვე შექმნილია",
    /* Pressing a world this child has already been to offers the trip again, not a new door. */
    tryAgain: "სცადე თავიდან",
    locked: "ჯერ დახურულია",
    lockedNote: (world: string) =>
      `${world} ჯერ დახურულია — წინა თავგადასავალი ჯერ არ დასრულებულა.`,
    forChild: (name: string) => `${name}ს სამყაროები`,
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
    /*
      A way off the waiting screen.

      Nothing here can be recalled — the story is already being written on the server — so the
      button says what it honestly does: stop waiting and go back. The run is remembered, so
      coming back rejoins this same book rather than paying for another.
    */
    stopWaiting: "შეაჩერე და დაბრუნდი",
    stopWaitingNote: "ისტორია იწერება. შეგიძლია დაელოდო აქ, ან უკან დაბრუნდე — დაკარგული არაფერია.",
    heading: " პერსონალიზებული Preview იქმნება",
    subheading: "ს პირველი გვერდი უკვე მზადდება ✨",
    /* Honest: a whole sixteen-page story plus its cover takes minutes, not seconds. */
    reassurance: "დარჩი ამ ჯადოსნურ მომენტში — ჩვეულებრივ 2–4 წუთი სჭირდება.",
    atelier: "BEKI BOOK ATELIER · 2–4 წუთი",
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
    /* The server's own rate limit, said as what it is — a busy moment, not a fault of the parent. */
    tooBusy:
      "ამ წუთას ძალიან ბევრი ზღაპარი იწერება. სცადე რამდენიმე წუთში — შენი მონაცემები შენახულია.",
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
      title: "ციფრული წიგნი",
      features: ["PDF ჩამოტვირთვა", "Online Reader", "Adventure World"],
      upgradeNote: "ბეჭდურზე გადასვლა მოგვიანებით +65 ₾",
    },
    print: {
      title: "პერსონალიზებული ბეჭდური წიგნი",
      badge: "ყველაზე ემოციური არჩევანი",
      features: [
        "ყველაფერი ციფრული პაკეტიდან",
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
    /* One word each. Three of these share a row on a 375px phone, which leaves about 100px
       apiece — "ტელეფონის ნომერი" wrapped to two lines and pushed the row out of shape. The
       field below each one says the long version. */
    tabPhone: "ნომრით",
    /* The switcher on the panel: the two ways into an account that work today. */
    methodGroup: "შესვლის მეთოდი",
    tabMagicLink: "ბმულით",
    /* The one line that says an account can be made here at all. */
    needAccount: "ანგარიში არ გაქვს? დარეგისტრირდი",
    haveAccount: "უკვე გაქვს ანგარიში? შესვლა",
    registerSubmit: "რეგისტრაცია ",
    tabPassword: "პაროლით",
    sendMagicLink: "გამომიგზავნე Magic Link ",
    magicLinkSent: (email: string) => `ბმული გაიგზავნა — შეამოწმე ${email}.`,
    openMagicLink: "გახსენი Magic Link (დემო)",
    phoneLabel: "ტელეფონის ნომერი",
    phoneDemoNote: "SMS არ გაიგზავნება",
    sendCode: "კოდის მიღება ",
    usePassword: "ან შექმენი პაროლი",
    useMagicLink: "ან ერთჯერადი ბმულით",
    passwordLabel: "პაროლი",
    passwordRepeatLabel: "გაიმეორე პაროლი",
    passwordHint: "მინიმუმ 8 სიმბოლო, ერთი დიდი და ერთი პატარა ლათინური ასო და ერთი ციფრი.",
    passwordMismatch: "პაროლები არ ემთხვევა.",
    passwordSubmit: "გაგრძელება ",
    passwordFailed: "შესვლა ვერ მოხერხდა.",
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
    /* The package a parent is buying, named in the language the rest of the page is in.
       These two lines were the last English left on the order summary. */
    packageDigital: "ციფრული",
    packagePrint: "ბეჭდური + ციფრული",
    printTitle: "ბეჭდური ვერსიის შეკვეთა",
    printLead: "მიიღე უკვე შექმნილი წიგნი ბეჭდურად",
    title: "დაასრულე შეკვეთა",
    secure: "უსაფრთხო გადახდა",
    zeroTotal: "გადასახდელი თანხა განულებულია",
    zeroTotalNote: "ბარათის მონაცემები აღარ არის საჭირო.",
    recipient: "მიმღები",
    pickLocation: "აირჩიე ლოკაცია რუკაზე",
    pickLocationTitle: "სად მივიტანოთ?",
    pickLocationHint: "მოძებნე ქუჩა და აირჩიე ჩამონათვალიდან.",
    pickLocationConfirm: "ამ მისამართის დადასტურება",
    pickLocationUnavailable: "რუკა ამჟამად მიუწვდომელია — ჩაწერე მისამართი ხელით.",
    addressNotes: "დამატებითი მითითება კურიერისთვის",
    addressNotesPlaceholder: "სადარბაზო, სართული, ბინა, კოდი, ორიენტირი",
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
    alreadyOwnedDigital: "უკვე შეძენილი ციფრული ",
    deliveryLine: "მიწოდება საქართველოში ",
    discountLine: "შენი ფასდაკლება ",
    total: "ჯამი ",
    printReuseNote: "უკვე შექმნილი წიგნი დაიბეჭდება — ისტორია ხელახლა არ გენერირდება.",
    bookLanguage: "წიგნის ენა",
    payFirstNote: "სრული წიგნი მხოლოდ წარმატებული გადახდის შემდეგ შეიქმნება.",
  },

  generating: {
    heading: "ახლა იქმნება",
    failedTitle: "წიგნი ვერ შეიქმნა",
    failedBody:
      "წიგნის შექმნა შეწყდა. ჩვენ უკვე ვმუშაობთ პრობლემის მოსაგვარებლად. არაფერი დაკარგულა.",
    stillWorking:
      "წიგნის მომზადებას ჩვეულებრივზე ცოტა მეტი დრო სჭირდება — ის ისევ იხატება და არაფერი დაკარგულა. შეგიძლია აქ დაელოდო, ან მოგვიანებით დაფაზე ნახო: როგორც კი მზად იქნება, იქ გამოჩნდება.",
    titleSuffix: "ს ამბავში მაგია იწყება...",
    companionPrefix: "რექსმა ",
    companionSuffix: "ს ახალი სამყაროს კარი გაიღო",
    leaveNote: "შეგიძლია თავისუფლად გახვიდე — წიგნის მზადებისას ელფოსტასაც გამოგიგზავნით.",
    /* A Beki book is nine paintings and a print-ready file; a minute was never true. */
    softTime: "ჩვეულებრივ 5–10 წუთი",
    stageLabel: "ნაბიჯი ",
    orderMissing: "შეკვეთა ვერ მოიძებნა.",
    toDashboard: "დაფაზე გადასვლა",
    pagesDrawn: "დახატული გვერდები",
    pageAlt: (spread: number) => `გვერდი ${spread}`,
    spreadsDrawn: (done: number, total: number) => `დაიხატა ${done} / ${total} ილუსტრაცია`,
    ariaLabel: (hero: string) => `${hero}ს ამბავი იბადება`,
    stages: [
      "გმირის სახეს ვამზადებთ",
      "ისტორიის გზას ვწერთ",
      "ილუსტრაციებს ვაცოცხლებთ",
      "თექვსმეტ გვერდს ერთ წიგნად ვკრავთ",
    ],
    /*
      Where the job actually is, keyed by the book's own status.

      The list above is a timer: four lines that advance every eight seconds whether or not
      anything happened. The server has known the real answer all along and nothing read it, so a
      book that stalled at page three kept telling the parent it was being bound.
    */
    statusLine: {
      Pending: "შეკვეთა მიღებულია — ვიწყებთ",
      Generating: "ისტორიას ვწერთ",
      GeneratingStory: "ისტორიას ვწერთ",
      StoryReady: "ილუსტრაციებს ვხატავთ",
      GeneratingPdf: "წიგნს ერთად ვკრავთ",
      Completed: "წიგნი მზადაა",
    } as Record<string, string>,
  },

  generated: {
    ready: " წიგნი მზადაა",
    digitalNote: "ეს არის შენი ციფრული ვერსია",
    languageNote: "წიგნის ენა: ",
    deliveryNote:
      "ბეჭდურ წიგნს მიიღებ მითითებულ მისამართზე — თბილისში 4–5 დღეში, საქართველოს სხვა რეგიონებში 5–8 დღეში.",
    pageBadge: " 16 გვერდი",
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
