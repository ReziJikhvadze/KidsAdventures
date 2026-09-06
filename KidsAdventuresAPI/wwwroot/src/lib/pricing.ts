/**
 * Beki prices, held in tetri (GEL minor units) so no float arithmetic ever
 * touches a total. The server is authoritative; these values drive display and
 * the optimistic total shown before the order is created.
 */
export const PRICES = {
  /*
    TEMPORARY — 1 GEL, matching GelPricing.DigitalMinor on the server while a real card is put
    through the live gateway. Both have to move together: this one is what the site quotes, that
    one is what is charged, and a parent must never be shown a different figure from the one
    that leaves their account. Back to 1400 when the test passes.
  */
  digital: 100,
  /*
    TEMPORARY — 1 GEL, matching GelPricing.PrintMinor while the printed book is put through the
    live gateway too. Back to 7900 with the digital one when the test passes.
  */
  print: 100,
  /** Difference charged when a customer upgrades an existing digital book. */
  printUpgrade: 6500,
  /*
    Gift wrapping, 5 GEL, and only on the printed book — a digital one has no parcel. Matches
    GelPricing.GiftWrapMinor; the server is what actually adds it to a total, and this is here
    so the checkbox can say what it costs before the quote comes back.
  */
  giftWrap: 500,
} as const;

/**
 * The most printed copies one order may carry, matching GelPricing.MaxPrintQuantity. The stepper
 * stops here; the server clamps to the same number, so a client that asks for more is priced for
 * five rather than refused.
 */
export const MAX_PRINT_QUANTITY = 5;

export type BookPackage = "digital" | "print";

export type PurchaseType = "new_book" | "print_upgrade";

export function basePriceMinor(purchaseType: PurchaseType, bookPackage: BookPackage): number {
  if (purchaseType === "print_upgrade") return PRICES.printUpgrade;
  return bookPackage === "print" ? PRICES.print : PRICES.digital;
}

export interface PromoDiscount {
  code: string;
  /** Percent off, 1-100. Mutually exclusive with `full`. */
  percentOff?: number;
  /** Whether the code zeroes the order. */
  full?: boolean;
}

export function discountMinor(base: number, promo: PromoDiscount | null): number {
  if (!promo) return 0;
  if (promo.full) return base;
  if (!promo.percentOff) return 0;
  return Math.round((base * promo.percentOff) / 100);
}

export function totalMinor(base: number, promo: PromoDiscount | null): number {
  return Math.max(0, base - discountMinor(base, promo));
}

export const DELIVERY_DAYS = {
  tbilisi: "2–3",
  regions: "5–7",
} as const;
