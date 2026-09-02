import { useLocation, useRouter } from "@tanstack/react-router";
import { ArrowLeft } from "lucide-react";
import { useCallback, useEffect, useRef, useState } from "react";

import { getAdventureMap } from "@/lib/api/worlds";
import type { WorldNodeState } from "@/lib/api/types";
import { useAuth } from "@/lib/auth/AuthContext";
import { useT } from "@/lib/i18n";
import type { JourneyDraft } from "@/lib/journey/draft";
import {
  FLIGHT_ROUTES,
  ISLAND_SPOTS,
  SELECTOR_ART,
  SELECTOR_WORLDS,
  type SelectorWorldId,
} from "@/lib/journey/worldSelector";
import { useWorldById, type WorldId } from "@/lib/worlds";

type Variant = "desktop" | "mobile";

/**
 * Whether the server would let this child start here.
 *
 * `null` is a visitor with no progress at all, who may start anywhere. Everything else mirrors
 * `WorldProgressService.EnsureCanStartAsync`: `Unlocked` and `Next` only.
 */
function startableState(state: WorldNodeState | null | undefined): boolean {
  return !state || state === "Unlocked" || state === "Next";
}

/**
 * Sections of the home page a parent can be sent here from, so the arrow can put them back
 * exactly where they were standing rather than at the top of a very long page.
 *
 * A closed list rather than "whatever `from` says": the value ends up in a URL this page hands
 * to an anchor, and an open one would follow anything a link chose to put there.
 */
const LANDING_SECTIONS = new Set(["books", "worlds", "pricing", "faq", "final", "footer"]);

/**
 * The home page itself, with no section named.
 *
 * The buttons at the top of the site — the hero's own call to action, "choose a world" in both
 * headers, the sticky bar on a phone — are not standing in a section a fragment can name. They
 * used to send nobody at all, which meant the arrow fell through to `/#worlds`: the painted map
 * two thirds of the way down a page the reader had pressed a button at the top of.
 */
const LANDING_TOP = "top";

function backHrefFromSearch(search: string): string {
  let params: URLSearchParams;
  try {
    params = new URLSearchParams(search);
  } catch {
    return "/";
  }

  /*
    Back to the worlds on the home page, wherever the parent came in from.

    Choosing a world lives on the home page now; this route is what a bookmark or an older link
    still reaches. Sending someone from here to their cabinet — a page of books — put them a step
    further from what they were in the middle of doing, which is the report this fixes.

    Two exceptions, both about landing where you actually were: a section of the home page names
    itself in `from=`, and a book cover off the shelf carries `?world=` and goes back to the shelf.
  */
  const from = params.get("from");

  /*
    A third exception, and it is not the home page at all: the parent's space.

    `from=world` and `from=dashboard` are both the cabinet — the first is what
    `continueViaPickerHref` has always written, from the days when the child's map was its own
    route. A parent who pressed "new book" there and then turned back was being put on the
    marketing page, one section of a page they were not on when they left.

    The child comes back with them. The cabinet opens on whichever child owns the newest book,
    which for a family with two of them is not the one whose button was just pressed.
  */
  if (from === "world" || from === "dashboard") {
    const characterId = params.get("characterId");
    return characterId ? `/dashboard?characterId=${encodeURIComponent(characterId)}` : "/dashboard";
  }

  if (from === LANDING_TOP) return "/";
  if (from && LANDING_SECTIONS.has(from)) return `/#${from}`;
  if (params.has("world") && !from) return "/#books";
  return "/#worlds";
}

