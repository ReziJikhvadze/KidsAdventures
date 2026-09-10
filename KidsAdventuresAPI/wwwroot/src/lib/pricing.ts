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

/**
 * Delivery inside Georgia, mirroring `Domain/GeorgianDelivery.cs`.
 *
 * The address picks the menu and the parent picks off it: Tbilisi chooses between five working
 * days for nothing and three for 7 GEL, everywhere else is one flat 8 GEL in five to seven. The
 * server is authoritative — every total on the checkout comes back from a quote — and these are
 * here for what the screen has to draw before the first quote lands, exactly as the book prices
 * are.
 */
export const DELIVERY = {
  TbilisiStandard: { priceMinor: 0, minDays: 5, maxDays: 5 },
  TbilisiExpress: { priceMinor: 700, minDays: 3, maxDays: 3 },
  Regional: { priceMinor: 800, minDays: 5, maxDays: 7 },
} as const;

export type DeliveryOptionId = keyof typeof DELIVERY;

/*
  Tbilisi spelled every way a parent might type it, matched the way the server matches it.

  Anything unrecognised is a region, which is the safe direction to be wrong in: it quotes the
  higher price, and a Tbilisi address that is not recognised until the parent finishes typing
  makes the total go down rather than up.
*/
const TBILISI_NAMES = ["თბილისი", "tbilisi", "tiflis"];

export function isTbilisiAddress(...parts: (string | null | undefined)[]): boolean {
  const haystack = parts.filter(Boolean).join(" ").toLowerCase();
  return TBILISI_NAMES.some((name) => haystack.includes(name.toLowerCase()));
}

/** The options this address may choose between, in the order they should be shown. */
export function deliveryOptionsFor(...parts: (string | null | undefined)[]): DeliveryOptionId[] {
  return isTbilisiAddress(...parts) ? ["TbilisiStandard", "TbilisiExpress"] : ["Regional"];
}

/**
 * The option to use for this address: what was asked for when the address allows it, and
 * otherwise the first one it does allow. Same rule as the server, so the two never disagree
 * about what is selected while a quote is in flight.
 */
export function resolveDeliveryOption(
  requested: DeliveryOptionId | undefined,
  ...parts: (string | null | undefined)[]
): DeliveryOptionId {
  const allowed = deliveryOptionsFor(...parts);
  return requested && allowed.includes(requested) ? requested : allowed[0];
}

/**
 * The same options, in the order a parent reads them: soonest first.
 *
 * Deliberately not the order `deliveryOptionsFor` returns, and this is the whole reason it is a
 * second function rather than a re-sort of the first. That array's first entry is the *default* -
 * it is what an address that has not chosen yet is charged, on the server as well as here - and
 * the default is the free one. Sorting that array to read better would quietly move every Tbilisi
 * order onto the seven-lari window.
 *
 * So: the list is ordered here, the money is decided there, and neither can be tidied into the
 * other by accident.
 */
export function deliveryOptionsForDisplay(
  ...parts: (string | null | undefined)[]
): DeliveryOptionId[] {
  return [...deliveryOptionsFor(...parts)].sort(
    (left, right) => DELIVERY[left].minDays - DELIVERY[right].minDays,
  );
}

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
