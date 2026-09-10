import { ArrowLeft, Download, Minus, Plus, Sparkles } from "lucide-react";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useParams } from "@tanstack/react-router";

import { AppHeader } from "@/components/adventrya/AppHeader";
import { StorybookVolume } from "@/components/adventrya/storybook/StorybookVolume";
import { ApiError } from "@/lib/api/client";
import {
  downloadAdventurePack,
  generatePackPdf,
  getAdventurePack,
  markPackRead,
  pollAdventurePack,
} from "@/lib/api/adventure-packs";
import type { AdventurePackDetailResponse } from "@/lib/api/types";
import { useAuth } from "@/lib/auth/AuthContext";
import { rememberReadLocally } from "@/lib/books-read";
import { BekiLoader } from "@/components/adventrya/BekiLoader";
import { useIllustrationUrl } from "@/lib/hooks/useIllustrationUrl";
import { useT } from "@/lib/i18n";
import { NewBookCharacterContext } from "@/lib/story/newBookCharacter";
import { useWorldById, isWorldId } from "@/lib/worlds";

/*
  The book, in pages.

  Eight painted spreads, two leaves each — the number the home page promises in words and the one
  `pageCount` documents. Named here because the reader is the last place that still guessed at it.
*/
const BOOK_PAGE_COUNT = 16;

