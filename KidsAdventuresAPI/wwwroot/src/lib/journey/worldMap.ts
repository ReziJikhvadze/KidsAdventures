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

export interface MapPoint {
  x: number;
  y: number;
}

export interface WorldAnchor {
  /** Percent across and down the painting, at the world's lit gateway. */
  x: number;
  y: number;
  /** Generous interactive destination area, as a percentage of the painting. */
  hitWidth: number;
  hitHeight: number;
  /**
   * Which way the label opens. Islands on the left of the painting open right, into the
   * empty middle; islands on the right open left. A label that opened outward would run
   * off the edge of the screen, which is what used to happen to "თვითმფრინავები".
   */
  side: "left" | "right";
  /**
   * Extra pixels to drop the label by when it hangs under its pin instead of beside it.
   *
   * Two islands the painting places close together give their labels the same line, and they
   * touch. Which two is a fact about the painting, not about the layout, so it belongs here
   * with the rest of what the painting decided — and it is one number to revisit when the art
   * changes, rather than a media query to rediscover.
   */
}

export interface MapArt {
  /** PNG fallback for the two illustrations. */
  src: Record<MapLayout, string>;
  /** High-quality, compact primary source for modern browsers. */
  optimizedSrc: Record<MapLayout, string>;
  anchors: Record<MapLayout, Record<WorldId, WorldAnchor>>;
  /** Beki's lantern: the starting point for guide and selection light. */
  origin: Record<MapLayout, MapPoint>;
  /** Separate hand-tuned light trails keep both illustrations visually truthful. */
  routes: Record<MapLayout, Record<WorldId, string>>;
}

export const WORLD_MAP: MapArt = {
  src: {
    wide: "/adventrya/adventrya-world-map-portals-wide.png",
    tall: "/adventrya/adventrya-world-map-portals-tall.png",
  },
  optimizedSrc: {
    wide: "/adventrya/adventrya-world-map-portals-wide.jpg",
    tall: "/adventrya/adventrya-world-map-portals-tall.jpg",
  },
  anchors: {
    wide: {
      dinosaurs: { x: 17.5, y: 31, hitWidth: 24, hitHeight: 34, side: "left" },
      space: { x: 40.2, y: 30.8, hitWidth: 20, hitHeight: 34, side: "left" },
      airplanes: { x: 59.4, y: 30, hitWidth: 19, hitHeight: 34, side: "right" },
      pirates: { x: 81.6, y: 31, hitWidth: 24, hitHeight: 35, side: "right" },
      animals: { x: 16.2, y: 69.5, hitWidth: 27, hitHeight: 29, side: "left" },
      magic: { x: 82.1, y: 69.8, hitWidth: 27, hitHeight: 30, side: "right" },
    },
    tall: {
      space: { x: 29.6, y: 20.3, hitWidth: 39, hitHeight: 25, side: "left" },
      airplanes: { x: 69.7, y: 19.7, hitWidth: 38, hitHeight: 25, side: "right" },
      dinosaurs: { x: 28, y: 45, hitWidth: 40, hitHeight: 22, side: "left" },
      pirates: { x: 77.3, y: 45.7, hitWidth: 39, hitHeight: 22, side: "right" },
      animals: { x: 27, y: 69, hitWidth: 42, hitHeight: 21, side: "left" },
      magic: { x: 76.6, y: 69, hitWidth: 41, hitHeight: 21, side: "right" },
    },
  },
  origin: {
    wide: { x: 46.3, y: 71.5 },
    tall: { x: 54, y: 82.5 },
  },
  routes: {
    wide: {
      dinosaurs: "M46.3 71.5 C42 59 29 45 17.5 31",
      space: "M46.3 71.5 C46 56 44 42 40.2 30.8",
      airplanes: "M46.3 71.5 C51 56 57 42 59.4 30",
      pirates: "M46.3 71.5 C58 60 74 48 81.6 31",
      animals: "M46.3 71.5 C38 65 27 67 16.2 69.5",
      magic: "M46.3 71.5 C57 64 71 65 82.1 69.8",
    },
    tall: {
      dinosaurs: "M54 82.5 C46 70 35 58 28 45",
      space: "M54 82.5 C48 61 39 37 29.6 20.3",
      airplanes: "M54 82.5 C59 60 65 36 69.7 19.7",
      pirates: "M54 82.5 C61 70 71 57 77.3 45.7",
      animals: "M54 82.5 C47 76 36 71 27 69",
      magic: "M54 82.5 C61 76 70 71 76.6 69",
    },
  },
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
}

export const WORLD_TINT: Record<WorldId, WorldTint> = {
  space: { tint: "#a889ff" },
  airplanes: { tint: "#67b6ff" },
  dinosaurs: { tint: "#f0b64a" },
  animals: { tint: "#6fd07a" },
  pirates: { tint: "#4fd6e0" },
  magic: { tint: "#ffa24a" },
};
