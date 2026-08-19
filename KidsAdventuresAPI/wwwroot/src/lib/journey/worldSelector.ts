import type { WorldId } from "@/lib/worlds";

/**
 * The world selector's own names for the six islands, and what each one is in this product.
 *
 * The design handoff arrived with its own vocabulary — `clouds`, `ocean`, `kingdom` — baked into
 * a 996-line stylesheet that positions each hotspot by class name against a fixed painting.
 * Renaming them to match `WorldId` would mean rewriting the delivered CSS, and rewriting a
 * delivered stylesheet is how the next revision of the artwork stops being a drop-in.
 *
 * So the two vocabularies are kept apart and this table is the only place they meet. It is also
 * the only file to touch when new art lands with different island names.
 */
export const SELECTOR_WORLDS = [
  { id: "clouds", worldId: "airplanes" },
  { id: "space", worldId: "space" },
  { id: "animals", worldId: "animals" },
  { id: "ocean", worldId: "pirates" },
  { id: "kingdom", worldId: "magic" },
  { id: "dinosaurs", worldId: "dinosaurs" },
] as const satisfies readonly { id: string; worldId: WorldId }[];

export type SelectorWorldId = (typeof SELECTOR_WORLDS)[number]["id"];

export type FlightRoute = {
  /** SVG path across a 0–100 viewBox, so it scales with the painting rather than the screen. */
  path: string;
  /** Beki's heart, where the star sets off. */
  start: readonly [number, number];
  /** The island it lands on. */
  end: readonly [number, number];
};

/**
 * Where the star flies, per artwork.
 *
 * Verbatim from the handoff. The two paintings are composed differently — the islands sit in
 * different places on the landscape and the portrait masters — so each has its own set, and
 * neither can be derived from the other. Hand-tuned to the art: if the art changes, these are
 * measured again rather than adjusted.
 */
export const FLIGHT_ROUTES: Record<"desktop" | "mobile", Record<SelectorWorldId, FlightRoute>> = {
  desktop: {
    clouds: {
      path: "M49.5 54.7 C46 48 43 42 43 36 C43 29 35 25 29 21",
      start: [49.5, 54.7],
      end: [29, 21],
    },
    space: {
      path: "M49.5 54.7 C54 48 58 42 58 36 C58 29 67 25 75 20",
      start: [49.5, 54.7],
      end: [75, 20],
    },
    animals: {
      path: "M49.5 54.7 C43 56 37 53 32 49 C27 45 22 44 17 44",
      start: [49.5, 54.7],
      end: [17, 44],
    },
    ocean: {
      path: "M49.5 54.7 C58 56 64 52 69 48 C74 44 79 44 84 44",
      start: [49.5, 54.7],
      end: [84, 44],
    },
    kingdom: {
      path: "M49.5 54.7 C46 59 42 64 39 68 C35 72 31 73 27 74",
      start: [49.5, 54.7],
      end: [27, 74],
    },
    dinosaurs: {
      path: "M49.5 54.7 C55 59 59 64 63 67 C67 70 71 71 75 72",
      start: [49.5, 54.7],
      end: [75, 72],
    },
  },
  mobile: {
    clouds: {
      path: "M50.6 59.6 C48 52 43 44 39 36 C35 27 30 20 24 16",
      start: [50.6, 59.6],
      end: [24, 16],
    },
    space: {
      path: "M50.6 59.6 C53 52 58 44 62 36 C67 27 72 20 77 16",
      start: [50.6, 59.6],
      end: [77, 16],
    },
    animals: {
      path: "M50.6 59.6 C43 56 37 50 32 45 C28 42 24 40 20 39",
      start: [50.6, 59.6],
      end: [20, 39],
    },
    ocean: {
      path: "M50.6 59.6 C57 56 63 50 68 45 C72 42 77 40 81 39",
      start: [50.6, 59.6],
      end: [81, 39],
    },
    kingdom: {
      path: "M50.6 59.6 C44 62 38 65 31 67 C28 68 25 69 22 69",
      start: [50.6, 59.6],
      end: [22, 69],
    },
    dinosaurs: {
      path: "M50.6 59.6 C56 62 63 65 69 67 C73 68 77 69 80 69",
      start: [50.6, 59.6],
      end: [80, 69],
    },
  },
};

/** The two approved masters, landscape and portrait. */
export const SELECTOR_ART: Record<"desktop" | "mobile", string> = {
  desktop: "/adventrya/world-selector/world-map-desktop-master.webp",
  mobile: "/adventrya/world-selector/world-map-mobile-master.webp",
};
