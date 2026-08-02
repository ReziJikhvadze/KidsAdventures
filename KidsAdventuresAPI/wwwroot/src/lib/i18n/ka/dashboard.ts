export const dashboard = {
  sidebar: {
    parentLabel: "ბავშვის პროფილები",
    addChild: "＋ დაამატე ბავშვის პროფილი",
    noStoriesYet: "ჯერ არ აქვს ისტორია",
    storyCount: (count: number) => (count === 1 ? "1 თავგადასავალი" : `${count} თავგადასავალი`),
    links: {
      home: " მთავარი",
      myBooks: " ჩემი წიგნები",
      downloads: " ჩამოტვირთვები",
    },
    privacy: "ბავშვების მონაცემები დაცულია და გამოიყენება მხოლოდ მათი ისტორიებისთვის.",
  },

  library: {
    heading: (name: string) => `${name}ს უკვე გახსნილი ისტორიები`,
    bookIndex: (index: number) => `წიგნი ${index}`,
    printOrdered: " ბეჭდური ვერსია შეკვეთილია",
    orderPrint: "შეუკვეთე ბეჭდური ვერსია · 65 ₾ ",
    formatDigital: "Digital",
    formatBoth: "Digital + Printed",
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
