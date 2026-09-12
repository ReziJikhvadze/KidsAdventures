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

/**
 * When a parcel sent today would arrive, as dates rather than a count of days.
 *
 * "3 working days" asks the parent to do the arithmetic, and to know it is working days; a date
 * is the answer they wanted. Counted from tomorrow, Monday to Friday, which is how the couriers
 * count. A guess in good faith rather than a promise - the same window the option has always
 * quoted, said as a day.
 */
export function deliveryWindow(
  option: DeliveryOptionId,
  from: Date = new Date(),
): { from: Date; to: Date } {
  const { minDays, maxDays } = DELIVERY[option];
  return { from: addWorkingDays(from, minDays), to: addWorkingDays(from, maxDays) };
}

function addWorkingDays(from: Date, days: number): Date {
  const date = new Date(from.getFullYear(), from.getMonth(), from.getDate());
  let left = days;
  while (left > 0) {
    date.setDate(date.getDate() + 1);
    const day = date.getDay();
    if (day !== 0 && day !== 6) left -= 1;
  }
  return date;
}

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
 * What a parent is given before they have chosen anything: the soonest option the address
 * allows. In Tbilisi that is the three-day window at 7 GEL; everywhere else there is only one
 * option and this returns it.
 *
 * Deliberately not `deliveryOptionsFor(...)[0]`, which is the *free* one and was what an
 * unchosen checkout used to land on. Sold as a keepsake for a birthday, the difference between
 * three days and five is most of what the parent is buying, and the option that answers that
 * was the one they had to notice and press.
 *
 * This is a client-side preference, and the server does not share it: `GeorgianDelivery.DefaultFor`
 * still answers with the free option when a request names none. Nothing relies on the two
 * agreeing, because the checkout never leaves it unnamed — the quote and the order both send the
 * value this module resolved (see CheckoutStage), and the server honours any option the address
 * allows. If a caller is ever added that omits `deliveryOption` on a print order, that caller
 * gets the server's default and not this one.
 */
export function preferredDeliveryOption(...parts: (string | null | undefined)[]): DeliveryOptionId {
  return deliveryOptionsForDisplay(...parts)[0];
}

/**
 * The option to use for this address: what was asked for when the address allows it, and
 * otherwise the one `preferredDeliveryOption` would start them on.
 *
 * The clamp half of this is unchanged and is what the server does too — a parent who picked the
 * free Tbilisi window and then typed an address in Batumi cannot keep it, and is moved to the
 * option that address really has rather than being refused at the till.
 */
export function resolveDeliveryOption(
  requested: DeliveryOptionId | undefined,
  ...parts: (string | null | undefined)[]
): DeliveryOptionId {
  const allowed = deliveryOptionsFor(...parts);
  return requested && allowed.includes(requested) ? requested : preferredDeliveryOption(...parts);
}

/**
 * The same options, in the order a parent reads them: soonest first.
 *
 * Deliberately not the order `deliveryOptionsFor` returns, and this is the whole reason it is a
 * second function rather than a re-sort of the first. That array mirrors the server, whose own
 * first entry is still what an address that names no option is charged - the free one. Sorting
 * the mirror to read better would have silently changed what a request with no option in it
 * costs, which is a different decision from the one below.
 *
 * The checkout does now start a parent on the soonest option rather than the free one, and it
 * does it by NAMING that option on every quote and every order - see `preferredDeliveryOption`,
 * which reads this list's first entry. So the reading order is decided here, the mirror stays a
 * mirror, and the default is a third thing that is written down rather than inherited from
 * either.
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
