import { useEffect, useMemo, useState } from "react";
import { createPortal } from "react-dom";
import { Maximize2, Minimize2, X } from "lucide-react";
import type { StoryPathWorld } from "@/lib/api/story-path";
import type { ThemeType } from "@/lib/api/types";
import { MAP_LAYOUTS } from "@/lib/story-path/mapLayouts";
import { buildSmoothPathD } from "@/lib/story-path/pathUtils";
import { THEME_TINTS } from "@/lib/story/themeTints";
import { MapNode } from "@/components/story-path/MapNode";
import { cn } from "@/lib/utils";

type WorldMapProps = {
  world: StoryPathWorld;
  theme: ThemeType;
  highlightNodeIndex?: number | null;
  className?: string;
  onNodeSelect?: (nodeIndex: number) => void;
};

export function WorldMap({
  world,
  theme,
  highlightNodeIndex = null,
  className,
  onNodeSelect,
}: WorldMapProps) {
  const layout = MAP_LAYOUTS[theme];
  const themeTint = THEME_TINTS[theme];
  const pathD = useMemo(() => buildSmoothPathD(layout.nodes), [layout.nodes]);
  const [isFullscreen, setIsFullscreen] = useState(false);

  const completeCount = useMemo(
    () => world.nodes.filter((n) => n.status === "Complete").length,
    [world.nodes],
  );
  const litPathLength = Math.min(completeCount / Math.max(world.nodes.length - 1, 1), 1);

  const currentIndex = useMemo(() => {
    const active = world.nodes.find(
      (n) => n.status === "Unlocked" || n.status === "ReadyToRead" || n.status === "Generating",
    );
    return active?.chapterIndex ?? null;
  }, [world.nodes]);

  useEffect(() => {
    if (!isFullscreen) return;
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") setIsFullscreen(false);
    };
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    window.addEventListener("keydown", onKeyDown);
    return () => {
      document.body.style.overflow = previousOverflow;
      window.removeEventListener("keydown", onKeyDown);
    };
  }, [isFullscreen]);

  const mapCard = (
    <div
      className={cn(
        "relative overflow-hidden rounded-3xl border border-border shadow-card",
        isFullscreen && "h-full w-full rounded-2xl border-0",
        className,
      )}
    >
      <div className={cn("relative w-full", isFullscreen ? "h-full" : "aspect-[16/10] sm:aspect-[16/9]")}>
        <img
          src={layout.artwork}
          alt=""
          className="absolute inset-0 h-full w-full object-cover"
          aria-hidden
        />
        {/* Gentle vignette + top scrim so nodes and the trail read clearly over the art */}
        <div
          className="absolute inset-0"
          style={{
            background:
              "radial-gradient(ellipse at 50% 55%, transparent 40%, rgba(15,18,30,0.35) 100%)",
          }}
          aria-hidden
        />

        {/* The trail/road — drawn behind the nodes, sharing the same 0–100 coordinate space */}
        <svg
          viewBox="0 0 100 100"
          preserveAspectRatio="none"
          className="pointer-events-none absolute inset-0 h-full w-full"
          aria-hidden
        >
          {/* Road base */}
          <path
            d={pathD}
            fill="none"
            stroke="rgba(255,255,255,0.34)"
            strokeWidth="9"
            strokeLinecap="round"
            strokeLinejoin="round"
            vectorEffect="non-scaling-stroke"
          />
          {/* Lit progress (completed chapters) */}
          {litPathLength > 0 && (
            <path
              d={pathD}
              fill="none"
              stroke={themeTint}
              strokeWidth="9"
              strokeLinecap="round"
              strokeLinejoin="round"
              pathLength={1}
              strokeDasharray={`${litPathLength} 1`}
              vectorEffect="non-scaling-stroke"
              className="motion-safe:transition-[stroke-dasharray] motion-reduce:transition-none duration-700"
              style={{ filter: `drop-shadow(0 0 3px ${themeTint})` }}
            />
          )}
          {/* Dashed centre line for a storybook "trail" feel */}
          <path
            d={pathD}
            fill="none"
            stroke="rgba(255,255,255,0.85)"
            strokeWidth="1.6"
            strokeLinecap="round"
            strokeDasharray="0.2 4"
            vectorEffect="non-scaling-stroke"
          />
        </svg>

        {/* Chapter nodes — plain HTML overlay so they stay perfectly circular */}
        {layout.nodes.map((pos, index) => {
          const node = world.nodes.find((n) => n.chapterIndex === index);
          if (!node) return null;
          return (
            <MapNode
              key={index}
              index={index}
              status={node.status}
              xPct={pos.x}
              yPct={pos.y}
              themeTint={themeTint}
              label={node.title}
              coverUrl={node.coverIllustrationUrl}
              glowing={highlightNodeIndex === index}
              isCurrent={currentIndex === index}
              onSelect={onNodeSelect}
            />
          );
        })}

        {/* Immersive fullscreen toggle */}
        <button
          type="button"
          onClick={() => setIsFullscreen((v) => !v)}
          className="absolute right-3 top-3 inline-flex h-9 w-9 items-center justify-center rounded-full border border-white/40 bg-black/45 text-white backdrop-blur transition hover:bg-black/65"
          aria-label={isFullscreen ? "Exit fullscreen map" : "View map fullscreen"}
        >
          {isFullscreen ? <Minimize2 className="h-4 w-4" /> : <Maximize2 className="h-4 w-4" />}
        </button>
      </div>

      {world.isWorldComplete && !isFullscreen && (
        <p className="relative border-t border-border/60 bg-card px-4 py-3 text-center text-sm text-muted-foreground">
          <span className="font-semibold text-foreground">Saga complete! 🎉 </span>
          Every chapter of this world has been read.
        </p>
      )}
    </div>
  );

  if (isFullscreen && typeof document !== "undefined") {
    return createPortal(
      <div className="fixed inset-0 z-[100] flex flex-col bg-background p-3 pt-[max(0.75rem,env(safe-area-inset-top))] sm:p-5">
        <div className="mb-2 flex items-center justify-between">
          <p className="font-display text-sm font-semibold sm:text-base">
            {world.theme} — chapter map
          </p>
          <button
            type="button"
            onClick={() => setIsFullscreen(false)}
            className="inline-flex h-10 w-10 items-center justify-center rounded-full border border-border bg-card transition hover:bg-secondary"
            aria-label="Close fullscreen map"
          >
            <X className="h-4 w-4" />
          </button>
        </div>
        <div className="min-h-0 flex-1">{mapCard}</div>
      </div>,
      document.body,
    );
  }

  return mapCard;
}
