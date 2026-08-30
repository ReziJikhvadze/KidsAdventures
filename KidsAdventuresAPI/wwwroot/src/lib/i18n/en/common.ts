export const common = {
  brand: "Beki",
  brandTagline: "stories that remember",
  currencySymbol: "₾",

  states: {
    dashboardFailed: "The dashboard could not be loaded.",
    loading: "Loading…",
    bookFailed: "The book could not be loaded.",
  },

  actions: {
    back: "Back",
    backLink: "Go back",
    cancel: "Cancel",
    close: "Close",
    /* The button under the child's details. It said "close", which described the dialog rather
       than the act: a parent filling in their child's name is keeping it, not dismissing it. */
    remember: "Remember",
    change: "Change",
    remove: "Remove",
    add: "Add",
    added: "Added",
    apply: "Apply",
    seeAll: "See all",
    checking: "We're checking…",
    signOut: "Sign out",
    previous: "Previous",
    next: "Next",
  },

  labels: {
    required: "Required",
    optional: "Optional",
    optionalSuffix: " · optional",
    name: "Name",
    birthDate: "Date of birth",
    email: "Email",
    phone: "Phone number",
    or: "or",
    and: "and",
    andSpaced: " & ",
  },

  nav: {
    home: "Home",
    homeAria: "Beki home",
    primaryNav: "Primary navigation",
    books: "Books",
    howItWorks: "How it works",
    pricing: "Pricing",
    faq: "FAQ",
    childWorld: "Child's world",
    mySpace: "My space",
    myWorld: "My world",
    createBook: "Create a book ",
    changeLanguage: "Change language",
    georgian: "English",
    parentSpace: "Parent space",
    openDashboard: "Open the parent space",
    myCabinet: "My cabinet",
  },

  footer: {
    blurb: "Personalised stories where every new adventure carries your child's world forward.",
    help: "Help",
    contact: "Contact",
    delivery: "Delivery",
    product: "Product",
    myWorld: "My world",
    reader: "Online Reader",
    adventureMap: "Adventure Map",
    legal: "Privacy · Terms and conditions",
    madeIn: "Made in Georgia",
  },

  /** Relationship chips offered when adding a supporting character. */
  relationships: [
    "Sister",
    "Brother",
    "Mum",
    "Dad",
    "Grandma",
    "Grandpa",
    "Friend",
    "Cousin",
    "Dog",
    "Cat",
    "Other",
  ],

  genders: {
    girl: "Girl",
    boy: "Boy",
  },

  date: {
    day: "Day",
    month: "Month",
    year: "Year",
    months: [
      "January",
      "February",
      "March",
      "April",
      "May",
      "June",
      "July",
      "August",
      "September",
      "October",
      "November",
      "December",
    ],
  },

  eyeColors: {
    brown: "Brown",
    blue: "Blue",
    green: "Green",
    grey: "Grey",
  },

  fallbackHeroName: "your hero",

  contactForm: {
    eyebrow: "Contact",
    title: "We would love to hear from you",
    lead: "Questions about the stories, printing or delivery? Write to us — we'll reply by email.",
    nameLabel: "Your name",
    namePlaceholder: "e.g. Ana",
    emailLabel: "Email",
    emailPlaceholder: "you@example.com",
    messageLabel: "Message",
    messagePlaceholder: "How can we help?",
    send: "Send message",
    sending: "Sending…",
    sentTitle: "Message sent",
    sentBody: (brand: string) =>
      `Thanks for reaching out. The ${brand} team will get back to you at the email you provided.`,
    sendAnother: "Send another message",
    failed: "Could not send your message.",
  },
};
