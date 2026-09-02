export const dashboard = {
  sidebar: {
    pagingLabel: "Children pages",
    newBook: "Create a new book",
    parentLabel: "Child profiles",
    addChild: "Add a child profile",
    noStoriesYet: "First adventure not started yet",
    storyCount: (count: number) =>
      count === 1 ? "1 completed adventure" : `${count} completed adventures`,
  },

  library: {
    heading: (name: string) => `${name}'s books`,
    bookCount: (count: number) => (count === 1 ? "1 book" : `${count} books`),
    openBook: (title: string) => `Open "${title}"`,
    otherChild: (name: string) =>
      `${name} has no books yet. Another child's books appear when you pick their profile on the left.`,

    read: "Read",
    readAgain: "Read again",
    readMark: "read",
    pdfBusy: "Preparing…",
    drawing: "The book is being drawn…",
    pdfNotReady: "The PDF is still being prepared — try again in a minute.",
    downloadHeld: "The book is in its final check — the download opens shortly.",
    pdfFailed: "The PDF could not be downloaded — please try again shortly.",
    failedTitle: "The book could not finish",
    failedBody:
      "The book generation was interrupted. We are already working on it. Nothing is lost.",
    failedCta: "Contact us",
    stalledNote:
      "The book is taking longer than usual — it is still being drawn and nothing is lost. You can wait here, or check the dashboard later: it will appear there as soon as it is ready.",

    orderPrint: (price: string) => `Print · ${price}`,
    printEdition: "Printed edition",
    printDetail: (tbilisi: string, regions: string) =>
      `Hardcover. ${tbilisi} days in Tbilisi, ${regions} days elsewhere.`,
    printOrdered: "Printed book on its way ✓",

    statusCreated: "Created",
    statusDownloaded: "Downloaded",
    statusPrinted: "Printed",
    statusLabel: "Status",

    pagingLabel: "Library pages",
    pageOf: (page: number, total: number) => `Page ${page} of ${total}`,
  },

  empty: {
    title: (name: string) => ` ${name}'s world is still empty`,
    lead: "The first adventure starts here",
    cta: "Create the first adventure",
    trust: [
      "You see the preview before paying",
      "Details are deleted automatically after 7 days if you do not complete the order",
    ],
  },
};