type Props = {
  draft: JourneyDraft;
  onChange: (patch: Partial<JourneyDraft> | ((prev: JourneyDraft) => JourneyDraft)) => void;
  /**
   * Standing inside another page rather than being one.
   *
   * The selector carries a page's worth of chrome — the wordmark, and an arrow back to the home
   * page — which is right at /themes and wrong halfway down the home page itself, where the
   * header is already on screen and "back" would point at the ground the reader is standing on.
   */
  embedded?: boolean;
  /**
   * Worlds this child already has a finished book in.
   *
   * The one thing the parent's space adds to the picker: a tick on the islands they have been
   * to. A preview does not count — an unpaid draft is not a book, and marking one would tell a
   * parent they own something they have not bought.
   */
  completedWorldIds?: readonly WorldId[];
  /**
   * Extra query carried into `/create` when an island is chosen — the child, the book being
   * continued, the cast. On `/themes` these arrive in the address and are read from there; the
   * parent's space already holds them and hands them straight in.
   */
  startSearch?: Record<string, string>;
};

/**
 * Which of the six worlds this child has already been to, and which are still shut.
 *
 * The map endpoint is the authority on progress and needs a session, so this asks for nothing
 * while signed out and returns nothing when the request fails — an unreachable API leaves the
 * painting exactly as it looks for a first-time visitor, which is the safe way to be wrong: six
 * open islands and no false lock in front of a book somebody is trying to buy.
 */
function useChildWorldStates(
  characterId: string | null,
  enabled: boolean,
): Record<string, WorldNodeState> | null {
  const [states, setStates] = useState<Record<string, WorldNodeState> | null>(null);

  useEffect(() => {
    if (!characterId || !enabled) {
      setStates(null);
      return;
    }

    let cancelled = false;
    void getAdventureMap(characterId)
      .then((map) => {
        if (cancelled) return;
        const next: Record<string, WorldNodeState> = {};
        for (const node of map.worlds) next[node.worldId] = node.state;
        setStates(next);
      })
      .catch(() => {
        /* No map, no locks. See above. */
        if (!cancelled) setStates(null);
      });

    return () => {
      cancelled = true;
    };
  }, [characterId, enabled]);

  return states;
}

/**
 * The delivered world selector, wired into the journey.
 *
 * The markup, the class names, the artwork and the flight paths are the design handoff's own,
 * reproduced rather than reinterpreted: the stylesheet positions six islands by class name
 * against a fixed painting, so a renamed element is a hotspot in the wrong place. What changed
 * is everything the handoff could not know about — where the words come from, and what happens
 * when the button is pressed.
 *
 * The handoff shipped as a standalone page with a script that runs once on DOMContentLoaded and
 * a CTA that calls window.location.assign. Neither survives here. A deferred script never fires
 * again after the first client-side navigation, so a parent arriving at /themes from anywhere
 * inside the app would meet a dead painting; and a hard navigation unmounts JourneyDraftProvider,
 * which is where everything the parent has typed lives. So the behaviour is a component and the
 * CTA is a router navigation.
 */
