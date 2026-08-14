import { ArrowRight, Compass } from "lucide-react";
import { Link, useRouter } from "@tanstack/react-router";
import { type CSSProperties, type ReactNode, useRef, useState } from "react";

import { AppHeader } from "@/components/adventrya/AppHeader";
import { WorldIcon } from "@/components/adventrya/landing/icons";
import { BekiGuide } from "@/components/adventrya/journey/BekiGuide";
import { useT } from "@/lib/i18n";
import type { JourneyDraft } from "@/lib/journey/draft";
import { primaryCharacter } from "@/lib/journey/draft";
import { STAGE_PROGRESS, progressLabelForStage } from "@/lib/journey/stages";
import { WORLD_MAP, WORLD_TINT } from "@/lib/journey/worldMap";
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
 * The map is the page. Everything else is one line of question, a guide who says one short
 * sentence at a time, and — once a world is picked — a single panel about that world. A
 * parent should be able to answer in a few seconds, and the child leaning over their
 * shoulder should have something to look at that looks back.
 *
 * Where the worlds sit on the painting is data, not stylesheet: lib/journey/worldMap.ts.
 * Both the wide and the tall coordinates are written onto every pin as custom properties and
 * a media query picks between them, so the layout switch costs no JavaScript and cannot
 * disagree between the server render and the browser.
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
    The painting drifts a little against the pointer. It is a few pixels, deliberately —
    enough that the islands feel like they are floating in front of the sky rather than
    printed on it, not so much that anyone has to aim at a moving target. Written straight
    to custom properties on the stage and throttled to one frame, so React never re-renders
    for a mouse move.
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
      stage.style.setProperty("--drift-x", `${(-dx * 18).toFixed(2)}px`);
      stage.style.setProperty("--drift-y", `${(-dy * 12).toFixed(2)}px`);
    });
  };

  const releasePointer = () => {
    setHovered(null);
    const stage = stageRef.current;
    if (!stage) return;
    stage.style.setProperty("--drift-x", "0px");
    stage.style.setProperty("--drift-y", "0px");
  };

  const focusAnchor = focusId ? WORLD_MAP.anchors.wide[focusId] : null;
  const focusAnchorTall = focusId ? WORLD_MAP.anchors.tall[focusId] : null;

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

      {/* The map, full bleed. Everything else floats over it. */}
      <div
        className="world-pick-stage"
        ref={stageRef}
        onPointerMove={trackPointer}
        onPointerLeave={releasePointer}
      >
        <picture>
          <source media="(max-width: 780px)" srcSet={WORLD_MAP.src.tall} />
          <img className="first-map-painting" src={WORLD_MAP.src.wide} alt="" aria-hidden="true" />
        </picture>
        <div className="first-map-vignette" aria-hidden="true" />
        {/*
          The warm glow that follows the focused island. Its position comes from the same
          table the pins do, so it cannot drift out of step with them — which is what six
          hand-written `.focus-{id}` rules used to do every time the art moved.
        */}
        <div
          className={`first-map-focus ${focusId ? "is-lit" : ""}`}
          aria-hidden="true"
          style={
            focusAnchor && focusAnchorTall && focusId
              ? ({
                  "--focus-x": `${focusAnchor.x}%`,
                  "--focus-y": `${focusAnchor.y}%`,
                  "--focus-x-tall": `${focusAnchorTall.x}%`,
                  "--focus-y-tall": `${focusAnchorTall.y}%`,
                  // The island is lit by its own lamp, not a generic warm one.
                  "--focus-tint": WORLD_TINT[focusId].tint,
                } as CSSProperties)
              : undefined
          }
        />
        <div className="first-map-stars" aria-hidden="true">
          {Array.from({ length: 16 }, (_, i) => (
            <i key={i} />
          ))}
        </div>

        <svg
          className="first-map-routes"
          viewBox="0 0 1000 650"
          preserveAspectRatio="none"
          aria-hidden="true"
        >
          <defs>
            <linearGradient id="firstPathFuture" x1="0" x2="1">
              <stop offset="0" stopColor="#f8cf78" stopOpacity=".18" />
              <stop offset="1" stopColor="#c7a0ff" stopOpacity=".42" />
            </linearGradient>
            <linearGradient id="firstPathActive" x1="0" x2="1">
              <stop offset="0" stopColor="#ffba46" />
              <stop offset=".52" stopColor="#fff0b6" />
              <stop offset="1" stopColor="#c99cff" />
            </linearGradient>
            <filter id="firstPathGlow">
              <feGaussianBlur stdDeviation="4" result="glow" />
              <feMerge>
                <feMergeNode in="glow" />
                <feMergeNode in="SourceGraphic" />
              </feMerge>
            </filter>
          </defs>
          {WORLDS.map((world) => (
            <path
              key={`future-${world.id}`}
              className="first-route-future"
              d={world.firstMapRoute}
              fill="none"
              stroke="url(#firstPathFuture)"
              strokeWidth="2"
            />
          ))}
          {/*
            The lit route draws itself from the child's feet outward when a world takes
            focus, rather than simply appearing. It is the one animation on this screen that
            says something: this is the way there.
          */}
          {WORLDS.map((world) => (
            <path
              key={`active-${world.id}`}
              className={`first-route-active ${focusId === world.id ? "is-active" : ""}`}
              d={world.firstMapRoute}
              fill="none"
              stroke="url(#firstPathActive)"
              strokeWidth="2.5"
              filter="url(#firstPathGlow)"
            />
          ))}
        </svg>

        {WORLDS.map((world) => {
          const wide = WORLD_MAP.anchors.wide[world.id];
          const tall = WORLD_MAP.anchors.tall[world.id];
          const lamp = WORLD_TINT[world.id];
          return (
            <button
              key={world.id}
              type="button"
              /* first-node-{id} carries no position any more; it is left for the handful of
                 per-world label nudges the narrow layout still needs. */
              className={`world-pick-pin first-node-${world.id}${WORLD_MAP.emblemsInArt ? " is-emblem" : ""}${selectedId === world.id ? " is-selected" : ""}${focusId === world.id ? " is-focus" : ""}`}
              data-first-world-id={world.id}
              data-side={wide.side}
              style={
                {
                  "--pin-x": `${wide.x}%`,
                  "--pin-y": `${wide.y}%`,
                  "--pin-x-tall": `${tall.x}%`,
                  "--pin-y-tall": `${tall.y}%`,
                  "--world-tint": lamp.tint,
                  "--world-deep": lamp.deep,
                  "--label-drop": `${wide.drop ?? 0}px`,
                  "--label-drop-tall": `${tall.drop ?? 0}px`,
                  "--emblem-size": `${WORLD_MAP.emblemSize}%`,
                } as CSSProperties
              }
              aria-pressed={selectedId === world.id}
              aria-label={`${world.mapTitle}. ${world.teaserBody}`}
              onMouseEnter={() => setHovered(world.id)}
              onFocus={() => setHovered(world.id)}
              onBlur={() => setHovered(null)}
              onClick={() => onChange({ worldId: world.id })}
            >
              <span className="world-pick-pin-halo" aria-hidden="true" />
              {/*
                Drawn only when the painting has no emblem of its own. Where it does, that
                emblem is the marker and this would be the same world's identity stated twice,
                by two things that never quite agree on size, colour or centre.
              */}
              {WORLD_MAP.emblemsInArt ? null : (
                <span className="world-pick-pin-dot" aria-hidden="true">
                  <WorldIcon type={world.id} />
                </span>
              )}
              {/*
                The name, and what happens if you take it. Where there is a pointer this
                appears on hover and goes away again, so six labels are never on the painting
                at once; where there is no pointer to hover with, it stays.
              */}
              <span className="world-pick-pin-name">
                {world.theme}
                <b aria-hidden="true">{copy.enter}</b>
              </span>
            </button>
          );
        })}

        {/*
          Beki lives on the map, not beside it. Positioned against the page instead, he
          floated above the header on a phone, where the map is one band of a stacked column
          rather than the whole page.
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
        {/*
          Words only. The cover thumbnail repeated in miniature what the map behind it was
          already showing at full size, and the wish moved to the details step — it is a
          question about the story, and it belongs where the other questions are asked.
        */}
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
