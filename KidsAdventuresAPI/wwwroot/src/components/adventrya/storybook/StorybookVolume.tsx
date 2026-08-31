import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { ChevronLeft, ChevronRight, Lock } from "lucide-react";

import { preloadIllustration, useIllustrationUrl } from "@/lib/hooks/useIllustrationUrl";
import { useMediaQuery } from "@/lib/hooks/useMediaQuery";
import { useT } from "@/lib/i18n";
import { WORLD_COVER_ART, type WorldId, isWorldId } from "@/lib/worlds";
import type { StoryPageContent } from "@/lib/api/types";

export type StorybookLeaf =
  | { kind: "cover" }
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
  /**
   * Eight spreads rather than pages carrying art and text together. Comes from the book, not
   * from a page: an older book's page reports isTextOnlyPage false too, so reading it per page
   * would strip every older book of its words.
   */
  isSpreadBook?: boolean;
  className?: string;
  /** When false, omit controls (thumbnail / shared teaser). */
  interactive?: boolean;
  initialIndex?: number;
  /**
   * Turn the book by itself every N ms, wrapping back to the cover at the end.
   *
   * Opt-in, and set only on the landing hero: a book that keeps turning under someone who has
   * started reading is worse than one that never moved. It stops for good at the first sign of a
   * human — a click, a key, a wheel, a swipe — and never resumes.
   */
  autoAdvanceMs?: number;
  /**
   * Demo variants. `preview` stays single-page in the create layout so
   * `uses-desktop-spread` does not fight `ux-preview-product` max-height.
   */
  variant?: "full" | "hero" | "preview" | "display";
};

/** Beki's canonical portrait, shown until a book carries one drawn for its own world. */
const BEKI_PORTRAIT = "/adventrya/beki.webp";

function fallbackCover(worldId?: string | null): string {
  if (worldId && isWorldId(worldId)) return WORLD_COVER_ART[worldId as WorldId];
  return WORLD_COVER_ART.dinosaurs;
}

function buildLeaves(options: {
  pages: StoryPageContent[];
  lockedPageCount: number;
  isUnlocked: boolean;
}): StorybookLeaf[] {
  // No title page. It carried "this book belongs to <name>" and an epigraph, which the cover
  // had already said, so the second thing a child saw was the first thing repeated. The story
  // starts on the page after the cover now.
  const leaves: StorybookLeaf[] = [{ kind: "cover" }];
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
        {/* The cover said the book was the child's twice, above and below the title. Once is
            the point; twice reads as a template that forgot it had already said it. */}
        <small>{t.story.storybook.belongsTo(heroName)}</small>
        <h2>{title}</h2>
      </div>
    </article>
  );
}

