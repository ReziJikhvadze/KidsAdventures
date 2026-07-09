import { useCallback, useEffect, useMemo, useState } from "react";
import { Check, Gift, Sparkles } from "lucide-react";
import type { StoryPageContent } from "@/lib/api/types";
import {
  regionToStyle,
  resolvePageInteractive,
  type ResolvedPageInteractive,
} from "@/lib/story/interactiveDefaults";
import {
  loadPageInteractiveSession,
  savePageInteractiveSession,
} from "@/lib/story/interactiveSession";
import { cn } from "@/lib/utils";

const AVATAR_REACTIONS = [
  { emoji: "👋", label: "Wave!" },
  { emoji: "😄", label: "Giggle!" },
  { emoji: "😮", label: "Surprise!" },
] as const;

const FIND_IT_AUTO_REVEAL_MS = 8000;
const COUNTING_AUTO_COMPLETE_MS = 10000;

type InteractiveIllustrationLayerProps = {
  page: StoryPageContent;
  pageIndex: number;
  allPages?: StoryPageContent[];
  packId?: string;
  childName?: string;
  hasHeroPhoto?: boolean;
  enabled?: boolean;
};

export function InteractiveIllustrationLayer({
  page,
  pageIndex,
  allPages,
  packId,
  childName,
  hasHeroPhoto = true,
  enabled = true,
}: InteractiveIllustrationLayerProps) {
  const interactive = useMemo(
    () =>
      enabled
        ? resolvePageInteractive(page, pageIndex, {
            childName,
            hasHeroPhoto,
            allPages: allPages ?? [page],
          })
        : null,
    [enabled, page, pageIndex, childName, hasHeroPhoto, allPages],
  );

  const saved = useMemo(
    () => loadPageInteractiveSession(packId, pageIndex),
    [packId, pageIndex],
  );

  const [avatarReaction, setAvatarReaction] = useState<(typeof AVATAR_REACTIONS)[number] | null>(
    null,
  );
  const [avatarTapCount, setAvatarTapCount] = useState(saved.avatarTapped ? 1 : 0);
  const [findItFound, setFindItFound] = useState(saved.findItFound ?? false);
  const [countValue, setCountValue] = useState(saved.countValue ?? 0);
  const [countingDone, setCountingDone] = useState(saved.countingDone ?? false);
  const [tappedCountIndices, setTappedCountIndices] = useState<Set<number>>(
    () => new Set(saved.tappedCountIndices ?? []),
  );
  const [revealDone, setRevealDone] = useState(saved.revealDone ?? false);

  useEffect(() => {
    const session = loadPageInteractiveSession(packId, pageIndex);
    setAvatarReaction(null);
    setAvatarTapCount(session.avatarTapped ? 1 : 0);
    setFindItFound(session.findItFound ?? false);
    setCountValue(session.countValue ?? 0);
    setCountingDone(session.countingDone ?? false);
    setTappedCountIndices(new Set(session.tappedCountIndices ?? []));
    setRevealDone(session.revealDone ?? false);
  }, [pageIndex, page.content, packId]);

  useEffect(() => {
    if (!interactive?.findIt || findItFound) return;
    const reveal = window.setTimeout(() => {
      setFindItFound(true);
      savePageInteractiveSession(packId, pageIndex, { findItFound: true });
    }, FIND_IT_AUTO_REVEAL_MS);
    return () => window.clearTimeout(reveal);
  }, [interactive?.findIt, findItFound, pageIndex, packId]);

  useEffect(() => {
    if (!interactive?.counting || countingDone) return;
    const timer = window.setTimeout(() => {
      const target = interactive.counting!.target;
      setCountValue(target);
      setCountingDone(true);
      const allIndices = Array.from({ length: target }, (_, i) => i);
      savePageInteractiveSession(packId, pageIndex, {
        countValue: target,
        countingDone: true,
        tappedCountIndices: allIndices,
      });
      setTappedCountIndices(new Set(allIndices));
    }, COUNTING_AUTO_COMPLETE_MS);
    return () => window.clearTimeout(timer);
  }, [interactive?.counting, countingDone, pageIndex, packId]);

  const handleAvatarTap = useCallback(() => {
    const reaction = AVATAR_REACTIONS[avatarTapCount % AVATAR_REACTIONS.length];
    setAvatarReaction(reaction);
    setAvatarTapCount((c) => c + 1);
    savePageInteractiveSession(packId, pageIndex, { avatarTapped: true });
    window.setTimeout(() => setAvatarReaction(null), 1200);
  }, [avatarTapCount, packId, pageIndex]);

  const handleFindItTap = useCallback(() => {
    setFindItFound(true);
    savePageInteractiveSession(packId, pageIndex, { findItFound: true });
  }, [packId, pageIndex]);

  const handleRevealTap = useCallback(() => {
    setRevealDone(true);
    savePageInteractiveSession(packId, pageIndex, { revealDone: true });
  }, [packId, pageIndex]);

  const handleCountZoneTap = useCallback(
    (zoneIndex: number) => {
      if (!interactive?.counting || countingDone) return;
      if (tappedCountIndices.has(zoneIndex)) return;

      const nextTapped = new Set(tappedCountIndices);
      nextTapped.add(zoneIndex);
      const nextValue = nextTapped.size;
      const done = nextValue >= interactive.counting.target;

      setTappedCountIndices(nextTapped);
      setCountValue(nextValue);
      if (done) setCountingDone(true);

      savePageInteractiveSession(packId, pageIndex, {
        countValue: nextValue,
        countingDone: done,
        tappedCountIndices: [...nextTapped],
      });
    },
    [countingDone, interactive?.counting, packId, pageIndex, tappedCountIndices],
  );

  if (!interactive) return null;

  return (
    <div className="pointer-events-none absolute inset-0 z-10" aria-hidden={false}>
      {interactive.avatarTap && (
        <AvatarTapZone
          interactive={interactive}
          reaction={avatarReaction}
          childName={childName}
          onTap={handleAvatarTap}
        />
      )}
      {interactive.findIt && (
        <FindItZone interactive={interactive} found={findItFound} onFound={handleFindItTap} />
      )}
      {interactive.counting && (
        <CountingZone
          interactive={interactive}
          value={countValue}
          done={countingDone}
          tappedIndices={tappedCountIndices}
          onZoneTap={handleCountZoneTap}
        />
      )}
      {interactive.revealItem && (
        <RevealItemZone interactive={interactive} revealed={revealDone} onReveal={handleRevealTap} />
      )}
    </div>
  );
}

