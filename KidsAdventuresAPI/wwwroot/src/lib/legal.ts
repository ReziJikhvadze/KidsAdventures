import { BRAND_NAME } from "@/lib/brand";

/** Display date for legal pages — update when you materially change the documents. */
export const LEGAL_LAST_UPDATED = "2 September 2026";

export const LEGAL_CONTACT_EMAILS = ["support@adventrya.com", "info@adventrya.com"] as const;

/** Shown on Terms, Privacy, and legal footers. */
export const LEGAL_CONTACT_EMAIL = LEGAL_CONTACT_EMAILS.join(" or ");

export const LEGAL_OPERATOR_NAME = BRAND_NAME;

export const LEGAL_WEBSITE = "https://beki.ge";
