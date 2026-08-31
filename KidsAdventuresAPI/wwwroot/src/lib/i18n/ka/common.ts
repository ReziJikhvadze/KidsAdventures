export const common = {
  brand: "Beki",
  brandTagline: "stories that remember",
  currencySymbol: "₾",

  states: {
    dashboardFailed: "Dashboard ვერ ჩაიტვირთა.",
    loading: "იტვირთება…",
    bookFailed: "წიგნი ვერ ჩაიტვირთა.",
  },

  actions: {
    back: "უკან",
    backLink: "უკან დაბრუნება",
    cancel: "გაუქმება",
    close: "დახურვა",
    /* The button under the child's details. It said "close", which described the dialog rather
       than the act: a parent filling in their child's name is keeping it, not dismissing it. */
    remember: "დამახსოვრება",
    change: "შეცვლა",
    remove: "წაშლა",
    add: "დამატება",
    added: "დამატებულია",
    apply: "გამოყენება",
    seeAll: "ყველას ნახვა",
    checking: "ვამოწმებთ…",
    signOut: "გასვლა",
    previous: "წინა",
    next: "შემდეგი",
  },

  labels: {
    required: "აუცილებელი",
    optional: "არასავალდებულო",
    optionalSuffix: " · არასავალდებულო",
    name: "სახელი",
    birthDate: "დაბადების თარიღი",
    email: "ელფოსტა",
    phone: "ტელეფონის ნომერი",
    or: "ან",
    and: "და",
    andSpaced: " & ",
  },

  nav: {
    home: "მთავარი",
    homeAria: "Beki მთავარი",
    primaryNav: "მთავარი ნავიგაცია",
    books: "წიგნები",
    howItWorks: "როგორ მუშაობს",
    pricing: "ფასები",
    faq: "FAQ",
    childWorld: "ბავშვის სამყარო",
    mySpace: "ჩემი სივრცე",
    myWorld: "ჩემი სამყარო",
    /* The header and footer name the act, not a possession: nobody has a world yet. */
    chooseWorld: "სამყაროს არჩევა",
    createBook: "შექმენი წიგნი ",
    changeLanguage: "ენის შეცვლა",
    georgian: "ქართული",
    parentSpace: "მშობლის სივრცე",
    openDashboard: "მშობლის სივრცის გახსნა",
    myCabinet: "ჩემი კაბინეტი",
  },

  footer: {
    blurb:
      "პერსონალიზებული ისტორიები, სადაც ყოველი ახალი თავგადასავალი ბავშვის სამყაროს აგრძელებს.",
    help: "დახმარება",
    contact: "კონტაქტი",
    delivery: "მიწოდება",
    product: "პროდუქტი",
    myWorld: "ჩემი სამყარო",
    chooseWorld: "სამყაროს არჩევა",
    reader: "Online Reader",
    adventureMap: "Adventure Map",
    legal: "კონფიდენციალურობა · წესები და პირობები",
    madeIn: "შექმნილია საქართველოში",
  },

  /** Relationship chips offered when adding a supporting character. */
  relationships: [
    "და",
    "ძმა",
    "დედა",
    "მამა",
    "ბებია",
    "ბაბუა",
    "მეგობარი",
    "ბიძაშვილი",
    "ძაღლი",
    "კატა",
    "სხვა",
  ],

  genders: {
    girl: "გოგო",
    boy: "ბიჭი",
  },

  date: {
    day: "დღე",
    month: "თვე",
    year: "წელი",
    months: [
      "იანვარი",
      "თებერვალი",
      "მარტი",
      "აპრილი",
      "მაისი",
      "ივნისი",
      "ივლისი",
      "აგვისტო",
      "სექტემბერი",
      "ოქტომბერი",
      "ნოემბერი",
      "დეკემბერი",
    ],
  },

  eyeColors: {
    brown: "ყავისფერი",
    blue: "ლურჯი",
    green: "მწვანე",
    grey: "ნაცრისფერი",
  },

  fallbackHeroName: "შენი გმირი",

  /*
    The contact page was written half in each language: a Georgian heading over an English form.
    Both are here now, so the page follows the language the visitor chose like every other screen.
  */
  contactForm: {
    eyebrow: "კონტაქტი",
    title: "მოხარული ვიქნებით შენი წერილის",
    lead: "კითხვები ამბების, ბეჭდვის ან მიწოდების შესახებ? მოგვწერე — პასუხს ელფოსტაზე მიიღებ.",
    nameLabel: "შენი სახელი",
    namePlaceholder: "მაგ. ანა",
    emailLabel: "ელფოსტა",
    emailPlaceholder: "you@example.com",
    messageLabel: "შეტყობინება",
    messagePlaceholder: "როგორ დაგეხმაროთ?",
    send: "გაგზავნა",
    sending: "იგზავნება…",
    sentTitle: "შეტყობინება გაიგზავნა",
    sentBody: (brand: string) =>
      `გმადლობთ, რომ მოგვწერეთ. ${brand}-ის გუნდი მითითებულ ელფოსტაზე გიპასუხებთ.`,
    sendAnother: "კიდევ ერთი შეტყობინების გაგზავნა",
    failed: "შეტყობინება ვერ გაიგზავნა.",
  },
};
