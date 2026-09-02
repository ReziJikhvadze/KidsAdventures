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
