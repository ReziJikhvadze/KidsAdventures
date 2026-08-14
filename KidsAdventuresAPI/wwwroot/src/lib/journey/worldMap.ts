import type { WorldId } from "@/lib/worlds";

/**
 * The map, as seven pictures the app arranges rather than one it can only crop.
 *
 * It used to be a single painting with the islands fixed inside it. Everything hard about this
 * screen came from that: a phone needed a second painting because cropping a landscape scene
 * threw away the islands at its edges; a desktop column narrowed the frame and pulled every pin
 * off its island; and two islands the painter happened to place close together produced labels
 * that touched, which the layout then had to work around.
 *
 * Now each island is its own transparent sprite and this table says where it goes. A different
 * arrangement for a phone is a column of numbers rather than a second commission, nothing is
 * ever cropped, and an island that sits awkwardly is moved by editing one line.
 *
 * Cut from the delivered sheet by design/tools/split-sprites.py.
 */

/**
 * Three arrangements, one per shape of stage.
 *
 * "wide" is a spread across a tall desktop stage. "tall" is two columns down a phone. "mid" is
 * the one in between and the one that had to be drawn from scratch: a browser window between
 * the two layouts leaves a map band about 280px high and the full width, which is the wrong
 * shape for either. A single row of six across a shallow arc is the arrangement a short wide
 * strip actually wants.
 */
export type MapLayout = "wide" | "mid" | "tall";

export interface IslandPlacement {
  /** Centre of the sprite, as a percentage of the stage. */
  x: number;
  y: number;
  /** Sprite width, as a percentage of the stage width, so it scales with the map. */
  w: number;
  /**
   * Which way the label opens. Islands on the left open right, into the middle; islands on the
   * right open left. A label opening outward would run off the edge of the screen.
   */
  side: "left" | "right";
}

export const ISLAND_SRC: Record<WorldId, string> = {
  space: "/adventrya/worlds/space.webp",
  airplanes: "/adventrya/worlds/airplanes.webp",
  dinosaurs: "/adventrya/worlds/dinosaurs.webp",
  pirates: "/adventrya/worlds/pirates.webp",
  animals: "/adventrya/worlds/animals.webp",
  magic: "/adventrya/worlds/magic.webp",
};

/** The open book the child and Beki stand on, where the path begins. */
export const BOOK_SRC = "/adventrya/worlds/book.webp";

export const ISLAND_LAYOUT: Record<MapLayout, Record<WorldId, IslandPlacement>> = {
  /*
    Two rows of three above the book. The order across the top is the order on the sheet, so
    the picture a parent sees matches the one the artist composed.
  */
  wide: {
    // The rows sit two percent further apart than they look like they need to: the label hangs
    // under its island, and at the tighter spacing it arrived within a few pixels of the island
    // below whenever a hover scaled the one it belongs to.
    space: { x: 16, y: 21, w: 30, side: "left" },
    airplanes: { x: 50, y: 15, w: 31, side: "left" },
    dinosaurs: { x: 84, y: 21, w: 30, side: "right" },
    pirates: { x: 16, y: 54, w: 31, side: "left" },
    animals: { x: 50, y: 48, w: 30, side: "right" },
    magic: { x: 84, y: 54, w: 31, side: "right" },
  },
  /*
    One row of six, on a shallow arc.

    A window between the two layouts is wide but leaves the map only about 280px tall, and two
    rows of anything readable will not fit in that. Six across will: the width is the one thing
    this shape has. The arc keeps it from reading as a filmstrip, and the ends dip so the row
    settles into the corners rather than running out of them.
  */
  mid: {
    space: { x: 10, y: 34, w: 15, side: "left" },
    airplanes: { x: 26, y: 30, w: 15, side: "left" },
    dinosaurs: { x: 42, y: 28, w: 15, side: "left" },
    pirates: { x: 58, y: 28, w: 15, side: "right" },
    animals: { x: 74, y: 30, w: 15, side: "right" },
    magic: { x: 90, y: 34, w: 15, side: "right" },
  },

  /*
    Two by three on a phone, not a zigzag down six rows.

    The zigzag read beautifully on paper and did not fit: the map band on a phone is about 520px
    tall, and six rows of island leave 85px a row for a sprite 130px high. They overlapped, their
    labels landed on the island below, and the book at the bottom disappeared behind the last of
    them. Two columns halve the rows, which is the only thing that buys back the height.
  */
  tall: {
    space: { x: 27, y: 13, w: 36, side: "left" },
    airplanes: { x: 73, y: 13, w: 36, side: "right" },
    dinosaurs: { x: 27, y: 38, w: 36, side: "left" },
    animals: { x: 73, y: 38, w: 36, side: "right" },
    pirates: { x: 27, y: 63, w: 36, side: "left" },
    magic: { x: 73, y: 63, w: 36, side: "right" },
  },
};

