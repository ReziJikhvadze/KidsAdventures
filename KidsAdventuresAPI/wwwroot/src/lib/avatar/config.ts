/**
 * Adventrya avatar config — powered by DiceBear Adventurer
 * (illustrated cartoon style by Lisa Wischofsky, CC BY 4.0).
 * Stored as JSON on the child; rendered live in the builder; converted to
 * Character DNA for OpenAI story illustrations.
 */

export type PersonalizationType = "avatar" | "photo";

export type AvatarGender = "girl" | "boy";

export type AvatarConfig = {
  /** Library marker — always "adventurer" for this pipeline. */
  library: "adventurer";
  gender: AvatarGender;
  skinColor: string;
  hair: string;
  hairColor: string;
  eyes: string;
  eyebrows: string;
  mouth: string;
  /** none | blush | freckles | birthmark */
  features: string;
  /** none | variant01…05 */
  glasses: string;
  /** none | variant01…06 */
  earrings: string;
  /** Full-body outfit for the composed hero (head stays Adventurer). */
  outfit: string;
  /** Hex without # — shirt / accent color. */
  outfitColor: string;
};

export type AvatarOption = {
  id: string;
  label: string;
  swatch?: string;
  hint?: string;
};

export const DEFAULT_AVATAR_CONFIG: AvatarConfig = {
  library: "adventurer",
  gender: "girl",
  skinColor: "f2d3b1",
  hair: "long01",
  hairColor: "6a4e35",
  eyes: "variant01",
  eyebrows: "variant01",
  mouth: "variant01",
  features: "none",
  glasses: "none",
  earrings: "none",
  outfit: "explorer",
  outfitColor: "f07167",
};

export const OUTFIT_OPTIONS: AvatarOption[] = [
  { id: "explorer", label: "Explorer", hint: "Vest + boots" },
  { id: "hoodie", label: "Hoodie", hint: "Cozy everyday" },
  { id: "astronaut", label: "Astronaut", hint: "Space suit" },
  { id: "captain", label: "Captain", hint: "Adventure coat" },
  { id: "superhero", label: "Superhero", hint: "Cape + boots" },
  { id: "party", label: "Party", hint: "Dress / smart" },
];

export const OUTFIT_COLOR_OPTIONS: AvatarOption[] = [
  { id: "f07167", label: "Coral", swatch: "#f07167" },
  { id: "4dabf7", label: "Sky", swatch: "#4dabf7" },
  { id: "63e6be", label: "Mint", swatch: "#63e6be" },
  { id: "fcc419", label: "Sun", swatch: "#fcc419" },
  { id: "b197fc", label: "Lavender", swatch: "#b197fc" },
  { id: "364fc7", label: "Navy", swatch: "#364fc7" },
  { id: "e8590c", label: "Orange", swatch: "#e8590c" },
  { id: "2f9e44", label: "Forest", swatch: "#2f9e44" },
];

export const GENDER_OPTIONS: AvatarOption[] = [
  { id: "girl", label: "Girl", hint: "Longer hair styles first" },
  { id: "boy", label: "Boy", hint: "Shorter hair styles first" },
];

export const SKIN_COLOR_OPTIONS: AvatarOption[] = [
  { id: "f2d3b1", label: "Fair", swatch: "#f2d3b1" },
  { id: "ecad80", label: "Light", swatch: "#ecad80" },
  { id: "d08b5b", label: "Warm", swatch: "#d08b5b" },
  { id: "ae5d29", label: "Medium", swatch: "#ae5d29" },
  { id: "9e5622", label: "Tan", swatch: "#9e5622" },
  { id: "763900", label: "Deep", swatch: "#763900" },
  { id: "614335", label: "Rich", swatch: "#614335" },
];

export const HAIR_COLOR_OPTIONS: AvatarOption[] = [
  { id: "0e0e0e", label: "Black", swatch: "#0e0e0e" },
  { id: "562306", label: "Dark brown", swatch: "#562306" },
  { id: "6a4e35", label: "Brown", swatch: "#6a4e35" },
  { id: "ac6511", label: "Copper", swatch: "#ac6511" },
  { id: "cb6820", label: "Auburn", swatch: "#cb6820" },
  { id: "ab2a18", label: "Red", swatch: "#ab2a18" },
  { id: "b9a05f", label: "Dirty blonde", swatch: "#b9a05f" },
  { id: "e5d7a3", label: "Blonde", swatch: "#e5d7a3" },
  { id: "afafaf", label: "Silver", swatch: "#afafaf" },
  { id: "dba3be", label: "Pink", swatch: "#dba3be" },
];

