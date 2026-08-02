/**
 * Every localStorage key that belongs to a signed-in session, in one place.
 *
 * These were previously declared as loose string literals next to whichever module
 * happened to write them, which is how logout came to clear only two of the six: the
 * token and the cached user. The journey draft outlived the session, so signing out
 * and reloading `/create` re-populated the form with the previous parent's child —
 * name, birth date, eye colour, photo and shipping address. On a shared device that
 * is a disclosure of a child's personal data, not a stale cache.
 *
 * Anything listed here is wiped by `clearSessionState()`. Device preferences that
 * carry no personal data (e.g. `storybook-font-size`) are deliberately not listed —
 * they should survive a sign-out.
 */
export const SESSION_KEYS = {
  token: "adventurepacks_token",
  user: "adventurepacks_user",
  journeyDraft: "adventrya-create-draft-v1",
  guestPreviewUsed: "ka_guest_preview_used",
  guestPreviewId: "ka_guest_preview_id",
  guestStoryId: "ka_guest_story_id",
} as const;

/**
 * Broadcast after a wipe so live hooks can drop their in-memory copy.
 *
 * Clearing storage alone is not enough: `useJourneyDraft` reads localStorage once on
 * mount and re-persists on every edit, so signing out while `/create` is open would
 * write the cleared draft straight back. Listeners reset instead.
 */
export const SESSION_CLEARED_EVENT = "adventrya:session-cleared";

/** Drops every session-scoped key. Safe to call when already signed out. */
export function clearSessionState(): void {
  if (typeof window === "undefined") return;
  for (const key of Object.values(SESSION_KEYS)) {
    try {
      localStorage.removeItem(key);
    } catch {
      /* private mode / quota — a key we cannot remove must not abort the rest */
    }
  }
  window.dispatchEvent(new Event(SESSION_CLEARED_EVENT));
}
