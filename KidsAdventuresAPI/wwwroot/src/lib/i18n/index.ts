import { ka } from "./ka";

/**
 * Adventrya ships a Georgian-only interface. The demo's header language control
 * changes the *book* language, not the platform language, which is why there is
 * no locale switching here. The catalogue is still namespaced by locale so a
 * second interface language can be added without touching call sites.
 */
export type UiLocale = "ka";

export const UI_LOCALE: UiLocale = "ka";

const catalogues = { ka } as const;

export type Messages = (typeof catalogues)[UiLocale];

export const t: Messages = catalogues[UI_LOCALE];

/** Languages a book itself can be generated in. */
export const BOOK_LANGUAGES = [
  { code: "ka", label: "ქართული" },
  { code: "en", label: "English" },
] as const;

export type BookLanguage = (typeof BOOK_LANGUAGES)[number]["code"];

export const DEFAULT_BOOK_LANGUAGE: BookLanguage = "ka";

export function bookLanguageLabel(code: string): string {
  return BOOK_LANGUAGES.find((l) => l.code === code)?.label ?? code;
}

const gelFormatter = new Intl.NumberFormat("ka-GE", {
  minimumFractionDigits: 0,
  maximumFractionDigits: 2,
});

/** Renders an amount held in tetri (GEL minor units) as e.g. "14" or "12.5". */
export function formatGelAmount(minorUnits: number): string {
  return gelFormatter.format(minorUnits / 100);
}

/** Renders an amount held in tetri as e.g. "14 ₾". */
export function formatGel(minorUnits: number): string {
  return `${formatGelAmount(minorUnits)} ₾`;
}

/**
 * Georgian mobile numbers are nine digits and conventionally start with 5.
 * Stored and sent to the API in +995XXXXXXXXX form.
 */
export function normalizeGeorgianPhone(input: string): string | null {
  const digits = input.replace(/\D/g, "").replace(/^995/, "");
  return /^5\d{8}$/.test(digits) ? `+995${digits}` : null;
}

export function formatGeorgianPhone(input: string): string {
  const digits = input.replace(/\D/g, "").replace(/^995/, "").slice(0, 9);
  const parts = [digits.slice(0, 3), digits.slice(3, 5), digits.slice(5, 7), digits.slice(7, 9)];
  return parts.filter(Boolean).join(" ");
}
