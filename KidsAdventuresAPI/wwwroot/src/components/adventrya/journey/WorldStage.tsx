import { ArrowRight } from "lucide-react";
import { useRouter } from "@tanstack/react-router";
import { type CSSProperties, type ReactNode, useCallback, useEffect, useState } from "react";

import { AppHeader } from "@/components/adventrya/AppHeader";
import { useT } from "@/lib/i18n";
import type { JourneyDraft } from "@/lib/journey/draft";
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
  const [hovered, setHovered] = useState<WorldId | null>(null);
  const [hasInteracted, setHasInteracted] = useState(Boolean(draft.worldId));
  const [isSurprising, setIsSurprising] = useState(false);
  const [surpriseFocus, setSurpriseFocus] = useState<WorldId | null>(null);
  const [chosenByBeki, setChosenByBeki] = useState(false);
  const selectedId = draft.worldId;

  // A selected world remains the default, but hovering another one must still preview it.
  // That lets a child or parent safely compare destinations before changing their mind.
  // Once a child has chosen a world, keep the visible route, copy, and CTA tied to that choice.
  // Hovering another gateway can still invite a click, but it must not make the screen describe
  // one story while the button starts another.
  const focusId = surpriseFocus ?? selectedId ?? hovered ?? null;
  const focus = WORLDS.find((w) => w.id === focusId) ?? null;
  const selectedWorld = WORLDS.find((w) => w.id === selectedId) ?? null;
  const copy = t.journey.firstMap;

  const chooseWorld = useCallback(
    (worldId: WorldId, byBeki = false) => {
      setHovered(null);
      setSurpriseFocus(null);
      setHasInteracted(true);
      setIsSurprising(false);
      setChosenByBeki(byBeki);

      if (typeof navigator !== "undefined" && "vibrate" in navigator) {
        navigator.vibrate(14);
      }

      onChange({ worldId });
    },
    [onChange],
  );

  const letBekiChoose = useCallback(() => {
    if (selectedId || isSurprising || WORLDS.length === 0) return;
    setHasInteracted(true);
    setIsSurprising(true);
  }, [WORLDS.length, isSurprising, selectedId]);

  useEffect(() => {
    if (selectedId) setHasInteracted(true);
  }, [selectedId]);

  useEffect(() => {
    if (!isSurprising || WORLDS.length === 0) return;

    const ids = WORLDS.map((world) => world.id);
    const target = ids[Math.floor(Math.random() * ids.length)];
    let cursor = 0;
    setSurpriseFocus(ids[cursor]);

    const interval = window.setInterval(() => {
      cursor = (cursor + 1) % ids.length;
      setSurpriseFocus(ids[cursor]);
    }, 130);
    const finish = window.setTimeout(() => chooseWorld(target, true), 1180);

    return () => {
      window.clearInterval(interval);
      window.clearTimeout(finish);
    };
  }, [WORLDS, chooseWorld, isSurprising]);

  const bekiLine = selectedWorld
    ? chosenByBeki
      ? copy.beki.chosenByBeki(selectedWorld.mapLabel)
      : copy.beki.chosen(selectedWorld.mapLabel)
    : copy.beki.greeting;

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
          minimal
        />
      )}

      {/* The map, full bleed. Everything else floats over it. */}
      <div className="world-pick-stage">
        <div className="world-pick-map">
          <picture>
            <source
              media="(max-width: 999px) and (orientation: portrait), (min-width: 1000px) and (max-aspect-ratio: 4 / 3)"
              srcSet={WORLD_MAP.optimizedSrc.tall}
              type="image/jpeg"
            />
            <source srcSet={WORLD_MAP.optimizedSrc.wide} type="image/jpeg" />
            <img
              className="first-map-painting"
              src={WORLD_MAP.src.wide}
              alt=""
              aria-hidden="true"
              fetchPriority="high"
            />
          </picture>
          <div className="first-map-vignette" aria-hidden="true" />
          {!hasInteracted && !selectedId ? (
            <div
              className="first-map-guide-origin"
              aria-hidden="true"
              style={
                {
                  "--origin-x": `${WORLD_MAP.origin.wide.x}%`,
                  "--origin-y": `${WORLD_MAP.origin.wide.y}%`,
                  "--origin-x-tall": `${WORLD_MAP.origin.tall.x}%`,
                  "--origin-y-tall": `${WORLD_MAP.origin.tall.y}%`,
                } as CSSProperties
              }
            >
              <i />
            </div>
          ) : null}
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
          {focusId ? (
            <>
              <svg
                className="first-map-selection-route first-map-selection-route-wide"
                viewBox="0 0 100 100"
                preserveAspectRatio="none"
                aria-hidden="true"
              >
                <path key={`wide-${focusId}`} d={WORLD_MAP.routes.wide[focusId]} pathLength="1" />
              </svg>
              <svg
                className="first-map-selection-route first-map-selection-route-tall"
                viewBox="0 0 100 100"
                preserveAspectRatio="none"
                aria-hidden="true"
              >
                <path key={`tall-${focusId}`} d={WORLD_MAP.routes.tall[focusId]} pathLength="1" />
              </svg>
            </>
          ) : null}

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
                className={`world-pick-pin first-node-${world.id}${selectedId === world.id ? " is-selected" : ""}${focusId === world.id ? " is-focus" : ""}`}
                data-first-world-id={world.id}
                data-side={wide.side}
                style={
                  {
                    "--pin-x": `${wide.x}%`,
                    "--pin-y": `${wide.y}%`,
                    "--pin-x-tall": `${tall.x}%`,
                    "--pin-y-tall": `${tall.y}%`,
                    "--hit-width": `${wide.hitWidth}%`,
                    "--hit-height": `${wide.hitHeight}%`,
                    "--hit-width-tall": `${tall.hitWidth}%`,
                    "--hit-height-tall": `${tall.hitHeight}%`,
                    "--world-tint": lamp.tint,
                  } as CSSProperties
                }
                aria-pressed={selectedId === world.id}
                aria-label={`${world.mapTitle}. ${world.teaserBody}`}
                disabled={isSurprising}
                onMouseEnter={() => setHovered(world.id)}
                onFocus={() => setHovered(world.id)}
                onBlur={() => setHovered(null)}
                onClick={() => chooseWorld(world.id)}
              >
                {/* The illustration is the portal. This button is a generous invisible hit-area. */}
                {/*
                Short name on the map, full title in the panel. The old pins carried the
                whole book title on cards up to 329px wide, which is what made six of them
                unreadable over one painting.
              */}
                <span className="world-pick-pin-name">{world.mapLabel}</span>
              </button>
            );
          })}
        </div>
      </div>

      {/* One line of question. The paragraph that used to sit under it is gone. */}
      <header className="world-pick-ask">
        <p className="world-pick-kicker">{copy.eyebrow}</p>
        <h1>{copy.title}</h1>
        <span className="world-pick-instruction">{copy.guidance}</span>
      </header>

      <aside
        className={`world-pick-panel ${focus ? "is-open" : ""}${selectedWorld ? " is-selected" : ""}`}
        aria-live={isSurprising ? "off" : "polite"}
      >
        {/*
          Words only. The cover thumbnail repeated in miniature what the map behind it was
          already showing at full size, and the wish moved to the details step — it is a
          question about the story, and it belongs where the other questions are asked.
        */}
        {focus ? (
          <div className="world-pick-panel-copy">
            <div>
              <small>{selectedId === focus.id ? copy.selected : focus.chapter}</small>
              <strong>{selectedId === focus.id ? focus.mapLabel : focus.mapTitle}</strong>
              <p>{focus.teaserBody}</p>
            </div>
          </div>
        ) : (
          <p className="world-pick-hint">{copy.emptySelection}</p>
        )}

        <p className="world-pick-beki-line">{bekiLine}</p>

        {selectedWorld ? (
          <div className="world-pick-actions world-pick-actions-selected">
            {/* A router navigation keeps the in-memory journey draft intact. */}
            <button type="button" className="button button-primary" onClick={start}>
              {copy.continueTo(selectedWorld.mapLabel)}
              <ArrowRight aria-hidden="true" />
            </button>
          </div>
        ) : (
          <div className="world-pick-actions world-pick-actions-idle">
            <button
              type="button"
              className="button button-quiet world-pick-surprise"
              disabled={isSurprising}
              onClick={letBekiChoose}
            >
              {isSurprising ? copy.bekiChoosing : copy.letBekiChoose}
            </button>
          </div>
        )}
      </aside>
    </main>
  );
}
