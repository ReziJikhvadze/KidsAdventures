import { ArrowRight, Compass } from "lucide-react";
import { Link, useRouter } from "@tanstack/react-router";
import { type CSSProperties, type ReactNode, useRef, useState } from "react";

import { AppHeader } from "@/components/adventrya/AppHeader";
import { BekiGuide } from "@/components/adventrya/journey/BekiGuide";
import { useT } from "@/lib/i18n";
import type { JourneyDraft } from "@/lib/journey/draft";
import { primaryCharacter } from "@/lib/journey/draft";
import { STAGE_PROGRESS, progressLabelForStage } from "@/lib/journey/stages";
import {
  BOOK_LAYOUT,
  BOOK_SRC,
  ISLAND_LAYOUT,
  ISLAND_SRC,
  WORLD_TINT,
  routeFor,
} from "@/lib/journey/worldMap";
import { useWorlds, type WorldId } from "@/lib/worlds";

type Props = {
  draft: JourneyDraft;
  onChange: (patch: Partial<JourneyDraft> | ((prev: JourneyDraft) => JourneyDraft)) => void;
  /** Optional override; themes route uses AppHeader with demo progress. */
  header?: ReactNode;
};

/**
 * Choosing the world the first book happens in.
 *
 * The map is the page, and the map is seven pictures the app arranges: six islands and the
 * open book they grow out of. Each island already carries its own lit emblem, so nothing is
 * drawn on top of it — the code adds only what paint cannot have, which is the state of being
 * looked at, the state of having been chosen, and the route between the two.
 *
 * Both layouts' coordinates are written onto every island as custom properties and a media
 * query picks between them, so switching arrangement costs no JavaScript and cannot disagree
 * between the server render and the browser.
 */
