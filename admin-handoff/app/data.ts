export type OperationalStatus =
  | "Paid"
  | "Pending"
  | "Failed"
  | "Ready"
  | "Generating"
  | "Review"
  | "Not required"
  | "Ready for print"
  | "In production"
  | "Packed"
  | "Shipped"
  | "Delivered"
  | "Not created"
  | "Delayed"
  | "Cancelled";

export type OrderRecord = {
  id: string;
  bookTitle: string;
  childName: string;
  parentName: string;
  email: string;
  phone: string;
  product: "Digital" | "Printed + Digital" | "Print Upgrade";
  price: string;
  paymentStatus: OperationalStatus;
  generationStatus: OperationalStatus;
  printStatus: OperationalStatus;
  deliveryStatus: OperationalStatus;
  theme: string;
  themeKey: "dino" | "space" | "magic" | "pirate" | "animals" | "air";
  bookLanguage: "ქართული" | "English";
  createdAt: string;
  createdDate: string;
  city: string;
  initial: string;
};

export type OperationalView = {
  stage: string;
  stageDetail: string;
  owner: "Adventrya" | "BookLab" | "Courier" | "Customer";
  nextAction: string;
  issue: string | null;
  issueTone: "danger" | "warning" | "info" | "success";
  sla: string;
};

export function getOrderOperationalView(order: OrderRecord): OperationalView {
  if (order.paymentStatus === "Failed") {
    return {
      stage: "გადახდის შეცდომა",
      stageDetail: "შეკვეთა არ გააქტიურებულა",
      owner: "Customer",
      nextAction: "გადახდის ხელახლა ცდა",
      issue: "გადახდა ვერ დასრულდა",
      issueTone: "danger",
      sla: "დაუყოვნებლივ",
    };
  }

  if (order.generationStatus === "Failed") {
    return {
      stage: "გენერაცია შეჩერებულია",
      stageDetail: "ილუსტრაცია · გვერდი 4",
      owner: "Adventrya",
      nextAction: "შეცდომის შემოწმება",
      issue: "ავტომატური retry უშედეგოა",
      issueTone: "danger",
      sla: "8 წუთი",
    };
  }

  if (order.generationStatus === "Review") {
    return {
      stage: "Admin Review",
      stageDetail: "წიგნის ხარისხის შემოწმება",
      owner: "Adventrya",
      nextAction: "წიგნის შემოწმება",
      issue: "ელოდება ოპერატორს",
      issueTone: "warning",
      sla: "1სთ 12წთ",
    };
  }

  if (order.deliveryStatus === "Delayed") {
    return {
      stage: "მიწოდება დაგვიანებულია",
      stageDetail: "კურიერის ბოლო განახლება 26 ივლისი",
      owner: "Courier",
      nextAction: "კურიერთან გადამოწმება",
      issue: "SLA დარღვეულია",
      issueTone: "danger",
      sla: "34 საათი",
    };
  }

  if (order.printStatus === "Packed") {
    return {
      stage: "შეფუთულია",
      stageDetail: "კურიერის შეკვეთას ელოდება",
      owner: "Adventrya",
      nextAction: "კურიერის შექმნა",
      issue: "დღეს უნდა გაიგზავნოს",
      issueTone: "warning",
      sla: "დღეს",
    };
  }

  if (order.printStatus === "In production") {
    return {
      stage: "იბეჭდება",
      stageDetail: "BookLab-ის წარმოებაშია",
      owner: "BookLab",
      nextAction: "წარმოების დასრულება",
      issue: null,
      issueTone: "success",
      sla: "ვადაში",
    };
  }

  if (order.printStatus === "Ready for print") {
    return {
      stage: "PDF approval",
      stageDetail: "საბეჭდი ვერსია მზადაა",
      owner: "Adventrya",
      nextAction: "PDF-ის დადასტურება",
      issue: "პარტნიორთან ჯერ არ გაგზავნილა",
      issueTone: "info",
      sla: "2 საათი",
    };
  }

  if (order.product === "Digital") {
    return {
      stage: "Digital მზადაა",
      stageDetail: "წიგნი მომხმარებლის ბიბლიოთეკაშია",
      owner: "Customer",
      nextAction: "არ სჭირდება",
      issue: null,
      issueTone: "success",
      sla: "დასრულებულია",
    };
  }

  return {
    stage: "დამუშავება",
    stageDetail: "შემდეგ ეტაპს ელოდება",
    owner: "Adventrya",
    nextAction: "შეკვეთის შემოწმება",
    issue: null,
    issueTone: "info",
    sla: "ვადაში",
  };
}

