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
export function newBookHref(characterId?: string | null): string {
  const params = new URLSearchParams();
  if (characterId) params.set("characterId", characterId);
  const query = params.toString();
  return query ? `/themes?${query}` : "/themes";
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
