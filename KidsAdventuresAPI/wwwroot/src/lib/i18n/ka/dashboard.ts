export const dashboard = {
  sidebar: {
    pagingLabel: "ბავშვების გვერდები",
    newBook: "ახალი წიგნის შექმნა",
    parentLabel: "ბავშვის პროფილები",
    addChild: "＋ დაამატე ბავშვის პროფილი",
    noStoriesYet: "ჯერ არ აქვს ისტორია",
    storyCount: (count: number) => (count === 1 ? "1 თავგადასავალი" : `${count} თავგადასავალი`),
  },

  library: {
    heading: (name: string) => `${name}ს უკვე გახსნილი ისტორიები`,
    openBook: (title: string) => `გახსენი "${title}"`,
    bookIndex: (index: number) => `წიგნი ${index}`,
    printOrdered: " ბეჭდური ვერსია შეკვეთილია",
    orderPrint: "შეუკვეთე ბეჭდური ვერსია · 65 ₾ ",
    formatDigital: "Digital",
    formatBoth: "Digital + Printed",
    pagingLabel: "წიგნების გვერდები",
    pageOf: (page: number, total: number) => `გვერდი ${page} / ${total}`,
  },

  empty: {
    title: (name: string) => ` ${name}ს სამყარო ჯერ ცარიელია`,
    lead: "პირველი თავგადასავალი აქედან იწყება",
    body: (name: string) =>
      `შექმენი ${name}ს პერსონალიზებული ისტორია. პირველი წიგნი მის სამყაროში პირველ ადგილს, მეგობარსა და მოგონებას გააჩენს.`,
    cta: "შექმენი პირველი თავგადასავალი",
    trust: [
      " Preview-ს გადახდამდე ნახავ",
      " მონაცემები 7 დღეში ავტომატურად წაიშლება, თუ შეკვეთას არ დაასრულებ",
    ],
  },
};
