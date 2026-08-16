export const story = {
  storybook: {
    brand: "ADVENTRYA",
    /*
      One string, not a prefix and a suffix with the name wedged between them. Georgian declines
      a name — the suffix was a literal "-ს", so the cover read "ზუკა-ს ეკუთვნის", which is a
      template seam rather than a sentence. A name is in the dative here and takes ს either way.
    */
    belongsTo: (hero: string) => `ამბავი, რომელიც ${hero.trim()}ს ეკუთვნის`,
    nextChapter: (hero: string) => `${hero}ს შემდეგი თავი`,
    adventureOf: (hero: string) => `${hero}ს თავგადასავალი`,
    coverLabel: (total: number) => `ყდა · ${total} გვერდი`,
    spreadLabel: (from: number, to: number, total: number) => `გვერდები ${from}–${to} / ${total}`,
    pageLabel: (page: number, total: number) => `გვერდი ${page} / ${total}`,
    pages: "წიგნის გვერდები",
    previous: "წინა",
    next: "შემდეგი",
    previousPage: "წინა გვერდი",
    nextPage: "შემდეგი გვერდი",
    gestureHint: "გადაფურცლე, გამოიყენე ისრები ან კლავიატურა",
    flipAria: (hero: string) => `${hero} — გადასაფურცლი წიგნი`,

    qrTitle: "თავგადასავალი აქ არ სრულდება",
    /* Print gets a QR; a screen gets a button, so the two ask for different verbs. */
    backScan: (hero: string) => `დაასკანერე და გააგრძელე ${hero}ს მოგზაურობა სხვა სამყაროში.`,
    backTap: (hero: string) => `გააგრძელე ${hero}ს მოგზაურობა სხვა სამყაროში.`,
    backCta: "ახალი თავგადასავალი",

    lockedNote: "სრული წიგნი შეიქმნება გადახდის შემდეგ",
    lockedPagePrefix: "გვერდი ",
    lockedPageSuffix: " შენთვის უკვე მზადდება",
  },

  reader: {
    digitalBook: "ს Digital წიგნი",
    library: " ბიბლიოთეკა",
    flipPrefix: "გადაფურცლე ",
    flipSuffix: "ს ამბავი",
    lead: "დიდ ეკრანზე წიგნი იშლება, მობილურზე კი თითო გვერდად იკითხება.",
    ariaLabel: (hero: string) => `${hero}ს სრული Online Reader`,
    memoryPrefix: "ეს მოგონება ",
    memorySuffix: "ს მომავალ ისტორიებში შენახული დარჩება.",
    worldPassport: "ნახე გახსნილი სამყარო",
    illustrating: {
      atelier: "ADVENTRYA BOOK ATELIER",
      title: "ვხატავთ წიგნის სურათებს",
      leadWaiting:
        "წიგნს მაშინ გაჩვენებთ, როცა ყველა სურათი მზად იქნება — რომ ერთიანად, დასრულებული ნახო.",
      lead: "ზღაპარი უკვე დაწერილია — ქვემოთ შეგიძლია წაიკითხო. სურათები სათითაოდ ჩნდება.",
      email: "შეგიძლია დახურო ეს გვერდი. როცა წიგნი მზად იქნება, მეილს მოგწერთ.",
      progress: (done: number, total: number) => `${done} სურათი ${total}-დან`,
      failed: "სურათების ნაწილი ვერ დაიხატა — ხელახლა ვცდილობთ. ტექსტი შენახულია.",
    },
    pdf: {
      building: "მზადდება…",
      atelier: "ADVENTRYA PRINT ATELIER",
      title: "საბეჭდ PDF-ს ვამზადებთ",
      lead: "წიგნს საბეჭდად ვაწყობთ — ჩამოტვირთვა ავტომატურად დაიწყება.",
      email: "შეგიძლია დახურო ეს გვერდი. როცა PDF მზად იქნება, მეილს მოგწერთ.",
    },
  },

  map: {
    ariaLabel: (hero: string) => `${hero}ს თავგადასავლების ცოცხალი რუკა`,
    titleSuffix: "ს თავგადასავლების რუკა",
    lead: "ყოველი ახალი წიგნი სამყაროს კიდევ ერთ ნაწილს ხსნის",
    progress: (unlocked: number, total: number) =>
      unlocked === 2 && total === 6
        ? "ორი გახსნილი სამყარო ექვსიდან"
        : `${unlocked} გახსნილი სამყარო ${total}-დან`,
    ofTotal: (total: number) => `${total} სამყაროდან`,
    memorySaved: "მოგონება შენახულია",
    nextReady: "შემდეგი გზა მზადაა",
    unlockWithNewBook: "გახსენი ახალი წიგნით",
    panCue: " გადაადგილე რუკა ",
    legendUnlocked: " გახსნილი სამყარო",
    legendNext: " შემდეგი გზა",
    legendFuture: " გასახსნელი სამყარო",
    statusCompleted: (index: number) => `წიგნი ${index} · დასრულებული`,
    statusNext: "შემდეგი თავი · მზადაა",
    statusFuture: "ჯერ გამოუკვლეველია",
  },

  world: {
    welcomeBack: " კეთილი იყოს შენი დაბრუნება",
    titleSuffix: "ს სამყარო ისევ ცოცხლდება",
    lead: "რექსს ყველა მოგონება ახსოვს. აირჩიე, სად გაგრძელდება მათი გზა.",
    statBook: " წიგნი",
    statMemory: " მოგონება",
    statWorld: " სამყარო",
    explanation: "შენახული მოგონება შეგიძლია თავიდან წაიკითხო ან აქედან ახალი გზა გააგრძელო.",
    guidance:
      "შეეხე რუკაზე ნებისმიერ სამყაროს. უკვე ნაპოვნი მეგობრები, მოგონებები და მიზნები ახალ თავშიც ბუნებრივად გაგრძელდება.",
    readyNote: "რექსი მზადაა — ოქროსფერი რუკა ამ სამყაროსკენ მიუძღვის.",
    lockedNote: "ეს სამყარო ჯერ ჩაკეტილია. ახალი წიგნი მის კარიბჭეს გააღებს.",
    continueFromMemory: "გააგრძელე ამ მოგონებიდან",
    unlockNext: "გახსენი შემდეგი თავგადასავალი",
    lastMemory: "ბოლო მოგონება",
    lastMemoryNote: "რექსს თქვენი ბოლო თავგადასავალი ახსოვს.",
    profileLine: (age: number, stories: number) => `${age} წლის · ${stories} დასრულებული ამბავი`,
    openedStoriesSuffix: "ს უკვე გახსნილი ისტორიები",
    archiveNote:
      "ძველი წიგნები უცვლელად ინახება; ახალი ინფორმაცია მხოლოდ მომავალ ისტორიებზე ვრცელდება.",
  },
};