function StoryFace({
  leaf,
  heroName,
  pageSide,
  totalStoryPages,
  isSpreadBook = false,
}: {
  leaf:
    | Extract<StorybookLeaf, { kind: "story" }>
    | Extract<StorybookLeaf, { kind: "locked" }>
    | Extract<StorybookLeaf, { kind: "qr" }>;
  heroName: string;
  pageSide?: "left" | "right";
  totalStoryPages: number;
  isSpreadBook?: boolean;
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

  // The back cover.
  //
  // It used to be a story page with a decorative square standing in for a QR code, which on a
  // screen is a picture of a thing you cannot use. Print gets the QR; here the same invitation
  // is a button that works, and the page is built as the cover's sibling rather than as another
  // page of the book, because it is the last thing a child sees when the book shuts.
  if (leaf.kind === "qr") {
    return (
      <article className={`storybook-back ${pageSide ? `page-${pageSide}` : ""}`}>
        <div className="storybook-back-glow" aria-hidden="true" />
        <span className="storybook-brand">{t.story.storybook.brand}</span>
        {/* Beki sees the child off. The canonical art is the fallback: a book drawn before
            per-book Beki existed, or one whose extra picture failed, still has him here. */}
        <img className="storybook-back-guide" src={BEKI_PORTRAIT} alt="" aria-hidden="true" />
        <div className="storybook-back-copy">
          <strong>{t.story.storybook.qrTitle}</strong>
          <p>{t.story.storybook.backTap(heroName)}</p>
          <a className="storybook-back-cta" href="/create">
            {t.story.storybook.backCta}
          </a>
        </div>
      </article>
    );
  }

  // A preview page now carries its real text with the artwork withheld, so only the
  // illustration is obscured. `kind: "locked"` remains for responses that send a bare
  // count and no text at all — those still render the full-page lock panel.
  const artLocked = leaf.kind === "story" && leaf.page.isLocked === true;

  // A spread is a picture and the page of words facing it, so each side shows one of the two.
  //
  // Every page used to draw both, and the copy fell back to the caption when a page had no prose
  // of its own — which printed the caption across the illustration, the exact thing giving the
  // words their own page was meant to stop. Older books, where a page really did carry art and
  // text together, still render both, which is why this asks the book and not the page.
  const isSpreadArt = isSpreadBook && leaf.kind === "story" && !leaf.page.isTextOnlyPage;
  const isSpreadText = isSpreadBook && leaf.kind === "story" && leaf.page.isTextOnlyPage === true;
  const showArt = !isSpreadText;
  const showCopy = !isSpreadArt;

  const copy =
    leaf.kind === "story"
      ? leaf.page.content || (isSpreadArt ? "" : leaf.page.caption || leaf.page.title)
      : `${t.story.storybook.lockedPagePrefix}${leaf.pageNumber}${t.story.storybook.lockedPageSuffix}`;

  // The prose page borrows the inside cover's treatment: paper, a ruled frame and serif type.
  //
  // It used to reuse the page-copy overlay, which is a light-on-dark gradient built to sit on
  // top of an illustration. With no picture underneath, that gradient is a dark panel over
  // nothing — and the prose was sized to be a caption on artwork rather than a page to read.
  if (isSpreadText && leaf.kind === "story") {
    return (
      <article
        className={`storybook-page storybook-text-page ${pageSide ? `page-${pageSide}` : ""}`}
      >
        <div className="inside-cover-constellation" aria-hidden="true" />
        {leaf.page.caption || leaf.page.title ? (
          <small>{leaf.page.caption || leaf.page.title}</small>
        ) : null}
        <p>{leaf.page.content}</p>
        <i>{t.story.storybook.pageLabel(pageNumber, totalStoryPages)}</i>
      </article>
    );
  }

  return (
    <article
      className={`storybook-page ${pageSide ? `page-${pageSide}` : ""} ${
        locked ? "is-locked" : ""
      } ${artLocked ? "is-art-locked" : ""}`}
    >
      <div className="storybook-page-content">
        {showArt ? (
          <div
            className="storybook-page-art"
            style={artUrl ? { backgroundImage: `url("${artUrl}")` } : undefined}
          />
        ) : null}
        {/* Sibling of the art, not a child: a CSS filter blurs its descendants too, so a
            badge nested inside the blurred element would be unreadable. */}
        {artLocked ? (
          <span className="storybook-art-lock">
            <Lock aria-hidden="true" />
            <small>{t.story.storybook.lockedNote}</small>
          </span>
        ) : null}
        {showCopy ? (
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
        ) : null}
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
  isSpreadBook = false,
}: {
  leaf: StorybookLeaf | null;
  heroName: string;
  title: string;
  coverSrc: string;
  pageSide?: "left" | "right";
  totalStoryPages: number;
  isSpreadBook?: boolean;
}) {
  // Nothing to draw rather than a filler page: with the title page gone, an out-of-range leaf
  // is the blank right-hand side of the last spread, and a blank page is what belongs there.
  if (!leaf) return <article className="storybook-page storybook-page-blank" aria-hidden="true" />;
  if (leaf.kind === "cover")
    return <CoverFace heroName={heroName} title={title} coverSrc={coverSrc} />;
  return (
    <StoryFace
      leaf={leaf}
      heroName={heroName}
      pageSide={pageSide}
      totalStoryPages={totalStoryPages}
      isSpreadBook={isSpreadBook}
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
  isSpreadBook = false,
  className,
  interactive = true,
  initialIndex = 0,
  variant = "full",
  autoAdvanceMs,
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
  /*
    Where the turn is going, held for as long as it runs.

    This used to be `fromIndex`, the page being left — which was the same thing as `index`,
    because `index` is not moved until the animation commits. Both faces of the turning sheet
    were therefore built from the same leaf, and the page being turned to never appeared until
    it snapped in at the end. The destination is the half that was missing.
  */
  const [turnTo, setTurnTo] = useState<number | null>(null);
  const timers = useRef<number[]>([]);
  const swipeX = useRef<number | null>(null);
  const resolvedCover = useIllustrationUrl(coverImageUrl) ?? fallbackCover(worldId);
  const totalStoryPages = pages.length + lockedPageCount;

  useEffect(() => () => timers.current.forEach((id) => window.clearTimeout(id)), []);

  // Fetch the pictures on either side of where the reader is standing.
  //
  // A page used to start downloading its illustration only once it had been turned to, so every
  // turn began with a blank frame for as long as the image took. Reading forward is the common
  // case, so the next few leaves matter most; one leaf back covers a child flipping to look at
  // a picture again. Already-cached paths return immediately and cost nothing.
  useEffect(() => {
    const ahead = 3;
    for (let i = index - 1; i <= index + ahead; i++) {
      if (i < 0 || i > lastIndex || i === index) continue;
      const leaf = leaves[i];
      if (leaf?.kind === "story" && !leaf.page.isLocked) {
        preloadIllustration(leaf.page.illustrationUrl);
      }
    }
  }, [index, lastIndex, leaves]);

  // The shape of the book, now that the title page is gone.
  //
  //   0            the cover, alone, closed
  //   1 .. content the story in picture/prose pairs, so a spread always starts on an odd index
  //   last         the back cover, alone, closed again
  //
  // Everything below is derived from those three facts rather than restated, because the old
  // version had the same rule written four times with different off-by-ones.
  const backIndex = leaves[lastIndex]?.kind === "qr" ? lastIndex : -1;
  const contentLast = backIndex === -1 ? lastIndex : lastIndex - 1;

  // Desktop spreads begin on odd indices; nudge off an even one.
  useEffect(() => {
    if (!desktopSpread) return;
    if (index === 0 || index === backIndex || index % 2 === 1) return;
    const id = window.setTimeout(() => setIndex(Math.max(1, index - 1)), 0);
    return () => window.clearTimeout(id);
  }, [desktopSpread, index, backIndex]);

  const spreadSteps = useMemo(() => {
    const steps = [0];
    for (let i = 1; i <= contentLast; i += 2) steps.push(i);
    if (backIndex !== -1) steps.push(backIndex);
    return steps;
  }, [contentLast, backIndex]);

  const goTo = useCallback(
    (next: number, direction: "next" | "previous") => {
      if (!interactive || turning || next < 0 || next > lastIndex) return;
      if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
        setIndex(next);
        return;
      }
      setTurnTo(next);
      setTurning(direction);
      const id = window.setTimeout(() => {
        setIndex(next);
        setTurning(null);
        setTurnTo(null);
      }, 900);
      timers.current.push(id);
    },
    [interactive, turning, lastIndex],
  );

  /*
    Turning by itself, until somebody takes over.

    `handedOver` is one-way: the first interaction of any kind ends the demonstration for the
    life of the component. Reaching the end resets to the cover rather than turning back through
    the whole book, which is what "start again" looks like on a shelf.
  */
  const stepIndex = desktopSpread ? spreadSteps.indexOf(index) : index;
  const prevTarget = desktopSpread ? (spreadSteps[Math.max(0, stepIndex - 1)] ?? 0) : index - 1;
  const nextTarget = desktopSpread
    ? (spreadSteps[Math.min(spreadSteps.length - 1, stepIndex + 1)] ?? lastIndex)
    : index + 1;
  const canPrev = index > 0;
  const canNext = desktopSpread ? stepIndex < spreadSteps.length - 1 : index < lastIndex;

  /*
    Turning by itself, until somebody takes over.

    `handedOver` is one-way: the first interaction of any kind ends the demonstration for the
    life of the component. At the end it resets to the cover rather than turning back through
    the whole book, which is what "start again" looks like on a shelf.

    It steps by `nextTarget` rather than index + 1 — on a desktop spread only odd indices are
    valid landings, and stepping by one put the book on an even index that the nudge effect
    immediately undid, so it turned once and then sat rocking between two pages.
  */
  const [handedOver, setHandedOver] = useState(false);
  const handOver = useCallback(() => setHandedOver(true), []);

  // A ref, so the timer reads where the book is without listing it as a dependency: depending on
  // the index would tear the interval down and rebuild it on every turn, drifting the cadence.
  const live = useRef({ turning, canNext, nextTarget, goTo });
  live.current = { turning, canNext, nextTarget, goTo };

  useEffect(() => {
    if (!autoAdvanceMs || !interactive || handedOver) return;
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;

    const id = window.setInterval(() => {
      // Nothing turns in a tab nobody is looking at; it would only land them mid-book.
      if (document.hidden || live.current.turning) return;
      if (live.current.canNext) live.current.goTo(live.current.nextTarget, "next");
      else setIndex(0);
    }, autoAdvanceMs);

    return () => window.clearInterval(id);
  }, [autoAdvanceMs, interactive, handedOver]);

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

  // The back cover sits past the last story page, so numbering it produces inverted
  // ranges like "7–6" on the final step. It gets its own label instead.
  //
  // Story page N is leaf N, now that nothing stands between the cover and the story.
  const progressLabel =
    index === 0
      ? t.story.storybook.coverLabel(totalStoryPages || leaves.length - 1)
      : index === backIndex
        ? t.story.storybook.qrTitle
        : desktopSpread
          ? t.story.storybook.spreadLabel(
              index,
              Math.min(totalStoryPages, index + 1),
              totalStoryPages || 1,
            )
          : t.story.storybook.pageLabel(index, totalStoryPages || 1);

  const spreadClass = desktopSpread ? "uses-desktop-spread" : "uses-single-page";
  // A book is shut at both ends. It used to open on the last page and stay open, so a story
  // that had just finished sat there gaping instead of closing the way it started.
  const shutAt = (at: number) => at === 0 || at === backIndex;
  /*
    A book that is opening is already open.

    This used to read `index` alone, and `index` does not move until the turn commits — so for the
    whole 900ms of opening the cover, the volume was still one page wide, and the moment the turn
    ended it snapped to two. The cover therefore swung across a book of one width and landed on a
    book of another. Counting the destination as well means the covers turn inside the footprint
    the book is going to have, and nothing resizes underneath them.
  */
  const isClosed = shutAt(index) && (turnTo === null || shutAt(turnTo));
  const openClass = isClosed ? "is-closed" : "is-open";
  const showSpread = desktopSpread && !isClosed;

  /*
    Which spread lies under a turning sheet.

    A leaf carries one page on each face, so the two pages it is not carrying belong to the paper
    underneath: turning forward, the left page stays put and the right is the one being uncovered;
    turning back, it is the other way round. Reading both from `index` meant the page revealed
    behind a departing leaf was the page that had just left on it.

    Opening or closing the covers is the exception — one of the two indices is the closed book,
    which has no spread of its own — so the open side is what lies underneath either way.
  */
  const turningCover = turning !== null && turnTo !== null && (index === 0 || turnTo === 0);
  const spreadPages = ((): { left: number; right: number } => {
    if (turning === null || turnTo === null) return { left: index, right: index + 1 };
    if (turningCover) {
      const open = Math.max(index, turnTo);
      return { left: open, right: open + 1 };
    }
    return turning === "next"
      ? { left: index, right: turnTo + 1 }
      : { left: turnTo, right: index + 1 };
  })();

  const leftLeaf = leaves[spreadPages.left] ?? null;
  const rightLeaf = spreadPages.right <= contentLast ? (leaves[spreadPages.right] ?? null) : null;

  return (
    <div
      ref={rootRef}
      className={`${resolvedClassName} ${spreadClass} ${openClass}`.trim()}
      tabIndex={interactive ? 0 : undefined}
      role={interactive ? "region" : undefined}
      aria-label={t.story.storybook.flipAria(heroName)}
      /*
        One place to notice a reader has arrived. Capture-phase and on the root, so it fires for
        every way of turning a page — corner, control, key, wheel, swipe — without each handler
        having to remember to say so.
      */
      onPointerDownCapture={handOver}
      onKeyDownCapture={handOver}
      onWheelCapture={handOver}
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
                  isSpreadBook={isSpreadBook}
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
                  isSpreadBook={isSpreadBook}
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
              isSpreadBook={isSpreadBook}
            />
          )}
        </div>

        {/*
          Which face shows which page no longer depends on the direction.

          Both turns now start the sheet flat and unflipped, so the face you are looking at when a
          turn begins is always the page you are on, and the face revealed as it lands is always
          the page you are going to. The old rule swapped the two for a backward turn, to
          compensate for a backward animation that started flipped — and that animation is gone.
        */}
        {turning && turnTo !== null && desktopSpread && (index === 0 || turnTo === 0) ? (
          <div className={`storybook-cover-turn turn-${turning}`} aria-hidden="true">
            <div className="storybook-turn-face storybook-turn-front">
              <LeafView
                leaf={leaves[index] ?? null}
                heroName={heroName}
                title={title}
                coverSrc={resolvedCover}
                totalStoryPages={totalStoryPages}
                isSpreadBook={isSpreadBook}
              />
            </div>
            <div className="storybook-turn-face storybook-turn-back">
              <LeafView
                leaf={leaves[turnTo] ?? null}
                heroName={heroName}
                title={title}
                coverSrc={resolvedCover}
                totalStoryPages={totalStoryPages}
                isSpreadBook={isSpreadBook}
              />
            </div>
            <i className="storybook-page-curl" />
          </div>
        ) : null}

        {turning && turnTo !== null && desktopSpread && index > 0 && turnTo > 0 ? (
          <div className={`storybook-spread-turn turn-${turning}`} aria-hidden="true">
            {/*
              One leaf, two faces, and which two depends on the way it is going: forward it lifts
              the right page of this spread and puts down the left page of the next; backward it
              lifts the left page of this spread and puts down the right page of the previous one.
            */}
            <div className="storybook-turn-face storybook-turn-front">
              <LeafView
                leaf={leaves[turning === "next" ? index + 1 : index] ?? null}
                heroName={heroName}
                title={title}
                coverSrc={resolvedCover}
                totalStoryPages={totalStoryPages}
                isSpreadBook={isSpreadBook}
              />
            </div>
            <div className="storybook-turn-face storybook-turn-back">
              <LeafView
                leaf={leaves[turning === "next" ? turnTo : turnTo + 1] ?? null}
                heroName={heroName}
                title={title}
                coverSrc={resolvedCover}
                totalStoryPages={totalStoryPages}
                isSpreadBook={isSpreadBook}
              />
            </div>
            <i className="storybook-page-curl" />
          </div>
        ) : null}

        {turning && turnTo !== null && !desktopSpread ? (
          <div className={`storybook-turn-sheet turn-${turning}`} aria-hidden="true">
            <div className="storybook-turn-face storybook-turn-front">
              <LeafView
                leaf={leaves[turning === "next" ? index : turnTo] ?? null}
                heroName={heroName}
                title={title}
                coverSrc={resolvedCover}
                totalStoryPages={totalStoryPages}
                isSpreadBook={isSpreadBook}
              />
            </div>
            <div className="storybook-turn-face storybook-turn-back">
              <LeafView
                leaf={leaves[turning === "next" ? turnTo : index] ?? null}
                heroName={heroName}
                title={title}
                coverSrc={resolvedCover}
                totalStoryPages={totalStoryPages}
                isSpreadBook={isSpreadBook}
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
            {/* No page counter on the shop window. On the home page the sample book is there to
                be looked at, and "3 / 16" under it is bookkeeping for a reader who has not
                bought anything yet. The reader itself keeps its counter. */}
            {variant === "hero" ? null : (
              <span className="storybook-progress" aria-live="polite">
                {progressLabel}
              </span>
            )}
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
          {/*
            The page rail is gone: a row of eighteen numbered buttons under a picture book is a
            table of contents nobody asked for, and it was already hidden in the preview. Prev,
            next and the progress counter above are what remain, and nothing is unreachable —
            every page is still one turn from its neighbour.
          */}
          <p className="storybook-gesture-hint">{t.story.storybook.gestureHint}</p>
        </>
      ) : null}
    </div>
  );
}
