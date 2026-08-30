import { useMemo } from "react";

import { useT, type Messages } from "@/lib/i18n";

export const WORLD_IDS = [
  "dinosaurs",
  "space",
  "pirates",
  "animals",
  "airplanes",
  "magic",
] as const;

export type WorldId = (typeof WORLD_IDS)[number];

export function isWorldId(value: unknown): value is WorldId {
  return typeof value === "string" && (WORLD_IDS as readonly string[]).includes(value);
}

/** Fixed routes on the progression map, independent of which worlds are unlocked. */
export const STORY_MAP_ROUTES = {
  open: "M250 330 C320 310 360 214 435 130 S560 184 690 280",
  future: "M690 280 C672 344 630 386 610 430 S742 462 850 442",
  futureSky: "M690 280 C760 237 804 168 835 124",
} as const;

export interface World {
  id: WorldId;
  /** Short theme label, e.g. "დინოზავრები". */
  theme: string;
  /** Short, story-led label used only on the first interactive map. */
  mapLabel: string;
  /** Place name, e.g. "დაკარგული ხეობა". */
  place: string;
  /** Locative form used inside generated sentences. */
  into: string;
  /** Title shown on the adventure map node. */
  mapTitle: string;
  chapter: string;
  teaserTitle: string;
  teaserBody: string;
  memoryTitle: string;
  memoryBody: string;
  bookTitle: (hero: string) => string;
  synopsis: (hero: string) => string;
}

function buildWorlds(messages: Messages): World[] {
  return WORLD_IDS.map((id) => ({
    id,
    ...messages.worlds[id],
  }));
}

/**
 * World copy is translated, so it can no longer be a module constant — the value
 * would be frozen to whichever locale happened to be active at import time. These
 * hooks rebuild it from the active catalogue; the returned shapes are unchanged, so
 * call sites keep indexing exactly as before.
 */
export function useWorlds(): World[] {
  const messages = useT();
  return useMemo(() => buildWorlds(messages), [messages]);
}

export function useWorldById(): Record<WorldId, World> {
  const worlds = useWorlds();
  return useMemo(
    () => Object.fromEntries(worlds.map((w) => [w.id, w])) as Record<WorldId, World>,
    [worlds],
  );
}

/** Cover art shipped with the demo, reused for preview and library thumbnails. */
/*
  A world's own painting, used wherever a book has no cover of its own yet.

  Three images shared between six worlds is what this was: pirates borrowed the magic castle,
  animals borrowed the dinosaurs, aeroplanes borrowed space. On the parent's space that showed
  as a card headed "საიდუმლო კუნძული" over a picture of a castle. `public/adventrya/worlds/`
  has had all six since the world art was drawn; nothing was pointing at them.
*/
export const WORLD_COVER_ART: Record<WorldId, string> = {
  dinosaurs: "/adventrya/worlds/dinosaurs.webp",
  space: "/adventrya/worlds/space.webp",
  pirates: "/adventrya/worlds/pirates.webp",
  animals: "/adventrya/worlds/animals.webp",
  airplanes: "/adventrya/worlds/airplanes.webp",
  magic: "/adventrya/worlds/magic.webp",
};
