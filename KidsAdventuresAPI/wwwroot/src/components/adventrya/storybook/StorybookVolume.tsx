import { useCallback, useContext, useEffect, useMemo, useRef, useState } from "react";
import { ChevronLeft, ChevronRight, Lock } from "lucide-react";
import { Link } from "@tanstack/react-router";

import { preloadIllustration, useIllustrationUrl } from "@/lib/hooks/useIllustrationUrl";
import { useMediaQuery } from "@/lib/hooks/useMediaQuery";
import { useT } from "@/lib/i18n";
import { WORLD_COVER_ART, type WorldId, isWorldId } from "@/lib/worlds";
import type { StoryPageContent } from "@/lib/api/types";
import { NewBookCharacterContext, NewBookReturnContext } from "@/lib/story/newBookCharacter";

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
  /**
   * Print each spread's picture across both open pages instead of cropping it to one.
   *
   * A prop of its own rather than something read off `isSpreadBook`, because the two say
   * different things: `isSpreadBook` says the book was *written* as spreads, and this says the
   * artwork on disk is one wide painting that can be cut down the middle. The landing hero
   * passes the first and not the second — its demo art is 900×1350 portraits drawn one per page,
   * and halving those would show a child a book of cropped fragments.
   *
   * Desktop spreads only. A phone shows one page at a time, where a half-painting has nothing to
   * sit beside, so the single-page path is untouched by this.
   */
  fullBleedSpreads?: boolean;
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
  /**
   * The line above the title on the cover.
   *
   * A real book says whose story it is — "ეს ამბავი ანასია" — which is the whole point of it and
   * the reason a child recognises the cover across a room. The sample on the home page belongs
   * to nobody, so it names its world instead of a child who does not exist.
   */
  coverCaption?: string;
};

/** Beki's canonical portrait, shown until a book carries one drawn for its own world. */
/*
  Beki, and not the lamb that used to stand here.

  `beki.webp` is a different character altogether — an early one, kept on the site long after the
  book, the world map and the print assets had all moved to the canonical Beki. So the back cover
  of the sample book introduced a creature the parent would never meet again.

  Cropped from `Assets/Beki/beki-canonical-v2.png`, the same file the printed book is built from,
  to the 2:3 this frame has always used. Its own dark violet ground is kept rather than cut out:
  the frame masks its edges away radially, and the approved cutout is only 320px wide, which is
  softer than this slot on any modern screen.
*/
const BEKI_PORTRAIT = "/adventrya/beki-canonical.webp";

/*
  Is this picture a spread, or a page?

  `isSpreadBook` cannot answer it. Books from v1 to v4 are projected through MasterStoryProjection
  and come out with text-only pages too, so they report the same flag — but their illustrations
  were drawn 1024×1536, one portrait per page. Stretching one of those across an open book with
  `background-size: 200%` does not crop it, it distorts it, which is a worse failure than the
  cropping this whole feature exists to fix. Nothing in the DTO distinguishes the two, and the
  screens that pass the prop cannot know either.

  So the prop is the opt-in and the picture itself is the gate: measure the image, and only a
  landscape one is treated as a painting spanning two pages. Measured once per URL and kept for
  the life of the tab, because an illustration cannot change shape after it has been drawn.
*/
type ArtShape = "landscape" | "portrait";
const artShapes = new Map<string, ArtShape>();
const artShapeProbes = new Map<string, Promise<ArtShape>>();

function probeArtShape(url: string): Promise<ArtShape> {
  const known = artShapes.get(url);
  if (known) return Promise.resolve(known);
  const running = artShapeProbes.get(url);
  if (running) return running;

  const probe = new Promise<ArtShape>((resolve) => {
    const image = new Image();
    image.onload = () =>
      resolve(image.naturalWidth > image.naturalHeight ? "landscape" : "portrait");
    // A picture that will not load cannot be measured, and the honest answer for anything
    // unmeasured is the path that was already right for every book.
    image.onerror = () => resolve("portrait");
    image.src = url;
  }).then((shape) => {
    artShapes.set(url, shape);
    artShapeProbes.delete(url);
    return shape;
  });

  artShapeProbes.set(url, probe);
  return probe;
}

