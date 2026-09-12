export const journey = {
  steps: {
    /* The three marks under the header. Named rather than numbered: "2 / 3" tells a parent how
       far they are, and the name tells them what they are doing. */
    trailOne: "სამყარო",
    trailTwo: "გმირი",
    trailThree: "წიგნი",
    one: "ნაბიჯი 1 / 3",
    two: "ნაბიჯი 2 / 3",
    three: "ნაბიჯი 3 / 3 · ნიმუში",
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
    /* The optional one. "თუ გნებავთ" says outright that this is a favour rather than a
       condition, so nobody reads two ticks and assumes both are required. */
    marketingConsent: "მსურს სიახლეებისა და შეთავაზებების მიღება",
    /* The one action on this form: it makes the book. */
    continue: "შექმენი წიგნი",
    /*
      What this form says instead, while a book for this child is already being written. The note
      is the whole explanation the screen gives: the card above it is closed, so a parent who
      expected a form needs to be told why there is not one.
    */
    resume: "დაუბრუნდი ნიმუშს",
    resumeNote: "ამ ბავშვის ნიმუში უკვე მზადდება. ჯერ ის დაასრულე - მერე შეძლებ ახლის შექმნას.",
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
      newChild: "ახალი ბავშვი",
      loading: "გმირები იტვირთება…",
      scrollBack: "წინა ბავშვები",
      scrollOn: "შემდეგი ბავშვები",
      /* While the chosen child's details and photo are being brought back from the account. */
      fetching: "გმირი მოაქვს…",
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
      "მარცხნივ სწორი ფოტო - ბავშვი კამერისკენ იყურება და სახე კარგად ჩანს; მარჯვნივ არასწორი - გვერდულად შემობრუნებული და შორიდან.",
    photoUpload: "ფოტოს ატვირთვა",
    photoReplace: "შეცვალე ფოტო",
    photoChecking: "ფოტო მზადდება…",
    /*
      Keyed by the code the server returns, so each refusal says what to do differently.
      "This photo will not do" sends a parent back to the picker with nothing to change.
    */
    photoRejected: {
      not_a_person: "ფოტოზე ადამიანი ვერ ვნახეთ - ატვირთე სურათი, სადაც ბავშვი ჩანს.",
      unsuitable: "ეს ფოტო არ გამოდგება - ატვირთე სურათი, სადაც ბავშვი ჩანს.",
      unreadable: "ფაილი ვერ წავიკითხეთ - ატვირთე JPG, PNG ან WEBP ფოტო.",
      too_large: "ფოტო ძალიან დიდია - აირჩიე უფრო პატარა სურათი.",
      unavailable: "ფოტოს შემოწმება ვერ მოხერხდა - სცადე ხელახლა ატვირთვა.",
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
    /*
      The server refuses an age outside 1–18, and did so after the parent had already pressed
      "create the book" and been moved to the waiting screen — where the refusal arrived as an
      English sentence written for an API client. The form is where this belongs: a date that
      cannot make a book is one the parent can still fix, in the same dialog every other missing
      answer uses. The range is the server's; see AdventurePacksController.
    */
    birthDateRange: "დაბადების თარიღი შეამოწმე - წიგნი 1-დან 18 წლამდე ბავშვისთვის იქმნება.",
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
    brandLabel: "Beki - მთავარი",
    /* The arrow no longer always leads home — it leads back to wherever the parent came from. */
    backLabel: "უკან დაბრუნება",
    /* "Create", not "let's go to this world": the button makes a book, and the world is already
       chosen by the time it can be pressed. */
    create: "შექმნა",
    continueTo: (world: string) => `შექმნა - ${world}`,
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
      `${world} ჯერ დახურულია - წინა თავგადასავალი ჯერ არ დასრულებულა.`,
    forChild: (name: string) => `${name}ს სამყაროები`,
  },

  firstMap: {
    title: "აირჩიე შენი ზღაპარი",
    letBekiChoose: "ბეკიმ აირჩიოს",
    bekiChoosing: "ბეკი არჩევს…",
    continueTo: (place: string) => `${place} - წავიდეთ!`,
    eyebrow: "ბეკის გზა · პირველი კარიბჭე",
    creating: "პირველ თავგადასავალს ქმნი",
    titlePrefix: "სად იწყება ",
    titleSuffix: "ს პირველი თავგადასავალი?",
    guidance: "დააჭირე შენს საყვარელ სამყაროს - ბეკი გზას გაგინათებს.",
    selectedHeading: "არჩეული სამყარო და სურვილი",
    emptySelection: "დააჭირე ერთ სამყაროს - შენი ამბავი აქ დაიწყება.",
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
      greeting: "გამარჯობა! მე ბეკი ვარ - შენი გზამკვლევი.",
      peek: (theme: string) => `${theme}? კარგი არჩევანია.`,
      chosen: (place: string) => `შესანიშნავია! ${place} გველოდება.`,
      chosenByBeki: (place: string) => `მე ვირჩევ: ${place}! წავიდეთ.`,
      alt: "ბეკი, შენი გზამკვლევი",
    },
  },

  previewLoader: {
    /* No "cover" in the waiting words: the parent is waiting for a book, and the cover is our
       vocabulary for the part of it that arrives last. */
    paintingCover: "ვქმნით შენს წიგნს…",
    heading: " პერსონალიზებული ნიმუში იქმნება",
    subheading: "ს პირველი გვერდი უკვე მზადდება ✨",
    ariaLabel: (hero: string) => `ნახე ${hero}ს ამბავი უფასოდ`,
    /*
      Three, and the last one names what is being made rather than how it is assembled.

      Five steps described the machinery — drawing the cover, bringing page one to life, binding
      the preview into a book — which is our vocabulary, not a parent's, and it made a two-minute
      wait read as five separate things going wrong one at a time.
    */
    stages: ["შენი წიგნის მომზადება", "შენი სამყაროს დახატვა", "ნიმუშის დასრულება"],
  },

  preview: {
    chooseWorldFirst: "ჯერ აირჩიე სამყარო, რომ ზღაპარი შენს არჩევანს დაემთხვეს.",
    failedTitle: "ზღაპარი ვერ დაიწერა",
    failed: "ზღაპრის შექმნისას რაღაც ხარვეზი მოხდა. სცადე თავიდან - შენი მონაცემები შენახულია.",
    expired: "შენი ზღაპრის ვადა ამოიწურა. შექმენი ახალი.",
    tookTooLong:
      "ზღაპარი მოსალოდნელზე დიდხანს გრძელდება. სცადე თავიდან - შენი მონაცემები შენახულია.",
    /* The cover already drawn and already paid for, offered on the way back rather than lost. */
    resumeHeading: "შენი ყდა მზადაა",
    resumeFallbackTitle: "დაუბრუნდი შენს წიგნს",
    resumeAction: "ნახვა",
    tryAgain: "თავიდან ცდა",
    /* The server's own rate limit, said as what it is — a busy moment, not a fault of the parent. */
    tooBusy:
      "ამ წუთას ძალიან ბევრი ზღაპარი იწერება. სცადე რამდენიმე წუთში - შენი მონაცემები შენახულია.",
    eyebrow: " პერსონალიზებული ნიმუში მზადაა",
    titlePrefix: "აი, როგორ იწყება ",
    titleSuffix: "ს ამბავი",
    freeFirstPage: "ნახე პირველი გვერდი უფასოდ",
    wishAcknowledged: "შენი სურვილიც ამბავშია ✨",
    packageHeading: "აირჩიე ფორმატი",
    packageQuestion: "როგორ გინდა მიიღო წიგნი?",
    selectedPackage: "არჩეული პაკეტი",
    continue: "შექმენი ამბავი · ",
    changeSelection: " არჩევანის შეცვლა",
    coverAlt: (hero: string) => `${hero}ს წიგნის ყდა`,
    /*
      The dedication leaf, the way the press sets it: whose book this is, how old they are, the
      title, and Beki inviting them in. The printed sample on the home page carries the same four
      lines; here they are about the real child rather than about ზუკა.
    */
    dedicationOwner: (hero: string) => `ეს წიგნი ეკუთვნის ${hero}ს`,
    dedicationAge: (years: number) => `${years} წლის`,
    dedicationInvite: (hero: string) =>
      `${hero}, ერთად გავუყვებით ამ ბილიკს. დროა, დაიწყოს ჩვენი თავგადასავალი!`,
  },

  packages: {
    digital: {
      title: "ციფრული წიგნი",
      /* Two of these three were left in English on an otherwise Georgian card. */
      features: ["PDF ჩამოტვირთვა", "ონლაინ კითხვა", "თავგადასავლების სამყარო"],
      upgradeNote: "ბეჭდურზე გადასვლა მოგვიანებით +65 ₾",
    },
    print: {
      title: "პერსონალიზებული ბეჭდური წიგნი",
      badge: "ყველაზე ემოციური არჩევანი",
      features: [
        "ყველაფერი ციფრული პაკეტიდან",
        "მიწოდება მთელ საქართველოში",
        "თბილისი 5 დღე უფასოდ · რეგიონები 5–7 დღე",
      ],
    },
  },

  auth: {
    eyebrow: " ერთი პატარა ნაბიჯი",
    titlePrefix: "შეინახე ",
    lead: "წიგნზე, Reader-ზე და მომავალ თავგადასავლებზე უსაფრთხო წვდომისთვის. პაროლი არ დაგჭირდება.",
    previewSaved: " ნიმუში შენახულია და გაგრძელების შემდეგ ზუსტად ეს წიგნი შეიქმნება.",
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
    magicLinkSent: (email: string) => `ბმული გაიგზავნა - შეამოწმე ${email}.`,
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
    /*
      The checkout speaks the way a parent would to a friend who is sending them something:
      a question where there is a question, the answer's shape where there is an answer, and no
      word from the order system. Rewritten together so the screen has one voice.
    */
    stepAddress: "სად მოვიდეს შენი წიგნი?",
    copiesFewer: "ერთით ნაკლები",
    copiesMore: "ერთით მეტი",
    giftWrap: "სასაჩუქრე შეფუთვა",
    giftWrapNote: "ლამაზად შეფუთული, საჩუქრად მზად",
    optional: "სურვილისამებრ",
    promoLabel: "პრომოკოდი გაქვს?",
    promoRemove: "კოდის მოხსნა",
    promoApplied: "პრომოკოდი გამოყენებულია",
    promoInvalid: "ეს კოდი არ მუშაობს ან ვადა გაუვიდა.",
    packageDigital: "ციფრული ვერსია",
    packagePrint: "ბეჭდური წიგნი + ციფრული ვერსია",
    printTitle: "ბეჭდური ვერსიის შეკვეთა",
    printLead: "მიიღე უკვე შექმნილი წიგნი ბეჭდურად",
    title: "შეკვეთის დასრულება",
    secure: "უსაფრთხო გადახდა",
    /* Under the button: where the card details go, which is not this page. */
    secureNote: "უსაფრთხო გადახდა ბანკის გვერდზე",
    zeroTotal: "გადასახდელი არაფერია",
    zeroTotalNote: "ბარათი არ დაგჭირდება - შეკვეთა პირდაპირ გააქტიურდება.",
    recipient: "მიმღების სახელი",
    pickLocation: "აირჩიე რუკაზე",
    pickLocationTitle: "სად მოვიდეს წიგნი?",
    pickLocationHint: "მოძებნე ქუჩა, ან დააჭირე რუკაზე.",
    /* The three moves, under the title, in the order they happen. */
    pickLocationSteps: "მოძებნე · დადე პინი · დაადასტურე",
    pickLocationSearch: "მოძებნე მისამართი",
    pickLocationSelected: "არჩეული მისამართი",
    pickLocationDrag: "გადაადგილე პინი ზუსტ ადგილას.",
    pickLocationConfirm: "ამ მისამართის დადასტურება",
    pickLocationUnavailable: "რუკა ახლა არ იტვირთება - ჩაწერე მისამართი ხელით.",
    /* The half no map knows, asked as the question it is. */
    addressNotes: "როგორ მოგნახოს კურიერმა?",
    /* One message per field, said beside the field. */
    requiredRecipient: "ჩაწერე, ვინ მიიღებს წიგნს.",
    requiredPhone: "ჩაწერე ტელეფონის ნომერი.",
    invalidPhone: "ნომერი 9 ციფრია, მაგალითად 599 12 34 56.",
    requiredAddress: "ჩაწერე მისამართი - ქალაქი, ქუჩა და შენობა.",
    fixFields: "შეავსე მონიშნული ველები.",
    addressNotesPlaceholder: "მაგალითად: სადარბაზო, სართული, კარის კოდი",
    addressPlaceholder: "ქალაქი, ქუჩა, შენობა, ბინა",
    shippingAddress: "მისამართი",
    addNewAddress: "სხვა მისამართზე",
    backToSavedAddresses: "უკან, შენახულ მისამართებზე",
    /* Correcting a saved address in place. */
    editAddress: "შეცვლა",
    editingAddress: "მისამართის შეცვლა",
    saveAddress: "შენახვა",
    cancelEdit: "გაუქმება",
    saveAddressFailed: "მისამართი ვერ შეინახა. სცადე კიდევ ერთხელ.",
    /*
      Beki's three lines, one at a time: what is still needed, that the address is good, that
      the order can go. Short, and never a second sentence.
    */
    beki: {
      whereTo: "თითქმის მზადაა! მითხარი, სად მოვიდეს წიგნი.",
      addressSet: "შესანიშნავია! წიგნი ამ მისამართზე მოვა.",
      ready: "ყველაფერი მზადაა! დავასრულოთ შეკვეთა.",
    },
    /* The server's one refusal a parent can act on: the book no longer matches its preview. */
    previewStale: "წიგნის პრევიუ შეიცვალა - გახსენი ერთხელ კიდევ და მერე დაასრულე შეკვეთა.",
    reopenPreview: "პრევიუს გახსნა",
    activateOrder: "შეკვეთის გააქტიურება",
    /* The photo is uploaded and the order created behind this button; it is seconds. */
    placingOrder: "შეკვეთა ფორმდება…",
    pay: (amount: string) => `გადახდა · ${amount} ₾`,
    summaryAlt: (hero: string) => "შენი შეკვეთა",
    summaryTitle: "შენი შეკვეთა",
    showDetails: "დეტალები",
    hideDetails: "დამალვა",
    quantity: "რაოდენობა",
    lineBook: "წიგნი",
    lineBooks: (count: number) => (count > 1 ? `წიგნი × ${count}` : "წიგნი"),
    alreadyOwnedDigital: "უკვე შეძენილი ციფრული ",
    deliveryLine: "მიწოდება საქართველოში ",
    deliveryHeading: "მიწოდება",
    deliveryMethod: "მიწოდების მეთოდი",
    /* Named by what they are; the wait and the price sit under the name. */
    deliveryName: {
      TbilisiExpress: "სწრაფი მიწოდება",
      TbilisiStandard: "სტანდარტული მიწოდება",
      Regional: "მიწოდება რეგიონში",
    },
    deliveryDays: (days: number) => `${days} სამუშაო დღე`,
    deliveryDaysRange: (min: number, max: number) => `${min}-${max} სამუშაო დღე`,
    deliveryFree: "უფასო",
    /* The day it should arrive, which is the thing a parent is choosing between. */
    deliveryExpected: (from: Date, to: Date) => {
      const months = [
        "იანვარს",
        "თებერვალს",
        "მარტს",
        "აპრილს",
        "მაისს",
        "ივნისს",
        "ივლისს",
        "აგვისტოს",
        "სექტემბერს",
        "ოქტომბერს",
        "ნოემბერს",
        "დეკემბერს",
      ];
      const day = (d: Date) => `${d.getDate()} ${months[d.getMonth()]}`;
      if (from.getTime() === to.getTime()) return `მოსალოდნელია ${day(from)}`;
      if (from.getMonth() === to.getMonth()) {
        return `მოსალოდნელია ${from.getDate()}-${to.getDate()} ${months[to.getMonth()]}`;
      }
      return `მოსალოდნელია ${day(from)} - ${day(to)}`;
    },
    deliveryPending: "მისამართის შემდეგ",
    deliveryRegional: "საქართველოს რეგიონები",
    deliveryTbilisi: "თბილისი",
    discountLine: "ფასდაკლება ",
    total: "ჯამი",
    bookLanguage: "წიგნის ენა",
  },

  generating: {
    heading: "ახლა იქმნება",
    /* Beki's last line, the one the checkout was leading to. */
    bekiLine: "წიგნი გზაშია!",
    failedTitle: "წიგნი ვერ შეიქმნა",
    failedBody:
      "წიგნის შექმნა შეწყდა. ჩვენ უკვე ვმუშაობთ პრობლემის მოსაგვარებლად. არაფერი დაკარგულა.",
    stillWorking:
      "წიგნის მომზადებას ჩვეულებრივზე ცოტა მეტი დრო სჭირდება - ის ისევ იხატება და არაფერი დაკარგულა. შეგიძლია აქ დაელოდო, ან მოგვიანებით დაფაზე ნახო: როგორც კი მზად იქნება, იქ გამოჩნდება.",
    titleSuffix: "ს ამბავში მაგია იწყება...",
    companionPrefix: "რექსმა ",
    companionSuffix: "ს ახალი სამყაროს კარი გაიღო",
    leaveNote: "შეგიძლია თავისუფლად გახვიდე - წიგნის მზადებისას ელფოსტასაც გამოგიგზავნით.",
    /* A Beki book is nine paintings and a print-ready file; a minute was never true. */
    softTime: "ჩვეულებრივ 5–10 წუთი",
    stageLabel: "ნაბიჯი ",
    orderMissing: "შეკვეთა ვერ მოიძებნა.",
    /*
      This screen keeps its own way out, and now owns the words for it.

      It borrowed `previewLoader.stopWaiting`, which went with the preview's exit panel. The two
      screens are not the same case: the preview's arrow in the header already leads back to the
      questions, while a paid book is being drawn on the server for minutes and the parent's
      place to wait is the cabinet, not the step before.
    */
    stopWaiting: "შეაჩერე და დაბრუნდი",
    toDashboard: "დაფაზე გადასვლა",
    pagesDrawn: "დახატული გვერდები",
    pageAlt: (spread: number) => `გვერდი ${spread}`,
    spreadsDrawn: (done: number, total: number) => `დაიხატა ${done} / ${total} ილუსტრაცია`,
    ariaLabel: (hero: string) => `${hero}ს ამბავი იბადება`,
    /*
      The four steps, named rather than narrated.

      They were first-person sentences - "გმირის სახეს ვამზადებთ" - which read as four things
      being said to the parent one after another. A list of steps with one of them lit is a
      diagram, and a diagram wants labels: the noun says what the step is, and which step the
      book is standing on is said by how the row is drawn, not by the tense of its verb.

      The last one also stopped promising a page count. "თექვსმეტ გვერდს" was the only place on
      this screen that named a number, and it is not the number the row above it counts - that
      one counts eight illustrations.
    */
    stages: [
      "გმირის მომზადება",
      "თავგადასავალის დაწერა",
      "ილუსტრაციების გაცოცხლება",
      "წიგნის აკინძვა",
    ],
    /*
      Where the job actually is, keyed by the book's own status.

      The list above is a timer: four lines that advance every eight seconds whether or not
      anything happened. The server has known the real answer all along and nothing read it, so a
      book that stalled at page three kept telling the parent it was being bound.
    */
    statusLine: {
      Pending: "შეკვეთა მიღებულია - ვიწყებთ",
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
      "ბეჭდურ წიგნს მიიღებ მითითებულ მისამართზე - თბილისში 5 დღეში უფასოდ ან 3 დღეში 7 ლარად, საქართველოს სხვა რეგიონებში 5–7 დღეში 8 ლარად.",
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
    createNext: "შექმენი შემდეგი თავის ნიმუში",
  },
};
