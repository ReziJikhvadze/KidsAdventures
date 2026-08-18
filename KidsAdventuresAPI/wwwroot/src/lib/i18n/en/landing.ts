export const landing = {
  announcement: " Your child's first personalised book — from 14 ₾",
  announcementLink: "See examples ",

  hero: {
    kicker: "Personalised adventures for children",
    titleLine1: "A story of their own,",
    titleEm: "that grows with every book",
    lead: "Create a personalised illustrated book where your child is the hero — and every new adventure continues the story before it.",
    primaryCta: "Create the first adventure ",
    primaryNote: "See the personalised cover and first page for free",
    secondaryCta: " See example books",
    proofDigital: "Digital book",
    proofPrint: "Printed + Digital · delivered",
    floatingNoteOne: "Zuka's companion",
    floatingNoteTwo: "Rex returns in the next book too",
    bookExample: "Example book",
    scroll: "Scroll down to see the books",
    confidence: ["Matched to their age", "Reading level for ages 5–7"],
  },

  books: {
    eyebrow: "See what you get before you create",
    titleLine1: "A book your child will remember",
    titleEm: "as their own",
    titleLine2: " story",
    lead: "Every book has a personalised cover, 7 illustrated story pages, and a QR code to continue the adventure.",
    priceFrom: "from 14 ₾",
    createSimilar: "Create something like this ",
    exampleAlt: (title: string) => `${title} — an example personalised book`,
    examples: [
      {
        theme: "dinosaurs",
        title: "Zuka and the Lost Valley",
        meta: "Friendship · discovery",
        age: "Ages 5–7",
      },
      {
        theme: "space",
        title: "Elene and the Path of Stars",
        meta: "Space · courage",
        age: "Ages 8–10",
      },
      {
        theme: "magic",
        title: "Nita and the City of Light",
        meta: "Magic · kindness",
        age: "Ages 2–4",
      },
    ],
  },

  how: {
    eyebrow: "Three simple steps",
    titleLine1: "From a few details —",
    titleEm: "to a world of their own",
    cta: "Start creating — about 3 minutes ",
    steps: [
      {
        title: "Introduce the little hero",
        body: "Name, date of birth, eye colour, and one clear portrait. Add a second character if you like.",
      },
      {
        title: "Choose their world",
        body: "Pick the first adventure from six themes, and add a short hint if you want to.",
      },
      {
        title: "See the preview and order",
        body: "Before paying you see the personalised cover and first page, choose the format there, and the book is created automatically after that.",
      },
    ],
  },

  memory: {
    eyebrow: " What makes Beki different",
    titleLine1: "The book ends.",
    titleEm: "Their world does not.",
    lead: "Friends made in the first book, the moments that mattered, and your child's goals come back naturally in later stories.",
    chain: [
      { label: "Book 01 · opened", title: "Valley of the Dinosaurs", note: "New friend: Rex" },
      {
        label: "Book 02 · next",
        title: "The World of Space",
        note: "Rex comes along to space too",
      },
    ],
    cta: "Explore Zuka's world ",
    /* A pin on the map shows only the short name of the place. This is what it says to a screen
       reader, which has to hear where the link goes rather than see the island it stands on. */
    mapPin: (world: string) => `Start an adventure — ${world}`,
  },

  worlds: {
    eyebrow: "Six worlds for the first choice",
    titleLine1: "Which door will they open",
    titleEm: "first?",
    lead: "The theme is only the beginning — every child's story is shaped to their age and their special wish.",
  },

  pricing: {
    eyebrow: "A simple choice, with no hidden costs",
    titleLine1: "Choose how this story",
    titleEm: "stays in the family",
    lead: "Both packages contain the same fully personalised story. Only the format differs.",
    popular: "Families' choice",
    digital: {
      name: "Digital",
      note: "Ready to read online",
      features: [
        " Personalised cover and first page free",
        " 7 illustrated story pages",
        " QR code for the next adventure",
        " PDF download",
        " Upgrade to Printed later",
      ],
      cta: "Choose Digital ",
      upgrade: "Upgrade to Printed later: +65 ₾",
    },
    print: {
      name: "Printed + Digital",
      note: "A keepsake you can hold",
      features: [
        " Everything in the Digital package",
        " High-quality printed book",
        " Delivery across Georgia",
      ],
      cta: "Choose Printed ",
      upgrade: "Tbilisi 4–5 days · other cities 5–8 days",
    },
    assurance: [
      {
        title: "Your child's data",
        heading: "Privacy from the start",
        body: "Unpaid draft details and photos are deleted automatically after 7 days.",
      },
      {
        title: "The full book",
        heading: "Created automatically after payment",
        body: "You watch the process on screen, and the Digital edition opens right there when it finishes.",
      },
      {
        title: "Delivery in Georgia",
        heading: "Already included in the price",
        body: "Tbilisi 4–5 days · elsewhere in Georgia 5–8 days.",
      },
    ],
  },

  benefits: {
    eyebrow: "Key advantages",
    items: [
      {
        title: "Your child is the hero",
        body: "Their name, their looks, and if you like a second hero too, woven naturally into the story",
      },
      {
        title: "Matched to their age automatically",
        body: "The reading level suits ages 2–4, 5–7, or 8–10",
      },
      {
        title: "The story remembers everything",
        body: "Characters and memories return in future adventures",
      },
    ],
  },

  voices: {
    eyebrow: "The moment we want to create",
    titleLine1: "When a child sees their own name",
    titleEm: "at the heart of the story",
    quotes: [
      {
        quote:
          "“When she saw her name on the cover she went quiet — then showed it to everyone, one by one, because it was her book.”",
        author: "Mariam · parent of a 5-year-old",
      },
      {
        quote:
          "“The most moving part was Rex coming back in the second story. For Zuka he is a real friend now.”",
        author: "Nino · parent of a 6-year-old",
      },
      {
        quote:
          "“Bedtime reading has become a little ritual. Now he chooses where the next adventure goes.”",
        author: "Giorgi · parent of a 7-year-old",
      },
    ],
    prototypeNote: "Illustrative text demonstrating the product experience",
  },

  faq: {
    eyebrow: "Frequently asked questions",
    titleLine1: "Everything to know before",
    titleEm: "the first adventure",
    contactLink: "Another question? Write to us",
    items: [
      {
        question: "What do I see before paying?",
        answer:
          "A cover created with your child's name and the first illustrated page. The remaining 6 pages are created and opened after a successful payment.",
      },
      {
        question: "How is my child's photo used?",
        answer:
          "The photo is needed so the hero in the illustrations resembles your child. Use a clear, well-lit portrait with the whole face visible.",
      },
      {
        question: "How long until I receive the printed book?",
        answer:
          "About 4–5 days in Tbilisi, and 5–8 days elsewhere in Georgia. Delivery is already included in the 79 ₾.",
      },
      {
        question: "When is the full book created?",
        answer:
          "The full 7-page book begins generating as soon as payment succeeds. You watch the process on screen, and the Digital edition opens there when it is done.",
      },
      {
        question: "Can I print a Digital book later?",
        answer:
          "Yes. Pay only the 65 ₾ difference and your Digital book becomes a Printed edition.",
      },
    ],
  },

  final: {
    eyebrow: " The first chapter begins here",
    titleLine1: "One day childhood ends.",
    titleEm: "Their world stays.",
    lead: "Create a book that delights them today — and brings them home again years from now.",
    note: "Digital from 14 ₾ · takes about 3 minutes to create",
  },
};
