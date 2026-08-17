export const dashboard = {
  sidebar: {
    pagingLabel: "Children pages",
    newBook: "Create a new book",
    parentLabel: "Child profiles",
    addChild: "＋ Add a child profile",
    noStoriesYet: "No stories yet",
    storyCount: (count: number) => (count === 1 ? "1 adventure" : `${count} adventures`),
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

  preview: {
    label: "Preview",
    title: "This is what your Parent Dashboard looks like",
    body: "A sample family, not yours. Sign in and this fills with your children, their map and the books you have already made.",
    cta: "Sign in",
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
