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
