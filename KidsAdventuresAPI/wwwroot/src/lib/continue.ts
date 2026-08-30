import type { ContinuationResponse } from "@/lib/api/types";
import type { WorldId } from "@/lib/worlds";

/**
 * Where a parent starting a *new* book goes: the world picker.
 *
 * Not `/create`. Every href in this file used to end in `#preview`, which mounts the stage that
 * fires a generation on sight — so "create a new book" spent money before anyone had chosen a
 * world or said whose book it was. A new book begins with a choice, and the choice lives on
 * `/themes`.
 *
 * The child is carried in the query, because a new book for a child who already exists must
 * arrive at the profile step knowing which child. Without it the journey creates a second copy
 * of the same kid at checkout.
 */
export function newBookHref(
  characterId?: string | null,
  options?: {
    /** Where the picker's own back arrow should return to; see `backHrefFromSearch`. */
    from?: "dashboard" | "world";
    /** Begin a genuinely blank draft — a new child rather than another book for this one. */
    fresh?: boolean;
  },
): string {
  const params = new URLSearchParams();
  if (characterId) params.set("characterId", characterId);
  if (options?.fresh) params.set("new", "1");
  if (options?.from) params.set("from", options.from);
  const query = params.toString();
  return query ? `/themes?${query}` : "/themes";
}

/**
 * Where "another world" goes: the picker, carrying the adventure it continues.
 *
 * Not `/create#preview`. That address mounts the stage that starts writing a book the moment it
 * appears, so the one button on a child's map spent a generation before anybody had said which
 * world it was for — the parent's only choice was made for them, and the screen that took it
 * offered no way out. The prior book and the cast that carries forward travel as query
 * parameters through the picker and land on the questions, where the parent confirms.
 */
export function continueViaPickerHref(options: {
  characterId: string;
  continuesFromBookId?: string | null;
  characterIds?: string[];
}): string {
  const params = new URLSearchParams();
  params.set("characterId", options.characterId);
  if (options.continuesFromBookId) {
    params.set("continuesFromBookId", options.continuesFromBookId);
  }
  if (options.characterIds?.length) {
    params.set("characterIds", options.characterIds.join(","));
  }
  params.set("from", "world");
  return `/themes?${params.toString()}`;
}

/** Builds the `/create` deep-link used when continuing a child's adventure. */
export function buildContinueHref(options: {
  worldId?: string | null;
  characterId?: string | null;
  continuesFromBookId?: string | null;
  characterIds?: string[];
  mode?: "first" | "continue";
}): string {
  const params = new URLSearchParams();
  params.set("mode", options.mode ?? "continue");
  if (options.worldId) {
    params.set("worldId", options.worldId);
    // JourneyScreen currently hydrates from `world` (legacy demo query).
    params.set("world", options.worldId);
  }
  if (options.characterId) params.set("characterId", options.characterId);
  if (options.continuesFromBookId) {
    params.set("continuesFromBookId", options.continuesFromBookId);
  }
  if (options.characterIds?.length) {
    params.set("characterIds", options.characterIds.join(","));
  }
  return `/create?${params.toString()}#preview`;
}

export function continueHrefFromMap(
  continuation: ContinuationResponse | null | undefined,
  characterId: string,
  worldId?: string | null,
): string {
  const ids = continuation?.carryForwardCharacters.map((c) => c.id) ?? [characterId];
  return buildContinueHref({
    mode: "continue",
    worldId: worldId ?? continuation?.suggestedWorldId ?? continuation?.fromWorldId,
    characterId,
    continuesFromBookId: continuation?.fromBookId,
    characterIds: ids,
  });
}

/*
  `firstJourneyHref` used to live here. It built a `/create#preview` link for a book that did not
  exist yet, which is the shape of the bug rather than a helper: a first book has no world, no
  child and nothing to preview. `newBookHref` replaces it.
*/
