import { createAvatar } from "@dicebear/core";
import { create as adventurerCreate, meta as adventurerMeta } from "@dicebear/adventurer";
import type { AvatarConfig } from "@/lib/avatar/config";

const adventurerStyle = { create: adventurerCreate, meta: adventurerMeta };

type HeadOptions = {
  /** Transparent so the head can sit cleanly on a full-body stage. */
  transparentBackground?: boolean;
  size?: number;
};

/** Build DiceBear Adventurer options from our stored config. */
export function toDiceBearOptions(config: AvatarConfig, options: HeadOptions = {}) {
  const features =
    config.features && config.features !== "none" ? [config.features as "blush" | "freckles" | "birthmark"] : [];
  const glasses =
    config.glasses && config.glasses !== "none"
      ? [config.glasses as "variant01" | "variant02" | "variant03" | "variant04" | "variant05"]
      : [];
  const earrings =
    config.earrings && config.earrings !== "none"
      ? [
          config.earrings as
            | "variant01"
            | "variant02"
            | "variant03"
            | "variant04"
            | "variant05"
            | "variant06",
        ]
      : [];

  const transparent = options.transparentBackground !== false;

  return {
    seed: `${config.gender}-${config.hair}-${config.skinColor}`,
    size: options.size ?? 512,
    // Transparent for full-body compositing — hair stays correctly placed (no bounce).
    ...(transparent
      ? { backgroundColor: ["transparent"] as string[] }
      : {
          backgroundColor: ["b6e3f4", "c0aede", "d1d4f9", "ffd5dc", "ffdfbf"],
          backgroundType: ["gradientLinear"] as ["gradientLinear"],
        }),
    // Fill circular face frame; slight down bias keeps chin in view.
    scale: 95,
    translateY: 2,
    skinColor: [config.skinColor.replace("#", "")],
    hair: [config.hair as never],
    hairColor: [config.hairColor.replace("#", "")],
    hairProbability: 100,
    eyes: [config.eyes as never],
    eyebrows: [config.eyebrows as never],
    mouth: [config.mouth as never],
    features,
    featuresProbability: features.length ? 100 : 0,
    glasses,
    glassesProbability: glasses.length ? 100 : 0,
    earrings,
    earringsProbability: earrings.length ? 100 : 0,
  };
}

export function createAdventurerSvg(config: AvatarConfig, options?: HeadOptions): string {
  return createAvatar(adventurerStyle, toDiceBearOptions(config, options)).toString();
}

export function createAdventurerDataUri(config: AvatarConfig, options?: HeadOptions): string {
  return createAvatar(adventurerStyle, toDiceBearOptions(config, options)).toDataUri();
}
