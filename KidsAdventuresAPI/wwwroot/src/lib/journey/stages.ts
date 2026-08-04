import { useCallback, useEffect, useState } from "react";
import { useLocation, useRouter } from "@tanstack/react-router";

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

/**
 * The create journey lives at a single route and moves through stages in the URL
 * hash, matching the demo's own URLs (`/create#preview`). Theme pick is `/themes`,
 * not `#world` — `#world` is only kept as a legacy alias that redirects.
 */
export function useJourneyStage(): [JourneyStage, (next: JourneyStage) => void] {
  // The hash comes from the router, not from window.location plus a hashchange
  // listener. That listener was the bug behind "უკან does nothing": the back control
  // is a router <Link>, the router navigates with history.pushState, and pushState
  // does not fire hashchange. The URL became /create#profile while the stage state
  // stayed on preview, so the screen never moved.
  const location = useLocation();
  const router = useRouter();

  const hashStage = normalizeStage(location.hash);
  const [stage, setStage] = useState<JourneyStage>(hashStage);

  // Server-rendered markup has no hash, so reconcile once on the client, then follow
  // the router for every later change.
  useEffect(() => {
    setStage(hashStage);
  }, [hashStage]);

  const goToStage = useCallback(
    (next: JourneyStage) => {
      if (next === "world") {
        window.location.assign("/themes");
        return;
      }

      setStage(next);
      void router.navigate({ to: "/create", hash: next });
      window.scrollTo({ top: 0, behavior: "instant" as ScrollBehavior });
    },
    [router],
  );

  return [stage, goToStage];
}

/** Accepts a hash with or without its leading '#', plus the demo's legacy aliases. */
function normalizeStage(rawHash: string | undefined): JourneyStage {
  const hash = (rawHash ?? "").replace(/^#/, "");
  if (hash === "details") return "profile";
  if (hash === "package") return "preview";
  if (hash === "book") return "generating";
  return isJourneyStage(hash) ? hash : "profile";
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
      // Auth is skipped when already signed in; preview is the real prior step.
      return "/create#preview";
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
