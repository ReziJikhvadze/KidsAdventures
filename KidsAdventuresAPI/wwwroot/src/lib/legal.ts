import { BRAND_NAME } from "@/lib/brand";

/**
 * The frame around a legal document, in each language one is written in.
 *
 * Not the interface locale: a reader switching the site to Georgian does not translate an English
 * privacy policy, so its label, its date and its closing line have to stay English while the page
 * they sit on does. `LegalDocument` picks the entry that matches the document's own `lang`.
 */
export const LEGAL_CHROME = {
  ka: {
    eyebrow: "სამართლებრივი",
    lastUpdated: "ბოლო განახლება:",
    questions: "კითხვები გაქვს?",
    contact: "დაგვიკავშირდი",
    date: "2 სექტემბერი 2026",
  },
  en: {
    eyebrow: "Legal",
    lastUpdated: "Last updated:",
    questions: "Questions?",
    contact: "Contact us",
    date: "2 September 2026",
  },
} as const;

export const LEGAL_CONTACT_EMAILS = ["support@adventrya.com", "info@adventrya.com"] as const;

/** Shown on Terms, Privacy, and legal footers. */
export const LEGAL_CONTACT_EMAIL = LEGAL_CONTACT_EMAILS.join(" or ");

export const LEGAL_OPERATOR_NAME = BRAND_NAME;

export const LEGAL_WEBSITE = "https://beki.ge";