export function WorldSelectorStage({
  draft,
  onChange,
  embedded = false,
  completedWorldIds,
  startSearch,
}: Props) {
  const t = useT();
  const router = useRouter();
  const worldById = useWorldById();
  const copy = t.journey.worldSelector;

  /*
    Back to where the reader actually came from.

    The arrow always pointed at `/`, so a parent who had scrolled to the foot of the home page
    and pressed the last call to action was returned to the top of it, and one who came from
    their own cabinet was thrown out to the marketing page entirely. Whoever sends a parent here
    now says so in `?from=`, and this reads it; `?world=` from a book cover is kept as the older
    spelling of "the shelf".
  */
  const search = useLocation().searchStr ?? "";
  const backHref = backHrefFromSearch(search);

  /*
    Whose map this is.

    Empty for everyone arriving from the marketing page: the painting is then simply a choice of
    six, because a visitor with no account has no progress to show. The cabinet is what supplies
    a child — its "new book" button carries the selected child's id — and that is the only case
    where an island can be shut.
  */
  const characterId = new URLSearchParams(search).get("characterId");
  const { isAuthenticated } = useAuth();
  const worldStates = useChildWorldStates(characterId, isAuthenticated && !embedded);

  /*
    Two ids for one place, and the seam is here on purpose.

    The stylesheet keys every hotspot and every focus layer to the handoff's own names —
    `.hotspot-clouds`, `.focus-kingdom` — while the rest of the product has called these worlds
    `airplanes` and `magic` since long before this painting existed. Renaming either side would
    mean editing the other; translating between them at the two points that cross the boundary
    costs one lookup and leaves both intact.
  */
  const selectedSelectorId =
    SELECTOR_WORLDS.find((world) => world.worldId === draft.worldId)?.id ?? null;

  const [selected, setSelected] = useState<SelectorWorldId | null>(selectedSelectorId);
  const [previewed, setPreviewed] = useState<SelectorWorldId | null>(null);
  const [ctaReady, setCtaReady] = useState(Boolean(selectedSelectorId));
  /** Bumped per selection so a re-picked world restarts its flight rather than resuming it. */
  const [flightRun, setFlightRun] = useState(0);
  const ctaTimer = useRef<number | null>(null);

  useEffect(() => {
    return () => {
      if (ctaTimer.current !== null) window.clearTimeout(ctaTimer.current);
    };
  }, []);

  /*
    Adopt a world the draft already knows about.

    The state above is seeded once, and on the first render the draft is always empty: it reads
    window.location, so JourneyDraftProvider fills it in an effect rather than in an initialiser.
    Everything that arrives with a world already decided lands after that — `/themes?world=magic`
    from a shared link, a parent coming back from the details step, the dashboard starting a
    second book — and without this the painting greets all of them unchosen and asks again.

    Choosing sets `selected` before it touches the draft, so by the time the draft catches up the
    two already agree and this does nothing. A restored world skips the wait for the star, which
    has nothing to announce: nobody just pressed anything.

    It follows the draft down as well as up. The draft is emptied under this page too — signing
    out on a shared device clears it, and so does arriving with `?new` — and a selector that kept
    its own copy would still be showing the previous choice, with a live button ready to carry it
    into a book it no longer belongs to.
  */
  useEffect(() => {
    const fromDraft = SELECTOR_WORLDS.find((world) => world.worldId === draft.worldId)?.id ?? null;
    if (fromDraft === selected) return;

    // A star still on its way belongs to the choice being replaced, and its timer would switch
    // the button back on after the selection under it had gone.
    if (ctaTimer.current !== null) {
      window.clearTimeout(ctaTimer.current);
      ctaTimer.current = null;
    }

    setSelected(fromDraft);
    setCtaReady(fromDraft !== null);
  }, [draft.worldId, selected]);

  /*
    Sideways only, and only while the selector is on screen.

    The handoff locked `overflow` outright on `body`. Two reasons that is not carried over: in a
    shared stylesheet it stops every other page in the product from scrolling, and even here it
    contradicts the handoff's own brief, which asks that short screens be allowed to scroll down
    and never across. Locking both axes on a phone in landscape clips the islands off the bottom
    with no way to reach them. So the horizontal axis is pinned and the vertical is left alone.
  */
  useEffect(() => {
    const previous = document.body.style.overflowX;
    document.body.style.overflowX = "hidden";
    return () => {
      document.body.style.overflowX = previous;
    };
  }, []);

  const chooseWorld = useCallback(
    (selectorId: SelectorWorldId) => {
      const world = SELECTOR_WORLDS.find((candidate) => candidate.id === selectorId);
      if (!world) return;
      /*
        Only what the server will actually accept.

        `WorldProgressService.EnsureCanStartAsync` refuses any node whose `CanStart` is false,
        and the map sets that flag for `Unlocked` and `Next` alone — so both a locked world and
        a finished one are refused. Offering them here does not open them; it walks a parent
        through the whole creation flow and fails at the moment they try to pay. Dimming a
        finished world is right and stays; letting it be chosen is not ours to decide from the
        browser.
      */
      if (!startableState(worldStates?.[world.worldId])) return;

      if (ctaTimer.current !== null) window.clearTimeout(ctaTimer.current);

      setSelected(selectorId);
      setPreviewed(null);
      setCtaReady(false);
      setFlightRun((run) => run + 1);
      onChange({ worldId: world.worldId });

      /*
        The button appears when the star lands, not before: offering it mid-flight is offering it
        over an animation the eye is still following. Halved from 1040ms — the flight was
        finished well before the button showed up, so the last half second was a pause with
        nothing happening in it. Honouring reduced motion means there is no flight to wait for,
        so it appears at once.
      */
      const wait = window.matchMedia("(prefers-reduced-motion: reduce)").matches ? 0 : 520;
      ctaTimer.current = window.setTimeout(() => setCtaReady(true), wait);
    },
    [onChange, worldStates],
  );

  const start = useCallback(() => {
    const world = SELECTOR_WORLDS.find((candidate) => candidate.id === selected);
    if (!world) return;

    // Carried through exactly as the old picker carried it: an existing child keeps their id, so
    // starting a second book does not create a second copy of the same kid at checkout.
    /*
      Handed in, or read from the address.

      `/themes` is reached by a link that carries the child and the adventure in its query. The
      parent's space renders this picker in place and already holds both, so it passes them as
      `startSearch` rather than putting them in an address nobody navigated to.
    */
    const incoming = (startSearch ?? router.state.location.search) as Record<string, unknown>;
    const text = (key: string) =>
      typeof incoming[key] === "string" ? (incoming[key] as string) : undefined;
    const forChild = text("characterId");

    /*
      Everything a continuation needs, carried straight through.

      The child's map used to link past this page to the preview, which starts writing a book on
      sight — so "another world" was a purchase, not a choice. It comes here first now, and this
      hands the prior book and the cast it carries forward on to the questions, where the parent
      confirms before anything is written.
    */
    const carried: Record<string, string> = {};
    for (const key of ["continuesFromBookId", "characterIds", "from"]) {
      const value = text(key);
      if (value) carried[key] = value;
    }

    /*
      Without a child, the form opens empty.

      The draft lives in a provider above the routes and every step of this journey is a
      client-side navigation, so it survives a finished book: a parent who made one for one child
      and then started another was met by the first child's name, birth date and photograph
      already filled in, and had to clear four fields before they could answer honestly. `new`
      is what JourneyDraftProvider reads as "this is a beginning" — it is set here rather than on
      every link into the picker, because this is the single door out of it.

      A child carried in the query is the opposite case and deliberately keeps its details: the
      cabinet asked for a second book for *that* child, and asking their parent to type them in
      again would be the same bug pointing the other way.
    */
    void router.navigate({
      to: "/create",
      search: {
        mode: carried.continuesFromBookId ? "continue" : "first",
        world: world.worldId,
        ...carried,
        ...(forChild ? { characterId: forChild } : { new: "1" }),
      },
      hash: "profile",
    });
  }, [router, selected, startSearch]);

  const selectedWorld = selected
    ? SELECTOR_WORLDS.find((world) => world.id === selected)
    : undefined;
  const status = !selectedWorld
    ? copy.statusIdle
    : ctaReady
      ? copy.statusReady(worldById[selectedWorld.worldId].mapTitle)
      : copy.statusFlying(worldById[selectedWorld.worldId].mapTitle);

  /*
    A <main> at /themes, a plain element inside another page — a document has one main, and the
    landing page's own is already open around this.
  */
  const Shell = embedded ? "div" : "main";

  return (
    <Shell
      className={`experience-shell${embedded ? " experience-shell-embedded" : ""}`}
      id="beki-world-selector"
      data-beki-world-selector
    >
      {/*
        Both stages are rendered, and the stylesheet's media queries decide which one is on
        screen. Choosing in JavaScript instead would mean the server and the browser disagreeing
        about which layout is in play on the first paint, which is a hydration mismatch.
      */}
      {(["desktop", "mobile"] as const).map((variant) => (
        <WorldStageArt
          key={variant}
          variant={variant}
          selected={selected}
          previewed={previewed}
          ctaReady={ctaReady}
          flightRun={flightRun}
          status={status}
          embedded={embedded}
          backHref={backHref}
          worldStates={worldStates}
          completedWorldIds={completedWorldIds}
          onPreview={setPreviewed}
          onChoose={chooseWorld}
          onStart={start}
        />
      ))}
    </Shell>
  );
}

