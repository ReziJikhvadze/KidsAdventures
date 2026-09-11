export const dashboard = {
  sidebar: {
    pagingLabel: "ბავშვების გვერდები",
    newBook: "ახალი წიგნის შექმნა",
    parentLabel: "ბავშვის პროფილები",
    /* No "＋" in the words: the button draws one beside them, and two plus signs on one
       control pushed the last word of a long Georgian label off the end of the pill. */
    addChild: "დაამატე ბავშვის პროფილი",
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
    pdfNotReady: "PDF ჯერ მზადდება - სცადე ერთ წუთში.",
    /* The book is finished and the file is deliberately not out yet. Said as a wait, because
       that is what it is: nothing is broken and nobody has to do anything. */
    downloadHeld: "წიგნი გადის ბოლო შემოწმებას - ჩამოტვირთვა მალე გაიხსნება.",
    /* Anything the server said that we do not have a sentence for. What used to appear here was
       the server's own English, code first — on a parent's shelf. */
    pdfFailed: "PDF ვერ ჩამოიტვირთა - ცოტა ხანში სცადე ხელახლა.",
    failedTitle: "წიგნი ვერ შეიქმნა",
    failedBody:
      "წიგნის შექმნა შეწყდა. ჩვენ უკვე ვმუშაობთ პრობლემის მოსაგვარებლად. არაფერი დაკარგულა.",
    failedCta: "დაგვიკავშირდი",
    stalledNote:
      "წიგნის მომზადებას ჩვეულებრივზე ცოტა მეტი დრო სჭირდება - ის ისევ იხატება და არაფერი დაკარგულა. შეგიძლია აქ დაელოდო, ან მოგვიანებით დაფაზე ნახო: როგორც კი მზად იქნება, იქ გამოჩნდება.",

    orderPrint: (price: string) => `ბეჭდვა · ${price}`,
    printEdition: "ბეჭდური ვერსია",
    /* Said once, in the order panel, instead of on every card down the shelf. */
    printDetail: (tbilisi: string, regions: string) =>
      `მაგარყდიანი წიგნი. თბილისში ${tbilisi} დღეში, სხვა რეგიონებში ${regions} დღეში.`,
    printOrdered: "ბეჭდური წიგნი გზაშია ✓",

    /*
      What has already been done with this book, in the order the money and the effort went in:
      a book that was printed was also downloaded, and one that was downloaded was created
      first — so the furthest step is the one worth showing.
    */
    statusCreated: "შექმნილია",
    statusDownloaded: "ჩამოტვირთულია",
    statusPrinted: "ბეჭდური",
    statusLabel: "სტატუსი",

    /*
      The unbought preview, on the same shelf as the books.

      "პრივიუ" is the word the whole journey already uses for it - the preview screen, the
      sign-in screen and the home page all say it - so the shelf says it too rather than
      inventing a second name for the same object. The badge is what separates this card from a
      book; everything below it says where the story stands and how long it will be there.
    */
    statusPreview: "პრივიუ",
    previewWriting: "წიგნი იწერება…",
    previewReady: "პრივიუ მზადაა",
    previewFailed: "პრივიუ ვერ შეიქმნა",
    /* The one button, and it says both halves: the card opens the preview screen, where the book
       can be looked at once more, and the order is the button at the foot of that screen. It
       used to say only "შეუკვეთე სრული წიგნი", which read as a jump to the checkout. */
    previewOrder: "ნახე პრივიუ და შეუკვეთე",
    previewOpen: "ნახე პრივიუ",
    previewRetry: "სცადე ხელახლა",
    /*
      The clock. An unbought preview and the portrait it was given are deleted a day after it is
      made, so a card that stayed silent about it would be promising something we delete. Whole
      hours only, rounded down: the number has to be one we can keep.
    */
    previewExpiresIn: (hours: number) => `დარჩა ${hours} საათი`,
    previewExpiresSoon: "ბოლო საათი",

    pagingLabel: "წიგნების გვერდები",
    pageOf: (page: number, total: number) => `გვერდი ${page} / ${total}`,

    /* The two shelves: the books that are books, and what was made lately and is not one yet. */
    readyHeading: "მზად არის წასაკითხად",
    recentHeading: "ახლახან შექმნილი",
    /* Under the "new book" tile at the end of the shelf; the tile's own words are the button's. */
    newTileHint: "კიდევ ერთი თავგადასავალი",
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

  /* The one thing on this page a parent may change about themselves rather than about a book. */
  preferences: {
    heading: "პარამეტრები",
    marketing: "მსურს სიახლეებისა და შეთავაზებების მიღება",
  },
};
