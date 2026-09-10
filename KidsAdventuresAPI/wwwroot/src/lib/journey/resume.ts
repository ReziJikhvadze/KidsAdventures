import { heroKeyOf, type PendingRun } from "@/lib/journey/pendingRun";
import type { BookPackage } from "@/lib/pricing";
import { SESSION_KEYS } from "@/lib/storage/session";

/**
 * Enough to find the book again, and nothing about the child.
 *
 * The journey draft is deliberately never written to the device: a name, a date of birth and
 * a photograph must not outlive the tab on a shared computer. But an emailed sign-in link opens
 * in a NEW tab, and that tab used to arrive at the checkout with no world, no child and no
 * preview — auto-placing a digital order that failed with "choose a world", after the auth
 * screen had promised the parent their preview was saved.
 *
 * What is kept is a pointer: the preview run's id (the server row that holds the story, the
 * child's details and the parked portrait), the world, the chosen package, and the saved
 * character's id when there is one. The child's details are read back from the server, by the
 * signed-in parent, when the journey is resumed.
 */
export type JourneyResume = {
  runId: string;
  worldId: string | null;
  bookPackage: BookPackage;
  characterId?: string;
  /**
   * Which child this preview was drawn for, in the form `pendingRun` states it.
   *
   * `characterId` answers the same question for a saved character and nothing at all for a guest,
   * who is most of the people this pointer exists for. Without it the pointer could say "you have
   * a preview" but not "of this child", and the preview screen could not use it to decide whether
   * to rejoin a book or start one - which is the decision that costs money.
   *
   * Optional because pointers written before this existed are still on parents' devices. A
   * missing key is read as "unknown", never as "a different child".
   */
  heroKey?: string;
  storyNotes?: string;
  savedAt: number;
};

/** A resume older than this is a book the parent walked away from, not one they are finishing. */
const RESUME_TTL_MS = 24 * 60 * 60 * 1000;

export function readJourneyResume(): JourneyResume | null {
  try {
    const raw = localStorage.getItem(SESSION_KEYS.journeyResume);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as Partial<JourneyResume>;
    if (typeof parsed.runId !== "string" || typeof parsed.savedAt !== "number") return null;
    if (Date.now() - parsed.savedAt > RESUME_TTL_MS) {
      clearJourneyResume();
      return null;
    }
    return {
      runId: parsed.runId,
      worldId: typeof parsed.worldId === "string" ? parsed.worldId : null,
      bookPackage: parsed.bookPackage === "digital" ? "digital" : "print",
      characterId: typeof parsed.characterId === "string" ? parsed.characterId : undefined,
      heroKey: typeof parsed.heroKey === "string" ? parsed.heroKey : undefined,
      storyNotes: typeof parsed.storyNotes === "string" ? parsed.storyNotes : undefined,
      savedAt: parsed.savedAt,
    };
  } catch {
    return null;
  }
}

export function writeJourneyResume(resume: Omit<JourneyResume, "savedAt">): void {
  try {
    localStorage.setItem(
      SESSION_KEYS.journeyResume,
      JSON.stringify({ ...resume, savedAt: Date.now() } satisfies JourneyResume),
    );
  } catch {
    /* private mode / quota — the journey still works within the tab */
  }
}

/** Updates one or two fields of a stored resume without touching the rest. */
export function patchJourneyResume(patch: Partial<Omit<JourneyResume, "savedAt" | "runId">>): void {
  const current = readJourneyResume();
  if (!current) return;
  writeJourneyResume({ ...current, ...patch });
}

export function clearJourneyResume(): void {
  try {
    localStorage.removeItem(SESSION_KEYS.journeyResume);
  } catch {
    /* ignore */
  }
}

/**
 * The pointer, when it belongs to the book being made right now - and in the shape the preview
 * screen already knows how to rejoin.
 *
 * The same rule `resumablePendingRun` applies, deliberately: a world or a child that *disagrees*
 * rejects the pointer, while a world or child the draft has not filled in yet does not, because
 * that is what an empty draft on a fresh page load looks like. Stating it twice with two different
 * answers is how a screen ends up billing for a book nobody asked for.
 *
 * Why this exists at all: the pending-run id is cleared the moment a preview finishes, so from
 * then on the only record that a paid cover exists on this device is this pointer. The preview
 * screen consulted the cleared one and not this one - so a parent who came back to the preview
 * with their answers still filled in could start, and pay for, a second cover of the same book.
 */
export function resumableJourneyRun(
  hero: { serverId?: string; name: string; birthDate: string },
  worldId: string | null,
): PendingRun | null {
  const resume = readJourneyResume();
  if (!resume) return null;

  if (worldId && resume.worldId && resume.worldId !== worldId) return null;

  const heroKnown = !!hero.serverId || !!hero.name.trim();
  if (heroKnown && resume.heroKey && resume.heroKey !== heroKeyOf(hero)) return null;

  return {
    runId: resume.runId,
    worldId: resume.worldId,
    heroKey: resume.heroKey ?? heroKeyOf(hero),
  };
}
