import type { ContinuationResponse } from "@/lib/api/types";
import type { WorldId } from "@/lib/worlds";

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

export function firstJourneyHref(worldId: WorldId, characterId?: string | null): string {
  return buildContinueHref({
    mode: "first",
    worldId,
    characterId: characterId ?? undefined,
  });
}
