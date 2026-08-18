/**
 * Beki prices, held in tetri (GEL minor units) so no float arithmetic ever
 * touches a total. The server is authoritative; these values drive display and
 * the optimistic total shown before the order is created.
 */
export const PRICES = {
  digital: 1400,
  print: 7900,
  /** Difference charged when a customer upgrades an existing digital book. */
  printUpgrade: 6500,
} as const;

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
  tbilisi: "4–5",
  regions: "5–8",
} as const;