/**
 * Where the book sits, and where the route leaves it.
 *
 * pathX/pathY are not the book's centre. The book sprite has a golden path painted into it,
 * rising from the child's feet and off the top edge; a route drawn from the middle of the book
 * started underneath that painted one and ran beside it, so the map had two golden paths
 * disagreeing about where the journey begins. These are the point where the painted one leaves
 * the sprite, which is where the drawn one picks it up.
 */
export const BOOK_LAYOUT: Record<
  MapLayout,
  { x: number; y: number; w: number; pathX: number; pathY: number }
> = {
  wide: { x: 50, y: 84, w: 66, pathX: 54, pathY: 72 },
  mid: { x: 50, y: 78, w: 40, pathX: 52, pathY: 63 },
  tall: { x: 50, y: 88, w: 96, pathX: 55, pathY: 81 },
};

/**
 * The light each world gives off.
 *
 * The sprite carries its own lit emblem — a star over the observatory, a palm over the lagoon —
 * so the app never draws an icon. What it draws is the light: the glow under an island being
 * looked at, the ring around the one that has been chosen, and the route to it.
 */
export interface WorldTint {
  tint: string;
  deep: string;
}

export const WORLD_TINT: Record<WorldId, WorldTint> = {
  space: { tint: "#a889ff", deep: "#3a2470" },
  airplanes: { tint: "#67b6ff", deep: "#173a72" },
  dinosaurs: { tint: "#f0b64a", deep: "#5a3a12" },
  animals: { tint: "#6fd07a", deep: "#1c4a28" },
  pirates: { tint: "#4fd6e0", deep: "#10464f" },
  magic: { tint: "#ffa24a", deep: "#6a2f10" },
};

/**
 * The route from the book to an island, in the SVG's own 1000x650 space.
 *
 * Drawn from the same numbers as the islands rather than written by hand. Hand-drawn curves
 * were how the old map worked, and they were wrong the moment anything moved — a path is not a
 * fact about a world, it is the line between two places this table already knows.
 */
export interface RouteGeometry {
  /** The whole curve, from the book to the island's landing point. */
  d: string;
  /** Where it arrives — the far end, for the marker that sits there. */
  x: number;
  y: number;
}

export function routeFor(layout: MapLayout, world: WorldId): RouteGeometry {
  const from = BOOK_LAYOUT[layout];
  const to = ISLAND_LAYOUT[layout][world];
  const [ox, oy] = [from.pathX * 10, from.pathY * 6.5];

  // Not the island's centre: a route ending there stops underneath the sprite, and what a
  // reader sees is a line that fades out rather than one that arrives. It ends a little below,
  // where the rock hangs, so the last of it tucks under the island and the marker sits on the
  // edge — the difference between a path that goes somewhere and a path that stops.
  const landing = to.y + to.w * 0.18;
  const [tx, ty] = [to.x * 10, landing * 6.5];

  // Bowed towards the middle, so the routes lean rather than run straight and a route to a far
  // corner leaves the book going roughly the way the painted path already points.
  const mx = (ox + tx) / 2;
  const cx = mx + (500 - mx) * 0.55;
  const cy = (oy + ty) / 2;
  return {
    d: `M ${ox.toFixed(1)} ${oy.toFixed(1)} Q ${cx.toFixed(1)} ${cy.toFixed(1)} ${tx.toFixed(1)} ${ty.toFixed(1)}`,
    x: tx,
    y: ty,
  };
}