/** Friendly labels for Adventurer long hair styles. */
export const HAIR_LONG_OPTIONS: AvatarOption[] = [
  { id: "long01", label: "Long soft", hint: "Classic long" },
  { id: "long02", label: "Long wavy", hint: "Gentle waves" },
  { id: "long03", label: "Long side", hint: "Side sweep" },
  { id: "long04", label: "Long layers", hint: "Layered" },
  { id: "long05", label: "Long curls", hint: "Curly ends" },
  { id: "long06", label: "Long thick", hint: "Full volume" },
  { id: "long07", label: "Long braid vibe", hint: "Pulled back feel" },
  { id: "long08", label: "Long fringe", hint: "With bangs" },
  { id: "long09", label: "Long flow", hint: "Flowing" },
  { id: "long10", label: "Long sleek", hint: "Straight sleek" },
  { id: "long11", label: "Long bouncy", hint: "Bouncy" },
  { id: "long12", label: "Long romantic", hint: "Soft romantic" },
  { id: "long13", label: "Long adventure", hint: "Explorer look" },
  { id: "long14", label: "Long playful", hint: "Playful" },
  { id: "long15", label: "Long hero", hint: "Story hero" },
  { id: "long16", label: "Long dreamy", hint: "Dreamy" },
  { id: "long17", label: "Long bold", hint: "Bold shape" },
  { id: "long18", label: "Long classic", hint: "Timeless" },
  { id: "long19", label: "Long fluffy", hint: "Fluffy" },
  { id: "long20", label: "Long windswept", hint: "Windswept" },
  { id: "long21", label: "Long twin vibe", hint: "Twin feel" },
  { id: "long22", label: "Long cascade", hint: "Cascade" },
  { id: "long23", label: "Long soft curl", hint: "Soft curls" },
  { id: "long24", label: "Long fairy", hint: "Fairy-tale" },
  { id: "long25", label: "Long princess", hint: "Princess" },
  { id: "long26", label: "Long wild", hint: "Wild free" },
];

/** Friendly labels for Adventurer short hair styles. */
export const HAIR_SHORT_OPTIONS: AvatarOption[] = [
  { id: "short01", label: "Short neat", hint: "Neat crop" },
  { id: "short02", label: "Short soft", hint: "Soft short" },
  { id: "short03", label: "Short wavy", hint: "Wavy short" },
  { id: "short04", label: "Short curly", hint: "Curly short" },
  { id: "short05", label: "Short spiky", hint: "Spiky fun" },
  { id: "short06", label: "Short side", hint: "Side part" },
  { id: "short07", label: "Short messy", hint: "Tousled" },
  { id: "short08", label: "Short buzz vibe", hint: "Very short" },
  { id: "short09", label: "Short bowl", hint: "Even fringe" },
  { id: "short10", label: "Short hero", hint: "Adventure cut" },
  { id: "short11", label: "Short fluffy", hint: "Fluffy top" },
  { id: "short12", label: "Short cool", hint: "Cool cut" },
  { id: "short13", label: "Short classic", hint: "Classic" },
  { id: "short14", label: "Short bold", hint: "Bold" },
  { id: "short15", label: "Short sporty", hint: "Sporty" },
  { id: "short16", label: "Short textured", hint: "Textured" },
  { id: "short17", label: "Short modern", hint: "Modern" },
  { id: "short18", label: "Short playful", hint: "Playful" },
  { id: "short19", label: "Short explorer", hint: "Explorer" },
];

export const EYE_OPTIONS: AvatarOption[] = [
  { id: "variant01", label: "Bright" },
  { id: "variant02", label: "Happy" },
  { id: "variant03", label: "Curious" },
  { id: "variant04", label: "Soft" },
  { id: "variant05", label: "Wide" },
  { id: "variant06", label: "Gentle" },
  { id: "variant07", label: "Sparkle" },
  { id: "variant08", label: "Dreamy" },
  { id: "variant09", label: "Bold" },
  { id: "variant10", label: "Kind" },
  { id: "variant11", label: "Alert" },
  { id: "variant12", label: "Sweet" },
];

