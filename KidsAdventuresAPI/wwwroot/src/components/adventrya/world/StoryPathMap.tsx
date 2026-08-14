import { Check, Compass, Sparkles } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";

import { WorldIcon } from "@/components/adventrya/landing/icons";
import { useCenterMapNode } from "@/lib/hooks/useCenterMapNode";
import { useT } from "@/lib/i18n";
import type { AdventureMapResponse, WorldNodeResponse, WorldNodeState } from "@/lib/api/types";
import { STORY_MAP_ROUTES, useWorldById, WORLD_IDS, isWorldId, type WorldId } from "@/lib/worlds";

export type StoryPathMapProps = {
  map: AdventureMapResponse;
  activeWorldId: string | null;
  /** A server-verified world that has just been opened by the finished reader flow. */
  celebrationWorldId?: string | null;
  onSelect: (worldId: string) => void;
  compact?: boolean;
  className?: string;
};

function nodeCssState(state: WorldNodeState): "completed" | "next" | "unexplored" {
  if (state === "Completed") return "completed";
  if (state === "Next" || state === "Unlocked") return "next";
  return "unexplored";
}

// These two are plain helpers, not components, so they take the translations and the
// world dictionary as arguments instead of calling the hooks themselves. The component
// below already holds both, and a hook called from a helper is only ever correct by
// accident — the moment one of these is called conditionally the hook order breaks.
function statusLabel(
  t: ReturnType<typeof useT>,
  state: WorldNodeState,
  sequenceNumber?: number | null,
): string {
  if (state === "Completed") {
    return t.story.map.statusCompleted(sequenceNumber ?? 1);
  }
  if (state === "Next" || state === "Unlocked") return t.story.map.statusNext;
  return t.story.map.statusFuture;
}

function memoryCopy(
  worlds: ReturnType<typeof useWorldById>,
  node: WorldNodeResponse | undefined,
  worldId: WorldId,
) {
  const world = worlds[worldId];
  if (!node) {
    return { title: world.mapTitle, body: world.memoryBody, chapter: world.chapter };
  }
  return {
    title: node.bookTitle || world.mapTitle,
    body: world.memoryBody,
    chapter: world.chapter,
  };
}

function MapMotion({ activeId }: { activeId: string | null }) {
  return (
    <div className={`story-world-motion motion-for-${activeId ?? "none"}`} aria-hidden="true">
      <div className="world-aura aura-dinosaurs" />
      <div className="world-aura aura-space" />
      <div className="world-aura aura-pirates" />
      <div className="world-aura aura-animals" />
      <div className="world-aura aura-magic" />
      <div className="world-aura aura-airplanes" />
      <div className="dino-breath" />
      <div className="space-orbit">
        <i />
      </div>
      <div className="ship-sway">
        <WorldIcon type="pirates" />
      </div>
      <div className="animal-fireflies">
        <i />
        <i />
        <i />
        <i />
        <i />
      </div>
      <div className="magic-sparks">
        <i />
        <i />
        <i />
        <i />
      </div>
      <div className="plane-flight">
        <WorldIcon type="airplanes" />
        <i />
      </div>
    </div>
  );
}

