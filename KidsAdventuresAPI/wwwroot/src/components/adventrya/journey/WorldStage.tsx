import { ArrowRight, Compass } from "lucide-react";
import { Link, useRouter } from "@tanstack/react-router";
import { type ReactNode, useState } from "react";

import { AppHeader } from "@/components/adventrya/AppHeader";
import { WorldIcon } from "@/components/adventrya/landing/icons";
import { useT } from "@/lib/i18n";
import type { JourneyDraft } from "@/lib/journey/draft";
import { primaryCharacter } from "@/lib/journey/draft";
import { STAGE_PROGRESS, progressLabelForStage } from "@/lib/journey/stages";
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
 * This screen used to be a reading exercise: 134 words and 980 characters laid out beside a map
 * that got 38% of the width on a desktop and did not appear until halfway down a phone. Six
 * labels of six different widths competed across the picture, and the one question being asked —
 * which of these six places — was the hardest thing on the page to find.
 *
 * The map is the page now. Everything else is one line of question and, once a world is picked,
 * a single panel about that world. That is the whole trade: the picture is what a child leans in
 * for, and a parent should be able to answer in a few seconds and feel good about the answer.
 * Time spent here is hesitation, not delight — the map worth lingering over is /world, where a
 * child looks at what they have already opened.
 */
export function WorldStage({ draft, onChange, header }: Props) {
  const WORLDS = useWorlds();
  const t = useT();
  const router = useRouter();
  const hero = primaryCharacter(draft);
  const heroName = hero.name.trim() || t.common.fallbackHeroName;
  const [hovered, setHovered] = useState<WorldId | null>(null);
  const selectedId = draft.worldId;

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
          childName={heroName}
        />
      )}

      {/* The map, full bleed. Everything else floats over it. */}
      <div className="world-pick-stage" onMouseLeave={() => setHovered(null)}>
        <div className="first-map-painting" aria-hidden="true" />
        <div className="first-map-vignette" aria-hidden="true" />
        <div
          className={`first-map-focus ${focusId ? `focus-${focusId}` : ""}`}
          aria-hidden="true"
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

        {/*
          Pins, and only pins. Each used to carry a chapter number, a title and a call to action
          on a card up to 329px wide; six of those over one painting is a page of text pinned to
          a picture. The name of the place belongs in the panel, where there is room to say it
          once and say it properly.
        */}
        {WORLDS.map((world) => (
          <button
            key={world.id}
            type="button"
            className={`world-pick-pin first-node-${world.id}${selectedId === world.id ? " is-selected" : ""}${focusId === world.id ? " is-focus" : ""}`}
            data-first-world-id={world.id}
            aria-pressed={selectedId === world.id}
            aria-label={`${world.mapTitle}. ${world.teaserBody}`}
            onMouseEnter={() => setHovered(world.id)}
            onFocus={() => setHovered(world.id)}
            onBlur={() => setHovered(null)}
            onClick={() => onChange({ worldId: world.id })}
          >
            <span className="world-pick-pin-halo" aria-hidden="true" />
            {/*
              The world's own mark, not a number. "1" through "6" told a parent the order we
              happened to list them in, which is not a fact about the worlds and not something
              anyone needed; the icon says what the place is before the name is even read.
            */}
            <span className="world-pick-pin-dot" aria-hidden="true">
              <WorldIcon type={world.id} />
            </span>
            {/*
              The short name of the world, not its book title. The old pins carried the full
              title — "დაკარგული დინოზავრების ხეობა" — on cards up to 329px wide, which is what
              made six of them unreadable over one painting. One or two words fits under a dot.
            */}
            <span className="world-pick-pin-name">{world.theme}</span>
          </button>
        ))}
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