export const EYEBROW_OPTIONS: AvatarOption[] = [
  { id: "variant01", label: "Soft" },
  { id: "variant02", label: "Natural" },
  { id: "variant03", label: "Arched" },
  { id: "variant04", label: "Straight" },
  { id: "variant05", label: "Bold" },
  { id: "variant06", label: "Gentle" },
  { id: "variant07", label: "Raised" },
  { id: "variant08", label: "Classic" },
];

export const MOUTH_OPTIONS: AvatarOption[] = [
  { id: "variant01", label: "Smile" },
  { id: "variant02", label: "Happy" },
  { id: "variant03", label: "Grin" },
  { id: "variant04", label: "Soft smile" },
  { id: "variant05", label: "Excited" },
  { id: "variant06", label: "Curious" },
  { id: "variant07", label: "Brave" },
  { id: "variant08", label: "Cheerful" },
  { id: "variant09", label: "Gentle" },
  { id: "variant10", label: "Joy" },
];

export const FEATURE_OPTIONS: AvatarOption[] = [
  { id: "none", label: "None" },
  { id: "blush", label: "Blush" },
  { id: "freckles", label: "Freckles" },
  { id: "birthmark", label: "Birthmark" },
];

export const GLASSES_OPTIONS: AvatarOption[] = [
  { id: "none", label: "None" },
  { id: "variant01", label: "Round" },
  { id: "variant02", label: "Classic" },
  { id: "variant03", label: "Fun" },
  { id: "variant04", label: "Cool" },
  { id: "variant05", label: "Adventure" },
];

export const EARRING_OPTIONS: AvatarOption[] = [
  { id: "none", label: "None" },
  { id: "variant01", label: "Small" },
  { id: "variant02", label: "Hoops" },
  { id: "variant03", label: "Studs" },
  { id: "variant04", label: "Drops" },
  { id: "variant05", label: "Sparkle" },
  { id: "variant06", label: "Bold" },
];

export function hairStylesForGender(gender: AvatarGender): AvatarOption[] {
  return gender === "boy"
    ? [...HAIR_SHORT_OPTIONS, ...HAIR_LONG_OPTIONS.slice(0, 6)]
    : [...HAIR_LONG_OPTIONS, ...HAIR_SHORT_OPTIONS.slice(0, 6)];
}

export function normalizeHairForGender(config: AvatarConfig): AvatarConfig {
  const allowed = new Set(hairStylesForGender(config.gender).map((o) => o.id));
  if (allowed.has(config.hair)) return config;
  return {
    ...config,
    hair: config.gender === "boy" ? "short01" : "long01",
  };
}

/** Migrate older custom-SVG configs into Adventurer defaults. */
export function coerceAvatarConfig(raw: unknown): AvatarConfig {
  if (!raw || typeof raw !== "object") return { ...DEFAULT_AVATAR_CONFIG };
  const o = raw as Record<string, unknown>;
  if (o.library === "adventurer" && typeof o.hair === "string") {
    return {
      library: "adventurer",
      gender: o.gender === "boy" ? "boy" : "girl",
      skinColor: String(o.skinColor ?? DEFAULT_AVATAR_CONFIG.skinColor),
      hair: String(o.hair),
      hairColor: String(o.hairColor ?? DEFAULT_AVATAR_CONFIG.hairColor),
      eyes: String(o.eyes ?? DEFAULT_AVATAR_CONFIG.eyes),
      eyebrows: String(o.eyebrows ?? DEFAULT_AVATAR_CONFIG.eyebrows),
      mouth: String(o.mouth ?? DEFAULT_AVATAR_CONFIG.mouth),
      features: String(o.features ?? "none"),
      glasses: String(o.glasses ?? "none"),
      earrings: String(o.earrings ?? "none"),
      outfit: String(o.outfit ?? DEFAULT_AVATAR_CONFIG.outfit),
      outfitColor: String(o.outfitColor ?? DEFAULT_AVATAR_CONFIG.outfitColor).replace("#", ""),
    };
  }
  // Legacy custom builder → sensible Adventurer defaults by gender
  const gender = o.gender === "boy" ? "boy" : "girl";
  return {
    ...DEFAULT_AVATAR_CONFIG,
    gender,
    hair: gender === "boy" ? "short07" : "long01",
  };
}

export function skinSwatch(id: string): string {
  return `#${id.replace("#", "")}`;
}

export function hairSwatch(id: string): string {
  return `#${id.replace("#", "")}`;
}