/**
 * One painting and everything standing on it.
 *
 * Rendered twice — once per master — because the stylesheet swaps them with media queries. The
 * layer order below is the handoff's and is load-bearing: the dimmer sits over the art, the
 * focus layer punches the chosen island back through the dimmer, and the vignette closes the
 * edges over both.
 */
function WorldStageArt({
  variant,
  selected,
  previewed,
  ctaReady,
  flightRun,
  status,
  embedded,
  backHref,
  worldStates,
  completedWorldIds,
  onPreview,
  onChoose,
  onStart,
}: {
  variant: Variant;
  selected: SelectorWorldId | null;
  previewed: SelectorWorldId | null;
  ctaReady: boolean;
  flightRun: number;
  status: string;
  embedded: boolean;
  /** Where the arrow in the corner goes; see WorldSelectorStage. */
  backHref: string;
  /** This child's progress through the six worlds, or null when there is no child. */
  worldStates: Record<string, WorldNodeState> | null;
  /** Worlds with a finished book, marked with a tick. See WorldSelectorStage. */
  completedWorldIds?: readonly WorldId[];
  onPreview: (id: SelectorWorldId | null) => void;
  onChoose: (id: SelectorWorldId) => void;
  onStart: () => void;
}) {
  const t = useT();
  const worldById = useWorldById();
  const copy = t.journey.worldSelector;
  const art = { backgroundImage: `url("${SELECTOR_ART[variant]}")` };

  return (
    <section
      className={`art-stage ${variant}-stage${selected ? " has-selection" : ""}`}
      data-variant={variant}
      data-preview={previewed ?? undefined}
      data-selected={selected ?? undefined}
      aria-label={copy.stageLabel}
    >
      <div className="ambient-art" style={art} aria-hidden="true" />

      <div className={`master-frame ${variant}-frame`}>
        <div className="master-art" role="img" aria-label={copy.artLabel} style={art} />
        <div className="art-dimmer" aria-hidden="true" />

        <div className="focus-layers" aria-hidden="true">
          <div className={`focus-layer${selected ? ` focus-${selected}` : ""}`} style={art} />
          <div className="center-preserve" style={art} />
        </div>

        <div aria-hidden="true">
          {/* Keyed by the run so choosing the same island twice replays the flight: without it
              React keeps the finished SVG and the star never leaves Beki's heart again. */}
          {selected ? (
            <MagicFlight key={`${selected}-${flightRun}`} variant={variant} worldId={selected} />
          ) : null}
        </div>

        <div className="edge-vignette" aria-hidden="true" />

        <header className="experience-header">
          {/*
            The only way off this page used to be the browser's own back button: the map is a
            full-viewport painting with no app header above it, which is deliberate, but it left
            a parent who opened it from a book cover with nowhere to go. Neither this nor the
            wordmark belongs to a copy of the map sitting inside another page.
          */}
          {!embedded ? (
            /*
              The arrow, and only the arrow. The wordmark beside it went to the home page while
              the arrow went one step back, so the corner of the map offered two different
              exits and the larger one abandoned whatever the parent had started.
            */
            <div className="map-header-start">
              <a className="map-back" href={backHref} aria-label={copy.backLabel}>
                <ArrowLeft aria-hidden="true" />
              </a>
            </div>
          ) : null}

          <div className="headline">
            <p>{copy.eyebrow}</p>
            <h1>{copy.title}</h1>
            <span>{copy.lead}</span>
          </div>

          {/*
            No progress rail. It named three steps — world, hero, book — above a painting whose
            whole proposition is that choosing is one tap; a parent who has not started yet does
            not need to be told there are two more forms behind this one.
          */}
        </header>

        <div className="world-map">
          {SELECTOR_WORLDS.map((world) => {
            const isSelected = selected === world.id;
            const place = worldById[world.worldId];
            /* The one addition the parent's space makes to this picker. */
            const finished = completedWorldIds?.includes(world.worldId) ?? false;

            /*
              Only a child has a state. Without one every island is simply open, which is what a
              visitor who has never signed in should see.
            */
            /*
              Two states, and neither shuts a door.

              A world this child already has a book in is dimmed — there is nothing new behind
              it, and the button there offers to try it again rather than to open it. Everything
              else is lit, including worlds the server has not "unlocked" yet: locking turned six
              places to visit into four refusals, and a parent who wants the forest first should
              have the forest first.
            */
            const isDone = worldStates?.[world.worldId] === "Completed";
            const showAction = isSelected && ctaReady;

            const spot = ISLAND_SPOTS[variant][world.id];

            return (
              <div
                key={world.id}
                className={`world-node hotspot-${world.id}${isSelected ? " is-selected" : ""}${
                  previewed === world.id ? " is-previewed" : ""
                }${isDone || finished ? " is-done" : ""}`}
                data-world-node={world.id}
                /* Placed from the same island coordinates the star flies to, so a tap and a
                   landing can never again disagree about where a world is. */
                style={{
                  left: `${spot.cx - spot.w / 2}%`,
                  top: `${spot.cy - spot.h / 2}%`,
                  width: `${spot.w}%`,
                  height: `${spot.h}%`,
                }}
              >
                <button
                  type="button"
                  className="world-hotspot"
                  aria-label={
                    isDone || finished
                      ? `${place.mapTitle} — ${copy.visited}`
                      : `${place.mapTitle}: ${place.teaserBody}`
                  }
                  aria-pressed={isSelected}
                  onClick={() => onChoose(world.id)}
                  onPointerEnter={() => onPreview(world.id)}
                  onPointerLeave={() => onPreview(null)}
                  onFocus={() => onPreview(world.id)}
                  onBlur={() => onPreview(null)}
                >
                  <span className="hotspot-marker" aria-hidden="true">
                    <i />
                  </span>
                  {finished ? (
                    <span className="world-finished" aria-hidden="true">
                      <svg viewBox="0 0 24 24">
                        <path d="m5 12 4 4L19 6" />
                      </svg>
                    </span>
                  ) : null}
                </button>

                {/*
                  The title and the button are one object.

                  They used to be two: a name pinned near the island and, on the far side of the
                  island, a button that appeared from nowhere when it was chosen. A parent had to
                  find the second thing after reading the first, and on the lowest islands the
                  button had nowhere to be. Together they read as a card belonging to that
                  island — the name, then the one thing to do about it, growing downwards out of
                  the name it belongs to.

                  The card cannot be inside the hotspot: a button does not go inside a button.
                  It sits over it, transparent to the pointer except for the button itself, so
                  the whole island stays one big click target until there is something to press.
                */}
                <div className="world-card">
                  {/* The stylesheet shows the full title where there is room and the short one
                      where there is not, so both are set and neither is chosen in script. */}
                  <span className="world-label">
                    <strong className="full-title">{place.mapTitle}</strong>
                    <strong className="short-title">{place.mapLabel}</strong>
                    {/* One line under the name, and only for a child whose map this is: where
                        they have been, and what is not open to them yet. */}
                    {isDone || finished ? <em className="world-state">{copy.visited}</em> : null}
                  </span>

                  <div
                    className={`world-action${showAction ? " is-ready" : ""}`}
                    aria-hidden={!showAction}
                  >
                    <button
                      type="button"
                      className="continue-button"
                      data-world-id={world.id}
                      aria-label={copy.continueTo(place.mapTitle)}
                      disabled={!showAction}
                      tabIndex={showAction ? 0 : -1}
                      onClick={onStart}
                    >
                      {copy.create}
                      <svg viewBox="0 0 24 24" aria-hidden="true">
                        <path d="M5 12h13m-5-5 5 5-5 5" />
                      </svg>
                    </button>
                  </div>
                </div>
              </div>
            );
          })}
        </div>

        <p className="selection-status sr-only" aria-live="polite">
          {status}
        </p>
      </div>
    </section>
  );
}

