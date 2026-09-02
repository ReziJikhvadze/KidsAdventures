/**
 * Every enum the API answers in, in the operator's language.
 *
 * The API speaks in its own vocabulary — order statuses, packages, pipeline names, check ids —
 * because those are the words the database, the supplier's documents and the logs use. The
 * console translates at the edge, and a value none of these tables knows still renders under its
 * raw name, which is exactly the string an operator would need to search the logs for.
 */

export const ORDER_STATUS_TEXT: Record<string, string> = {
  Pending: "მოლოდინში",
  Paid: "გადახდილი",
  Fulfilled: "შესრულებული",
  Failed: "ჩავარდნილი",
  Cancelled: "გაუქმებული",
  Refunded: "დაბრუნებული",
};

export const ORDER_STATUSES = ["Pending", "Paid", "Fulfilled", "Failed", "Cancelled", "Refunded"];

export const PACKAGE_TEXT: Record<string, string> = {
  Print: "ბეჭდური + ციფრული",
  Digital: "ციფრული",
};

export const ORDER_TYPE_TEXT: Record<string, string> = {
  NewBook: "ახალი წიგნი",
  PrintUpgrade: "ბეჭდურზე გადასვლა",
};

export const BOOK_STATUS_TEXT: Record<string, string> = {
  Pending: "რიგში",
  Generating: "იწერება",
  GeneratingStory: "იწერება / იხატება",
  StoryReady: "ტექსტი მზადაა",
  GeneratingPdf: "PDF მზადდება",
  Completed: "დასრულებული",
  Failed: "ჩავარდნილი",
};

export const PRINT_STATUS_TEXT: Record<string, string> = {
  AwaitingPrint: "ბეჭდვის რიგში",
  Printing: "იბეჭდება",
  Shipped: "გზაშია",
  Delivered: "მიწოდებულია",
  Cancelled: "გაუქმებულია",
};

export const PRINT_STATUSES = ["AwaitingPrint", "Printing", "Shipped", "Delivered", "Cancelled"];

export const PIPELINE_TEXT: Record<string, string> = {
  beki: "BEKI",
  legacy: "ძველი (A5)",
};

export const PROVIDER_TEXT: Record<string, string> = {
  Bog: "საქართველოს ბანკი",
  Stripe: "Stripe",
  Promo: "პრომო (უფასო)",
};

export const RESOLUTION_TEXT: Record<string, string> = {
  acknowledged: "ნანახია",
  fixed: "გამოსწორდა",
  wont_fix: "არ გამოსწორდება",
  false_alarm: "ცრუ განგაში",
};

export const CLASS_TEXT: Record<string, string> = {
  all: "ყველა",
  press: "საბეჭდი",
  digital: "ციფრული",
  shared: "საერთო",
  package: "პაკეტი",
};

export const GATE_STATUS_TEXT: Record<string, string> = {
  PASS: "გავიდა",
  FAIL: "ვერ გავიდა",
  NEEDS_HUMAN: "ელოდება ადამიანს",
  UNKNOWN: "უცნობი",
};

/**
 * What each check actually looks at.
 *
 * A check this table has no entry for still renders — under its raw id, which is what an operator
 * would need to search for.
 */