function AvatarTapZone({
  interactive,
  reaction,
  childName,
  onTap,
}: {
  interactive: ResolvedPageInteractive;
  reaction: (typeof AVATAR_REACTIONS)[number] | null;
  childName?: string;
  onTap: () => void;
}) {
  const region = interactive.avatarTap?.region ?? { x: 12, y: 35, w: 28, h: 45 };

  return (
    <>
      <button
        type="button"
        className="pointer-events-auto absolute rounded-2xl border-2 border-primary/25 bg-primary/8 story-interactive-shimmer motion-reduce:animate-none focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
        style={regionToStyle(region)}
        onClick={(e) => {
          e.stopPropagation();
          onTap();
        }}
        aria-label={childName ? `Tap ${childName} in the picture` : "Tap the hero in the picture"}
      />
      {reaction && (
        <div
          className="pointer-events-none absolute z-20 flex flex-col items-center motion-safe:animate-rise motion-reduce:animate-none"
          style={{
            left: `${region.x + region.w / 2}%`,
            top: `${Math.max(region.y - 8, 4)}%`,
            transform: "translateX(-50%)",
          }}
        >
          <span className="text-3xl drop-shadow-md">{reaction.emoji}</span>
          <span className="mt-1 rounded-full bg-card/95 px-2 py-0.5 text-xs font-semibold shadow-soft">
            {childName ? `${childName} says ${reaction.label}` : reaction.label}
          </span>
        </div>
      )}
    </>
  );
}

function FindItZone({
  interactive,
  found,
  onFound,
}: {
  interactive: ResolvedPageInteractive;
  found: boolean;
  onFound: () => void;
}) {
  const findIt = interactive.findIt!;

  return (
    <>
      {!found && (
        <div className="pointer-events-none absolute inset-x-0 top-2 z-20 flex justify-center px-3">
          <p className="max-w-sm rounded-full bg-card/95 px-3 py-1.5 text-center text-xs font-medium text-foreground shadow-soft motion-safe:animate-pop-in">
            {findIt.prompt}
          </p>
        </div>
      )}
      {!found && (
        <button
          type="button"
          className="pointer-events-auto absolute rounded-full border-2 border-primary/45 bg-primary/12 story-interactive-shimmer motion-reduce:animate-none"
          style={regionToStyle(findIt.region)}
          onClick={(e) => {
            e.stopPropagation();
            onFound();
          }}
          aria-label={`Find the ${findIt.objectLabel}`}
        />
      )}
      {found && (
        <div
          className="pointer-events-none absolute flex flex-col items-center motion-safe:animate-pop-in motion-reduce:animate-none"
          style={{
            left: `${findIt.region.x + findIt.region.w / 2}%`,
            top: `${findIt.region.y + findIt.region.h / 2}%`,
            transform: "translate(-50%, -50%)",
          }}
        >
          <Sparkles className="h-6 w-6 text-primary" />
          <span className="mt-1 rounded-full bg-card/95 px-2 py-0.5 text-xs font-semibold capitalize shadow-soft">
            Found the {findIt.objectLabel}!
          </span>
        </div>
      )}
    </>
  );
}

