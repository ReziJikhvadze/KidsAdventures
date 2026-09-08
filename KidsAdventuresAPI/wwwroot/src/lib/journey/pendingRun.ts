import { SESSION_KEYS } from "@/lib/storage/session";

/**
 * A preview the parent walked away from, and which book it belongs to.
 *
 * Leaving the creation screen keeps the run id on the device — that is what lets a return pick up
 * the same book rather than paying for a second one — but the id alone resumed the wrong book:
 * start a book for one child, go back to the dashboard, start one for another, and the loader
 * showed the first child's story as the second's. The world and the hero travel with the id, and
 * a stored run is only rejoined when they match.
 *
 * Shared rather than copied: the dashboard and the child's world both point at the run, and a
 * second reader that expected a bare id read the JSON back as one and pointed at nothing.
 */
export type PendingRun = { runId: string; worldId: string | null; heroKey: string };

/**
 * Which child a run was started for.
 *
 * A saved character is its own id; a child who exists only in this draft is named by what the
 * parent typed, which is the only identity there is before the cabinet has one.
 */
export function heroKeyOf(hero: { serverId?: string; name: string; birthDate: string }): string {
  return hero.serverId ?? `${hero.name.trim().toLowerCase()}|${hero.birthDate}`;
}

// Storage can throw — Safari in private mode is the usual culprit — and a book that is already
// being written should not be lost to a failed write of its own id.
export function readPendingRun(): PendingRun | null {
  if (typeof window === "undefined") return null;
  try {
    const raw = localStorage.getItem(SESSION_KEYS.pendingBookRunId);
    if (!raw) return null;
    // Earlier versions stored the bare id. It belongs to nobody in particular, so it is dropped
    // rather than resumed into whichever book happens to be open now.
    if (!raw.startsWith("{")) return null;
    const parsed = JSON.parse(raw) as Partial<PendingRun>;
    return typeof parsed.runId === "string" && typeof parsed.heroKey === "string"
      ? { runId: parsed.runId, worldId: parsed.worldId ?? null, heroKey: parsed.heroKey }
      : null;
  } catch {
    return null;
  }
}

export function writePendingRun(run: PendingRun): void {
  try {
    localStorage.setItem(SESSION_KEYS.pendingBookRunId, JSON.stringify(run));
  } catch {
    /* ignore */
  }
}

/**
 * The stored run, when it belongs to the book being made right now.
 *
 * "A book" and not "any book": the stored run carries the world and the child it was started
 * for, so a run left behind by another book is not offered as this one. A blank world or a hero
 * the draft does not know yet is not a contradiction — that is exactly the empty draft of a
 * fresh page load — so only a world or a hero that *disagrees* rejects it.
 *
 * Shared between the questions and the preview on purpose. The preview reads it to rejoin a book
 * already being written; the questions read it to know there is one, and to stop a second being
 * started over the top of it. Two copies of this rule would eventually disagree, and the screen
 * that got it wrong would be the one billing for a book nobody asked for.
 */
export function resumablePendingRun(
  hero: { serverId?: string; name: string; birthDate: string },
  worldId: string | null,
): PendingRun | null {
  const pending = readPendingRun();
  if (!pending) return null;
  const heroKnown = !!hero.serverId || !!hero.name.trim();
  if (worldId && pending.worldId !== worldId) return null;
  if (heroKnown && pending.heroKey !== heroKeyOf(hero)) return null;
  return pending;
}

export function clearPendingRun(): void {
  try {
    localStorage.removeItem(SESSION_KEYS.pendingBookRunId);
  } catch {
    /* ignore */
  }
}

/**
 * The saved character a pending run belongs to, when it has one.
 *
 * `heroKeyOf` answers with the cabinet's id for a child the account already holds and with
 * `name|birthDate` for one who exists only in the draft. The separator is what tells them apart,
 * because an id never contains it — and only the first kind is worth putting in a link, where it
 * lets the journey load the child back rather than asking for them again.
 */
export function savedCharacterIdOf(run: PendingRun): string | undefined {
  return run.heroKey.includes("|") ? undefined : run.heroKey;
}