export function StoryPathMap({
  map,
  activeWorldId,
  celebrationWorldId = null,
  onSelect,
  compact = false,
  className = "",
}: StoryPathMapProps) {
  const WORLD_BY_ID = useWorldById();
  const t = useT();
  const scrollRef = useRef<HTMLDivElement>(null);
  const [hoveredId, setHoveredId] = useState<string | null>(null);
  const [celebratingId, setCelebratingId] = useState<string | null>(celebrationWorldId);
  const celebratedWorldIds = useRef(new Set<string>());
  const emphasizedId = hoveredId ?? activeWorldId;
  const childName = map.characterName || t.common.fallbackHeroName;
  const unlockedCount = map.worlds.filter(
    (w) => w.state === "Completed" || w.state === "Unlocked" || w.state === "Next",
  ).length;
  const total = map.totalWorlds || map.worlds.length || WORLD_IDS.length;
  const progressPct = total > 0 ? Math.min(100, (map.completedCount / total) * 100) : 0;

  const nodesById = useMemo(() => {
    const dict: Record<string, WorldNodeResponse> = {};
    for (const node of map.worlds) dict[node.worldId] = node;
    return dict;
  }, [map.worlds]);

  const activeNode =
    (activeWorldId && nodesById[activeWorldId]) ||
    map.worlds.find((w) => w.state === "Next") ||
    map.worlds.find((w) => w.state === "Completed") ||
    map.worlds[0];

  const activeId = activeWorldId ?? activeNode?.worldId ?? null;
  const cssState = activeNode ? nodeCssState(activeNode.state) : "unexplored";
  const worldIdForCopy = (activeId && isWorldId(activeId) ? activeId : "dinosaurs") as WorldId;
  const memory = memoryCopy(WORLD_BY_ID, activeNode, worldIdForCopy);

  useCenterMapNode(scrollRef, `[data-world-id="${activeId}"]`, activeId);

  // The handoff only happens once per mounted map. Parent state can keep the selected node
  // after the query is removed without replaying the entrance animation on a later re-render.
  useEffect(() => {
    if (!celebrationWorldId || celebratedWorldIds.current.has(celebrationWorldId)) return;
    celebratedWorldIds.current.add(celebrationWorldId);
    setCelebratingId(celebrationWorldId);
  }, [celebrationWorldId]);

  useEffect(() => {
    if (!celebratingId) return;
    const timer = window.setTimeout(() => {
      setCelebratingId((current) => (current === celebratingId ? null : current));
    }, 1600);
    return () => window.clearTimeout(timer);
  }, [celebratingId]);

  return (
    <section
      className={`story-path-map ${compact ? "story-path-map-compact" : ""} ${className}`.trim()}
      aria-label={t.story.map.ariaLabel(childName)}
    >
      <div className="story-map-topline">
        <div>
          <p>
            <Compass aria-hidden="true" /> STORY PATH
          </p>
          <h2>
            {childName}
            {t.story.map.titleSuffix}
          </h2>
          <span>{t.story.map.lead}</span>
        </div>
        <div className="story-map-progress" aria-label={t.story.map.progress(unlockedCount, total)}>
          <strong>{map.completedCount}</strong>
          <span>
            გახსნილი
            <br />
            {t.story.map.ofTotal(total)}
          </span>
          <i>
            <b style={{ width: `${progressPct}%` }} />
          </i>
        </div>
      </div>

      <div className="story-map-scroll" ref={scrollRef}>
        <div className="story-map-canvas">
          <div className="story-map-painting" aria-hidden="true" />
          <div className="story-map-vignette" aria-hidden="true" />
          <div className="story-map-stars" aria-hidden="true">
            {Array.from({ length: 14 }, (_, i) => (
              <i key={i} />
            ))}
          </div>

          <svg
            className="story-map-route"
            viewBox="0 0 1000 650"
            preserveAspectRatio="none"
            aria-hidden="true"
          >
            <defs>
              <linearGradient id="storyRouteOpen" x1="0" x2="1">
                <stop offset="0" stopColor="#f6b94f" />
                <stop offset=".56" stopColor="#ffe6a2" />
                <stop offset="1" stopColor="#c89cff" />
              </linearGradient>
              <linearGradient id="storyRouteFuture" x1="0" x2="1">
                <stop offset="0" stopColor="#c89cff" stopOpacity=".78" />
                <stop offset="1" stopColor="#dce7ff" stopOpacity=".14" />
              </linearGradient>
              <filter id="storyRouteGlow">
                <feGaussianBlur stdDeviation="4" result="glow" />
                <feMerge>
                  <feMergeNode in="glow" />
                  <feMergeNode in="SourceGraphic" />
                </feMerge>
              </filter>
            </defs>
            <path
              className="story-route-open"
              d={STORY_MAP_ROUTES.open}
              filter="url(#storyRouteGlow)"
            />
            <path className="story-route-future" d={STORY_MAP_ROUTES.future} />
            <path className="story-route-future story-route-sky" d={STORY_MAP_ROUTES.futureSky} />
          </svg>

          <MapMotion activeId={emphasizedId} />

          {WORLD_IDS.map((id) => {
            const node = nodesById[id];
            const state = node?.state ?? "Locked";
            const visual = nodeCssState(state);
            const world = WORLD_BY_ID[id];
            const selected = activeId === id;
            const emphasized = emphasizedId === id;
            const celebrating = celebratingId === id;
            return (
              <button
                key={id}
                type="button"
                className={`story-world-node story-node-${id} is-${visual} ${selected ? "is-selected" : ""} ${emphasized ? "is-emphasized" : ""} ${celebrating ? "is-celebrating" : ""}`}
                data-world-id={id}
                aria-pressed={selected}
                aria-label={`${world.mapTitle}. ${world.chapter}`}
                onClick={() => onSelect(id)}
                onMouseEnter={() => setHoveredId(id)}
                onMouseLeave={() => setHoveredId(null)}
                onFocus={() => setHoveredId(id)}
                onBlur={() => setHoveredId(null)}
              >
                <span className="story-node-orbit" aria-hidden="true" />
                <span className="story-node-marker" aria-hidden="true">
                  {visual === "completed" ? (
                    <Check />
                  ) : visual === "next" ? (
                    <Sparkles />
                  ) : (
                    <span className="story-lock" />
                  )}
                </span>
                <span className="story-node-copy">
                  <small>{world.chapter}</small>
                  <strong>{world.theme}</strong>
                </span>
              </button>
            );
          })}
        </div>
      </div>

      {/*
        The worlds, written out.

        On a phone the map is 930px of painting inside a 350px window, and every pin carried its
        chapter and its name on a card that ran off the edge — "თავი I · დაკარგ…", "დინოზავრებ…".
        Reading the six worlds meant dragging a picture sideways and squinting at clipped labels.
        The pins stay pins; the names live here, where they fit, and tapping one moves the map to
        it. On a wide screen the pins carry their own labels and this list stays out of the way.
      */}
      <ul className="story-map-worlds">
        {WORLD_IDS.map((id) => {
          const node = nodesById[id];
          const visual = nodeCssState(node?.state ?? "Locked");
          const world = WORLD_BY_ID[id];
          return (
            <li key={id}>
              <button
                type="button"
                className={`story-world-row is-${visual} ${activeId === id ? "is-selected" : ""}`}
                aria-pressed={activeId === id}
                onClick={() => onSelect(id)}
              >
                <span className="story-world-row-mark" aria-hidden="true">
                  {visual === "completed" ? (
                    <Check />
                  ) : visual === "next" ? (
                    <Sparkles />
                  ) : (
                    <span className="story-lock" />
                  )}
                </span>
                <span className="story-world-row-copy">
                  <small>{world.chapter}</small>
                  <strong>{world.mapTitle}</strong>
                  <span className="story-world-row-state">
                    {statusLabel(t, node?.state ?? "Locked", node?.sequenceNumber)}
                  </span>
                </span>
              </button>
            </li>
          );
        })}
      </ul>

      <div className={`story-map-memory memory-${cssState}`} aria-live="polite">
        <span className="story-memory-icon">
          {cssState === "completed" ? <Check /> : <Sparkles />}
        </span>
        <div>
          <small>{memory.chapter}</small>
          <strong>{memory.title}</strong>
          <p>{memory.body}</p>
        </div>
        <span className="story-memory-status">
          {statusLabel(t, activeNode?.state ?? "Locked", activeNode?.sequenceNumber)}
        </span>
      </div>

      <div className="story-map-pan-cue" aria-hidden="true">
        <span>←</span> {t.story.map.panCue} <span>→</span>
      </div>
    </section>
  );
}
