import { BookOpen, Check, Loader2, Lock, Play, Sparkles, Star } from "lucide-react";
import { cn } from "@/lib/utils";
import type { StoryPathNodeStatus } from "@/lib/api/story-path";
import { fetchIllustrationObjectUrl } from "@/lib/api/adventure-packs";
import { useAuthedImageUrl } from "@/hooks/useAuthedImageUrl";

type MapNodeProps = {
  index: number;
  status: StoryPathNodeStatus;
  /** Position as a percentage (0–100) of the map's width/height. */
  xPct: number;
  yPct: number;
  themeTint: string;
  label?: string | null;
  coverUrl?: string | null;
  glowing?: boolean;
  isCurrent?: boolean;
  onSelect?: (index: number) => void;
};

/**
 * A single saga chapter marker. Rendered as an absolutely-positioned HTML button (NOT inside the
 * stretched map SVG) so it stays a perfect circle and lands exactly on the painted trail at the
 * given percentage coordinates.
 */
export function MapNode({
  index,
  status,
  xPct,
  yPct,
  themeTint,
  label,
  coverUrl,
  glowing,
  isCurrent,
  onSelect,
}: MapNodeProps) {
  const isLocked = status === "Locked";
  const isUnlocked = status === "Unlocked";
  const isGenerating = status === "Generating";
  const isReadyToRead = status === "ReadyToRead";
  const isComplete = status === "Complete";
  // Every node is tappable so a curious child always gets a friendly response (start, read, or a
  // "still ahead" cliffhanger for locked chapters).
  const interactive = !!onSelect;

  const resolvedCoverUrl = useAuthedImageUrl(
    isReadyToRead || isComplete ? coverUrl : null,
    fetchIllustrationObjectUrl,
  );

  const chapterLabel = `Chapter ${index + 1}`;
  const ariaLabel = isLocked
    ? `${chapterLabel} — locked`
    : isGenerating
      ? `${chapterLabel} — your story is being created`
      : isUnlocked
        ? `Start ${chapterLabel}`
        : isComplete
          ? `${chapterLabel} complete${label ? `: ${label}` : ""} — read again`
          : `Read ${chapterLabel}${label ? `: ${label}` : ""}`;

  const showPulse = isUnlocked || (isCurrent && isReadyToRead);

  return (
    <div
      className="absolute -translate-x-1/2 -translate-y-1/2"
      style={{ left: `${xPct}%`, top: `${yPct}%` }}
    >
      <div className="relative flex flex-col items-center">
        {/* "Start here" pointer above the active chapter */}
        {isUnlocked && (
          <span
            className="pointer-events-none absolute -top-7 whitespace-nowrap rounded-full px-2.5 py-1 text-[10px] font-bold text-white shadow-md motion-safe:animate-bounce motion-reduce:animate-none"
            style={{ background: themeTint }}
          >
            Start
          </span>
        )}

        <button
          type="button"
          disabled={!interactive}
          onClick={() => interactive && onSelect(index)}
          aria-label={ariaLabel}
          title={label ?? chapterLabel}
          className={cn(
            "group relative grid place-items-center rounded-full transition-transform duration-200",
            "h-11 w-11 sm:h-14 sm:w-14",
            isUnlocked && "h-12 w-12 sm:h-16 sm:w-16",
            interactive ? "cursor-pointer hover:scale-110 active:scale-95" : "cursor-default",
          )}
        >
          {/* Pulsing halo for the chapter that wants attention */}
          {(showPulse || glowing) && (
            <span
              className="absolute inset-0 rounded-full motion-safe:animate-ping motion-reduce:hidden"
              style={{ background: themeTint, opacity: 0.35 }}
              aria-hidden
            />
          )}

          {/* Node face */}
          <span
            className={cn(
              "relative grid h-full w-full place-items-center overflow-hidden rounded-full border-2 border-white shadow-[0_4px_12px_-2px_rgba(20,20,30,0.45)]",
              isLocked && "border-white/70 bg-muted/90 backdrop-blur-[1px]",
              isGenerating && "bg-card",
              (isReadyToRead || isComplete) && "bg-card",
            )}
            style={
              isUnlocked
                ? { background: themeTint, boxShadow: `0 0 0 4px color-mix(in oklab, ${themeTint} 30%, transparent), 0 6px 14px -4px rgba(20,20,30,0.5)` }
                : isReadyToRead
                  ? { boxShadow: `0 0 0 3px ${themeTint}, 0 6px 14px -4px rgba(20,20,30,0.5)` }
                  : undefined
            }
          >
            {(isReadyToRead || isComplete) && resolvedCoverUrl && (
              <img
                src={resolvedCoverUrl}
                alt=""
                className={cn(
                  "absolute inset-0 h-full w-full object-cover",
                  isComplete && "opacity-70",
                )}
                aria-hidden
              />
            )}

            {isLocked && <Lock className="h-2/5 w-2/5 text-muted-foreground/70" aria-hidden />}
            {isGenerating && <Loader2 className="h-2/5 w-2/5 animate-spin text-primary" aria-hidden />}
            {isUnlocked && <Sparkles className="h-1/2 w-1/2 text-white drop-shadow" aria-hidden />}
            {isReadyToRead && !resolvedCoverUrl && (
              <BookOpen className="h-2/5 w-2/5" style={{ color: themeTint }} aria-hidden />
            )}
            {isReadyToRead && resolvedCoverUrl && (
              <span className="absolute inset-0 grid place-items-center bg-black/15 opacity-0 transition group-hover:opacity-100">
                <Play className="h-2/5 w-2/5 fill-white text-white drop-shadow" aria-hidden />
              </span>
            )}
            {isComplete && (
              <Check
                className={cn("relative h-2/5 w-2/5 drop-shadow", resolvedCoverUrl ? "text-white" : "text-mint")}
                strokeWidth={3}
                aria-hidden
              />
            )}
          </span>

          {/* Gold star badge on finished chapters */}
          {isComplete && (
            <Star
              className="absolute -right-1 -top-1 h-4 w-4 fill-sun text-sun drop-shadow sm:h-5 sm:w-5"
              aria-hidden
            />
          )}
        </button>

        {/* Chapter number chip */}
        <span
          className={cn(
            "pointer-events-none mt-1 rounded-full border bg-card/95 px-2 py-0.5 text-[10px] font-bold leading-none shadow-sm",
            isLocked ? "border-border/60 text-muted-foreground/70" : "border-border/70 text-foreground",
          )}
        >
          {index + 1}
        </span>
      </div>
    </div>
  );
}
