import { useRouter } from "@tanstack/react-router";
import { useCallback, useEffect, useRef, useState } from "react";

import { useT } from "@/lib/i18n";
import type { JourneyDraft } from "@/lib/journey/draft";
import {
  FLIGHT_ROUTES,
  SELECTOR_ART,
  SELECTOR_WORLDS,
  type SelectorWorldId,
} from "@/lib/journey/worldSelector";
import { useWorldById } from "@/lib/worlds";

type Variant = "desktop" | "mobile";

type Props = {
  draft: JourneyDraft;
  onChange: (patch: Partial<JourneyDraft> | ((prev: JourneyDraft) => JourneyDraft)) => void;
};

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
export function WorldSelectorStage({ draft, onChange }: Props) {
  const t = useT();
  const router = useRouter();
  const worldById = useWorldById();
  const copy = t.journey.worldSelector;

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
  */
  useEffect(() => {
    const fromDraft = SELECTOR_WORLDS.find((world) => world.worldId === draft.worldId)?.id ?? null;
    if (!fromDraft || fromDraft === selected) return;

    setSelected(fromDraft);
    setCtaReady(true);
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

      if (ctaTimer.current !== null) window.clearTimeout(ctaTimer.current);

      setSelected(selectorId);
      setPreviewed(null);
      setCtaReady(false);
      setFlightRun((run) => run + 1);
      onChange({ worldId: world.worldId });

      // The button appears when the star lands, not before: offering it mid-flight is offering
      // it over an animation the eye is still following. Honouring reduced motion means there
      // is no flight to wait for, so it appears at once.
      const wait = window.matchMedia("(prefers-reduced-motion: reduce)").matches ? 0 : 1040;
      ctaTimer.current = window.setTimeout(() => setCtaReady(true), wait);
    },
    [onChange],
  );

  const start = useCallback(() => {
    const world = SELECTOR_WORLDS.find((candidate) => candidate.id === selected);
    if (!world) return;

    // Carried through exactly as the old picker carried it: an existing child keeps their id, so
    // starting a second book does not create a second copy of the same kid at checkout.
    const incoming = router.state.location.search as Record<string, unknown>;
    const characterId = typeof incoming.characterId === "string" ? incoming.characterId : undefined;

    void router.navigate({
      to: "/create",
      search: { mode: "first", world: world.worldId, ...(characterId ? { characterId } : {}) },
      hash: "profile",
    });
  }, [router, selected]);

  const selectedWorld = selected
    ? SELECTOR_WORLDS.find((world) => world.id === selected)
    : undefined;
  const status = !selectedWorld
    ? copy.statusIdle
    : ctaReady
      ? copy.statusReady(worldById[selectedWorld.worldId].mapTitle)
      : copy.statusFlying(worldById[selectedWorld.worldId].mapTitle);

  return (
    <main className="experience-shell" id="beki-world-selector" data-beki-world-selector>
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
          onPreview={setPreviewed}
          onChoose={chooseWorld}
          onStart={start}
        />
      ))}
    </main>
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
          <a className="brand" href="/" aria-label={copy.brandLabel}>
            <span>Beki</span>
            <i />
          </a>

          <div className="headline">
            <p>{copy.eyebrow}</p>
            <h1>{copy.title}</h1>
            <span>{copy.lead}</span>
          </div>

          {/*
            The rail says where the parent is, and here that is the first step, not the last.
            The handoff drew the world as step three behind a finished hero and character — the
            order this journey deliberately reversed, because choosing a world is one tap a child
            enjoys and the form is the part a parent has to sit down for.
          */}
          <ol className="progress-rail" aria-label={copy.railLabel}>
            <li className="is-current" aria-current="step">
              <span>
                <svg viewBox="0 0 24 24" aria-hidden="true">
                  <circle cx="12" cy="12" r="8.4" />
                  <path d="M3.9 12h16.2M12 3.6c2.4 2.3 3.6 5.1 3.6 8.4s-1.2 6.1-3.6 8.4C9.6 18.1 8.4 15.3 8.4 12S9.6 5.9 12 3.6Z" />
                </svg>
              </span>
              <b>{copy.railWorld}</b>
            </li>
            <li>
              <span>
                <svg viewBox="0 0 24 24" aria-hidden="true">
                  <path d="M12 20.4 4.2 13A5.1 5.1 0 0 1 11.4 5.8l.6.7.6-.7A5.1 5.1 0 0 1 19.8 13Z" />
                </svg>
              </span>
              <b>{copy.railHero}</b>
            </li>
            <li>
              <span>
                <svg viewBox="0 0 24 24" aria-hidden="true">
                  <path d="m12 2.8 2.7 5.5 6.1.9-4.4 4.3 1 6.1-5.4-2.9-5.4 2.9 1-6.1-4.4-4.3 6.1-.9Z" />
                </svg>
              </span>
              <b>{copy.railBook}</b>
            </li>
          </ol>
        </header>

        <div className="world-map">
          {SELECTOR_WORLDS.map((world) => {
            const isSelected = selected === world.id;
            const showAction = isSelected && ctaReady;
            const place = worldById[world.worldId];

            return (
              <div
                key={world.id}
                className={`world-node hotspot-${world.id}${isSelected ? " is-selected" : ""}${
                  previewed === world.id ? " is-previewed" : ""
                }`}
                data-world-node={world.id}
              >
                <button
                  type="button"
                  className="world-hotspot"
                  aria-label={`${place.mapTitle}: ${place.teaserBody}`}
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
                  {/* The stylesheet shows the full title where there is room and the short one
                      where there is not, so both are set and neither is chosen in script. */}
                  <span className="world-label">
                    <small>{place.chapter}</small>
                    <strong className="full-title">{place.mapTitle}</strong>
                    <strong className="short-title">{place.mapLabel}</strong>
                  </span>
                </button>

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
                    {copy.continue}
                    <svg viewBox="0 0 24 24" aria-hidden="true">
                      <path d="M5 12h13m-5-5 5 5-5 5" />
                    </svg>
                  </button>
                </div>
              </div>
            );
          })}
        </div>

        <p className="selection-status sr-only" aria-live="polite">
          {status}
        </p>

        <p className="interaction-hint" aria-hidden="true">
          <span />
          {copy.hint}
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

      <g transform={`translate(${startX} ${startY})`}>
        <g className="heart-flare">
          <circle r="0.72" />
          <circle className="heart-ring" r="1.05" />
          <path d="M0-1.25.34-.34 1.25 0 .34.34 0 1.25-.34.34-1.25 0-.34-.34Z" />
        </g>
      </g>

      <path className="magic-route magic-route-glow" d={route.path} pathLength="1" />
      <path className="magic-route magic-route-core" d={route.path} pathLength="1" />

      <g className="flight-star" filter={`url(#${glowId})`}>
        <path d="M0-1.15.31-.31 1.15 0 .31.31 0 1.15-.31.31-1.15 0-.31-.31Z" />
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
          <path d="M0-1.35.36-.36 1.35 0 .36.36 0 1.35-.36.36-1.35 0-.36-.36Z" />
        </g>
      </g>
    </svg>
  );
}
