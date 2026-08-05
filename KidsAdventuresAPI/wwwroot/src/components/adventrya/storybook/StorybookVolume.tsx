import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { ChevronLeft, ChevronRight, Lock, Sparkles } from "lucide-react";

import { useIllustrationUrl } from "@/lib/hooks/useIllustrationUrl";
import { useMediaQuery } from "@/lib/hooks/useMediaQuery";
import { useT } from "@/lib/i18n";
import { WORLD_COVER_ART, type WorldId, isWorldId } from "@/lib/worlds";
import type { StoryPageContent } from "@/lib/api/types";

export type StorybookLeaf =
  | { kind: "cover" }
  | { kind: "inside" }
  | { kind: "story"; page: StoryPageContent; storyIndex: number }
  | { kind: "locked"; pageNumber: number }
  | { kind: "qr" };

export type StorybookVolumeProps = {
  heroName: string;
  title: string;
  coverImageUrl?: string | null;
  worldId?: string | null;
  pages: StoryPageContent[];
  lockedPageCount?: number;
  isUnlocked?: boolean;
  className?: string;
  /** When false, omit controls / rail (thumbnail / shared teaser). */
  interactive?: boolean;
  initialIndex?: number;
  /**
   * Demo variants. `preview` stays single-page in the create layout so
   * `uses-desktop-spread` does not fight `ux-preview-product` max-height.
   */
  variant?: "full" | "hero" | "preview" | "display";
};

function fallbackCover(worldId?: string | null): string {
  if (worldId && isWorldId(worldId)) return WORLD_COVER_ART[worldId as WorldId];
  return WORLD_COVER_ART.dinosaurs;
}

function buildLeaves(options: {
  pages: StoryPageContent[];
  lockedPageCount: number;
  isUnlocked: boolean;
}): StorybookLeaf[] {
  const leaves: StorybookLeaf[] = [{ kind: "cover" }, { kind: "inside" }];
  options.pages.forEach((page, storyIndex) => {
    leaves.push({ kind: "story", page, storyIndex });
  });

  // Locked pages that arrived with their text already have a leaf of their own. Only
  // synthesise blank placeholders for a count the pages do not account for, which is
  // what older responses (a count and nothing else) still send.
  const lockedWithText = options.pages.filter((page) => page.isLocked).length;
  const placeholders = Math.max(0, options.lockedPageCount - lockedWithText);
  const startLocked = options.pages.length + 1;
  for (let i = 0; i < placeholders; i++) {
    leaves.push({ kind: "locked", pageNumber: startLocked + i });
  }
  if (options.isUnlocked) {
    leaves.push({ kind: "qr" });
  }
  return leaves;
}

function CoverFace({
  heroName,
  title,
  coverSrc,
}: {
  heroName: string;
  title: string;
  coverSrc: string;
}) {
  const t = useT();
  return (
    <article className="storybook-cover">
      <div className="storybook-cover-art" style={{ backgroundImage: `url("${coverSrc}")` }} />
      <div className="storybook-cover-wash" aria-hidden="true" />
      <span className="storybook-brand">{t.story.storybook.brand}</span>
      <div className="storybook-cover-copy">
        <small>
          {t.story.storybook.belongsToPrefix}
          {heroName}
          {t.story.storybook.belongsToSuffix}
        </small>
        <h2>{title}</h2>
        <span>{t.story.storybook.coverOwnerLine(heroName)}</span>
      </div>
    </article>
  );
}

function InsideCoverFace({ heroName }: { heroName: string }) {
  const t = useT();
  return (
    <article className="storybook-inside-cover">
      <div className="inside-cover-constellation" aria-hidden="true" />
      <span className="inside-cover-mark">A</span>
      <small>{t.story.storybook.coverOwner}</small>
      <strong>{heroName}</strong>
      <p>{t.story.storybook.coverEpigraph}</p>
      <i>ADVENTRYA · PERSONAL STORY</i>
    </article>
  );
}