function CountingZone({
  interactive,
  value,
  done,
  tappedIndices,
  onZoneTap,
}: {
  interactive: ResolvedPageInteractive;
  value: number;
  done: boolean;
  tappedIndices: Set<number>;
  onZoneTap: (index: number) => void;
}) {
  const counting = interactive.counting!;
  const regions = counting.regions ?? [];

  return (
    <>
      <div className="pointer-events-none absolute inset-x-0 top-2 z-20 flex justify-center px-3">
        <p className="max-w-sm rounded-2xl bg-card/92 px-3 py-2 text-center text-xs font-medium text-foreground shadow-soft motion-safe:animate-pop-in">
          {counting.prompt}
        </p>
      </div>
      <div className="pointer-events-none absolute inset-x-0 bottom-3 flex justify-center">
        <span
          className={cn(
            "rounded-full px-3 py-1 text-xs font-semibold tabular-nums shadow-soft",
            done ? "bg-mint/20 text-foreground" : "bg-card/90 text-foreground",
          )}
        >
          {done ? "All counted!" : `${value}/${counting.target} ${counting.label}`}
        </span>
      </div>
      {!done &&
        regions.map((region, index) => (
          <button
            key={`count-${index}`}
            type="button"
            disabled={tappedIndices.has(index)}
            className={cn(
              "pointer-events-auto absolute flex min-h-11 min-w-11 items-center justify-center rounded-full border-2 transition",
              tappedIndices.has(index)
                ? "border-mint/50 bg-mint/25"
                : "border-primary/40 bg-primary/15 story-interactive-shimmer motion-reduce:animate-none",
            )}
            style={regionToStyle(region)}
            onClick={(e) => {
              e.stopPropagation();
              onZoneTap(index);
            }}
            aria-label={`Count ${counting.label} ${index + 1}`}
          >
            {tappedIndices.has(index) && <Check className="h-4 w-4 text-mint" aria-hidden />}
          </button>
        ))}
    </>
  );
}

function RevealItemZone({
  interactive,
  revealed,
  onReveal,
}: {
  interactive: ResolvedPageInteractive;
  revealed: boolean;
  onReveal: () => void;
}) {
  const revealItem = interactive.revealItem!;

  return (
    <>
      {!revealed && (
        <div className="pointer-events-none absolute inset-x-0 top-2 z-20 flex justify-center px-3">
          <p className="max-w-sm rounded-full bg-card/95 px-3 py-1.5 text-center text-xs font-medium text-foreground shadow-soft motion-safe:animate-pop-in">
            {revealItem.prompt}
          </p>
        </div>
      )}
      <button
        type="button"
        disabled={revealed}
        className={cn(
          "pointer-events-auto absolute rounded-2xl border-2 transition-transform motion-safe:duration-300",
          revealed
            ? "scale-105 border-amber-500/60 bg-amber-400/10"
            : "border-amber-500/50 bg-amber-400/15 story-interactive-shimmer motion-reduce:animate-none",
        )}
        style={regionToStyle(revealItem.region)}
        onClick={(e) => {
          e.stopPropagation();
          onReveal();
        }}
        aria-label={revealed ? `Revealed: ${revealItem.revealLabel}` : `Open the ${revealItem.coverLabel}`}
      >
        {!revealed && (
          <span className="flex h-full w-full items-center justify-center">
            <Gift className="h-6 w-6 text-amber-600 motion-safe:animate-bounce motion-reduce:animate-none" aria-hidden />
          </span>
        )}
      </button>
      {revealed && (
        <div
          className="pointer-events-none absolute z-20 flex max-w-[min(88%,20rem)] flex-col items-center motion-safe:animate-pop-in motion-reduce:animate-none"
          style={{
            left: `${revealItem.region.x + revealItem.region.w / 2}%`,
            top: `${Math.max(revealItem.region.y - 6, 4)}%`,
            transform: "translate(-50%, -100%)",
          }}
        >
          <span className="rounded-2xl bg-card/95 px-3 py-2 text-center text-xs font-semibold capitalize text-foreground shadow-soft">
            It's {revealItem.revealLabel}! ✨
          </span>
          {revealItem.funFact && (
            <span className="mt-1 rounded-2xl bg-card/90 px-3 py-2 text-center text-[11px] font-medium text-muted-foreground shadow-soft">
              {revealItem.funFact}
            </span>
          )}
        </div>
      )}
    </>
  );
}