export function WorldStage({ draft, onChange, header }: Props) {
  const WORLDS = useWorlds();
  const t = useT();
  const router = useRouter();
  const hero = primaryCharacter(draft);
  const heroName = hero.name.trim() || t.common.fallbackHeroName;
  const [hovered, setHovered] = useState<WorldId | null>(null);
  const selectedId = draft.worldId;
  const stageRef = useRef<HTMLDivElement>(null);
  const parallaxFrame = useRef(0);

  // What the panel is talking about: whatever is under the cursor, else what has been chosen.
  const focusId = hovered ?? selectedId ?? null;
  const focus = WORLDS.find((w) => w.id === focusId) ?? null;
  const copy = t.journey.firstMap;

  const start = () => {
    if (!selectedId) return;
    void router.navigate({
      to: "/create",
      search: { mode: "first", world: selectedId },
      hash: "profile",
    });
  };

  /*
    The islands drift a little against the pointer. It is a few pixels, deliberately — enough
    that they feel suspended in front of the sky rather than printed on it, not so much that
    anyone has to aim at a moving target. Written straight to custom properties on the stage
    and throttled to one frame, so React never re-renders for a mouse move.
  */
  const trackPointer = (event: React.PointerEvent<HTMLDivElement>) => {
    const stage = stageRef.current;
    if (!stage) return;
    const { clientX, clientY } = event;
    if (parallaxFrame.current) return;
    parallaxFrame.current = requestAnimationFrame(() => {
      parallaxFrame.current = 0;
      const box = stage.getBoundingClientRect();
      const dx = (clientX - box.left) / box.width - 0.5;
      const dy = (clientY - box.top) / box.height - 0.5;
      stage.style.setProperty("--drift-x", `${(-dx * 16).toFixed(2)}px`);
      stage.style.setProperty("--drift-y", `${(-dy * 11).toFixed(2)}px`);
    });
  };

  const releasePointer = () => {
    setHovered(null);
    const stage = stageRef.current;
    if (!stage) return;
    stage.style.setProperty("--drift-x", "0px");
    stage.style.setProperty("--drift-y", "0px");
  };

  const lit = focusId
    ? {
        wide: ISLAND_LAYOUT.wide[focusId],
        mid: ISLAND_LAYOUT.mid[focusId],
        tall: ISLAND_LAYOUT.tall[focusId],
      }
    : null;

  return (
    <main
      className={`screen world-pick selected-${selectedId ?? "none"} active-${focusId ?? "none"}`}
      data-active-theme={focusId ?? undefined}
    >
      {header ?? (
        // First step now: choosing the world comes before the child's details.
        <AppHeader
          backHref="/"
          progressLabel={progressLabelForStage("world")}
          progressValue={STAGE_PROGRESS.world}
        />
      )}

      <div
        className={`world-pick-stage ${selectedId ? "has-choice" : ""}`}
        ref={stageRef}
        onPointerMove={trackPointer}
        onPointerLeave={releasePointer}
      >
        <div className="first-map-sky" aria-hidden="true" />
        <div className="first-map-stars" aria-hidden="true">
          {Array.from({ length: 18 }, (_, i) => (
            <i key={i} />
          ))}
        </div>
        {/*
          The light on the island being looked at, sitting behind the sprites so it reads as
          the island glowing rather than a lamp shone at it. Its place and colour both come
          from the same table the islands do.
        */}
        <div
          className={`first-map-focus ${focusId ? "is-lit" : ""}`}
          aria-hidden="true"
          style={
            lit && focusId
              ? ({
                  "--focus-x": `${lit.wide.x}%`,
                  "--focus-y": `${lit.wide.y}%`,
                  "--focus-x-mid": `${lit.mid.x}%`,
                  "--focus-y-mid": `${lit.mid.y}%`,
                  "--focus-x-tall": `${lit.tall.x}%`,
                  "--focus-y-tall": `${lit.tall.y}%`,
                  "--focus-tint": WORLD_TINT[focusId].tint,
                } as CSSProperties)
              : undefined
          }
        />

        {/*
          Both layouts' routes are rendered and one is hidden, because an SVG path is an
          attribute rather than a style and a media query cannot rewrite it. Hiding costs six
          unused path elements; choosing in JavaScript would cost a hydration mismatch.
        */}
        {(["wide", "mid", "tall"] as const).map((layout) => (
          <svg
            key={layout}
            className={`first-map-routes routes-${layout}`}
            viewBox="0 0 1000 650"
            preserveAspectRatio="none"
            aria-hidden="true"
          >
            <defs>
              <linearGradient id={`firstPathActive-${layout}`} x1="0" x2="1">
                <stop offset="0" stopColor="#ffba46" />
                <stop offset=".52" stopColor="#fff0b6" />
                <stop offset="1" stopColor="#c99cff" />
              </linearGradient>
            </defs>
            {WORLDS.map((world) => (
              <path
                key={world.id}
                className={`first-route-active ${focusId === world.id ? "is-active" : ""}`}
                d={routeFor(layout, world.id)}
                fill="none"
                stroke={`url(#firstPathActive-${layout})`}
              />
            ))}
          </svg>
        ))}

        {WORLDS.map((world) => {
          const wide = ISLAND_LAYOUT.wide[world.id];
          const mid = ISLAND_LAYOUT.mid[world.id];
          const tall = ISLAND_LAYOUT.tall[world.id];
          const lamp = WORLD_TINT[world.id];
          return (
            <button
              key={world.id}
              type="button"
              className={`world-island${selectedId === world.id ? " is-selected" : ""}${focusId === world.id ? " is-focus" : ""}`}
              data-first-world-id={world.id}
              data-side={wide.side}
              style={
                {
                  "--pin-x": `${wide.x}%`,
                  "--pin-y": `${wide.y}%`,
                  "--pin-w": `${wide.w}%`,
                  "--pin-x-mid": `${mid.x}%`,
                  "--pin-y-mid": `${mid.y}%`,
                  "--pin-w-mid": `${mid.w}%`,
                  "--pin-x-tall": `${tall.x}%`,
                  "--pin-y-tall": `${tall.y}%`,
                  "--pin-w-tall": `${tall.w}%`,
                  "--world-tint": lamp.tint,
                  "--world-deep": lamp.deep,
                } as CSSProperties
              }
              aria-pressed={selectedId === world.id}
              aria-label={`${world.mapTitle}. ${world.teaserBody}`}
              onMouseEnter={() => setHovered(world.id)}
              onFocus={() => setHovered(world.id)}
              onBlur={() => setHovered(null)}
              onClick={() => onChange({ worldId: world.id })}
            >
              <span className="world-island-glow" aria-hidden="true" />
              <img
                className="world-island-art"
                src={ISLAND_SRC[world.id]}
                alt=""
                aria-hidden="true"
              />
              {/*
                The name, and what happens if you take it. Where there is a pointer this
                appears on hover and goes away again; where there is no pointer to hover with,
                it stays.
              */}
              <span className="world-island-name">
                {world.theme}
                <b aria-hidden="true">{copy.enter}</b>
              </span>
            </button>
          );
        })}

        {/* The book the whole world grows out of, and where every route begins. */}
        <img
          className="world-map-book"
          src={BOOK_SRC}
          alt=""
          aria-hidden="true"
          style={
            {
              "--book-x": `${BOOK_LAYOUT.wide.x}%`,
              "--book-y": `${BOOK_LAYOUT.wide.y}%`,
              "--book-w": `${BOOK_LAYOUT.wide.w}%`,
              "--book-x-mid": `${BOOK_LAYOUT.mid.x}%`,
              "--book-y-mid": `${BOOK_LAYOUT.mid.y}%`,
              "--book-w-mid": `${BOOK_LAYOUT.mid.w}%`,
              "--book-x-tall": `${BOOK_LAYOUT.tall.x}%`,
              "--book-y-tall": `${BOOK_LAYOUT.tall.y}%`,
              "--book-w-tall": `${BOOK_LAYOUT.tall.w}%`,
            } as CSSProperties
          }
        />

        {/*
          Beki lives on the map, not beside it. Positioned against the page instead, he floated
          above the header on a phone, where the map is one band of a stacked column rather
          than the whole page.
        */}
        <BekiGuide
          mood={selectedId ? "chosen" : hovered ? "peek" : "greeting"}
          peekTheme={hovered ? WORLDS.find((w) => w.id === hovered)?.theme : undefined}
        />
      </div>

      {/* One line of question. The paragraph that used to sit under it is gone. */}
      <header className="world-pick-ask">
        <p>
          <Compass aria-hidden="true" />
          {copy.eyebrow}
        </p>
        <h1>
          {copy.titlePrefix}
          {heroName}
          {copy.titleSuffix}
        </h1>
      </header>

      <aside className={`world-pick-panel ${focus ? "is-open" : ""}`} aria-live="polite">
        {focus ? (
          <div className="world-pick-panel-copy">
            <small>{focus.chapter}</small>
            <strong>{focus.mapTitle}</strong>
            <p>{focus.teaserBody}</p>
          </div>
        ) : (
          <p className="world-pick-hint">{copy.emptySelection}</p>
        )}

        <div className="world-pick-actions">
          <Link className="button button-quiet" to="/">
            {t.common.actions.back}
          </Link>
          {/*
            A router navigation, not a plain <a>. An anchor here was a full page load, which
            unmounted the draft provider and threw away everything the parent had entered.
          */}
          <button
            type="button"
            className={`button button-primary${!selectedId ? " is-disabled" : ""}`}
            disabled={!selectedId}
            onClick={start}
          >
            {copy.continue}
            <ArrowRight aria-hidden="true" />
          </button>
        </div>
      </aside>
    </main>
  );
}
