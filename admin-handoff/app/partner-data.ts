export type PartnerOrderReference = {
  orderId: string;
  productionTitle: string;
  themeKey: "dino" | "space" | "magic" | "pirate" | "animals" | "air";
  coverInitial: string;
  quantity: number;
  trimSize: string;
  pageCount: string;
  binding: string;
  colorProfile: string;
};

/**
 * Deliberately restricted production DTO.
 * It contains no parent account, phone, email, address, payment, interests,
 * Extra Wish, or child profile metadata.
 */
export const partnerOrderReferences: PartnerOrderReference[] = [
  {
    orderId: "ADV-1046",
    productionTitle: "ელისოს მაგიური გასაღები",
    themeKey: "magic",
    coverInitial: "ე",
    quantity: 1,
    trimSize: "210 × 210 mm",
    pageCount: "ყდა + 7 გვერდი",
    binding: "Hardcover · სქელი ყდა",
    colorProfile: "CMYK · 300 DPI",
  },
  {
    orderId: "ADV-1045",
    productionTitle: "ლუკა მეკობრეთა კუნძულზე",
    themeKey: "pirate",
    coverInitial: "ლ",
    quantity: 1,
    trimSize: "210 × 210 mm",
    pageCount: "ყდა + 7 გვერდი",
    binding: "Hardcover · სქელი ყდა",
    colorProfile: "CMYK · 300 DPI",
  },
  {
    orderId: "ADV-1044",
    productionTitle: "Mia and the Forest Friends",
    themeKey: "animals",
    coverInitial: "M",
    quantity: 1,
    trimSize: "210 × 210 mm",
    pageCount: "Cover + 7 pages",
    binding: "Hardcover",
    colorProfile: "CMYK · 300 DPI",
  },
  {
    orderId: "ADV-1048",
    productionTitle: "ზუკა და მზრუნველი რექსი",
    themeKey: "dino",
    coverInitial: "ზ",
    quantity: 1,
    trimSize: "210 × 210 mm",
    pageCount: "ყდა + 7 გვერდი",
    binding: "Hardcover · სქელი ყდა",
    colorProfile: "CMYK · 300 DPI",
  },
];

export function getPartnerOrderReference(orderId: string) {
  return partnerOrderReferences.find((item) => item.orderId === orderId);
}
