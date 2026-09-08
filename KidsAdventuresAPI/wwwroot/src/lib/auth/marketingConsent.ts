/**
 * A yes given before there was an account to give it to.
 *
 * The tick sits beside the terms on the create form, which is stage one of the journey; the
 * sign-in is stage four. So a parent who agrees to hear from us does it several screens before
 * the server has a row to write it on. This holds the answer until there is one.
 *
 * Only a yes is ever kept. An untouched box is not a decision and there is nothing to record —
 * a new account already defaults to no — so writing "false" here would mean a parent who signed
 * in on one device could have a consent they gave on another silently overwritten by a form they
 * simply did not fill in.
 *
 * `localStorage`, not `sessionStorage`: signing in by magic link opens a new tab, and on a phone
 * usually a new window. Same browser profile in the ordinary case; when it is not, there is
 * nothing to read and the account keeps the answer it already had.
 *
 * Cleared as soon as it is spent, so a tick from last week cannot attach itself to a sign-in
 * that has nothing to do with it.
 */

import { updateMarketingConsent } from "@/lib/api/auth";

const KEY = "beki:marketing-consent";

/** Keeps a yes for the sign-in that has not happened yet. A no clears anything held. */
export function rememberMarketingConsent(consent: boolean): void {
  if (typeof window === "undefined") return;
  try {
    if (consent) window.localStorage.setItem(KEY, "1");
    else window.localStorage.removeItem(KEY);
  } catch {
    /* Private mode, or storage the browser refuses. The parent's space can still be used. */
  }
}

/**
 * Sends a held yes now that there is an account, and forgets it either way.
 *
 * Forgotten even when the request fails: a consent this parent gave on a form they have since
 * finished is not worth retrying against every future session, and the switch in their space is
 * one press away. Failures are swallowed for the same reason — nothing here is worth interrupting
 * a sign-in for.
 */
export async function flushMarketingConsent(): Promise<void> {
  if (typeof window === "undefined") return;

  let held = false;
  try {
    held = window.localStorage.getItem(KEY) === "1";
    if (held) window.localStorage.removeItem(KEY);
  } catch {
    return;
  }

  if (!held) return;
  try {
    await updateMarketingConsent(true);
  } catch {
    /* See above. */
  }
}
