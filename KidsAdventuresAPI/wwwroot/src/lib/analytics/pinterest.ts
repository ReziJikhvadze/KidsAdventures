declare global {
  interface Window {
    pintrk?: (...args: unknown[]) => void;
  }
}

/** SHA-256 hex digest, used to hash emails client-side before they ever leave the browser. */
async function sha256Hex(value: string): Promise<string | null> {
  if (typeof window === "undefined" || !window.crypto?.subtle) return null;
  const bytes = new TextEncoder().encode(value);
  const digest = await window.crypto.subtle.digest("SHA-256", bytes);
  return Array.from(new Uint8Array(digest))
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("");
}

/**
 * Pinterest Enhanced Match: associate subsequent tag events with the signed-in
 * visitor's email. The email is normalized and SHA-256 hashed in the browser, so
 * the raw address is never transmitted. No-ops when the tag isn't loaded yet or
 * for anonymous visitors.
 */
export async function setPinterestEnhancedMatch(email: string | null | undefined): Promise<void> {
  if (typeof window === "undefined" || typeof window.pintrk !== "function") return;
  if (!email) return;
  const normalized = email.trim().toLowerCase();
  if (!normalized) return;
  const hashed = await sha256Hex(normalized);
  if (!hashed) return;
  window.pintrk("set", { em: hashed });
}
