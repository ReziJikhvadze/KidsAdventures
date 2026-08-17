import { ArrowRight } from "lucide-react";
import { useRouter } from "@tanstack/react-router";
import { type CSSProperties, type ReactNode, useCallback, useEffect, useState } from "react";

import { AppHeader } from "@/components/adventrya/AppHeader";
import { WorldMapCanvas, WorldMapPin } from "@/components/adventrya/journey/WorldMapCanvas";
import { useT } from "@/lib/i18n";
import type { JourneyDraft } from "@/lib/journey/draft";
import { STAGE_PROGRESS, progressLabelForStage } from "@/lib/journey/stages";
import { WORLD_MAP } from "@/lib/journey/worldMap";
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
 * The painting and its pins are WorldMapCanvas, which the landing page also shows — this screen
 * is what surrounds them: the question, the guide, the panel, and what a pin means when it is
 * clicked. Where the worlds sit on the painting is data, not stylesheet: lib/journey/worldMap.ts.
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
    /*
      Carry the child through.

      This used to hard-code the whole query, which was fine while the only way here was from
      the landing page with nobody chosen yet. The dashboard now sends parents here to start a
      new book for a child who already exists, and dropping the id made the journey create a
      second copy of the same kid at checkout.
    */
    const incoming = router.state.location.search as Record<string, unknown>;
    const characterId = typeof incoming.characterId === "string" ? incoming.characterId : undefined;
    void router.navigate({
      to: "/create",
      search: { mode: "first", world: selectedId, ...(characterId ? { characterId } : {}) },
      hash: "profile",
    });
  };

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
        <WorldMapCanvas focusId={focusId} className="world-pick-map" priority>
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
          {WORLDS.map((world) => (
            <WorldMapPin
              key={world.id}
              world={world}
              as="choice"
              label={`${world.mapTitle}. ${world.teaserBody}`}
              selected={selectedId === world.id}
              focus={focusId === world.id}
              disabled={isSurprising}
              onMouseEnter={() => setHovered(world.id)}
              onFocus={() => setHovered(world.id)}
              onBlur={() => setHovered(null)}
              onSelect={() => chooseWorld(world.id)}
            />
          ))}
        </WorldMapCanvas>
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
