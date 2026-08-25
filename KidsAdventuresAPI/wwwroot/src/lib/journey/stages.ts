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
        // Client-side: a hard navigation would unmount JourneyDraftProvider and lose
        // everything the parent has entered.
        void router.navigate({ to: "/themes" });
        return;
      }

      setStage(next);

      /*
        Replaced, not pushed.

        Seven stages that each pushed an entry turned one page into a seven-deep stack, so a
        parent who finished a book and opened the dashboard had to press the browser's back
        button six times to reach the home page — through checkout and the sign-in on the way.
        The journey is one route with one address bar entry; moving between its steps is not
        moving between pages, and the header's own back control is what walks the flow.
      */
      void router.navigate({ to: "/create", hash: next, replace: true });
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
    // World first, so it is the one that leads back out of the journey; details step back to it.
    case "world":
      return "/";
    case "profile":
      return options.mode === "continue" ? "/dashboard" : "/themes";
    case "preview":
      return "/create#profile";
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

/*
  The world comes first.

  Choosing the world is one tap and it is the part a child wants to do; entering a name, a date
  of birth and a photograph is the part a parent has to sit down for. Asking for the form first
  made the effort the price of admission. The order is now world, then details, then the preview.
*/
export const STAGE_PROGRESS: Record<JourneyStage, number> = {
  world: 33,
  profile: 66,
  preview: 100,
  auth: 100,
  checkout: 100,
  generating: 100,
  generated: 100,
};

export function progressLabelForStage(stage: JourneyStage): string {
  switch (stage) {
    case "world":
      return "ნაბიჯი 1 / 3";
    case "profile":
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