function StoryFace({
  leaf,
  heroName,
  pageSide,
  totalStoryPages,
}: {
  leaf:
    | Extract<StorybookLeaf, { kind: "story" }>
    | Extract<StorybookLeaf, { kind: "locked" }>
    | Extract<StorybookLeaf, { kind: "qr" }>;
  heroName: string;
  pageSide?: "left" | "right";
  totalStoryPages: number;
}) {
  const t = useT();
  const artUrl = useIllustrationUrl(leaf.kind === "story" ? leaf.page.illustrationUrl : null);
  const locked = leaf.kind === "locked";
  const pageNumber =
    leaf.kind === "story"
      ? leaf.storyIndex + 1
      : leaf.kind === "locked"
        ? leaf.pageNumber
        : totalStoryPages;

  if (leaf.kind === "qr") {
    return (
      <article className={`storybook-page ${pageSide ? `page-${pageSide}` : ""}`}>
        <div className="storybook-page-content">
          <div
            className="storybook-page-art"
            style={{ backgroundImage: `url("${fallbackCover()}")` }}
          />
          <div className="storybook-page-copy">
            <div className="storybook-qr-moment">
              <span className="storybook-qr" aria-hidden="true" />
              <div>
                <strong>{t.story.storybook.qrTitle}</strong>
                <small>
                  {t.story.storybook.qrScanPrefix}
                  {heroName}
                  {t.story.storybook.qrWorldSuffix}
                </small>
              </div>
            </div>
          </div>
        </div>
      </article>
    );
  }

  // A preview page now carries its real text with the artwork withheld, so only the
  // illustration is obscured. `kind: "locked"` remains for responses that send a bare
  // count and no text at all — those still render the full-page lock panel.
  const artLocked = leaf.kind === "story" && leaf.page.isLocked === true;

  const copy =
    leaf.kind === "story"
      ? leaf.page.content || leaf.page.caption || leaf.page.title
      : `${t.story.storybook.lockedPagePrefix}${leaf.pageNumber}${t.story.storybook.lockedPageSuffix}`;

  return (
    <article
      className={`storybook-page ${pageSide ? `page-${pageSide}` : ""} ${
        locked ? "is-locked" : ""
      } ${artLocked ? "is-art-locked" : ""}`}
    >
      <div className="storybook-page-content">
        <div
          className="storybook-page-art"
          style={artUrl ? { backgroundImage: `url("${artUrl}")` } : undefined}
        />
        {/* Sibling of the art, not a child: a CSS filter blurs its descendants too, so a
            badge nested inside the blurred element would be unreadable. */}
        {artLocked ? (
          <span className="storybook-art-lock">
            <Lock aria-hidden="true" />
            <small>{t.story.storybook.lockedNote}</small>
          </span>
        ) : null}
        <div className="storybook-page-copy">
          <small>
            {leaf.kind === "story"
              ? leaf.page.caption ||
                leaf.page.title ||
                t.story.storybook.pageLabel(pageNumber, totalStoryPages)
              : t.story.storybook.lockedNote}
          </small>
          <p>{copy}</p>
        </div>
      </div>
      {locked ? (
        <div className="storybook-lock">
          <span>
            <Lock />
          </span>
          <strong>
            {t.story.storybook.lockedPagePrefix}
            {leaf.pageNumber}
            {t.story.storybook.lockedPageSuffix}
          </strong>
          <small>{t.story.storybook.lockedNote}</small>
        </div>
      ) : null}
    </article>
  );
}

function LeafView({
  leaf,
  heroName,
  title,
  coverSrc,
  pageSide,
  totalStoryPages,
}: {
  leaf: StorybookLeaf | null;
  heroName: string;
  title: string;
  coverSrc: string;
  pageSide?: "left" | "right";
  totalStoryPages: number;
}) {
  if (!leaf) return <InsideCoverFace heroName={heroName} />;
  if (leaf.kind === "cover")
    return <CoverFace heroName={heroName} title={title} coverSrc={coverSrc} />;
  if (leaf.kind === "inside") return <InsideCoverFace heroName={heroName} />;
  return (
    <StoryFace
      leaf={leaf}
      heroName={heroName}
      pageSide={pageSide}
      totalStoryPages={totalStoryPages}
    />
  );
}

