export const story = {
  storybook: {
    brand: "ADVENTRYA",
    belongsTo: (hero: string) => `A story that belongs to ${hero.trim()}`,
    nextChapter: (hero: string) => `${hero}'s next chapter`,
    adventureOf: (hero: string) => `${hero}'s adventure`,
    coverLabel: (total: number) => `Cover · ${total} pages`,
    spreadLabel: (from: number, to: number, total: number) => `Pages ${from}–${to} / ${total}`,
    pageLabel: (page: number, total: number) => `Page ${page} / ${total}`,
    railSpread: (from: number, to: number) => `Pages ${from}–${to}`,
    railPage: (page: number) => `Page ${page}`,
    railCover: "Cover",
    pages: "Book pages",
    previous: "Previous",
    next: "Next",
    previousPage: "Previous page",
    nextPage: "Next page",
    gestureHint: "Swipe, use the arrows, or your keyboard",
    flipAria: (hero: string) => `${hero} — a book you can leaf through`,

    qrTitle: "The adventure does not end here",
    /* Print gets a QR; a screen gets a button, so the two ask for different verbs. */
    backScan: (hero: string) => `Scan and continue ${hero}'s journey in another world.`,
    backTap: (hero: string) => `Continue ${hero}'s journey in another world.`,
    backCta: "A new adventure",

    lockedNote: "The full book is created after payment",
    lockedPagePrefix: "Page ",
    lockedPageSuffix: " is already being prepared for you",
  },

  reader: {
    digitalBook: "'s Digital book",
    library: " library",
    flipPrefix: "Leaf through ",
    flipSuffix: "'s story",
    lead: "On a large screen the book opens as a spread; on a phone it reads one page at a time.",
    ariaLabel: (hero: string) => `${hero}'s full Online Reader`,
    memoryPrefix: "This memory stays with ",
    memorySuffix: " in future stories.",
    worldPassport: "See the world you opened",
    illustrating: {
      atelier: "ADVENTRYA BOOK ATELIER",
      title: "Painting the pictures",
      leadWaiting:
        "We'll show the book once every picture is done, so you meet it finished rather than half-drawn.",
      lead: "The story is written — you can read it below. The pictures arrive one at a time.",
      email: "You can close this page. We'll email you when the book is ready.",
      progress: (done: number, total: number) => `${done} of ${total} pictures`,
      failed: "Some pictures could not be drawn — we're trying again. The story is safe.",
    },
    pdf: {
      building: "Preparing…",
      atelier: "ADVENTRYA PRINT ATELIER",
      title: "Preparing your printable PDF",
      lead: "We're setting the book for print — the download starts on its own.",
      email: "You can close this page. We'll email you when the PDF is ready.",
    },
  },

  map: {
    ariaLabel: (hero: string) => `A living map of ${hero}'s adventures`,
    titleSuffix: "'s map of adventures",
    lead: "Every new book opens another part of the world",
    progress: (unlocked: number, total: number) =>
      unlocked === 2 && total === 6
        ? "Two worlds opened out of six"
        : `${unlocked} of ${total} worlds opened`,
    ofTotal: (total: number) => `of ${total} worlds`,
    memorySaved: "Memory saved",
    nextReady: "The next path is ready",
    unlockWithNewBook: "Open it with a new book",
    panCue: " Drag the map ",
    legendUnlocked: " Opened world",
    legendNext: " Next path",
    legendFuture: " World to open",
    statusCompleted: (index: number) => `Book ${index} · completed`,
    statusNext: "Next chapter · ready",
    statusFuture: "Not yet explored",
  },

  world: {
    welcomeBack: " Welcome back",
    titleSuffix: "'s world comes alive again",
    lead: "Rex remembers every moment. Choose where their path continues.",
    statBook: " book",
    statMemory: " memory",
    statWorld: " world",
    explanation: "You can reread a saved memory or start a new path from here.",
    guidance:
      "Tap any world on the map. Friends already found, memories, and goals carry naturally into the new chapter.",
    readyNote: "Rex is ready — the golden path leads to this world.",
    lockedNote: "This world is still locked. A new book opens its gate.",
    continueFromMemory: "Continue from this memory",
    unlockNext: "Open the next adventure",
    lastMemory: "Last memory",
    lastMemoryNote: "Rex remembers your last adventure.",
    profileLine: (age: number, stories: number) =>
      `Age ${age} · ${stories} completed ${stories === 1 ? "story" : "stories"}`,
    openedStoriesSuffix: "'s stories opened so far",
    archiveNote:
      "Older books stay exactly as they were; new information applies only to future stories.",
  },
};
