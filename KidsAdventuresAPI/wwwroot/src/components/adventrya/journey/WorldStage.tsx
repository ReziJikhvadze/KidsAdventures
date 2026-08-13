import { Compass, Sparkles } from "lucide-react";
import { Link, useRouter } from "@tanstack/react-router";
import { type ReactNode, useState } from "react";

import { AppHeader } from "@/components/adventrya/AppHeader";
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
 * Partner Demo `/themes` first-map screen.
 * DOM mirrors demo: theme-intro ∥ first-map-controls ∥ first-story-map.
 */
export function WorldStage({ draft, onChange, header }: Props) {
  const WORLDS = useWorlds();
  const t = useT();
  const router = useRouter();
  const hero = primaryCharacter(draft);
  const heroName = hero.name.trim() || t.common.fallbackHeroName;
  const [hovered, setHovered] = useState<WorldId | null>(null);
  const selectedId = draft.worldId;
  const activeId = hovered ?? selectedId ?? WORLDS[0]?.id ?? null;
  const selected = WORLDS.find((w) => w.id === selectedId) ?? WORLDS.find((w) => w.id === activeId);
  const copy = t.journey.firstMap;

  return (
    <main
      className={`screen theme-screen first-map-screen selected-${selectedId ?? "none"} active-${activeId ?? "none"}`}
      data-active-theme={activeId ?? undefined}
    >
      <div className="first-map-page-sky" aria-hidden="true" />
      <div className="grain" aria-hidden="true" />
      {header ?? (
        // First step now: choosing the world comes before the child's details.
        <AppHeader
          backHref="/"
          progressLabel={progressLabelForStage("world")}
          progressValue={STAGE_PROGRESS.world}
          childName={heroName}
        />
      )}

      <section className="theme-intro">
        <div className="profile-line">
          <span className="mini-avatar" aria-hidden="true">
            {heroName.slice(0, 1)}
          </span>
          <span>
            <small>პირველი თავგადასავლის გმირი</small>
            {heroName}
          </span>
        </div>
        <p className="eyebrow">
          <Sparkles aria-hidden="true" />
          {copy.eyebrow}
        </p>
        <h1>
          {copy.titlePrefix}
          {heroName}
          {copy.titleSuffix}
        </h1>
        <p className="theme-guidance">{copy.guidance}</p>
      </section>

      <section className="first-map-controls" aria-label="არჩეული სამყარო და სურვილი">
        <div className="selected-story">
          {selected ? (
            <>
              <span className="selected-glyph" aria-hidden="true">
                <Compass />
              </span>
              <div>
                <small>{selected.chapter}</small>
                <strong>{selected.mapTitle}</strong>
                <p>{selected.teaserBody}</p>
              </div>
            </>
          ) : (
            <>
              <span className="selected-glyph" aria-hidden="true">
                {copy.emptyGlyph}
              </span>
              <div>
                <strong>{copy.selectedHeading}</strong>
                <p>{copy.emptySelection}</p>
              </div>
            </>
          )}
        </div>

        <label className="theme-wish-field">
          <span>{copy.wishLabel}</span>
          <textarea
            value={draft.storyNotes}
            placeholder={copy.wishPlaceholder}
            onChange={(e) => onChange({ storyNotes: e.target.value })}
          />
          <small>{copy.wishHint}</small>
        </label>

        <div className="theme-actions">
          {/* First step of the journey now, so back leads out of it rather than to the form. */}
          <Link className="button button-quiet button-back" to="/">
            უკან
          </Link>
          {/*
            A router navigation, not a plain <a>. An anchor here was a full page load,
            which unmounted the draft provider and threw away everything the parent had
            entered — the child's name arrived at checkout empty, and creating the
            character then failed outright.
          */}
          <button
            type="button"
            className={`button button-primary${!selectedId ? " is-disabled" : ""}`}
            disabled={!selectedId}
            onClick={() => {
              if (!selectedId) return;
              void router.navigate({
                to: "/create",
                search: { mode: "first", world: selectedId },
                hash: "profile",
              });
            }}
          >
            {copy.continue}
            <Sparkles aria-hidden="true" />
          </button>
        </div>
      </section>

      <section
        className="first-story-map"
        aria-label="აირჩიე პირველი თავგადასავლის სამყარო რუკაზე"
        onMouseLeave={() => setHovered(null)}
      >
        <div className="first-story-map-scroll">
          <div className="first-story-map-canvas">
            <div className="first-map-painting" aria-hidden="true" />
            <div className="first-map-vignette" aria-hidden="true" />
            <div
              className={`first-map-focus ${activeId ? `focus-${activeId}` : ""}`}
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
                  className={`first-route-active ${activeId === world.id ? "is-active" : ""}`}
                  d={world.firstMapRoute}
                  fill="none"
                  stroke="url(#firstPathActive)"
                  strokeWidth="2.5"
                  filter="url(#firstPathGlow)"
                />
              ))}
            </svg>

            <div className="first-map-origin" aria-hidden="true">
              <span>
                <Compass />
              </span>
              <small>{heroName}ს ამბავი აქ იწყება</small>
            </div>

            {WORLDS.map((world, index) => {
              const isSelected = selectedId === world.id;
              const isActive = activeId === world.id;
              return (
                <button
                  key={world.id}
                  type="button"
                  className={`first-map-node first-node-${world.id}${isSelected ? " is-selected" : ""}${isActive ? " is-active" : ""}`}
                  data-first-world-id={world.id}
                  aria-pressed={isSelected}
                  aria-label={`${world.mapTitle}. ${world.teaserBody}`}
                  onMouseEnter={() => setHovered(world.id)}
                  onMouseLeave={() => setHovered(null)}
                  onFocus={() => setHovered(world.id)}
                  onBlur={() => setHovered(null)}
                  onClick={() => onChange({ worldId: world.id })}
                >
                  <span className="first-node-orbit" aria-hidden="true" />
                  <span className="first-node-marker" aria-hidden="true">
                    <Compass />
                  </span>
                  <span className="first-node-copy">
                    <small>სამყარო 0{index + 1}</small>
                    <strong>{world.mapTitle}</strong>
                    <em>{isSelected ? copy.selected : copy.activate}</em>
                  </span>
                </button>
              );
            })}

            {selected ? (
              <div className="first-map-live-caption" aria-live="polite">
                <span className="selected-glyph" aria-hidden="true">
                  <Compass />
                </span>
                <span>
                  <small>{selected.chapter}</small>
                  <strong>{selected.mapTitle}</strong>
                  <em>ეს სამყარო შენს შეხებაზე გაცოცხლდა</em>
                </span>
              </div>
            ) : null}
          </div>
        </div>

        <div className="first-map-pan-cue" aria-hidden="true">
          <span>←</span> გადაადგილე რუკა და შეეხე სამყაროს <span>→</span>
        </div>
      </section>
    </main>
  );
}
