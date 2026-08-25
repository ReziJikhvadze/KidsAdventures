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
  /*
    The same two masters the world picker is painted from.

    There were two maps of one world: the portals art here, and the approved master at /themes.
    A visitor met one on the landing page, chose a world on the other, and the islands were not
    even in the same places. There is one painting now, and these are its files — the numbers
    below are measured off it, and they are the numbers the picker's hotspots and its flying star
    already use.
  */
  src: {
    wide: "/adventrya/world-selector/world-map-desktop-master.webp",
    tall: "/adventrya/world-selector/world-map-mobile-master.webp",
  },
  optimizedSrc: {
    wide: "/adventrya/world-selector/world-map-desktop-master.webp",
    tall: "/adventrya/world-selector/world-map-mobile-master.webp",
  },
  /*
    Measured off the master, and the same six numbers the picker reads: the centre of each
    island and the size of its own footprint. Islands left of the middle open their labels
    rightwards, into the empty sky around Beki, and the ones on the right open left — a label
    that opened outward would leave the painting.
  */
  anchors: {
    wide: {
      airplanes: { x: 30.5, y: 21, hitWidth: 23, hitHeight: 23, side: "left" },
      space: { x: 71.5, y: 23, hitWidth: 23, hitHeight: 19, side: "right" },
      animals: { x: 16.5, y: 48.5, hitWidth: 24, hitHeight: 25, side: "left" },
      pirates: { x: 84, y: 50, hitWidth: 24, hitHeight: 23, side: "right" },
      magic: { x: 25, y: 75, hitWidth: 26, hitHeight: 27, side: "left" },
      dinosaurs: { x: 71.5, y: 78, hitWidth: 26, hitHeight: 25, side: "right" },
    },
    tall: {
      airplanes: { x: 24, y: 17, hitWidth: 39, hitHeight: 18, side: "left" },
      space: { x: 75, y: 20, hitWidth: 44, hitHeight: 16, side: "right" },
      animals: { x: 23, y: 39, hitWidth: 36, hitHeight: 20, side: "left" },
      pirates: { x: 77, y: 41, hitWidth: 40, hitHeight: 19, side: "right" },
      magic: { x: 22, y: 66, hitWidth: 40, hitHeight: 21, side: "left" },
      dinosaurs: { x: 78, y: 69, hitWidth: 40, hitHeight: 19, side: "right" },
    },
  },
  /* Beki's chest, where every trail starts on this painting. */
  origin: {
    wide: { x: 49.5, y: 54.7 },
    tall: { x: 50.6, y: 59.6 },
  },
  /* The picker's own flights, which arc away from Beki before turning rather than running
     straight at an island. */
  routes: {
    wide: {
      airplanes: "M49.5 54.7 C46 48 43 42 43 36 C43.53 28.65 36.05 24.7 30.5 21",
      space: "M49.5 54.7 C54 48 58 42 58 36 C58 29.35 63.72 25.55 71.5 23",
      animals: "M49.5 54.7 C43 56 37 53 32 49 C26.83 46.83 21.75 48.5 16.5 48.5",
      pirates: "M49.5 54.7 C58 56 64 52 69 48 C74 49.1 79 50 84 50",
      magic: "M49.5 54.7 C46 59 42 64 39 68 C34.3 73 30.3 74.15 25 75",
      dinosaurs: "M49.5 54.7 C55 59 59 64 63 67 C65.63 72.1 69.13 73.7 71.5 78",
    },
    tall: {
      airplanes: "M50.6 59.6 C46 50 41 39 34 30 C30.85 21.5 27 18 24 17",
      space: "M50.6 59.6 C55 50 60 39 67 30 C70.15 21.5 74 18 75 20",
      animals: "M50.6 59.6 C45 55 39 48 33 44 C29.05 40.9 25.5 39 23 39",
      pirates: "M50.6 59.6 C56 55 62 48 68 44 C71.95 42.9 75.5 41 77 41",
      magic: "M50.6 59.6 C46 62 40 65 34 67 C29 68 25 68.5 22 66",
      dinosaurs: "M50.6 59.6 C55 62 61 65 67 67 C72 68 76 68.5 78 69",
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