export function ReaderScreen() {
  const WORLD_BY_ID = useWorldById();
  const t = useT();
  const { bookId } = useParams({ from: "/reader/$bookId" });
  const { isAuthenticated, isLoading: authLoading } = useAuth();
  const [pack, setPack] = useState<AdventurePackDetailResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [downloading, setDownloading] = useState(false);
  // Kept apart from `error`, which replaces the whole book with a message. A PDF that failed
  // is no reason to stop showing the story.
  const [pdfError, setPdfError] = useState<string | null>(null);

  /*
    How close the reader is standing, and to which part of the picture.

    Zoom lives here rather than in StorybookVolume: the volume is the same component the home
    page, the preview and the order summary all mount, and none of them wants a magnifier. Here
    it is a transform on a wrapper, so the book itself is untouched - it keeps turning its own
    pages, and at 1x the wrapper takes no pointer events at all, so dragging still turns a leaf
    rather than sliding the page under the child's finger.
  */
  const [zoom, setZoom] = useState(1);
  const [pan, setPan] = useState({ x: 0, y: 0 });
  const dragFrom = useRef<{ x: number; y: number; panX: number; panY: number } | null>(null);

  useEffect(() => {
    if (authLoading) return;
    if (!isAuthenticated) {
      setLoading(false);
      setError(t.common.states.bookFailed);
      return;
    }

    let cancelled = false;
    setLoading(true);
    setError(null);

    void getAdventurePack(bookId)
      .then((detail) => {
        if (cancelled) return;
        setPack(detail);
        // The shelf ranks a read book's actions differently: once the story has been read, the
        // printed copy is the only thing its card still has to offer. A failed stamp is not
        // worth interrupting the story for — the next open tries again.
        rememberReadLocally(bookId);
        void markPackRead(bookId).catch(() => {});
      })
      .catch((err) => {
        if (cancelled) return;
        setError(err instanceof ApiError ? err.message : t.common.states.bookFailed);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [bookId, isAuthenticated, authLoading]);

  /*
    Zoom, kept inside what the window can show.

    The book is already as wide as the window at 1x, so panning is clamped to the overhang the
    scale created rather than to some invented canvas: at 1x there is no overhang and no pan,
    and every step out re-clamps rather than leaving the picture parked off screen.
  */
  const clampPan = useCallback((next: { x: number; y: number }, level: number) => {
    const room = (level - 1) / 2;
    const limitX = room * 100;
    const limitY = room * 100;
    return {
      x: Math.max(-limitX, Math.min(limitX, next.x)),
      y: Math.max(-limitY, Math.min(limitY, next.y)),
    };
  }, []);

  const stepZoom = useCallback((delta: number) => {
    setZoom((prev) => Math.max(1, Math.min(3, Math.round((prev + delta) * 10) / 10)));
  }, []);

  /* The view follows the level, rather than each press guessing where it will end up. */
  useEffect(() => {
    setPan((prev) => (zoom === 1 ? { x: 0, y: 0 } : clampPan(prev, zoom)));
  }, [zoom, clampPan]);

  /* A book that changes under the reader starts at arm's length again. */
  useEffect(() => {
    setZoom(1);
  }, [bookId]);

  // How far the illustrations have got. Only pages meant to carry a picture count: half of a
  // spread book is prose, and counting those would make a finished book look half done.
  const illustrationProgress = useMemo(() => {
    const pages = pack?.storyPages ?? [];
    const wanted = pages.filter((page) => !page.isTextOnlyPage);
    return { done: wanted.filter((page) => page.isIllustrated).length, total: wanted.length };
  }, [pack?.storyPages]);

  // A floor of 5 so the bar reads as "started" rather than "stuck" during the first poll, and
  // so a book whose PDF already exists does not flash an empty bar on its way to the download.
  const pdfPercent = Math.min(100, Math.max(5, pack?.progressPercent ?? 0));

  const isIllustrating =
    pack !== null &&
    (pack.isUnlocked === true || pack.accessLevel === "Full") &&
    illustrationProgress.total > 0 &&
    illustrationProgress.done < illustrationProgress.total;

  /*
    The book is not written yet.

    Two ways to be here, and both used to render an empty volume — covers with nothing between
    them, which reads as a book that failed rather than one that has not happened yet. The
    server's own `generationPending` is the reliable half; a book with no pages and no failure is
    the same state seen from the client, and covers anything the flag has not reached.
  */
  const isPending =
    pack !== null &&
    pack.status !== "Failed" &&
    pack.isFailed !== true &&
    (pack.generationPending === true || (pack.storyPages?.length ?? 0) === 0);

  // A book takes minutes to illustrate and the page said nothing about it, so a parent who
  // refreshed could not tell whether anything was happening. Polling while there is work left
  // means a reload — or simply leaving it open — shows the pictures as they land.
  useEffect(() => {
    if (!isIllustrating && !isPending) return;

    let cancelled = false;
    const timer = window.setInterval(() => {
      void getAdventurePack(bookId)
        .then((detail) => {
          if (!cancelled) setPack(detail);
        })
        .catch(() => {
          /* a failed refresh is not worth interrupting the reading for */
        });
    }, 8000);

    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, [bookId, isIllustrating, isPending]);

  const heroName = pack?.childName?.trim() || t.common.fallbackHeroName;
  // Through the authenticated fetch, like every other picture on this page: the cover is an
  // `/api/...` path, and handed straight to a background-image it answered 401 and the two
  // waiting panels showed a blank frame where the child's book should have been.
  const atelierCover = useIllustrationUrl(pack?.coverImageUrl);
  const worldId = pack?.worldId && isWorldId(pack.worldId) ? pack.worldId : null;
  const world = worldId ? WORLD_BY_ID[worldId] : null;
  const title =
    pack?.title?.trim() ||
    (world ? world.bookTitle(heroName) : t.story.storybook.adventureOf(heroName));
  const pages = pack?.storyPages ?? [];
  const isUnlocked = pack?.isUnlocked === true || pack?.accessLevel === "Full";
  /*
    Sixteen, when the server has not said.

    The fallback counted to seven — a page count from a book format this product left behind —
    so a locked reader was told the rest of the book was six pages when the home page, the
    pricing copy and `pageCount` all say sixteen. `BOOK_PAGE_COUNT` is the eight painted spreads,
    two leaves each. The server's own `lockedPageCount` still wins wherever it is given.
  */
  const lockedPageCount =
    pack?.lockedPageCount ?? (isUnlocked ? 0 : Math.max(0, BOOK_PAGE_COUNT - pages.length));
  const canVisitWorldPassport =
    Boolean(pack?.worldId) &&
    isUnlocked &&
    (pack?.status === "StoryReady" ||
      pack?.status === "GeneratingPdf" ||
      pack?.status === "Completed");

  // The button used to be disabled whenever the book had no PDF yet, and nothing on this page
  // could ask for one — so a finished book showed a dead button and no explanation. It now
  // starts the job, watches it, and downloads when the file exists.
  const onDownload = async () => {
    if (!pack || downloading) return;

    /*
      A finished book whose file is deliberately being held back.

      Asking for it anyway reaches a queue that refuses a Completed pack in English, and that
      refusal was rendered here verbatim — a code and a sentence about "story must be ready" on
      a page showing the parent their finished story. There is nothing to wait on in this
      browser, so this says what is happening and stops.
    */
    if (pack.downloadHeld || (pack.generationPipeline === "beki" && !pack.pdfUrl)) {
      setPdfError(t.story.reader.pdf.held);
      return;
    }

    setDownloading(true);
    setPdfError(null);
    try {
      let ready = pack;
      if (!ready.pdfUrl) {
        // A job already running is joined rather than started again: queuing rejects any pack
        // that is not StoryReady, so a second click during the build would only raise an error.
        if (ready.status !== "GeneratingPdf") {
          await generatePackPdf(ready.id);
        }
        ready = await pollAdventurePack(ready.id, (fresh) => setPack(fresh), {
          untilPdfReady: true,
          maxAttempts: 90,
        });
        setPack(ready);
      }
      await downloadAdventurePack(ready.id, `${title}.pdf`, title);
    } catch {
      // One Georgian line, whatever happened. What stood here forwarded the server's own string,
      // and the strings on this path are English operator messages with failure codes in them.
      setPdfError(t.story.reader.pdf.failed);
    } finally {
      setDownloading(false);
    }
  };

  const onPointerDown = (event: React.PointerEvent<HTMLDivElement>) => {
    if (zoom === 1) return;
    dragFrom.current = { x: event.clientX, y: event.clientY, panX: pan.x, panY: pan.y };
    event.currentTarget.setPointerCapture(event.pointerId);
  };

  const onPointerMove = (event: React.PointerEvent<HTMLDivElement>) => {
    const from = dragFrom.current;
    if (!from) return;
    /* In percent of the book's own width, so the same drag moves the same amount of picture
       whatever size the window has made it. */
    const box = event.currentTarget.getBoundingClientRect();
    setPan(
      clampPan(
        {
          x: from.panX + ((event.clientX - from.x) / box.width) * 100,
          y: from.panY + ((event.clientY - from.y) / box.height) * 100,
        },
        zoom,
      ),
    );
  };

  const endDrag = () => {
    dragFrom.current = null;
  };

  /*
    The book, whatever state it is in.

    One branch decides what fills the stage, and the order is the order a parent meets them:
    still opening, a book that failed, a book not written yet, and then the book. The reading
    view is last because it is the only one that is not an exception.
  */
  const stage =
    authLoading || loading ? (
      /*
      Waiting, drawn as the book rather than as a spinner in a hole.

      A finished book is eight painted spreads and they are large files; coming in from the
      parent's space there is a real pause before the first one lands. The spread is drawn at
      the size it is about to be, in its own paper, so when the picture arrives it arrives in
      place and nothing on the screen moves.
    */
      <div className="reader-waiting" role="status" aria-live="polite">
        <div className="reader-waiting-book">
          <i />
          <i />
          <span className="reader-waiting-mark" aria-hidden="true">
            <i />
            <i />
            <i />
          </span>
        </div>
        <small>{t.common.states.loading}</small>
      </div>
    ) : error ? (
      <p className="reader-note" role="alert">
        {error}
      </p>
    ) : pack && (pack.status === "Failed" || pack.isFailed) ? (
      <div className="reader-note-panel" role="alert">
        <h2>{t.dashboard.library.failedTitle}</h2>
        <p>{pack.errorMessage || t.dashboard.library.failedBody}</p>
      </div>
    ) : isPending ? (
      /*
      "Being drawn", said as a book that is coming rather than a book that is missing. The page
      polls behind this, so it becomes the real volume without anybody refreshing.
    */
      <div className="reader-note-panel" aria-live="polite">
        <h2>{t.story.reader.pending.title}</h2>
        <p>{pack?.progressMessage || t.story.reader.pending.body}</p>
      </div>
    ) : pack ? (
      <NewBookCharacterContext.Provider value={pack.primaryCharacterId ?? null}>
        <div
          className="reader-zoomer"
          style={{
            transform: `scale(${zoom}) translate(${pan.x}%, ${pan.y}%)`,
            cursor: zoom > 1 ? (dragFrom.current ? "grabbing" : "grab") : undefined,
            pointerEvents: zoom > 1 ? "auto" : undefined,
            touchAction: zoom > 1 ? "none" : undefined,
          }}
          onPointerDown={onPointerDown}
          onPointerMove={onPointerMove}
          onPointerUp={endDrag}
          onPointerCancel={endDrag}
        >
          <StorybookVolume
            className="storybook storybook-full"
            heroName={heroName}
            title={title}
            coverImageUrl={pack.coverImageUrl}
            worldId={pack.worldId}
            pages={pages}
            lockedPageCount={lockedPageCount}
            isUnlocked={isUnlocked}
            isSpreadBook={pack.isSpreadBook}
            fullBleedSpreads={pack.isSpreadBook}
            interactive
          />
        </div>
      </NewBookCharacterContext.Provider>
    ) : null;

  return (
    <div className="screen reader-shell reader-shell-book">
      <div className="reader-glow" aria-hidden="true" />
      <div className="grain" aria-hidden="true" />

      {/*
        Every piece of chrome this screen has, on one line.

        What stood here was the app header, a heading, a lead paragraph and three buttons - about
        240px of the window, above a book that was then capped at 390px a leaf. The book is what
        the screen is for, so the rest is a bar: where to go back to, what is being read, and the
        two things a parent might want that are not reading it.
      */}
      <div className="reader-bar">
        <Link
          className="reader-bar-icon"
          to="/dashboard"
          aria-label={t.story.reader.library.trim()}
        >
          <ArrowLeft aria-hidden="true" />
        </Link>
        <span className="reader-bar-title">
          <small>{heroName}</small>
          <strong>{title}</strong>
        </span>
        <span className="reader-bar-end">
          {canVisitWorldPassport && pack ? (
            <Link
              className="reader-bar-pill"
              to="/world"
              search={{ bookId: pack.id }}
              aria-label={t.story.reader.worldPassport}
            >
              <Sparkles aria-hidden="true" />
              <span>{t.story.reader.worldPassport}</span>
            </Link>
          ) : null}
          <button
            className="reader-bar-pill"
            type="button"
            disabled={!pack || downloading || isIllustrating || isPending}
            aria-label={t.journey.generated.downloadPdf}
            onClick={() => void onDownload()}
          >
            <Download aria-hidden="true" />
            <span>
              {downloading ? t.story.reader.pdf.building : t.journey.generated.downloadPdf}
            </span>
          </button>
        </span>
      </div>

      {/* Said where it happened, rather than over the book. */}
      {(pdfError ?? (pack?.downloadHeld ? t.story.reader.pdf.held : null)) ? (
        <p className="reader-bar-note" role={pdfError ? "alert" : undefined}>
          {pdfError ?? t.story.reader.pdf.held}
        </p>
      ) : null}

      <main className="reader-stage">
        {stage}

        {/*
          Closer, and to one part of the picture.

          The whole spread is already as wide as the window, so this is not about making the book
          bigger - it is about a child going looking for something inside a painting. It sits in
          the corner the page counter does not use, and only once there is a book to look at.
        */}
        {pack && !isPending && !error ? (
          <div className="reader-zoom">
            <button
              type="button"
              disabled={zoom <= 1}
              onClick={() => stepZoom(-0.4)}
              aria-label={t.story.reader.zoomOut}
            >
              <Minus aria-hidden="true" />
            </button>
            <b aria-live="polite">{Math.round(zoom * 100)}%</b>
            <button
              type="button"
              disabled={zoom >= 3}
              onClick={() => stepZoom(0.4)}
              aria-label={t.story.reader.zoomIn}
            >
              <Plus aria-hidden="true" />
            </button>
          </div>
        ) : null}
      </main>
    </div>
  );
}
