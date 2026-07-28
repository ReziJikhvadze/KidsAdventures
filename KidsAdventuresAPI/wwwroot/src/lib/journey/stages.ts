import { useCallback, useEffect, useState } from "react";

export const JOURNEY_STAGES = [
  "profile",
  "world",
  "preview",
  "auth",
  "checkout",
  "generating",
  "generated",
] as const;

export type JourneyStage = (typeof JOURNEY_STAGES)[number];

export function isJourneyStage(value: string): value is JourneyStage {
  return (JOURNEY_STAGES as readonly string[]).includes(value);
}

function stageFromHash(): JourneyStage {
  if (typeof window === "undefined") return "profile";
  const hash = window.location.hash.replace(/^#/, "");
  // Demo aliases
  if (hash === "details") return "profile";
  if (hash === "package") return "preview";
  if (hash === "book") return "generating";
  return isJourneyStage(hash) ? hash : "profile";
}

/**
 * The create journey lives at a single route and moves through stages in the URL
 * hash, matching the demo's own URLs (`/create#preview`). Theme pick is `/themes`,
 * not `#world` — `#world` is only kept as a legacy alias that redirects.
 */
export function useJourneyStage(): [JourneyStage, (next: JourneyStage) => void] {
  const [stage, setStage] = useState<JourneyStage>("profile");

  useEffect(() => {
    setStage(stageFromHash());
    const onHashChange = () => setStage(stageFromHash());
    window.addEventListener("hashchange", onHashChange);
    return () => window.removeEventListener("hashchange", onHashChange);
  }, []);

  const goToStage = useCallback((next: JourneyStage) => {
    if (next === "world") {
      window.location.assign("/themes");
      return;
    }
    setStage(next);
    if (typeof window !== "undefined" && window.location.hash !== `#${next}`) {
      window.history.pushState(null, "", `#${next}`);
    }
    window.scrollTo({ top: 0, behavior: "instant" as ScrollBehavior });
  }, []);

  return [stage, goToStage];
}

/** Back-link target for each stage, mirroring the demo's navigation model. */
export function backHrefForStage(
  stage: JourneyStage,
  options: { mode: "first" | "continue"; isPrintUpgrade: boolean; hasWorld: boolean },
): string {
  switch (stage) {
    case "profile":
      return "/";
    case "world":
      return "/create#profile";
    case "preview":
      return options.mode === "continue" || options.hasWorld
        ? "/create#profile"
        : "/themes";
    case "auth":
      return "/create#preview";
    case "checkout":
      if (options.isPrintUpgrade) return "/dashboard";
      return "/create#auth";
    default:
      return "/dashboard";
  }
}

export const STAGE_PROGRESS: Record<JourneyStage, number> = {
  profile: 33,
  world: 66,
  preview: 100,
  auth: 100,
  checkout: 100,
  generating: 100,
  generated: 100,
};

export function progressLabelForStage(stage: JourneyStage): string {
  switch (stage) {
    case "profile":
      return "ნაბიჯი 1 / 3";
    case "world":
      return "ნაბიჯი 2 / 3";
    case "preview":
      return "ნაბიჯი 3 / 3 · Preview";
    case "auth":
    case "checkout":
      return "შეკვეთა";
    case "generating":
    case "generated":
      return "წიგნის შექმნა";
  }
}
