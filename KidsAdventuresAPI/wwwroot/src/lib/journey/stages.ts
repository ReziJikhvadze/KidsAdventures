import { useCallback, useEffect, useState } from "react";
import { useLocation, useRouter } from "@tanstack/react-router";

import type { JourneyOrigin } from "@/lib/journey/draft";

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
      /*
        The world and the child stay in the address.

        Omitting `search` cleared it, so a reload on #preview or #checkout came back to a
        journey that had forgotten which world and which child — and the run in progress could
        not be rejoined, because the stored run is keyed to both. `new` is deliberately not
        carried: the provider treats it as "start blank" on every navigation it sees it on.
      */
      void router.navigate({
        to: "/create",
        hash: next,
        replace: true,
        search: (prev: Record<string, unknown>) => keepJourneySearch(prev),
      });
      window.scrollTo({ top: 0, behavior: "instant" as ScrollBehavior });
    },
    [router],
  );

  return [stage, goToStage];
}

/** The query keys that describe the book being made, and nothing that means "start over". */
const JOURNEY_SEARCH_KEYS = ["world", "worldId", "characterId", "characterIds", "mode", "orderId"];

function keepJourneySearch(prev: Record<string, unknown>): Record<string, unknown> {
  const kept: Record<string, unknown> = {};
  for (const key of JOURNEY_SEARCH_KEYS) {
    if (prev[key] !== undefined && prev[key] !== null && prev[key] !== "") kept[key] = prev[key];
  }
  return kept;
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
  options: {
    mode: "first" | "continue";
    isPrintUpgrade: boolean;
    hasWorld: boolean;
    /**
     * The child this book is for, and the book it continues, when there are any.
     *
     * They travel on the back link. The picker on the home page reads the address it is standing
     * on, and without them it treats the next choice as a fresh start — `new=1`, a cleared draft
     * and a blank form — so a parent stepping back to change the world for a child they had
     * already named would come forward again as somebody else.
     */
    characterId?: string | null;
    continuesFromBookId?: string | null;
    /** The screen the journey was entered from; see `JourneyDraft.cameFrom`. */
    cameFrom?: JourneyOrigin;
    /** The picker that chose the world, when it was one with a page of its own. */
    pickerHref?: string | null;
  },
): string {
  switch (stage) {
    // World first, so it is the one that leads back out of the journey; details step back to it.
    case "world":
      return "/";
    /*
      One step back, to the world picker, whatever brought the parent here.

      Continuing an adventure used to jump straight out to the dashboard from this step, which
      skipped the choice the parent had just made and, from the child's own map, put them on a
      screen they had not come from. Every route into the questions now passes through the
      picker, so the picker is what "back" means — carrying the child and the origin, so the
      picker's own arrow can finish the journey home.
    */
    /*
      Back to the home page's worlds, with the page under it.

      This used to go to `/themes` — a second, full-screen copy of the picker — so stepping back
      from the questions landed on what looked like a different page of worlds, and stepping back
      again threw the parent out to the top of the home page. One place chooses a world now, and
      it is a section of a page they can carry on scrolling.
    */
    case "profile":
      // The picker the parent actually used, when it was `/themes`; otherwise the home page's,
      // which is where the great majority of this journey begins.
      return options.pickerHref ?? worldsHref(options.characterId, options.continuesFromBookId);
    case "preview":
      return "/create#profile";
    case "auth":
      return "/create#preview";
    case "checkout":
      if (options.isPrintUpgrade) return originHref(options.cameFrom);
      // Auth is skipped when already signed in; preview is the real prior step.
      return "/create#preview";
    default:
      return originHref(options.cameFrom);
  }
}

/**
 * The worlds on the home page, carrying whatever the journey already knows.
 *
 * A query on `/` rather than on `/themes`: the picker there is a section of the page, and both it
 * and `JourneyDraftProvider` read the address the page is standing on.
 */
function worldsHref(characterId?: string | null, continuesFromBookId?: string | null): string {
  const params = new URLSearchParams();
  if (characterId) params.set("characterId", characterId);
  if (continuesFromBookId) params.set("continuesFromBookId", continuesFromBookId);
  const query = params.toString();
  return query ? `/?${query}#worlds` : "/#worlds";
}

/** Where a parent who leaves the journey lands: the screen they came in from. */
function originHref(cameFrom: JourneyOrigin | undefined): string {
  return cameFrom === "world" ? "/world" : "/dashboard";
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

/** The step label in the header, from the catalogue: an English visitor used to read Georgian here. */
export function progressLabelForStage(
  stage: JourneyStage,
  steps: { one: string; two: string; three: string; order: string; creating: string },
): string {
  switch (stage) {
    case "world":
      return steps.one;
    case "profile":
      return steps.two;
    case "preview":
      return steps.three;
    case "auth":
    case "checkout":
      return steps.order;
    case "generating":
    case "generated":
      return steps.creating;
  }
}
