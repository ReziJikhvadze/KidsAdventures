/**
 * Where a magic link should land, kept on this device as well as in the link.
 *
 * The address in the email is the authority: the server writes `next=` into it from the return
 * path the panel asked for, and the landing page follows that. This is the answer for the link
 * that arrives without one — a mail client that rewrote the query, a link forwarded by hand, a
 * deployment whose configured base URL swallowed it. The landing used to fall back to the
 * checkout for every such case, which put a parent who had pressed "my space" into the middle
 * of buying a book.
 *
 * `localStorage`, not `sessionStorage`: the link is usually opened from a mail app, which on a
 * phone is a new tab and sometimes a new window. It is still the same browser profile in the
 * common case, and when it is not there is nothing here to read and the default stands.
 *
 * Written when the link is requested and cleared when it is used, so a stale path from last
 * week cannot redirect a sign-in that has nothing to do with it.
 */

const KEY = "beki:magic-return";

/**
 * The answer to the first read, kept for the life of the page.
 *
 * The landing route is server-rendered and hydrated, and a hydration mismatch unmounts and
 * remounts it — asking storage a second time would find the key already cleared and send the
 * parent somewhere other than the first answer did.
 */
let taken: string | null | undefined;

/**
 * Mirrors the server's own guard: same-origin relative paths only, never `//host`, and no
 * control characters — a path carrying one is not a path.
 */
export function isSafeReturnPath(value: string | null | undefined): value is string {
  if (typeof value !== "string" || !value.startsWith("/") || value.startsWith("//")) return false;
  for (const char of value) {
    const code = char.codePointAt(0) ?? 0;
    if (code < 0x20 || code === 0x7f) return false;
  }
  return true;
}

export function rememberMagicReturnPath(path: string | undefined): void {
  if (typeof window === "undefined" || !isSafeReturnPath(path)) return;
  // A new request supersedes whatever the last read settled on, for the dev flow where the link
  // is followed in the same tab it was asked for.
  taken = undefined;
  try {
    window.localStorage.setItem(KEY, path);
  } catch {
    /* Private mode, or storage the browser refuses. The link's own `next=` still works. */
  }
}

/** Reads the remembered path and forgets it, so it is only ever spent on the link it belongs to. */
export function takeMagicReturnPath(): string | null {
  if (taken !== undefined) return taken;
  if (typeof window === "undefined") return null;
  try {
    const stored = window.localStorage.getItem(KEY);
    window.localStorage.removeItem(KEY);
    taken = isSafeReturnPath(stored) ? stored : null;
  } catch {
    taken = null;
  }
  return taken;
}
