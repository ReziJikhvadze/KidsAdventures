import { BookOpen, Download, Mail, Sparkles } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { Link, useParams } from "@tanstack/react-router";

import { AppHeader } from "@/components/adventrya/AppHeader";
import { StorybookVolume } from "@/components/adventrya/storybook/StorybookVolume";
import { ApiError } from "@/lib/api/client";
import {
  downloadAdventurePack,
  generatePackPdf,
  getAdventurePack,
  pollAdventurePack,
} from "@/lib/api/adventure-packs";
import type { AdventurePackDetailResponse } from "@/lib/api/types";
import { useAuth } from "@/lib/auth/AuthContext";
import { useT } from "@/lib/i18n";
import { useWorldById, isWorldId } from "@/lib/worlds";

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
        if (!cancelled) setPack(detail);
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

  // A book takes minutes to illustrate and the page said nothing about it, so a parent who
  // refreshed could not tell whether anything was happening. Polling while there is work left
  // means a reload — or simply leaving it open — shows the pictures as they land.
  useEffect(() => {
    if (!isIllustrating) return;

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
  }, [bookId, isIllustrating]);

  const heroName = pack?.childName?.trim() || t.common.fallbackHeroName;
  const worldId = pack?.worldId && isWorldId(pack.worldId) ? pack.worldId : null;
  const world = worldId ? WORLD_BY_ID[worldId] : null;
  const title =
    pack?.title?.trim() ||
    (world ? world.bookTitle(heroName) : t.story.storybook.adventureOf(heroName));
  const pages = pack?.storyPages ?? [];
  const isUnlocked = pack?.isUnlocked === true || pack?.accessLevel === "Full";
  const lockedPageCount = pack?.lockedPageCount ?? (isUnlocked ? 0 : Math.max(0, 7 - pages.length));
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
      await downloadAdventurePack(ready.id, `${title}.pdf`);
    } catch (err) {
      setPdfError(
        err instanceof ApiError
          ? err.message
          : (err as Error)?.message || t.common.states.bookFailed,
      );
    } finally {
      setDownloading(false);
    }
  };

  return (
    <div className="screen reader-shell reader-shell-shared-book">
      <div className="reader-glow" aria-hidden="true" />
      <div className="grain" aria-hidden="true" />

      <AppHeader backHref="/dashboard" />

      <main className="reader-shared-stage">
        <div className="reader-shared-heading">
          <p className="eyebrow">
            <Sparkles aria-hidden="true" /> ONLINE READER
          </p>
          <h1>
            {t.story.reader.flipPrefix}
            {heroName}
            {t.story.reader.flipSuffix}
          </h1>
          <p>{t.story.reader.lead}</p>
          <div className="ux-generated-actions" style={{ marginTop: 18 }}>
            <button
              className="button button-quiet"
              type="button"
              disabled={!pack || downloading || isIllustrating}
              onClick={() => void onDownload()}
            >
              <Download aria-hidden="true" />
              {downloading ? t.story.reader.pdf.building : t.journey.generated.downloadPdf}
            </button>
            {canVisitWorldPassport && pack ? (
              <Link className="button button-primary" to="/world" search={{ bookId: pack.id }}>
                <Sparkles aria-hidden="true" />
                {t.story.reader.worldPassport}
              </Link>
            ) : null}
            <Link className="button button-quiet" to="/dashboard">
              <BookOpen aria-hidden="true" />
              {t.story.reader.library.trim()}
            </Link>
          </div>
          {pdfError ? (
            <p className="eyebrow" role="alert" style={{ color: "#f1c970", marginTop: 12 }}>
              {pdfError}
            </p>
          ) : null}
        </div>

        {downloading ? (
          <div className="reader-illustrating" aria-live="polite">
            <div className="preview-atelier-book" aria-hidden="true">
              <div
                className="preview-atelier-art"
                style={
                  pack?.coverImageUrl
                    ? { backgroundImage: `url("${pack.coverImageUrl}")` }
                    : undefined
                }
              />
              <div className="preview-atelier-cover-lines" />
              <span className="preview-atelier-spine" />
              <div className="preview-atelier-page">
                <i />
                <i />
                <i />
              </div>
              <div className="preview-atelier-sparkles">
                {Array.from({ length: 6 }, (_, i) => (
                  <i key={i} />
                ))}
              </div>
            </div>

            <div className="preview-loader-copy">
              <small>{t.story.reader.pdf.atelier}</small>
              <strong>{t.story.reader.pdf.title}</strong>
              <div className="preview-loader-progress" aria-hidden="true">
                <i style={{ width: `${pdfPercent}%` }} />
              </div>
              <p className="reader-illustrating-count">{pdfPercent}%</p>
              <p>{pack?.progressMessage || t.story.reader.pdf.lead}</p>
              <p className="reader-illustrating-email">
                <Mail aria-hidden="true" />
                {t.story.reader.pdf.email}
              </p>
            </div>
          </div>
        ) : null}

        {isIllustrating ? (
          <div className="reader-illustrating" aria-live="polite">
            <div className="preview-atelier-book" aria-hidden="true">
              <div
                className="preview-atelier-art"
                style={
                  pack?.coverImageUrl
                    ? { backgroundImage: `url("${pack.coverImageUrl}")` }
                    : undefined
                }
              />
              <div className="preview-atelier-cover-lines" />
              <span className="preview-atelier-spine" />
              <div className="preview-atelier-page">
                <i />
                <i />
                <i />
              </div>
              <div className="preview-atelier-sparkles">
                {Array.from({ length: 6 }, (_, i) => (
                  <i key={i} />
                ))}
              </div>
            </div>

            <div className="preview-loader-copy">
              <small>{t.story.reader.illustrating.atelier}</small>
              <strong>{t.story.reader.illustrating.title}</strong>
              <div className="preview-loader-progress" aria-hidden="true">
                <i
                  style={{
                    width: `${Math.round((illustrationProgress.done / illustrationProgress.total) * 100)}%`,
                  }}
                />
              </div>
              <p className="reader-illustrating-count">
                {t.story.reader.illustrating.progress(
                  illustrationProgress.done,
                  illustrationProgress.total,
                )}
              </p>
              <p>{t.story.reader.illustrating.leadWaiting}</p>
              {/*
                The single most useful thing to say here. A book takes minutes, and without this
                a parent either sits watching a progress bar or closes the tab wondering whether
                they have just lost what they paid for.
              */}
              <p className="reader-illustrating-email">
                <Mail aria-hidden="true" />
                {t.story.reader.illustrating.email}
              </p>
            </div>
          </div>
        ) : null}

        {loading || authLoading ? (
          <p className="eyebrow" style={{ color: "#f8f2e5a8" }}>
            {t.common.states.loading}
          </p>
        ) : error ? (
          <p className="eyebrow" style={{ color: "#f1c970" }}>
            {error}
          </p>
        ) : pack && !isIllustrating ? (
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
            interactive
          />
        ) : null}
      </main>

      <div className="reader-memory">
        <Sparkles aria-hidden="true" />
        {t.story.reader.memoryPrefix}
        {heroName}
        {t.story.reader.memorySuffix}
      </div>
    </div>
  );
}
