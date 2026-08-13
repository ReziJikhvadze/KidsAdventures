import type { WorldId } from "@/lib/worlds";

/**
 * Where each world sits on the painting.
 *
 * This used to live in first-map.css as six `.first-node-{id} { left; top }` rules, which
 * meant the painting and the thing that points at it were described in different languages in
 * different files. Repainting the map meant hunting percentages through a stylesheet.
 *
 * It is data now, and there is exactly one of these tables per painting. When new art lands,
 * the numbers here change and nothing else does.
 *
 * See design/world-map-art-brief.md for the composition the next painting is drawn to.
 */

/** Landscape art for wide screens, portrait art for phones. */
export type MapLayout = "wide" | "tall";

export interface WorldAnchor {
  /** Percent across and down the painting, at the world's lit gateway. */
  x: number;
  y: number;
  /**
   * Which way the label opens. Islands on the left of the painting open right, into the
   * empty middle; islands on the right open left. A label that opened outward would run
   * off the edge of the screen, which is what used to happen to "თვითმფრინავები".
   */
  side: "left" | "right";
}

export interface MapArt {
  /** One painting per layout. The phone is not a crop of the desktop: see the brief. */
  src: Record<MapLayout, string>;
  anchors: Record<MapLayout, Record<WorldId, WorldAnchor>>;
}

export const WORLD_MAP: MapArt = {
  src: {
    wide: "/adventrya/adventrya-world-map.png",
    tall: "/adventrya/adventrya-world-map.png",
  },
  anchors: {
    wide: {
      space: { x: 43.5, y: 20, side: "left" },
      airplanes: { x: 83, y: 20, side: "right" },
      dinosaurs: { x: 24, y: 48, side: "left" },
      pirates: { x: 69, y: 43, side: "right" },
      animals: { x: 61, y: 66, side: "right" },
      magic: { x: 85, y: 68, side: "right" },
    },
    tall: {
      space: { x: 43.5, y: 20, side: "left" },
      airplanes: { x: 83, y: 20, side: "right" },
      dinosaurs: { x: 24, y: 48, side: "left" },
      pirates: { x: 69, y: 43, side: "right" },
      animals: { x: 61, y: 66, side: "right" },
      magic: { x: 85, y: 68, side: "right" },
    },
  },
};

/** Where the golden path begins — the child's feet, in the same percentages. */
export const MAP_ORIGIN: Record<MapLayout, { x: number; y: number }> = {
  wide: { x: 43, y: 80 },
  tall: { x: 43, y: 80 },
};

/**
 * The light each world gives off.
 *
 * Six identical cream discs told a parent that six things were clickable and nothing else.
 * A colour per world is the cheapest thing on the screen that carries meaning: the badge, its
 * halo and the glow that lands on the island are all lit by the same lamp, so a child who
 * cannot read "ტყე" still learns that the green one is the forest.
 *
 * `deep` is the base the disc is filled from and `tint` the bright rim and the light it
 * throws. Both are written rather than derived, because a colour that reads well as a 2px rim
 * over a painted night sky is not one a formula finds from the fill.
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