/**
 * Whether a resolved illustration is wider than it is tall.
 *
 * False while the measurement is still running, so a pair that has not been measured yet renders
 * the way it always did rather than flashing a stretched painting and then correcting itself. The
 * two halves of a pair ask about the same URL and so share one probe and one answer.
 */
function useIsSpreadShapedArt(url: string | null): boolean {
  const [shape, setShape] = useState<ArtShape | null>(() =>
    url ? (artShapes.get(url) ?? null) : null,
  );

  useEffect(() => {
    if (!url) {
      setShape(null);
      return;
    }
    const known = artShapes.get(url);
    if (known) {
      setShape(known);
      return;
    }
    let cancelled = false;
    setShape(null);
    void probeArtShape(url).then((measured) => {
      if (!cancelled) setShape(measured);
    });
    return () => {
      cancelled = true;
    };
  }, [url]);

  return shape === "landscape";
}

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
  caption,
  coverSrc,
}: {
  heroName: string;
  title: string;
  caption?: string;
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
        <small>{caption ?? t.story.storybook.belongsTo(heroName)}</small>
        {/* A book with no title is the sample on the home page: its cover is a painting, and the
            invented title lying across the bottom of it was the one thing on that shelf a
            visitor could not have. Every real book still names itself here. */}
        {title ? <h2>{title}</h2> : null}
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
  const newBookCharacterId = useContext(NewBookCharacterContext);
  const newBookReturnTo = useContext(NewBookReturnContext);
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
          <p>{t.story.storybook.backTap}</p>
          {/* To the worlds, not to the form. `/create` opens on the questions — a name, a date
              of birth, a photograph — which is the wrong thing to meet at the end of a story you
              have just read. The first step is choosing where the next one happens.

              `/themes` rather than the home page's own picker: this page is read inside a book,
              often full-screen, and sending someone back to a section of the marketing page
              means landing them halfway down it with the story still open behind. The dedicated
              picker is the whole screen, which is what "choose the next world" should be.

              A client-side `Link`, carrying this child: a hard `href` reloaded the app into a
              blank form with no world and no child, and the picker needs to know whose next
              book this is. */}
          <Link
            className="storybook-back-cta"
            to="/themes"
            search={{
              ...(newBookCharacterId ? { characterId: newBookCharacterId } : {}),
              // Where "back" means, once the parent is standing in the picker.
              ...(newBookReturnTo ? { from: newBookReturnTo } : {}),
            }}
          >
            {t.story.storybook.backCta}
          </Link>
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
  coverCaption,
  coverSrc,
  pageSide,
  totalStoryPages,
  isSpreadBook = false,
}: {
  leaf: StorybookLeaf | null;
  heroName: string;
  title: string;
  coverCaption?: string;
  coverSrc: string;
  pageSide?: "left" | "right";
  totalStoryPages: number;
  isSpreadBook?: boolean;
}) {
  // Nothing to draw rather than a filler page: with the title page gone, an out-of-range leaf
  // is the blank right-hand side of the last spread, and a blank page is what belongs there.
  if (!leaf) return <article className="storybook-page storybook-page-blank" aria-hidden="true" />;
  if (leaf.kind === "cover")
    return (
      <CoverFace heroName={heroName} title={title} caption={coverCaption} coverSrc={coverSrc} />
    );
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

/** Which physical half of the open volume a slot occupies. */
type SpreadSide = "left" | "right";

/**
 * The inside of the book's case, where there is no page at all.
 *
 * Board, not paper. An unprinted sheet of paper reads as a page whose words failed to arrive,
 * which is a bug the eye reports before the mind does; the dark inside of a cover reads as the
 * edge of the book, which is what is actually there. The colours are the casing's own — the back
 * cover and the open backing, in that order — so this is the same material those are made of,
 * seen from the inside rather than from behind.
 */
function BoardFace({ side }: { side?: SpreadSide }) {
  return <div className={`storybook-board ${side ? `board-${side}` : ""}`} aria-hidden="true" />;
}

/**
 * The picture-and-prose pair a half of the book belongs to.
 *
 * A spread book's leaves alternate — picture, words, picture, words — and the picture is a single
 * painting as wide as the open book. So a half of the volume does not belong to a leaf, it belongs
 * to a *pair*: it shows its half of the pair's painting, and the pair's words if the words belong
 * on that side.
 */
type SpreadPair = {
  art: Extract<StorybookLeaf, { kind: "story" }>;
  text: Extract<StorybookLeaf, { kind: "story" }>;
  /** True when the painting keeps its calm half on this side, which is where the prose goes. */
  showProse: boolean;
};

/**
 * What one half of the open book is going to draw.
 *
 * `leaf` is what that half showed before full-bleed existed and is still what it falls back to;
 * `pair` is set only when the half can be drawn as part of one painting. Which of the two wins is
 * decided in the component below, because it depends on a hook.
 */
type SpreadSlotPlan = {
  leaf: StorybookLeaf | null;
  pair: SpreadPair | null;
};

/**
 * Half a painting, and the words if they belong on this half.
 *
 * `background-size: 200% 100%` is what makes the two halves join: each half stretches the picture
 * to the width of the whole open book and then shows its own end of it, so the seam falls exactly
 * on the gutter no matter how wide the book is drawn. Nothing here may be scaled or nudged the way
 * `.storybook-page-art` is — a scale about each half's own centre would pull the two ends of the
 * painting apart at the fold, which is the one place a reader looks.
 */
function SpreadHalf({
  pair,
  side,
  artUrl,
  totalStoryPages,
}: {
  pair: SpreadPair;
  side: SpreadSide;
  artUrl: string;
  totalStoryPages: number;
}) {
  const t = useT();
  const prose = pair.showProse ? pair.text.page : null;
  return (
    <article className={`storybook-spread-full page-${side}`}>
      <div className="storybook-spread-full-art" style={{ backgroundImage: `url("${artUrl}")` }} />
      {prose ? (
        <div className="storybook-spread-prose">
          {prose.caption || prose.title ? <small>{prose.caption || prose.title}</small> : null}
          <p>{prose.content}</p>
          <i>{t.story.storybook.pageLabel(pair.text.storyIndex + 1, totalStoryPages)}</i>
        </div>
      ) : null}
    </article>
  );
}

/**
 * One half of the open book, drawn either as part of a painting or as the page it always was.
 *
 * Eligibility is decided here rather than in the resolver because it is two questions only hooks
 * can answer, and a pair goes full-bleed only if both say yes: its picture has actually *resolved*
 * to a URL, and that picture measures wider than it is tall. A locked preview page carries no
 * illustration at all, a page whose picture is still being fetched carries one that is not here
 * yet, and a legacy book carries a portrait drawn for a single page — all three fall through to
 * the existing art-page/paper-page pair, lock blur and all, and the first two stop falling through
 * the moment the picture arrives and measures landscape.
 *
 * `side` says which half of the painting to show; `pageSide` is the older hint that only tells a
 * paper page which way its borders and shadows lean, and the turning sheet has never set it — so
 * the two are separate arguments rather than one.
 *
 * A half with no leaf at all is the board. On an open book that half is not a page that happens to
 * be empty, it is the place where the pages have run out: the last leaf of a story lifts off the
 * inside of the back cover, and a story with an odd number of pages simply ends against it. The
 * cream blank page that used to be drawn there read as a bright sheet of paper bound in after the
 * end of the book — most visible on the final turn, where the reader watched the last page lift
 * off it. `.storybook-page-blank` is left where it is for the single-page paths, which reach it
 * from `LeafView` directly and never render a null leaf anyway.
 */
function SpreadSlot({
  plan,
  side,
  pageSide,
  heroName,
  title,
  coverCaption,
  coverSrc,
  totalStoryPages,
  isSpreadBook,
}: {
  plan: SpreadSlotPlan;
  side: SpreadSide;
  pageSide?: SpreadSide;
  heroName: string;
  title: string;
  coverCaption?: string;
  coverSrc: string;
  totalStoryPages: number;
  isSpreadBook: boolean;
}) {
  // Called on every render and with null whenever this half is not a candidate: the branch below
  // is a branch, not a reason for a hook to disappear.
  const artUrl = useIllustrationUrl(plan.pair ? plan.pair.art.page.illustrationUrl : null);
  const isSpreadShaped = useIsSpreadShapedArt(artUrl);
  if (plan.pair && artUrl && isSpreadShaped) {
    return (
      <SpreadHalf pair={plan.pair} side={side} artUrl={artUrl} totalStoryPages={totalStoryPages} />
    );
  }
  // The side comes from the slot rather than from a guess about which one runs out: it is almost
  // always the right half, and on a backward turn it is the sheet's own half that has nothing.
  if (!plan.leaf) return <BoardFace side={side} />;
  return (
    <LeafView
      leaf={plan.leaf}
      heroName={heroName}
      title={title}
      coverCaption={coverCaption}
      coverSrc={coverSrc}
      pageSide={pageSide}
      totalStoryPages={totalStoryPages}
      isSpreadBook={isSpreadBook}
    />
  );
}

export function StorybookVolume({
  heroName,
  title,
  coverCaption,
  coverImageUrl,
  worldId,
  pages,
  lockedPageCount = 0,
  isUnlocked = false,
  isSpreadBook = false,
  fullBleedSpreads = false,
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

  /*
    Which spread to draw, for the frames where `index` is not one.

    `useMediaQuery` reports false on its first render, so a book opened straight to a page — the
    shared-book screen opens on the last one — begins life as a single page and becomes a spread
    only once the query resolves. If that landing index is even it is the *prose* half of a pair,
    and until the nudge effect below moves it the book would draw the second half of one spread
    beside the first half of the next: a picture facing somebody else's words.

    Derived in render because a derivation cannot be a frame late. The effect stays — state has to
    converge too, since the corner controls and the turn targets read `index` — but nothing is
    ever *drawn* from a mid-pair index. On a phone this is the raw index, unchanged.
  */
  const displayIndex =
    desktopSpread && index !== 0 && index !== backIndex && index % 2 === 0
      ? Math.max(1, index - 1)
      : index;

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
    displayIndex === 0
      ? t.story.storybook.coverLabel(totalStoryPages || leaves.length - 1)
      : displayIndex === backIndex
        ? t.story.storybook.qrTitle
        : desktopSpread
          ? t.story.storybook.spreadLabel(
              displayIndex,
              Math.min(totalStoryPages, displayIndex + 1),
              totalStoryPages || 1,
            )
          : t.story.storybook.pageLabel(displayIndex, totalStoryPages || 1);

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
  const isClosed = shutAt(displayIndex) && (turnTo === null || shutAt(turnTo));
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
  const turningCover = turning !== null && turnTo !== null && (displayIndex === 0 || turnTo === 0);
  const spreadPages = ((): { left: number; right: number } => {
    if (turning === null || turnTo === null) return { left: displayIndex, right: displayIndex + 1 };
    if (turningCover) {
      const open = Math.max(displayIndex, turnTo);
      return { left: open, right: open + 1 };
    }
    return turning === "next"
      ? { left: displayIndex, right: turnTo + 1 }
      : { left: turnTo, right: displayIndex + 1 };
  })();

  /*
    An index no leaf can have, which is how a half of the book asks for nothing.

    Every lookup below ends in `leaves[i] ?? null`, and a half handed a null leaf already draws the
    inside of the case, so "there is no page here" needs no second code path — it is an index out
    of range.
  */
  const NO_LEAF = -1;

  /*
    What one physical half of the open book shows.

    This is the only question with a stable answer during a turn. The pages lying open mid-turn
    belong to two different spreads — forward it is this spread's left page beside the next
    spread's right one — so anything phrased as "the pair we are on" would draw the old cropped
    page under the sheet and then snap when the turn commits. A half that resolves on its own,
    from a leaf index and the side it lies on, is right for a hybrid without knowing it is one,
    and the faces of the turning sheet ask the same question, so what the sheet carries is what
    was underneath it a moment ago and what will be underneath it when it lands.
  */
  const resolveSlot = (leafIndex: number, side: SpreadSide): SpreadSlotPlan => {
    const leaf = leaves[leafIndex] ?? null;
    if (!fullBleedSpreads || leaf?.kind !== "story") return { leaf, pair: null };

    // The painting sits on the odd leaf and its words on the leaf after it, because the cover is
    // leaf 0 and the story starts on leaf 1.
    const pairStart = leafIndex % 2 === 1 ? leafIndex : leafIndex - 1;
    const art = leaves[pairStart];
    const text = leaves[pairStart + 1];
    if (art?.kind !== "story" || art.page.isTextOnlyPage === true) return { leaf, pair: null };
    // A picture with no words facing it is not a spread — the half that would face it does not
    // exist — so a pair that is not whole keeps the page it always had.
    if (text?.kind !== "story" || text.page.isTextOnlyPage !== true) return { leaf, pair: null };

    /*
      Which half the words sit on, spread by spread.

      A deliberate duplication of BekiSpreadRhythm.TextSides
      (KidsAdventuresAPI/Services/Story/Prompts/BekiSpreadRhythm.cs:25), the array the illustrator
      is briefed from: left, right, left, right. The painting is composed to leave *that* half
      calm, so this is not a layout preference the reader is free to make — it is the reader
      reading back a decision already taken when the picture was drawn. If that array ever stops
      alternating from spread 1, this has to follow it or the words will land on the busy half.
    */
    const spreadNumber = Math.floor(art.storyIndex / 2) + 1;
    const textSide: SpreadSide = spreadNumber % 2 === 1 ? "left" : "right";
    return { leaf, pair: { art, text, showProse: textSide === side } };
  };

  const renderSlot = (leafIndex: number, side: SpreadSide, pageSide?: SpreadSide) => (
    <SpreadSlot
      plan={resolveSlot(leafIndex, side)}
      side={side}
      pageSide={pageSide}
      heroName={heroName}
      title={title}
      coverCaption={coverCaption}
      coverSrc={resolvedCover}
      totalStoryPages={totalStoryPages}
      isSpreadBook={isSpreadBook}
    />
  );

  // The back cover is a shut book of its own, never the right-hand page of a spread, so a story
  // with an odd number of pages ends against the inside of it.
  const rightSlotIndex = spreadPages.right <= contentLast ? spreadPages.right : NO_LEAF;

  /*
    The faces of whatever sheet is turning, as halves rather than as leaves.

    A story sheet lifts the right half of this spread and puts down the left half of the next
    going forward, and the mirror of that coming back. A cover is the same sheet with the closed
    side of the book on one face: it lies on the half it is currently on and lands on the other.
    Both are the same two questions, so they are asked once — the side each face occupies is the
    same formula either way, since a forward turn always leaves the right half and arrives on the
    left.

    The two can name the same leaf: a story with an odd number of pages ends with the back cover
    as both the page after this spread and the destination, and a sheet showing the same page on
    both faces is a sheet that visibly does not turn. The right half of that last spread is blank
    underneath, so the face lifting off it is blank too. Guarded for every book, not only the
    full-bleed ones, because the collision is in the leaf model and not in the artwork.
  */
  const turnFrontIndex = turningCover
    ? displayIndex
    : turning === "next"
      ? displayIndex + 1
      : displayIndex;
  const turnBackIndex =
    turnTo === null ? NO_LEAF : turningCover ? turnTo : turning === "next" ? turnTo : turnTo + 1;
  const turnFrontSide: SpreadSide = turning === "next" ? "right" : "left";
  const turnBackSide: SpreadSide = turning === "next" ? "left" : "right";
  const turnFacesCollide = turnFrontIndex === turnBackIndex;

  /*
    The page stays on the sheet; the half underneath it gives way.

    A spread shows four halves at once — two lying open and two on the sheet — and mid-book those
    four are four different things. At the ends of the book they are not: the closed side of the
    book has one face, not two, so the open side was being used twice and a reader watched the
    same page in two places at once.

    Whichever face collides, it is the *slot* that yields, never the face. A turning leaf is the
    thing a reader is following, and both cover turns are the same motion in opposite directions:
    opening, the first page rides down on the back of the cover onto an empty side; closing, the
    first page rides away and leaves that same empty side behind. What it lands on, or lifts off,
    is the inside of the cover — board. Emptying the *face* instead, which is where this started,
    laid the destination page out on the left before the cover had moved at all and then turned a
    blank board over it: the turn described backwards, which is exactly how it read.

    Nothing needs to be faded. The sheet lands showing the very slot the commit is about to fill,
    so the frame before the commit and the frame after it are the same picture.

    The other end of the book works the same way, which is the whole point of stating it once. The
    back cover is a face like the front one: closing onto it, it comes down on the sheet and the
    empty inside it left is what the last page lifts off; opening from it, it lifts away on the
    sheet and the last spread is underneath. Putting board on the sheet there and letting the back
    cover appear at the commit was the identical inversion the front cover had — the destination
    arriving after the turn instead of on it.

    So a face carrying the back cover shows it whenever this turn genuinely starts or ends on it,
    and shows board only where it cannot be a passenger: a story with an odd number of pages ends
    with the back cover named by *both* faces of one sheet, and a sheet showing the same thing on
    both sides is a sheet that visibly does not turn. There the face that is not the one the turn
    is coming from or going to yields the board, which is also the only half that could be blank
    underneath anyway.

    Below the spread breakpoint none of this applies: the sheet covers the whole volume, so a face
    matching the page underneath is what makes the turn continuous rather than a duplicate anybody
    can see, and a shut book showing its own back cover is simply the back cover.
  */
  const underlayShows = (leafIndex: number) =>
    leafIndex !== NO_LEAF && (leafIndex === spreadPages.left || leafIndex === rightSlotIndex);
  // Guarded against backIndex's own -1: a book with no back cover must not match NO_LEAF.
  const isBackCover = (leafIndex: number) => backIndex !== -1 && leafIndex === backIndex;
  const turningOnSpread = turning !== null && turnTo !== null && desktopSpread;
  // Whether the shut back of the book is this turn's own origin or its own destination, which is
  // what makes the difference between a face carrying it and a face merely naming it.
  const backIsOrigin = isBackCover(displayIndex);
  const backIsDestination = turnTo !== null && isBackCover(turnTo);
  /*
    A face that names the back cover carries it when the turn genuinely starts on it — the face
    lifting away — or genuinely ends on it — the face coming down. Otherwise it is a passenger on
    somebody else's turn and yields the board. The collide guard is what remains for anything else
    that could ever name one leaf on both faces of a sheet.
  */
  const boardFront =
    turningOnSpread && (isBackCover(turnFrontIndex) ? !backIsOrigin : turnFacesCollide);
  const boardBack = turningOnSpread && isBackCover(turnBackIndex) && !backIsDestination;
  /*
    At most one half can be doubled — the closed side of the book only has the one face to spare —
    so the first face carrying a page that is also lying open names the slot that yields. A face
    already showing board carries no page and can double nothing.
  */
  const doubledIndex =
    !boardFront && underlayShows(turnFrontIndex)
      ? turnFrontIndex
      : !boardBack && underlayShows(turnBackIndex)
        ? turnBackIndex
        : NO_LEAF;
  const boardUnderlaySide: SpreadSide | null =
    turningOnSpread && doubledIndex !== NO_LEAF
      ? doubledIndex === spreadPages.left
        ? "left"
        : "right"
      : null;

  /*
    The back cover, and the one real link in the whole book.

    Turning a page by clicking works through two invisible halves laid over the volume, and they
    win every click on it — `.storybook-back-cta` asks for a z-index above them and does not get
    one, because the surface holding the pages is its own stacking context two levels below. So
    on the last page the button that says "a new adventure" turned the book back a page instead
    of going anywhere.

    The halves come off while that page is showing. Nothing is lost: the labelled buttons under
    the book, the arrow keys, the wheel and a swipe all still turn it, and this is the one page
    where the thing to do is written on it.
  */
  const showsBackCover = desktopSpread
    ? isBackCover(spreadPages.left) || isBackCover(rightSlotIndex)
    : leaves[index]?.kind === "qr";

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
                {boardUnderlaySide === "left" ? (
                  <BoardFace side="left" />
                ) : (
                  renderSlot(spreadPages.left, "left", "left")
                )}
              </div>
              <div className="storybook-spread-gutter" aria-hidden="true" />
              <div className="storybook-spread-leaf storybook-spread-right">
                {boardUnderlaySide === "right" ? (
                  <BoardFace side="right" />
                ) : (
                  renderSlot(rightSlotIndex, "right", "right")
                )}
              </div>
            </div>
          ) : (
            <LeafView
              leaf={leaves[displayIndex] ?? null}
              heroName={heroName}
              title={title}
              coverCaption={coverCaption}
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
        {turning && turnTo !== null && desktopSpread && (displayIndex === 0 || turnTo === 0) ? (
          <div className={`storybook-cover-turn turn-${turning}`} aria-hidden="true">
            {/*
              A cover lies on one half of the open book and lands on the other, so its two faces
              are halves like any other: opening, the cover is the right half and the first page
              of the story is the left one it puts down; closing, the reverse. This is where the
              duplication rule above earns its keep — the side of the sheet that is not the cover
              has no page of its own to carry.
            */}
            <div className="storybook-turn-face storybook-turn-front">
              {boardFront ? (
                <BoardFace side={turnFrontSide} />
              ) : (
                renderSlot(turnFrontIndex, turnFrontSide)
              )}
            </div>
            <div className="storybook-turn-face storybook-turn-back">
              {boardBack ? (
                <BoardFace side={turnBackSide} />
              ) : (
                renderSlot(turnBackIndex, turnBackSide)
              )}
            </div>
            <i className="storybook-page-curl" />
          </div>
        ) : null}

        {turning && turnTo !== null && desktopSpread && displayIndex > 0 && turnTo > 0 ? (
          <div className={`storybook-spread-turn turn-${turning}`} aria-hidden="true">
            <div className="storybook-turn-face storybook-turn-front">
              {boardFront ? (
                <BoardFace side={turnFrontSide} />
              ) : (
                renderSlot(turnFrontIndex, turnFrontSide)
              )}
            </div>
            <div className="storybook-turn-face storybook-turn-back">
              {boardBack ? (
                <BoardFace side={turnBackSide} />
              ) : (
                renderSlot(turnBackIndex, turnBackSide)
              )}
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
                coverCaption={coverCaption}
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
                coverCaption={coverCaption}
                coverSrc={resolvedCover}
                totalStoryPages={totalStoryPages}
                isSpreadBook={isSpreadBook}
              />
            </div>
            <i className="storybook-page-curl" />
          </div>
        ) : null}

        {interactive && canPrev && !showsBackCover ? (
          <button
            className="storybook-corner storybook-corner-previous"
            type="button"
            onClick={() => goTo(prevTarget, "previous")}
            aria-label={t.story.storybook.previousPage}
          />
        ) : null}
        {interactive && canNext && !showsBackCover ? (
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
            {/* The middle track is still occupied when the counter is hidden: the controls are a
                three-column grid, and dropping the element outright slid "next" into the centre
                and both buttons out of balance. */}
            <span
              className={`storybook-progress${variant === "hero" ? " is-spacer" : ""}`}
              aria-live="polite"
              aria-hidden={variant === "hero" ? true : undefined}
              style={variant === "hero" ? { visibility: "hidden" } : undefined}
            >
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
