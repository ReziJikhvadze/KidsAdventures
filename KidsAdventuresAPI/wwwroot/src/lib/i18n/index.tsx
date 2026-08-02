import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react";

import { en } from "./en";
import { ka } from "./ka";

/**
 * Adventrya ships Georgian and English interfaces.
 *
 * The catalogue used to be a module-level constant, which meant a language control
 * could never do anything: the value was captured at import time and no component
 * re-rendered when it changed. Screens now read their copy through `useT()`, so a
 * locale change propagates like any other context update.
 *
 * `Messages` is derived from the Georgian catalogue, and `catalogues` below is typed
 * with it — so a key added to `ka` and forgotten in `en` is a compile error, not a
 * blank string discovered in production.
 */
export type UiLocale = "ka" | "en";

export type Messages = typeof ka;

const catalogues: Record<UiLocale, Messages> = { ka, en };

export const UI_LOCALES: { code: UiLocale; label: string }[] = [
  { code: "ka", label: "ქართული" },
  { code: "en", label: "English" },
];

export const DEFAULT_UI_LOCALE: UiLocale = "ka";

/**
 * Deliberately not in `SESSION_KEYS`: the chosen interface language is a device
 * preference, not session data, and must survive signing out.
 */
const LOCALE_KEY = "adventrya-ui-locale";

function isUiLocale(value: string | null): value is UiLocale {
  return value === "ka" || value === "en";
}

function readStoredLocale(): UiLocale {
  if (typeof window === "undefined") return DEFAULT_UI_LOCALE;
  try {
    const stored = localStorage.getItem(LOCALE_KEY);
    return isUiLocale(stored) ? stored : DEFAULT_UI_LOCALE;
  } catch {
    return DEFAULT_UI_LOCALE;
  }
}

type LocaleContextValue = {
  locale: UiLocale;
  messages: Messages;
  setLocale: (locale: UiLocale) => void;
};

const LocaleContext = createContext<LocaleContextValue | null>(null);

export function LocaleProvider({ children }: { children: ReactNode }) {
  // Server render and first client paint must agree, so the stored preference is
  // picked up in an effect rather than during the initial render.
  const [locale, setLocaleState] = useState<UiLocale>(DEFAULT_UI_LOCALE);

  useEffect(() => {
    const stored = readStoredLocale();
    if (stored !== DEFAULT_UI_LOCALE) setLocaleState(stored);
  }, []);

  useEffect(() => {
    if (typeof document !== "undefined") {
      document.documentElement.lang = locale;
    }
  }, [locale]);

  const setLocale = useCallback((next: UiLocale) => {
    setLocaleState(next);
    try {
      localStorage.setItem(LOCALE_KEY, next);
    } catch {
      /* private mode — the switch still applies for this session */
    }
  }, []);

  const value = useMemo(
    () => ({ locale, messages: catalogues[locale], setLocale }),
    [locale, setLocale],
  );

  return <LocaleContext.Provider value={value}>{children}</LocaleContext.Provider>;
}

/**
 * The copy for the active locale.
 *
 * Falls back to Georgian rather than throwing when no provider is present, so an
 * isolated render (a test, a stray subtree) shows real copy instead of crashing.
 */
export function useT(): Messages {
  return useContext(LocaleContext)?.messages ?? catalogues[DEFAULT_UI_LOCALE];
}

/** The active locale plus its setter, for the language switcher itself. */
export function useLocale(): { locale: UiLocale; setLocale: (locale: UiLocale) => void } {
  const ctx = useContext(LocaleContext);
  return {
    locale: ctx?.locale ?? DEFAULT_UI_LOCALE,
    setLocale: ctx?.setLocale ?? (() => {}),
  };
}

/** Languages a book itself can be generated in — independent of the interface language. */
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
