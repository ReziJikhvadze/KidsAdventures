import { BRAND_NAME } from "@/lib/brand";

/**
 * Who is actually taking the money.
 *
 * The card scheme rules — and Bank of Georgia's own onboarding checklist — require a customer to
 * be able to read the merchant's identity, phone, email and postal address on the site before
 * they pay. This is the one place those facts live, so the footer, the contact page and the
 * policies can never quote three different phone numbers.
 *
 * Blank means "not published yet", and every surface that renders these skips a blank rather
 * than printing an empty label. A placeholder would be worse than an omission here: a made-up
 * address on a payment page is the exact thing the requirement exists to prevent.
 */
/*
  Typed as plain strings rather than left to `as const`. With literal types, an unfilled field is
  the type `""`, so `if (MERCHANT.phone)` narrows it to `never` and the compiler rejects the very
  code that handles the blank — the file would stop compiling the moment somebody filled one in.
*/
type MerchantDetails = {
  legalName: string;
  tradingName: string;
  taxId: string;
  email: string;
  phone: string;
  address: string;
  workingHours: string;
};

export const MERCHANT: MerchantDetails = {
  /** Registered company name, as it appears on the certificate. */
  legalName: "შპს ბეკი ჰოლდინგი",

  /** The name a customer knows us by, and the name on the card statement. */
  tradingName: BRAND_NAME,

  /** Identification number (საიდენტიფიკაციო კოდი), from the BOG merchant registration. */
  taxId: "402377110",

  /** Answered mailbox. Support and legal notices both arrive here, and it is what the site
      sends from — see EmailOptions.FromAddress. */
  email: "info@beki.ge",

  /** In international form, e.g. "+995 5XX XX XX XX". */
  phone: "+995 550 50 15 95",

  /** Street, city and postcode, in Georgian. */
  address: "თბილისი, გროზნოს 11ა",

  /** When someone calling the number above will get an answer. */
  workingHours: "ორშაბათი–პარასკევი, 10:00–18:00",
};

/** The fields with something to show, in the order a contact block reads them. */
export function merchantContactRows(): { label: string; value: string; href?: string }[] {
  const rows: { label: string; value: string; href?: string }[] = [];

  if (MERCHANT.legalName) rows.push({ label: "კომპანია", value: MERCHANT.legalName });
  if (MERCHANT.taxId) rows.push({ label: "საიდენტიფიკაციო კოდი", value: MERCHANT.taxId });
  if (MERCHANT.address) rows.push({ label: "მისამართი", value: MERCHANT.address });
  if (MERCHANT.phone) {
    rows.push({
      label: "ტელეფონი",
      value: MERCHANT.phone,
      // Stripped of spaces so a phone dials it; shown with them so a person can read it.
      href: `tel:${MERCHANT.phone.replace(/\s+/g, "")}`,
    });
  }
  if (MERCHANT.email) {
    rows.push({ label: "ელფოსტა", value: MERCHANT.email, href: `mailto:${MERCHANT.email}` });
  }
  if (MERCHANT.workingHours) rows.push({ label: "სამუშაო საათები", value: MERCHANT.workingHours });

  return rows;
}
