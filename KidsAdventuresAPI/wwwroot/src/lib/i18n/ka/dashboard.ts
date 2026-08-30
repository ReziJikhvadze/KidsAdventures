export const dashboard = {
  sidebar: {
    pagingLabel: "ბავშვების გვერდები",
    newBook: "ახალი წიგნის შექმნა",
    parentLabel: "ბავშვის პროფილები",
    addChild: "＋ დაამატე ბავშვის პროფილი",
    noStoriesYet: "პირველი თავგადასავალი ჯერ არ დაწყებულა",
    storyCount: (count: number) =>
      count === 1 ? "1 დასრულებული თავგადასავალი" : `${count} დასრულებული თავგადასავალი`,
  },

  library: {
    /* The shelf is the page now, so its heading is the page's title: whose books these are,
       and how many. What stood here described the shelf ("stories opened so far") above a
       paragraph that described it again. */
    heading: (name: string) => `${name}ს წიგნები`,
    bookCount: (count: number) => (count === 1 ? "1 წიგნი" : `${count} წიგნი`),
    openBook: (title: string) => `გახსენი "${title}"`,
    otherChild: (name: string) =>
      `${name}ს ჯერ წიგნი არ აქვს. სხვა ბავშვის წიგნები მარცხენა სიაში მისი პროფილის არჩევით გამოჩნდება.`,

    /* The three things a parent can do with a finished book, in the order the card offers
       them. "ხელახლა" is short on purpose: at any longer wording the row wraps onto a second
       line and the card grows taller than its neighbours on the shelf. */
    read: "წაიკითხე",
    readAgain: "ხელახლა",
    readMark: "წაკითხულია",
    pdfBusy: "მზადდება…",
    drawing: "წიგნი იხატება…",
    failedTitle: "წიგნი ვერ შეიქმნა",
    failedBody:
      "წიგნის შექმნა შეწყდა. ჩვენ უკვე ვმუშაობთ პრობლემის მოსაგვარებლად. არაფერი დაკარგულა.",
    failedCta: "დაგვიკავშირდი",
    stalledNote:
      "წიგნის მომზადებას ჩვეულებრივზე ცოტა მეტი დრო სჭირდება — ის ისევ იხატება და არაფერი დაკარგულა. შეგიძლია აქ დაელოდო, ან მოგვიანებით დაფაზე ნახო: როგორც კი მზად იქნება, იქ გამოჩნდება.",

    orderPrint: (price: string) => `ბეჭდვა · ${price}`,
    printEdition: "ბეჭდური ვერსია",
    /* Said once, in the order panel, instead of on every card down the shelf. */
    printDetail: (tbilisi: string, regions: string) =>
      `მაგარყდიანი წიგნი. თბილისში ${tbilisi} დღეში, სხვა რეგიონებში ${regions} დღეში.`,
    printOrdered: "ბეჭდური წიგნი გზაშია ✓",

    pagingLabel: "წიგნების გვერდები",
    pageOf: (page: number, total: number) => `გვერდი ${page} / ${total}`,
  },

  empty: {
    title: (name: string) => ` ${name}ს სამყარო ჯერ ცარიელია`,
    lead: "პირველი თავგადასავალი აქედან იწყება",
    cta: "შექმენი პირველი თავგადასავალი",
    trust: [
      "გადახდამდე ნახავ, როგორი გამოვა",
      "მონაცემები 7 დღეში ავტომატურად წაიშლება, თუ შეკვეთას არ დაასრულებ",
    ],
  },
};
