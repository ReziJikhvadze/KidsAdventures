import type { AvatarConfig } from "@/lib/avatar/config";
import { DEFAULT_AVATAR_CONFIG } from "@/lib/avatar/config";
import type { StoryThemeId } from "@/lib/themes";

export type AvatarPresetId = string;

export type AvatarPreset = {
  id: AvatarPresetId;
  label: string;
  hint: string;
  /** Soft tint for the card (CSS color). */
  tint: string;
  config: Omit<AvatarConfig, "library"> & { library?: "adventurer" };
};

function preset(
  id: string,
  label: string,
  hint: string,
  tint: string,
  partial: Partial<AvatarConfig>,
): AvatarPreset {
  return {
    id,
    label,
    hint,
    tint,
    config: {
      ...DEFAULT_AVATAR_CONFIG,
      ...partial,
      library: "adventurer",
    },
  };
}

/** Three cohesive starter heroes per theme — one tap, premium look. */
export const THEME_AVATAR_PRESETS: Record<StoryThemeId, AvatarPreset[]> = {
  space: [
    preset(
      "space-explorer",
      "Cosmic Explorer",
      "Soft hair · sky suit · curious eyes",
      "#dbeafe",
      {
        gender: "girl",
        hair: "long01",
        hairColor: "6a4e35",
        outfit: "astronaut",
        outfitColor: "4dabf7",
        eyes: "variant03",
        mouth: "variant01",
        features: "none",
      },
    ),
    preset(
      "space-pilot",
      "Star Pilot",
      "Neat cut · navy gear · brave smile",
      "#e0e7ff",
      {
        gender: "boy",
        hair: "short01",
        hairColor: "0e0e0e",
        outfit: "astronaut",
        outfitColor: "364fc7",
        eyes: "variant05",
        mouth: "variant07",
        features: "none",
      },
    ),
    preset(
      "space-dreamer",
      "Galaxy Dreamer",
      "Wavy hair · lavender suit · sparkle",
      "#ede9fe",
      {
        gender: "girl",
        hair: "long02",
        hairColor: "562306",
        outfit: "astronaut",
        outfitColor: "b197fc",
        eyes: "variant08",
        mouth: "variant04",
        features: "blush",
      },
    ),
  ],
  dinosaurs: [
    preset(
      "dino-ranger",
      "Jungle Ranger",
      "Adventure cut · explorer vest",
      "#dcfce7",
      {
        gender: "boy",
        hair: "short07",
        hairColor: "6a4e35",
        outfit: "explorer",
        outfitColor: "2f9e44",
        eyes: "variant03",
        mouth: "variant05",
      },
    ),
    preset(
      "dino-friend",
      "Fossil Friend",
      "Soft layers · mint explorer",
      "#ecfccb",
      {
        gender: "girl",
        hair: "long08",
        hairColor: "ac6511",
        outfit: "explorer",
        outfitColor: "63e6be",
        eyes: "variant01",
        mouth: "variant01",
        features: "freckles",
      },
    ),
    preset(
      "dino-roar",
      "Tiny Roar",
      "Spiky fun · sun vest · bold grin",
      "#fef9c3",
      {
        gender: "boy",
        hair: "short05",
        hairColor: "0e0e0e",
        outfit: "explorer",
        outfitColor: "fcc419",
        eyes: "variant09",
        mouth: "variant03",
      },
    ),
  ],
  airplanes: [
    preset(
      "sky-captain",
      "Sky Captain",
      "Neat hair · captain coat",
      "#e0f2fe",
      {
        gender: "boy",
        hair: "short13",
        hairColor: "562306",
        outfit: "captain",
        outfitColor: "4dabf7",
        eyes: "variant11",
        mouth: "variant07",
      },
    ),
    preset(
      "cloud-flyer",
      "Cloud Flyer",
      "Long soft · coral flight gear",
      "#fff1f2",
      {
        gender: "girl",
        hair: "long01",
        hairColor: "6a4e35",
        outfit: "hoodie",
        outfitColor: "f07167",
        eyes: "variant02",
        mouth: "variant01",
      },
    ),
    preset(
      "runway-ace",
      "Runway Ace",
      "Side sweep · navy hoodie",
      "#eef2ff",
      {
        gender: "girl",
        hair: "long03",
        hairColor: "0e0e0e",
        outfit: "hoodie",
        outfitColor: "364fc7",
        eyes: "variant05",
        mouth: "variant08",
        glasses: "variant02",
      },
    ),
  ],
  pirates: [
    preset(
      "sea-scout",
      "Sea Scout",
      "Tousled · captain coat · gold",
      "#ffedd5",
      {
        gender: "boy",
        hair: "short07",
        hairColor: "6a4e35",
        outfit: "captain",
        outfitColor: "e8590c",
        eyes: "variant03",
        mouth: "variant05",
      },
    ),
    preset(
      "treasure-seeker",
      "Treasure Seeker",
      "Windswept · coral captain",
      "#fef3c7",
      {
        gender: "girl",
        hair: "long20",
        hairColor: "ac6511",
        outfit: "captain",
        outfitColor: "f07167",
        eyes: "variant01",
        mouth: "variant01",
        features: "freckles",
      },
    ),
    preset(
      "deck-hero",
      "Deck Hero",
      "Short hero · navy coat",
      "#e0e7ff",
      {
        gender: "boy",
        hair: "short10",
        hairColor: "0e0e0e",
        outfit: "captain",
        outfitColor: "364fc7",
        eyes: "variant09",
        mouth: "variant07",
      },
    ),
  ],
  animals: [
    preset(
      "wild-friend",
      "Wild Friend",
      "Soft hair · forest explorer",
      "#dcfce7",
      {
        gender: "girl",
        hair: "long12",
        hairColor: "6a4e35",
        outfit: "explorer",
        outfitColor: "2f9e44",
        eyes: "variant06",
        mouth: "variant01",
        features: "blush",
      },
    ),
    preset(
      "trail-buddy",
      "Trail Buddy",
      "Messy cut · mint hoodie",
      "#ecfccb",
      {
        gender: "boy",
        hair: "short07",
        hairColor: "562306",
        outfit: "hoodie",
        outfitColor: "63e6be",
        eyes: "variant02",
        mouth: "variant08",
      },
    ),
    preset(
      "meadow-star",
      "Meadow Star",
      "Curls · sun party look",
      "#fef9c3",
      {
        gender: "girl",
        hair: "long05",
        hairColor: "cb6820",
        outfit: "party",
        outfitColor: "fcc419",
        eyes: "variant08",
        mouth: "variant10",
      },
    ),
  ],
};