export function StorybookVolume({
  heroName,
  title,
  coverImageUrl,
  worldId,
  pages,
  lockedPageCount = 0,
  isUnlocked = false,
  className,
  interactive = true,
  initialIndex = 0,
  variant = "full",
}: StorybookVolumeProps) {
  const t = useT();
  // Demo Ot uses min-width: 1024px for desktop spreads (not 781).
  const wideViewport = useMediaQuery("(min-width: 1024px)");
  // Preview column is too narrow for open spreads — keep single-page like a tall phone book.
  const desktopSpread = wideViewport && variant !== "preview" && variant !== "display";
  const resolvedClassName =
    className ?? `storybook storybook-${variant}${worldId ? ` theme-${worldId}` : ""}`;
  const leaves = useMemo(
    () => buildLeaves({ pages, lockedPageCount, isUnlocked }),
    [pages, lockedPageCount, isUnlocked],
  );
  const lastIndex = Math.max(0, leaves.length - 1);
  const [index, setIndex] = useState(Math.min(initialIndex, lastIndex));
  const [turning, setTurning] = useState<"next" | "previous" | null>(null);
  const [fromIndex, setFromIndex] = useState<number | null>(null);
  const timers = useRef<number[]>([]);
  const swipeX = useRef<number | null>(null);
  const resolvedCover = useIllustrationUrl(coverImageUrl) ?? fallbackCover(worldId);
  const totalStoryPages = pages.length + lockedPageCount;

  useEffect(() => () => timers.current.forEach((id) => window.clearTimeout(id)), []);

  // Desktop spreads land on even content indices (except cover / inside cover).
  useEffect(() => {
    if (!desktopSpread || index === 0 || index === 1 || index % 2 === 0) return;
    const id = window.setTimeout(() => setIndex(Math.max(2, index - 1)), 0);
    return () => window.clearTimeout(id);
  }, [desktopSpread, index]);

  const spreadSteps = useMemo(() => {
    const steps = [0, 1];
    for (let i = 2; i <= lastIndex; i += 2) steps.push(i);
    return steps;
  }, [lastIndex]);

  const goTo = useCallback(
    (next: number, direction: "next" | "previous") => {
      if (!interactive || turning || next < 0 || next > lastIndex) return;
      if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
        setIndex(next);
        return;
      }
      setFromIndex(index);
      setTurning(direction);
      const id = window.setTimeout(() => {
        setIndex(next);
        setTurning(null);
        setFromIndex(null);
      }, 900);
      timers.current.push(id);
    },
    [interactive, turning, lastIndex, index],
  );

  const stepIndex = desktopSpread ? spreadSteps.indexOf(index) : index;
  const prevTarget = desktopSpread ? (spreadSteps[Math.max(0, stepIndex - 1)] ?? 0) : index - 1;
  const nextTarget = desktopSpread
    ? (spreadSteps[Math.min(spreadSteps.length - 1, stepIndex + 1)] ?? lastIndex)
    : index + 1;
  const canPrev = index > 0;
  const canNext = desktopSpread ? stepIndex < spreadSteps.length - 1 : index < lastIndex;

  // Native non-passive wheel so preventDefault actually stops page scroll over the book.
  const rootRef = useRef<HTMLDivElement | null>(null);
  useEffect(() => {
    const node = rootRef.current;
    if (!node || !interactive) return;
    const handler = (event: WheelEvent) => {
      if (turning) return;
      if (Math.abs(event.deltaY) < 8 && Math.abs(event.deltaX) < 8) return;
      const forward =
        Math.abs(event.deltaY) >= Math.abs(event.deltaX) ? event.deltaY > 0 : event.deltaX > 0;
      if ((forward && canNext) || (!forward && canPrev)) {
        event.preventDefault();
        if (forward) goTo(nextTarget, "next");
        else goTo(prevTarget, "previous");
      }
    };
    node.addEventListener("wheel", handler, { passive: false });
    return () => node.removeEventListener("wheel", handler);
  }, [interactive, turning, canNext, canPrev, goTo, nextTarget, prevTarget]);

  // The QR leaf sits past the last story page, so numbering it produces inverted
  // ranges like "7–6" on the final step. It gets its own label instead.
  const progressLabel =
    index === 0
      ? t.story.storybook.coverLabel(totalStoryPages || leaves.length - 2)
      : index === 1
        ? t.story.storybook.insideCover
        : leaves[index]?.kind === "qr"
          ? t.story.storybook.qrTitle
          : desktopSpread && index >= 2
            ? t.story.storybook.spreadLabel(
                index - 1,
                Math.min(totalStoryPages, index),
                totalStoryPages || 1,
              )
            : t.story.storybook.pageLabel(Math.max(1, index - 1), totalStoryPages || 1);

  const spreadClass = desktopSpread ? "uses-desktop-spread" : "uses-single-page";
  const openClass = index === 0 ? "is-closed" : "is-open";
  const showSpread = desktopSpread && index > 0;

  const leftLeaf = index === 1 ? (leaves[1] ?? null) : (leaves[index] ?? null);
  const rightLeaf =
    index === 1 ? (leaves[2] ?? null) : (leaves[Math.min(lastIndex, index + 1)] ?? null);

  return (
    <div
      ref={rootRef}
      className={`${resolvedClassName} ${spreadClass} ${openClass}`.trim()}
      tabIndex={interactive ? 0 : undefined}
      role={interactive ? "region" : undefined}
      aria-label={t.story.storybook.flipAria(heroName)}
      onKeyDown={(event) => {
        if (!interactive) return;
        if (event.key === "ArrowLeft") goTo(prevTarget, "previous");
        if (event.key === "ArrowRight") goTo(nextTarget, "next");
      }}
    >
      <div
        className={`storybook-volume ${turning ? `is-turning turn-${turning}` : ""}`}
        onPointerDown={(event) => {
          if (interactive) swipeX.current = event.clientX;
        }}
        onPointerUp={(event) => {
          if (!interactive || swipeX.current === null) return;
          const delta = event.clientX - swipeX.current;
          swipeX.current = null;
          if (delta < -42 && canNext) goTo(nextTarget, "next");
          if (delta > 42 && canPrev) goTo(prevTarget, "previous");
        }}
      >
        <div className="storybook-contact-shadow" aria-hidden="true" />
        {!showSpread ? (
          <>
            <div className="storybook-back-cover" aria-hidden="true" />
            <div className="storybook-paper-stack" aria-hidden="true">
              <i />
              <i />
              <i />
            </div>
            <div className="storybook-spine" aria-hidden="true" />
          </>
        ) : (
          <div className="storybook-open-backing" aria-hidden="true" />
        )}

        <div className="storybook-surface">
          {showSpread ? (
            <div className="storybook-open-spread">
              <div className="storybook-spread-leaf storybook-spread-left">
                <LeafView
                  leaf={leftLeaf}
                  heroName={heroName}
                  title={title}
                  coverSrc={resolvedCover}
                  pageSide="left"
                  totalStoryPages={totalStoryPages}
                />
              </div>
              <div className="storybook-spread-gutter" aria-hidden="true" />
              <div className="storybook-spread-leaf storybook-spread-right">
                <LeafView
                  leaf={rightLeaf}
                  heroName={heroName}
                  title={title}
                  coverSrc={resolvedCover}
                  pageSide="right"
                  totalStoryPages={totalStoryPages}
                />
              </div>
            </div>
          ) : (
            <LeafView
              leaf={leaves[index] ?? null}
              heroName={heroName}
              title={title}
              coverSrc={resolvedCover}
              totalStoryPages={totalStoryPages}
            />
          )}
        </div>

        {turning && fromIndex !== null && desktopSpread && (index === 0 || fromIndex === 0) ? (
          <div className={`storybook-cover-turn turn-${turning}`} aria-hidden="true">
            <div className="storybook-turn-face storybook-turn-front">
              <LeafView
                leaf={leaves[turning === "next" ? fromIndex : index] ?? null}
                heroName={heroName}
                title={title}
                coverSrc={resolvedCover}
                totalStoryPages={totalStoryPages}
              />
            </div>
            <div className="storybook-turn-face storybook-turn-back">
              <LeafView
                leaf={leaves[turning === "next" ? index : fromIndex] ?? null}
                heroName={heroName}
                title={title}
                coverSrc={resolvedCover}
                totalStoryPages={totalStoryPages}
              />
            </div>
            <i className="storybook-page-curl" />
          </div>
        ) : null}

        {turning && fromIndex !== null && desktopSpread && fromIndex > 0 && index > 0 ? (
          <div className={`storybook-spread-turn turn-${turning}`} aria-hidden="true">
            <div className="storybook-turn-face storybook-turn-front">
              <LeafView
                leaf={leaves[turning === "next" ? fromIndex + 1 : index] ?? null}
                heroName={heroName}
                title={title}
                coverSrc={resolvedCover}
                totalStoryPages={totalStoryPages}
              />
            </div>
            <div className="storybook-turn-face storybook-turn-back">
              <LeafView
                leaf={leaves[turning === "next" ? index : fromIndex + 1] ?? null}
                heroName={heroName}
                title={title}
                coverSrc={resolvedCover}
                totalStoryPages={totalStoryPages}
              />
            </div>
            <i className="storybook-page-curl" />
          </div>
        ) : null}

        {turning && fromIndex !== null && !desktopSpread ? (
          <div className={`storybook-turn-sheet turn-${turning}`} aria-hidden="true">
            <div className="storybook-turn-face storybook-turn-front">
              <LeafView
                leaf={leaves[turning === "next" ? fromIndex : index] ?? null}
                heroName={heroName}
                title={title}
                coverSrc={resolvedCover}
                totalStoryPages={totalStoryPages}
              />
            </div>
            <div className="storybook-turn-face storybook-turn-back">
              <LeafView
                leaf={leaves[turning === "next" ? index : fromIndex] ?? null}
                heroName={heroName}
                title={title}
                coverSrc={resolvedCover}
                totalStoryPages={totalStoryPages}
              />
            </div>
            <i className="storybook-page-curl" />
          </div>
        ) : null}

        {interactive && canPrev ? (
          <button
            className="storybook-corner storybook-corner-previous"
            type="button"
            onClick={() => goTo(prevTarget, "previous")}
            aria-label={t.story.storybook.previousPage}
          />
        ) : null}
        {interactive && canNext ? (
          <button
            className="storybook-corner storybook-corner-next"
            type="button"
            onClick={() => goTo(nextTarget, "next")}
            aria-label={t.story.storybook.nextPage}
          />
        ) : null}
      </div>

      {interactive ? (
        <>
          <div className="storybook-controls">
            <button
              type="button"
              onClick={() => goTo(prevTarget, "previous")}
              disabled={!canPrev || !!turning}
              aria-label={t.story.storybook.previousPage}
            >
              <ChevronLeft size={13} absoluteStrokeWidth />
              <span>{t.story.storybook.previous}</span>
            </button>
            <span className="storybook-progress" aria-live="polite">
              {progressLabel}
            </span>
            <button
              type="button"
              onClick={() => goTo(nextTarget, "next")}
              disabled={!canNext || !!turning}
              aria-label={t.story.storybook.nextPage}
            >
              <span>{t.story.storybook.next}</span>
              <ChevronRight size={13} absoluteStrokeWidth />
            </button>
          </div>
          <div className="storybook-page-rail" aria-label={t.story.storybook.pages}>
            {(desktopSpread ? spreadSteps : leaves.map((_, i) => i)).map((step) => (
              <button
                key={step}
                className={index === step ? "selected" : ""}
                type="button"
                onClick={() => goTo(step, step > index ? "next" : "previous")}
                aria-label={
                  step === 0
                    ? t.story.storybook.railCover
                    : leaves[step]?.kind === "qr"
                      ? t.story.storybook.qrTitle
                      : desktopSpread && step > 1
                        ? t.story.storybook.railSpread(step - 1, Math.min(totalStoryPages, step))
                        : t.story.storybook.railPage(Math.max(1, step - 1))
                }
              >
                {step === 0 ? (
                  <Sparkles size={11} absoluteStrokeWidth />
                ) : leaves[step]?.kind === "qr" ? (
                  <Sparkles size={11} absoluteStrokeWidth />
                ) : desktopSpread && step > 1 ? (
                  `${step - 1}–${Math.min(totalStoryPages, step)}`
                ) : (
                  step
                )}
              </button>
            ))}
          </div>
          <p className="storybook-gesture-hint">{t.story.storybook.gestureHint}</p>
        </>
      ) : null}
    </div>
  );
}
