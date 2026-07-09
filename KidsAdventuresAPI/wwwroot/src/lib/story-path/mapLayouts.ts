import airplanesArt from "@/assets/story-path-airplanes.jpg";
import dinosaursArt from "@/assets/story-path-dinosaurs.jpg";
import spaceArt from "@/assets/story-path-space.jpg";
import piratesArt from "@/assets/story-path-pirates.jpg";
import animalsArt from "@/assets/story-path-animals.jpg";
import type { ThemeType } from "@/lib/api/types";

export type MapNodePosition = {
  x: number;
  y: number;
};

export type MapLayout = {
  /** Wide hand-painted terrain background for this theme's saga map (native ~3:2, cropped to 16:9 by the map card). */
  artwork: string;
  viewBox: string;
  /**
   * Exactly 5 stops — one per saga chapter — hand-traced to sit directly on
   * the painted trail in `artwork` (in the *cropped 16:9* frame the map
   * actually renders, not the raw image). The glowing trail line is derived
   * from these same points, so it can never drift out of sync.
   */
  nodes: MapNodePosition[];
};

/** 5-chapter saga trail per theme. Coordinates are 0–100 percent of the visible (cropped) artwork. */
export const MAP_LAYOUTS: Record<ThemeType, MapLayout> = {
  Airplanes: {
    artwork: airplanesArt,
    viewBox: "0 0 100 100",
    nodes: [
      { x: 43, y: 93 },
      { x: 49, y: 80 },
      { x: 45, y: 61 },
      { x: 49, y: 43 },
      { x: 52, y: 22 },
    ],
  },
  Dinosaurs: {
    artwork: dinosaursArt,
    viewBox: "0 0 100 100",
    nodes: [
      { x: 37, y: 92 },
      { x: 42, y: 69 },
      { x: 48, y: 50 },
      { x: 44, y: 31 },
      { x: 46, y: 14 },
    ],
  },
  Space: {
    artwork: spaceArt,
    viewBox: "0 0 100 100",
    nodes: [
      { x: 51, y: 90 },
      { x: 68, y: 71 },
      { x: 58, y: 53 },
      { x: 71, y: 39 },
      { x: 80, y: 22 },
    ],
  },
  Pirates: {
    artwork: piratesArt,
    viewBox: "0 0 100 100",
    nodes: [
      { x: 18, y: 83 },
      { x: 33, y: 60 },
      { x: 50, y: 50 },
      { x: 66, y: 36 },
      { x: 82, y: 19 },
    ],
  },
  Animals: {
    artwork: animalsArt,
    viewBox: "0 0 100 100",
    nodes: [
      { x: 40, y: 90 },
      { x: 44, y: 71 },
      { x: 47, y: 52 },
      { x: 54, y: 38 },
      { x: 63, y: 26 },
    ],
  },
};

export const THEME_ORDER: ThemeType[] = [
  "Airplanes",
  "Dinosaurs",
  "Space",
  "Pirates",
  "Animals",
];

export function getNextTheme(current: ThemeType): ThemeType | null {
  const index = THEME_ORDER.indexOf(current);
  if (index < 0 || index >= THEME_ORDER.length - 1) return null;
  return THEME_ORDER[index + 1];
}
