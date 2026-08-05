export const dashboard = {
  sidebar: {
    pagingLabel: "Children pages",
    newBook: "Create a new book",
    parentLabel: "Child profiles",
    addChild: "＋ Add a child profile",
    noStoriesYet: "No stories yet",
    storyCount: (count: number) => (count === 1 ? "1 adventure" : `${count} adventures`),
    links: {
      home: " Home",
      myBooks: " My books",
      downloads: " Downloads",
    },
    privacy: "Children's details are protected and used only for their own stories.",
  },

  library: {
    heading: (name: string) => `${name}'s stories opened so far`,
    openBook: (title: string) => `Open "${title}"`,
    bookIndex: (index: number) => `Book ${index}`,
    printOrdered: " Printed edition ordered",
    orderPrint: "Order the printed edition · 65 ₾ ",
    formatDigital: "Digital",
    formatBoth: "Digital + Printed",
    pagingLabel: "Library pages",
    pageOf: (page: number, total: number) => `Page ${page} of ${total}`,
  },

  empty: {
    title: (name: string) => ` ${name}'s world is still empty`,
    lead: "The first adventure starts here",
    body: (name: string) =>
      `Create ${name}'s personalised story. The first book brings a first place, a friend, and a memory into their world.`,
    cta: "Create the first adventure",
    trust: [
      " You see the preview before paying",
      " Details are deleted automatically after 7 days if you do not complete the order",
    ],
  },
};
