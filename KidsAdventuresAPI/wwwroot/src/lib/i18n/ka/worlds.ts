/**
 * Copy for the six Beki worlds.
 *
 * One world, one name. `mapLabel`, `place`, `mapTitle`, `chapter` and `memoryTitle` all name the
 * same place, and they used to disagree: the magic world was "სინათლის კარიბჭე" on the map and
 * "სინათლის ქალაქი" in the parent's space, and the dinosaur valley answered to three different
 * names depending on the screen. A reader met them as separate places. They are now the same
 * string everywhere, and anything new that names a world should take it from `mapTitle`.
 *
 * The fields that are deliberately *not* that name, and why:
 *   - `theme` is the category word ("დინოზავრები"), not the place.
 *   - `into` is the locative form used inside generated sentences, which is why it cannot be
 *     derived from `theme` by concatenation in Georgian.
 *   - `bookTitle` is the title of a book and declines the name into the sentence.
 *   - `teaserTitle` is the headline of the teaser page. Where it read as a second name for the
 *     place it was made to agree; where it is a phrase of its own it was left alone.
 */
export const worlds = {
  dinosaurs: {
    theme: "დინოზავრები",
    mapLabel: "დაკარგული ხეობა",
    place: "დაკარგული ხეობა",
    into: "დინოზავრებთან",
    mapTitle: "დაკარგული ხეობა",
    chapter: "თავი I · დაკარგული ხეობა",
    bookTitle: (hero: string) => `${hero} დაკარგულ ხეობაში`,
    synopsis: (hero: string) => "უცნაური კვალი. ძველი მეგობარი. ერთი დიდი თავგადასავალი.",
    teaserTitle: "უძველესი მეგობრობა",
    teaserBody: "ბავშვი შეხვდება რექსს და ერთად აღმოაჩენენ დაკარგულ ხეობას.",
    memoryTitle: "დაკარგული ხეობა",
    memoryBody:
      "აქ მოხდა პირველი შეხვედრა რექსთან — მეგობართან, რომელიც ყველა ახალ თავში გაჰყვება.",
  },
  space: {
    theme: "კოსმოსი",
    mapLabel: "ვარსკვლავების გზა",
    place: "ვარსკვლავების გზა",
    into: "ვარსკვლავებს შორის",
    mapTitle: "ვარსკვლავების გზა",
    chapter: "თავი II · ვარსკვლავების გზა",
    bookTitle: (hero: string) => `${hero} ვარსკვლავების გზაზე`,
    synopsis: (hero: string) => "ძველი მეგობრები. ახალი სამყარო. გზა ვარსკვლავებისკენ.",
    teaserTitle: "ვარსკვლავებს მიღმა",
    teaserBody: "შორეული პლანეტები, ვარსკვლავური რუკები და მეგობარი გზად.",
    memoryTitle: "ვარსკვლავების გზა",
    memoryBody: "აქ იპოვეს დაკარგული ვარსკვლავი და რუკაზე ახალი გზა გაანათეს.",
  },
  pirates: {
    theme: "მეკობრეები",
    mapLabel: "საიდუმლო კუნძული",
    place: "საიდუმლო კუნძული",
    into: "მეკობრეებთან",
    mapTitle: "საიდუმლო კუნძული",
    chapter: "თავი I · საიდუმლო კუნძული",
    bookTitle: (hero: string) => `${hero} და მბრწყინავი კუნძულის საიდუმლო`,
    synopsis: (hero: string) =>
      `ძველი ოქროსფერი რუკა ${hero}-ს ზღვაში დამალულ კუნძულამდე მიიყვანს, სადაც ყოველი ნაბიჯი ახალ საიდუმლოს ხსნის.`,
    teaserTitle: "საიდუმლო კუნძული",
    teaserBody: "ძველი რუკა ზღვაში დამალულ ამბავთან მიიყვანს.",
    memoryTitle: "საიდუმლო კუნძული",
    memoryBody: "ძველი ოქროსფერი რუკა ზღვაში დამალულ კუნძულზე მიუთითებს.",
  },
  animals: {
    theme: "ცხოველების სამყარო",
    mapLabel: "მოჯადოებული ტყე",
    place: "მოჯადოებული ტყე",
    into: "ჯადოსნურ ტყეში",
    mapTitle: "მოჯადოებული ტყე",
    chapter: "თავი I · მოჯადოებული ტყე",
    bookTitle: (hero: string) => `${hero} და მოჯადოებული ტყის მეგობრები`,
    synopsis: (hero: string) =>
      `${hero} მანათობელ ტყეში შედის, სადაც ყველა ბინადარს თავისი პატარა საიდუმლო აქვს და მეგობრობა ყველაზე მოულოდნელად იბადება.`,
    teaserTitle: "მოჯადოებული ტყე",
    teaserBody: "ტყე, სადაც ყველა ბინადარს თავისი პატარა საიდუმლო აქვს.",
    memoryTitle: "მოჯადოებული ტყე",
    memoryBody: "მანათობელი ტყე თავის საიდუმლოს მომავალ წიგნამდე ინახავს.",
  },
  airplanes: {
    theme: "თვითმფრინავები",
    mapLabel: "ღრუბლების ქალაქი",
    place: "ღრუბლების ქალაქი",
    into: "ღრუბლებს მიღმა",
    mapTitle: "ღრუბლების ქალაქი",
    chapter: "თავი I · ღრუბლების ქალაქი",
    bookTitle: (hero: string) => `${hero} და ღრუბლებს მიღმა დამალული ქალაქი`,
    synopsis: (hero: string) =>
      `${hero}-ის პირველი დიდი ფრენა უცნობი ჰორიზონტისკენ მიდის — იქ, სადაც ღრუბლებს მიღმა მთელი ქალაქია დამალული.`,
    teaserTitle: "ღრუბლებს ზემოთ",
    teaserBody: "პირველი დიდი ფრენა უცნობი ჰორიზონტისკენ.",
    memoryTitle: "ღრუბლების ქალაქი",
    memoryBody: "ღრუბლებს მიღმა კიდევ ერთი გზა ჩანს — მას შემდეგი თავგადასავალი გახსნის.",
  },
  magic: {
    theme: "მაგიური სამყარო",
    mapLabel: "სინათლის ქალაქი",
    place: "სინათლის ქალაქი",
    into: "მაგიის სამყაროში",
    mapTitle: "სინათლის ქალაქი",
    chapter: "თავი I · სინათლის ქალაქი",
    bookTitle: (hero: string) => `${hero} სინათლის ქალაქში`,
    synopsis: (hero: string) => "ერთი პატარა არჩევანი. ერთი დიდი ცვლილება.",
    teaserTitle: "სინათლის ქალაქი",
    teaserBody: "ცოცხალი წიგნები და ქალაქი, რომელიც მხოლოდ მამაცებს ეჩვენება.",
    memoryTitle: "სინათლის ქალაქი",
    memoryBody: "ქალაქის კარიბჭე სახელის გაგონებისას ნელა იწყებს ნათებას.",
  },
};
