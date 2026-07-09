/**
 * Remembers which specific book a per-book checkout is meant to unlock, so the billing
 * success page can auto-start its illustrations after the payment is confirmed.
 */
const KEY = "pendingIllustratePackId";

export function setPendingIllustration(packId: string): void {
  try {
    sessionStorage.setItem(KEY, packId);
  } catch {
    /* sessionStorage unavailable (private mode) — the user can still unlock from My Books. */
  }
}

export function takePendingIllustration(): string | null {
  try {
    const value = sessionStorage.getItem(KEY);
    if (value) sessionStorage.removeItem(KEY);
    return value;
  } catch {
    return null;
  }
}