/**
 * The star that carries the choice from Beki's lantern out to the island.
 *
 * SMIL rather than CSS, as delivered: `animateMotion` follows the same path the two drawn routes
 * use, so the star cannot drift off its own trail the way a keyframed translate does when the
 * painting is resized.
 */
function MagicFlight({ variant, worldId }: { variant: Variant; worldId: SelectorWorldId }) {
  const route = FLIGHT_ROUTES[variant][worldId];
  const [startX, startY] = route.start;
  const [endX, endY] = route.end;
  // Unique per stage and world: two <filter> elements sharing an id would have the mobile
  // painting's star reaching for the desktop one's glow.
  const glowId = `magic-glow-${variant}-${worldId}`;

  return (
    <svg
      className="magic-flight"
      viewBox="0 0 100 100"
      preserveAspectRatio="none"
      aria-hidden="true"
    >
      <defs>
        <filter id={glowId} x="-300%" y="-300%" width="700%" height="700%">
          <feGaussianBlur stdDeviation="0.7" result="blur" />
          <feMerge>
            <feMergeNode in="blur" />
            <feMergeNode in="SourceGraphic" />
          </feMerge>
        </filter>
      </defs>

      {/*
        A star leaves Beki, and a star is what is drawn.

        This was a filled disc inside a thin ring with a small star on top, which at the size it
        renders reads as a ringed planet sitting on Beki's chest — in a painting that already has
        three real planets in its sky. The disc and the ring are gone and the star is drawn large
        enough to cover the round medallion painted underneath it.

        Five points, not four. A four-pointed sparkle is a glint of light; a star is the thing a
        child draws when asked for one, and this one has to be recognised at a couple of
        millimetres by somebody who is three. The same shape flies and lands.
      */}
      <g transform={`translate(${startX} ${startY})`}>
        <g className="heart-flare">
          <path d="M0.0 -1.7 L0.38 -0.53 L1.62 -0.53 L0.62 0.2 L1.0 1.38 L0.0 0.65 L-1.0 1.38 L-0.62 0.2 L-1.62 -0.53 L-0.38 -0.53Z" />
        </g>
      </g>

      <path className="magic-route magic-route-glow" d={route.path} pathLength="1" />
      <path className="magic-route magic-route-core" d={route.path} pathLength="1" />

      <g className="flight-star" filter={`url(#${glowId})`}>
        <path d="M0.0 -1.15 L0.26 -0.36 L1.09 -0.36 L0.42 0.14 L0.68 0.93 L0.0 0.44 L-0.68 0.93 L-0.42 0.14 L-1.09 -0.36 L-0.26 -0.36Z" />
        <circle r="0.22" />
        <animateMotion
          path={route.path}
          begin="0.14s"
          dur="0.82s"
          fill="freeze"
          calcMode="spline"
          keyTimes="0;1"
          keySplines="0.22 0.75 0.28 1"
        />
      </g>

      <g transform={`translate(${endX} ${endY})`}>
        <g className="arrival-flare">
          <circle className="arrival-glow" r="0.8" />
          <circle className="arrival-ring" r="1.15" />
          <path d="M0.0 -1.35 L0.3 -0.42 L1.28 -0.42 L0.49 0.16 L0.79 1.09 L0.0 0.52 L-0.79 1.09 L-0.49 0.16 L-1.28 -0.42 L-0.3 -0.42Z" />
        </g>
      </g>
    </svg>
  );
}