export const CHECK_TEXT: Record<string, { label: string; about: string }> = {
  human_review: {
    label: "ვიზუალური შემოწმება ადამიანის მიერ",
    about: "ოპერატორი ათვალიერებს დახატულ წიგნს და ხელს აწერს კონკრეტულ რენდერს.",
  },
  image_review: {
    label: "სურათების შემოწმება მოდელით",
    about:
      "„ბლოკერი“ — თითოეულ გაშლილ გვერდს მოდელი ათვალიერებს დახატვის შემდეგ. " +
      "„ფლაგი“ (ნაგულისხმევი) — არ ათვალიერებს: გვერდი მიიღება ავტომატური გაზომვებით " +
      "(ნაკეცი, ზომა, ქვითარი), ხოლო ჩანაწერში იწერება, რომ შემოწმება არ ჩატარებულა.",
  },
  image_qa: {
    label: "სურათის ხარისხი",
    about: "ავტომატური შემოწმება პოულობს დამახინჯებულ ან თემას აცდენილ ილუსტრაციას.",
  },
  qa_unreadable: {
    label: "ხარისხის პასუხი ვერ წაიკითხა",
    about: "შემმოწმებელმა გაუგებარი პასუხი დააბრუნა. მტკიცებულება ინახება და შეტყობინებას ერთვის.",
  },
  centre_fold: {
    label: "შუა ნაკეცი",
    about: "გვერდის შუაში, ნაკეცის ხაზზე, მნიშვნელოვანი დეტალი ხომ არ ხვდება.",
  },
  cover_bands: {
    label: "ყდის ზოლები",
    about: "ყდის ზედა და ქვედა ზოლები — სათაური და ლოგო დაშვებულ არეშია თუ არა.",
  },
  name_fidelity: {
    label: "ბავშვის სახელის სისწორე",
    about:
      "ტექსტში და სათაურში ბავშვის სახელი ზუსტად ისეა დაწერილი, როგორც მშობელმა შეიყვანა. " +
      "ბრუნვის დაბოლოება დასაშვებია, სახელის ასოები კი არასდროს იცვლება.",
  },
  publish_after_review: {
    label: "გამოშვება დადასტურების შემდეგ",
    about: "ოპერატორმა დაადასტურა წიგნი, ფაილი კი მშობელთან მაინც ვერ გავიდა.",
  },
  press_file_missing: {
    label: "საბეჭდი ფაილი არ არსებობს",
    about:
      "ბეჭდვის რიგში წიგნია, რომელსაც საბეჭდი ინტერიერი არ აქვს — რიგი საკითხავ ასლს სთავაზობს.",
  },
  admin_regenerate: {
    label: "ხელახლა დახატვა ადმინის მიერ",
    about: "ოპერატორმა წიგნის, გვერდის ან ყდის ხელახლა დახატვა მოითხოვა. აქ ინახება ვინ და რატომ.",
  },
  sweep_buried: {
    label: "გაჩერებული წიგნი დახურა შემმოწმებელმა",
    about: "წიგნი, რომელიც დიდხანს არ პასუხობდა, ჩავარდნილად ჩაითვალა.",
  },
  VISUAL_QA: {
    label: "ვიზუალური QA",
    about: "ყველა გაშლილ გვერდს აქვს თუ არა ხარისხის ჩანაწერი, და რას ამბობს ის.",
  },
  COVER_CONTINUITY: {
    label: "ყდის უწყვეტობა",
    about: "ყდა ერთი მთლიანი ნახატია — წინა, ზურგი და კედელი ერთმანეთს ებმის.",
  },
  INTERIOR_CONTINUITY: {
    label: "შიდა გვერდების უწყვეტობა",
    about: "ყველა გაშლილ გვერდს აქვს კომპოზიციის ქვითარი და მიმოხილვა.",
  },
  TEXT_LAYER: {
    label: "ტექსტის ფენა",
    about: "ტექსტი ცალკე ფენაზეა და განლაგების ქვითარი ემთხვევა დაბეჭდილს.",
  },
  FONT_INTEGRITY: {
    label: "შრიფტების მთლიანობა",
    about: "შრიფტები PDF-შია ჩაშენებული და დამტკიცებული ნაკრებიდანაა.",
  },
  DIGITAL_GEOMETRY: {
    label: "ციფრული ვერსიის გეომეტრია",
    about: "საკითხავი ასლის ზომა და გვერდების რაოდენობა სპეციფიკაციას ემთხვევა.",
  },
  HANDBACK_COMPLETENESS: {
    label: "პაკეტის სისრულე",
    about: "მიმწოდებლისთვის გადასაცემ არქივში ყველა სავალდებულო ფაილია.",
  },
  PRESS_GEOMETRY: {
    label: "საბეჭდი გეომეტრია",
    about: "ბლიდი, ტრიმი და ნაკეცის ხაზები — ტიპოგრაფიის დაშვებებში.",
  },
  PRESS_COLOR: {
    label: "საბეჭდი ფერი (CMYK)",
    about: "ფერები CMYK/FOGRA39-შია და მთლიანი მელნის რაოდენობა ზღვარს არ სცილდება.",
  },
  PRESS_RESOLUTION: {
    label: "საბეჭდი გარჩევადობა",
    about: "სურათების რეალური გარჩევადობა ბეჭდვისთვის საკმარისია.",
  },
  TEXT_COLOR_INTEGRITY: {
    label: "ტექსტის ფერის მთლიანობა",
    about: "შავი ტექსტი ერთ საღებავზეა და ოთხფერიანად არ იბეჭდება.",
  },
  RENDER_VALIDATION: {
    label: "რენდერის ვალიდაცია",
    about: "დაგენერირებული ფაილი მართლა გაიხსნა და შემოწმდა, და არა მხოლოდ ჩაიწერა.",
  },
  QR: {
    label: "QR კოდი",
    about: "წიგნში ზუსტად ერთი QR არის და ის მუშა მისამართზე მიდის.",
  },
  ASSET_LOCK: {
    label: "აქტივების ჩაკეტვა",
    about: "წიგნი დამტკიცებულ შაბლონებსა და აქტივებზეა აწყობილი.",
  },
  EXACT_BEKI: {
    label: "ზუსტი BEKI",
    about: "ჰეშები და გეომეტრია ემთხვევა — ეს ინვარიანტია, გემოვნების საკითხი არა.",
  },
  SINGLE_COVER_MASTER: {
    label: "ერთი ყდის ორიგინალი",
    about: "ყდას ერთი წყარო აქვს და ის სწორი ზომისაა.",
  },
};

export function checkLabel(checkId: string): string {
  return CHECK_TEXT[checkId]?.label ?? checkId;
}

export function label(table: Record<string, string>, value: string | null | undefined): string {
  if (!value) return "—";
  return table[value] ?? value;
}

export function severityText(severity: string): string {
  return severity === "blocker" ? "ბლოკერი" : "ფლაგი";
}

/** The world ids the product uses, named for a list. */
export const WORLD_TEXT: Record<string, string> = {
  dinosaurs: "დინოზავრები",
  space: "კოსმოსი",
  pirates: "მეკობრეები",
  animals: "ცხოველები",
  airplanes: "თვითმფრინავები",
  magic: "ჯადოსნური",
  ocean: "ოკეანე",
  forest: "ტყე",
};