/** Fallback when no theme is selected yet. */
export const DEFAULT_AVATAR_PRESETS: AvatarPreset[] = [
  preset(
    "story-hero",
    "Story Hero",
    "Soft hair · explorer vest",
    "#fff7ed",
    {
      gender: "girl",
      hair: "long01",
      outfit: "explorer",
      outfitColor: "f07167",
    },
  ),
  preset(
    "brave-buddy",
    "Brave Buddy",
    "Neat cut · cozy hoodie",
    "#e0f2fe",
    {
      gender: "boy",
      hair: "short01",
      outfit: "hoodie",
      outfitColor: "4dabf7",
    },
  ),
  preset(
    "spark-star",
    "Spark Star",
    "Wavy hair · cape ready",
    "#fce7f3",
    {
      gender: "girl",
      hair: "long02",
      outfit: "superhero",
      outfitColor: "b197fc",
      features: "blush",
    },
  ),
];

export function presetsForTheme(themeId: StoryThemeId | null | undefined): AvatarPreset[] {
  if (themeId && themeId in THEME_AVATAR_PRESETS) {
    return THEME_AVATAR_PRESETS[themeId];
  }
  return DEFAULT_AVATAR_PRESETS;
}

export function applyPreset(preset: AvatarPreset): AvatarConfig {
  return {
    ...DEFAULT_AVATAR_CONFIG,
    ...preset.config,
    library: "adventurer",
  };
}

/** Four hair buckets — maps to a representative Adventurer style. */
export type HairCategoryId = "short" | "long" | "curly" | "tied";

export const HAIR_CATEGORY_OPTIONS: {
  id: HairCategoryId;
  label: string;
  hint: string;
  girlHair: string;
  boyHair: string;
}[] = [
  { id: "short", label: "Short", hint: "Neat & easy", girlHair: "short02", boyHair: "short01" },
  { id: "long", label: "Long", hint: "Classic soft", girlHair: "long01", boyHair: "long03" },
  { id: "curly", label: "Curly", hint: "Bouncy curls", girlHair: "long05", boyHair: "short04" },
  { id: "tied", label: "Tied", hint: "Pulled back", girlHair: "long07", boyHair: "short06" },
];

export function hairCategoryFromStyle(hair: string): HairCategoryId {
  if (["long05", "long23", "long11", "short04"].includes(hair)) return "curly";
  if (["long07", "long21", "short06"].includes(hair)) return "tied";
  if (hair.startsWith("short")) return "short";
  return "long";
}

export function hairForCategory(
  category: HairCategoryId,
  gender: "girl" | "boy",
): string {
  const row = HAIR_CATEGORY_OPTIONS.find((o) => o.id === category) ?? HAIR_CATEGORY_OPTIONS[1];
  return gender === "boy" ? row.boyHair : row.girlHair;
}