export const orders: OrderRecord[] = [
  {
    id: "ADV-1048",
    bookTitle: "ზუკა და მზრუნველი რექსი",
    childName: "ზუკა · 3 წლის",
    parentName: "თამარ გოგიშვილი",
    email: "tamar.gogishvili@gmail.com",
    phone: "+995 555 12 34 42",
    product: "Printed + Digital",
    price: "79 ₾",
    paymentStatus: "Paid",
    generationStatus: "Review",
    printStatus: "Not created",
    deliveryStatus: "Not created",
    theme: "დინოზავრები",
    themeKey: "dino",
    bookLanguage: "ქართული",
    createdAt: "დღეს, 14:42",
    createdDate: "2026-07-28",
    city: "თბილისი",
    initial: "ზ",
  },
  {
    id: "ADV-1047",
    bookTitle: "ნინის ვარსკვლავების გზა",
    childName: "ნინი · 6 წლის",
    parentName: "ნათია კაპანაძე",
    email: "natia.kapanadze@icloud.com",
    phone: "+995 599 43 21 18",
    product: "Digital",
    price: "14 ₾",
    paymentStatus: "Paid",
    generationStatus: "Ready",
    printStatus: "Not required",
    deliveryStatus: "Not created",
    theme: "კოსმოსი",
    themeKey: "space",
    bookLanguage: "ქართული",
    createdAt: "დღეს, 13:18",
    createdDate: "2026-07-28",
    city: "ქუთაისი",
    initial: "ნ",
  },
  {
    id: "ADV-1046",
    bookTitle: "ელისოს მაგიური გასაღები",
    childName: "ელისო · 5 წლის",
    parentName: "გიორგი მელაძე",
    email: "giorgi.meladze@gmail.com",
    phone: "+995 577 65 44 41",
    product: "Print Upgrade",
    price: "65 ₾",
    paymentStatus: "Paid",
    generationStatus: "Ready",
    printStatus: "Ready for print",
    deliveryStatus: "Not created",
    theme: "მაგიური სამყარო",
    themeKey: "magic",
    bookLanguage: "ქართული",
    createdAt: "დღეს, 12:55",
    createdDate: "2026-07-28",
    city: "ბათუმი",
    initial: "ე",
  },
  {
    id: "ADV-1045",
    bookTitle: "ლუკა მეკობრეთა კუნძულზე",
    childName: "ლუკა · 8 წლის",
    parentName: "ანა დოლიძე",
    email: "ana.dolidze@gmail.com",
    phone: "+995 551 74 20 06",
    product: "Printed + Digital",
    price: "79 ₾",
    paymentStatus: "Paid",
    generationStatus: "Ready",
    printStatus: "In production",
    deliveryStatus: "Not created",
    theme: "მეკობრეები",
    themeKey: "pirate",
    bookLanguage: "ქართული",
    createdAt: "გუშინ, 18:06",
    createdDate: "2026-07-27",
    city: "თბილისი",
    initial: "ლ",
  },
  {
    id: "ADV-1044",
    bookTitle: "Mia and the Forest Friends",
    childName: "Mia · 7 years",
    parentName: "Sophie Richards",
    email: "sophie.richards@mail.com",
    phone: "+995 568 33 20 17",
    product: "Printed + Digital",
    price: "79 ₾",
    paymentStatus: "Paid",
    generationStatus: "Ready",
    printStatus: "Packed",
    deliveryStatus: "Pending",
    theme: "ცხოველები",
    themeKey: "animals",
    bookLanguage: "English",
    createdAt: "გუშინ, 16:37",
    createdDate: "2026-07-27",
    city: "თბილისი",
    initial: "M",
  },
  {
    id: "ADV-1043",
    bookTitle: "ანდრია ღრუბლებს ზემოთ",
    childName: "ანდრია · 4 წლის",
    parentName: "მარიამ ლომიძე",
    email: "mariam.lomidze@gmail.com",
    phone: "+995 555 80 14 29",
    product: "Digital",
    price: "14 ₾",
    paymentStatus: "Paid",
    generationStatus: "Failed",
    printStatus: "Not required",
    deliveryStatus: "Not created",
    theme: "თვითმფრინავები",
    themeKey: "air",
    bookLanguage: "ქართული",
    createdAt: "გუშინ, 15:12",
    createdDate: "2026-07-27",
    city: "რუსთავი",
    initial: "ა",
  },
  {
    id: "ADV-1042",
    bookTitle: "სანდროს კოსმოსური მეგობარი",
    childName: "სანდრო · 5 წლის",
    parentName: "ეკა ნადირაძე",
    email: "eka.nadiradze@gmail.com",
    phone: "+995 598 27 15 03",
    product: "Printed + Digital",
    price: "63.20 ₾",
    paymentStatus: "Paid",
    generationStatus: "Ready",
    printStatus: "Shipped",
    deliveryStatus: "Delayed",
    theme: "კოსმოსი",
    themeKey: "space",
    bookLanguage: "ქართული",
    createdAt: "22 ივლ, 10:30",
    createdDate: "2026-07-22",
    city: "ზუგდიდი",
    initial: "ს",
  },
];

export const attentionItems = [
  {
    id: "gen-failed",
    title: "ADV-1043 · ილუსტრაციის გენერაცია შეჩერდა",
    description: "გვერდი 4 · ავტომატური retry უშედეგოა",
    time: "8 წუთის წინ",
    tone: "danger",
    href: "/orders/ADV-1043?tab=generation",
  },
  {
    id: "delivery-delayed",
    title: "ADV-1042 · მიწოდება SLA-ს გასცდა",
    description: "ზუგდიდი · კურიერის ბოლო განახლება 26 ივლისი",
    time: "34 წუთის წინ",
    tone: "warning",
    href: "/orders/ADV-1042?tab=fulfillment",
  },
  {
    id: "review",
    title: "7 ახალი წიგნი ელოდება Admin Review-ს",
    description: "ყველაზე ძველი შეკვეთა 1 სთ 12 წთ",
    time: "დღეს",
    tone: "info",
    href: "/production?filter=review",
  },
];

export const navItems = [
  { key: "overview", label: "Overview", href: "/", icon: "grid" },
  { key: "orders", label: "შეკვეთები", href: "/orders", icon: "orders" },
  { key: "production", label: "Book Production", href: "/production", icon: "book" },
  { key: "fulfillment", label: "Print & Delivery", href: "/fulfillment", icon: "truck" },
  { key: "customers", label: "მომხმარებლები", href: "/customers", icon: "users" },
  { key: "promotions", label: "Promotions", href: "/promotions", icon: "tag" },
  { key: "audit", label: "Audit Log", href: "/audit", icon: "audit" },
  { key: "settings", label: "Settings", href: "/settings", icon: "settings" },
] as const;
