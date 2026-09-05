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
const AUTHORED_ROUTES: Record<"desktop" | "mobile", Record<SelectorWorldId, FlightRoute>> = {
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

/**
 * Where each island actually sits on the painting, as a share of the frame.
 *
 * One set of numbers, because there were four and they disagreed. The clickable box was placed
 * by the stylesheet, the star's landing was the last point of a hand-drawn path, the arrival
 * flare was its own pair, and the focus mask a third — all measured separately against the same
 * artwork, all drifting a few percent apart. That drift is what a parent sees as a star landing
 * beside an island rather than on it, and as a tap on one world selecting its neighbour.
 *
 * `cx`/`cy` is the middle of the island. `w`/`h` is how much of the frame it may be tapped by —
 * sized to the island's own footprint, not padded out until the boxes touch, because a box that
 * reaches its neighbour is a box that steals its taps.
 *
 * The art is drawn with background-size: 100% 100% into a frame of the master's own aspect
 * ratio, so these percentages are the painting's own coordinates. If the art is repainted, they
 * are measured again.
 */
export type IslandSpot = {
  readonly cx: number;
  readonly cy: number;
  readonly w: number;
  readonly h: number;
};

/**
 * Where each island is painted, for cropping a portrait of it.
 *
 * Deliberately not `ISLAND_SPOTS`. Those boxes are tap targets on the map: they are tuned so a
 * finger aimed at one island cannot land on its neighbour, which makes them tighter than the
 * painting and centred on the pin rather than on the picture. Cropping to them framed the
 * dinosaur with its head cut off, the observatory with the ocean island leaning into the corner,
 * and the forest sitting in the top-left with a quarter of the frame empty.
 *
 * These are the pictures instead: each island drawn round on the master itself, edge to edge,
 * waterfalls and hanging roots included. The set before this one was estimated from the map's
 * hotspots and was wrong in both ways the owner reported — the forest and the ocean sat off to
 * one side of their panels, and half the frames carried a corner of the island next door.
 *
 * The numbers are read as: `cx`/`cy` the centre and `w`/`h` the size, each as a percentage of the
 * master — so `w` is of 1672 and `h` of 941, and the two are not the same unit. An island's shape
 * on screen is `w / h * 1672 / 941`, which is what the panel is sized by. Only `WorldArtPanel`
 * reads them, so the map's hotspots and the star's flight are untouched.
 *
 * Measured against `world-map-desktop-master.webp`. Repaint the master and measure again.
 */
export const ISLAND_FRAMES: Record<SelectorWorldId, IslandSpot> = {
  clouds: { cx: 30.9, cy: 21.8, w: 19.1, h: 23.6 },
  space: { cx: 69.1, cy: 21.9, w: 20.3, h: 35.7 },
  animals: { cx: 14.9, cy: 42.4, w: 21.1, h: 28.7 },
  ocean: { cx: 84.1, cy: 43.5, w: 24.9, h: 18.5 },
  kingdom: { cx: 24.7, cy: 77.2, w: 20.6, h: 29 },
  dinosaurs: { cx: 71.8, cy: 71.3, w: 18.8, h: 36.4 },
};

export const ISLAND_SPOTS: Record<"desktop" | "mobile", Record<SelectorWorldId, IslandSpot>> = {
  desktop: {
    clouds: { cx: 30.5, cy: 21, w: 23, h: 23 },
    space: { cx: 71.5, cy: 23, w: 23, h: 19 },
    animals: { cx: 16.5, cy: 48.5, w: 24, h: 25 },
    ocean: { cx: 84, cy: 50, w: 24, h: 23 },
    kingdom: { cx: 25, cy: 75, w: 26, h: 27 },
    dinosaurs: { cx: 71.5, cy: 78, w: 26, h: 25 },
  },
  mobile: {
    clouds: { cx: 24, cy: 17, w: 39, h: 18 },
    space: { cx: 75, cy: 20, w: 44, h: 16 },
    animals: { cx: 23, cy: 39, w: 36, h: 20 },
    ocean: { cx: 77, cy: 41, w: 40, h: 19 },
    kingdom: { cx: 22, cy: 66, w: 40, h: 21 },
    dinosaurs: { cx: 78, cy: 69, w: 40, h: 19 },
  },
};

/**
 * Moves a hand-drawn flight so it ends on the island rather than near it.
 *
 * The curve is the handoff's and worth keeping — it arcs away from Beki before turning, which a
 * straight line between two points does not. Only its arrival is wrong, so the last cubic is
 * translated onto the true centre and its control points dragged along, most at the end and
 * least at the start, which bends the final approach without disturbing the shape of the flight.
 */
function landOn(path: string, [cx, cy]: readonly [number, number]): string {
  const numbers = path.match(/-?\d+(?:\.\d+)?/g);
  if (!numbers || numbers.length < 8) return path;

  const values = numbers.map(Number);
  const endX = values[values.length - 2];
  const endY = values[values.length - 1];
  const dx = cx - endX;
  const dy = cy - endY;

  // The last three pairs are the final segment's two controls and its endpoint.
  const weights = [0.35, 0.85, 1];
  for (let pair = 0; pair < weights.length; pair++) {
    const xi = values.length - 2 * (weights.length - pair);
    values[xi] += dx * weights[pair];
    values[xi + 1] += dy * weights[pair];
  }

  let index = 0;
  return path.replace(/-?\d+(?:\.\d+)?/g, () => {
    const next = values[index++];
    return String(Math.round(next * 100) / 100);
  });
}

/** The routes as authored, retargeted onto the measured islands. */
export const FLIGHT_ROUTES: Record<
  "desktop" | "mobile",
  Record<SelectorWorldId, FlightRoute>
> = Object.fromEntries(
  (["desktop", "mobile"] as const).map((variant) => [
    variant,
    Object.fromEntries(
      SELECTOR_WORLDS.map((world) => {
        const authored = AUTHORED_ROUTES[variant][world.id];
        const spot = ISLAND_SPOTS[variant][world.id];
        const end = [spot.cx, spot.cy] as const;
        return [world.id, { path: landOn(authored.path, end), start: authored.start, end }];
      }),
    ),
  ]),
) as Record<"desktop" | "mobile", Record<SelectorWorldId, FlightRoute>>;

/** The two approved masters, landscape and portrait. */
export const SELECTOR_ART: Record<"desktop" | "mobile", string> = {
  desktop: "/adventrya/world-selector/world-map-desktop-master.webp",
  mobile: "/adventrya/world-selector/world-map-mobile-master.webp",
};
