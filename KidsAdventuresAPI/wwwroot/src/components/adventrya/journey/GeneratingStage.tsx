import { Sparkles } from "lucide-react";
import { useEffect, useState } from "react";
import { useNavigate } from "@tanstack/react-router";

import * as adventurePacksApi from "@/lib/api/adventure-packs";
import * as ordersApi from "@/lib/api/orders";
import { BookFailedError, OrderStillWorkingError } from "@/lib/api/orders";
import type { OrderStatusResponse } from "@/lib/api/types";
import { useIllustrationUrl } from "@/lib/hooks/useIllustrationUrl";
import { useT } from "@/lib/i18n";
import { primaryCharacter, type JourneyDraft } from "@/lib/journey/draft";
import { useWorldById, WORLD_COVER_ART, isWorldId, type WorldId } from "@/lib/worlds";

type Props = {
  draft: JourneyDraft;
  onChange: (patch: Partial<JourneyDraft> | ((prev: JourneyDraft) => JourneyDraft)) => void;
};

/** A Beki book: eight painted spreads. What "N of 8" counts. */
const SPREAD_COUNT = 8;

/**
 * Which of the four stage lines a book's status is standing on.
 *
 * Completed maps to the last one rather than to nothing: the navigation away happens on the order
 * poll, and for the second or two between the pipeline finishing and the reader opening, the
 * screen should say the book is bound, not still be painting it.
 */
const STATUS_STEP: Record<string, number> = {
  Pending: 0,
  Generating: 1,
  GeneratingStory: 1,
  StoryReady: 2,
  GeneratingPdf: 3,
  Completed: 3,
};

/**
 * How often the order is asked whether the book is ready.
 *
 * Five seconds, and one asker. The order was polled every two and a half seconds and the book's
 * own progress every five, by two loops that did not know about each other; the book's row is
 * the one that changes, so it is asked at its own pace and the order slightly less often.
 */
const ORDER_POLL_MS = 5000;

/** One window of order polling before the "still working" notice goes up: fifteen minutes. */
const ORDER_POLL_ATTEMPTS = 180;

export function GeneratingStage({ draft, onChange }: Props) {
  const WORLD_BY_ID = useWorldById();
  const t = useT();
  const navigate = useNavigate();
  const hero = primaryCharacter(draft);

  // The order's own description of the book, for a screen that arrived with nothing else. A
  // parent coming back from the bank lands on a hard page load: the draft is gone, and until the
  // first poll answers, this screen only knows an order id.
  const [known, setKnown] = useState<OrderStatusResponse | null>(null);

  const heroName = hero.name.trim() || known?.childName?.trim() || t.common.fallbackHeroName;
  const worldId: WorldId =
    draft.worldId ?? (known?.worldId && isWorldId(known.worldId) ? known.worldId : "dinosaurs");
  const world = WORLD_BY_ID[worldId];
  const storedCover = useIllustrationUrl(draft.preview ? null : known?.coverImageUrl);
  const coverSrc = draft.preview?.coverImageDataUrl || storedCover || WORLD_COVER_ART[worldId];
  const bookTitle = draft.preview?.title || known?.title?.trim() || world.bookTitle(heroName);

  const [step, setStep] = useState(0);
  const [error, setError] = useState<string | null>(null);
  // The polling window ran out but the book is fine — a calm notice, never an error. A Beki
  // book is nine illustrations; taking longer than the poll is normal, not a fault.
  const [stillWorking, setStillWorking] = useState(false);
  const [progress, setProgress] = useState<string | null>(null);
  const [percent, setPercent] = useState<number | null>(null);
  // Where the job actually is, straight from the book's own row.
  const [bookStatus, setBookStatus] = useState<string | null>(null);
  // The spreads drawn so far, in page order. Real pictures beat a spinner: the book takes
  // minutes, and each finished illustration appearing here is the proof something is happening.
  const [pages, setPages] = useState<{ spread: number; url: string }[]>([]);
  const [spreadsDone, setSpreadsDone] = useState(0);

  // The timer only runs while nothing better is known. Once the book's status arrives the stage
  // list follows it, so the four lines stop being a clock and start being a report.
  const statusStep = STATUS_STEP[bookStatus ?? ""];

  useEffect(() => {
    if (statusStep !== undefined) return;
    const timer = window.setInterval(() => {
      setStep((s) => Math.min(s + 1, t.journey.generating.stages.length - 1));
    }, 8000);
    return () => window.clearInterval(timer);
  }, [t.journey.generating.stages.length, statusStep]);

  useEffect(() => {
    if (statusStep !== undefined) setStep(statusStep);
  }, [statusStep]);

  useEffect(() => {
    const orderId = draft.orderId;
    if (!orderId) {
      setError(t.journey.generating.orderMissing);
      return;
    }

    let cancelled = false;

    void (async () => {
      try {
        // Returning from the bank: reconcile payment before polling readiness.
        try {
          await ordersApi.confirmOrder(orderId);
        } catch {
          /* confirm is best-effort when webhook already ran */
        }

        // A poll window that runs out is not a failure — the notice goes up and polling
        // simply starts again, so a parent who stays on this page is still taken to the
        // finished book, however long it took to draw.
        let status: OrderStatusResponse | null = null;
        let attempts = 0;
        let transientFailures = 0;
        while (status === null && attempts < 50) {
          attempts++;
          try {
            status = await ordersApi.pollOrderUntilReady(
              orderId,
              (current) => {
                if (cancelled) return;
                setKnown(current);
                if (current.progressMessage) setProgress(current.progressMessage);
                if (typeof current.progressPercent === "number")
                  setPercent(current.progressPercent);
                if (current.packStatus) setBookStatus(current.packStatus);
                if (current.bookId) onChange({ bookId: current.bookId });
              },
              { intervalMs: ORDER_POLL_MS, maxAttempts: ORDER_POLL_ATTEMPTS },
            );
          } catch (err) {
            if (cancelled) return;
            if (err instanceof BookFailedError) {
              throw err;
            }
            if (err instanceof OrderStillWorkingError) {
              setStillWorking(true);
              continue;
            }
            // A single dropped request mid-poll is not a failed book. Keep watching through
            // brief network noise; only a persistent inability to ask counts as an error.
            transientFailures++;
            if (transientFailures >= 3) throw err;
            setStillWorking(true);
            await new Promise((r) => setTimeout(r, 4000));
          }
        }

        if (cancelled) return;
        if (status) {
          onChange({ bookId: status.bookId ?? draft.bookId });
          if (status.bookId) {
            void navigate({ to: "/reader/$bookId", params: { bookId: status.bookId } });
          } else {
            void navigate({ to: "/dashboard" });
          }
        } else {
          void navigate({ to: "/dashboard" });
        }
      } catch (err) {
        if (cancelled) return;
        if (err instanceof BookFailedError) {
          setError(err.parentMessage || t.journey.generating.failedBody);
        } else if (err instanceof Error && err.message) {
          // A declined or cancelled order throws its own reason — telling that parent
          // "generation was interrupted" would send them waiting on a book nobody charged for.
          setError(err.message);
        } else {
          setError(t.journey.generating.failedBody);
        }
      }
    })();

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [draft.orderId]);

  // Newest artwork, as it lands. The book id arrives with the first order poll; from then on
  // each finished spread is fetched once and kept — the endpoint returns an empty list for a
  // legacy-pipeline book, and this whole effect quietly does nothing.
  useEffect(() => {
    const bookId = draft.bookId;
    if (!bookId) return;

    let cancelled = false;
    const seen = new Set<number>();

    const tick = async () => {
      try {
        const status = await adventurePacksApi.getMakingOf(bookId);
        if (cancelled) return;
        setBookStatus(status.status);
        if (status.progressMessage) setProgress(status.progressMessage);
        if (typeof status.progressPercent === "number") setPercent(status.progressPercent);
        setSpreadsDone(status.spreads.length);
        for (const spread of status.spreads) {
          if (seen.has(spread)) continue;
          seen.add(spread);
          const url = await adventurePacksApi.fetchIllustrationObjectUrl(
            adventurePacksApi.makingOfImagePath(bookId, spread),
          );
          if (cancelled) return;
          setPages((prev) =>
            [...prev.filter((p) => p.spread !== spread), { spread, url }].sort(
              (a, b) => a.spread - b.spread,
            ),
          );
        }
      } catch {
        /* generation may not have started yet; the next poll will see it */
      }
    };

    void tick();
    const timer = window.setInterval(tick, 5000);
    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, [draft.bookId]);

  // The freshest picture takes the book's own frame, so the cover the parent already saw
  // gives way to pages they have not.
  const newest = pages.length > 0 ? pages[pages.length - 1] : null;
  const artSrc = newest?.url ?? coverSrc;

  if (error) {
    return (
      <section className="ux-preview-stage">
        <header className="ux-stage-heading ux-preview-heading">
          <p className="eyebrow">
            <Sparkles aria-hidden="true" /> {t.journey.generating.heading}
          </p>
          <h1>{t.journey.generating.failedTitle}</h1>
          <p className="ux-form-error">{error}</p>
          <button
            className="button journey-primary"
            type="button"
            onClick={() => void navigate({ to: "/dashboard" })}
          >
            {t.journey.generating.toDashboard}
          </button>
        </header>
      </section>
    );
  }

  return (
    <section
      className="journey-stage generating-stage ux-generating"
      aria-label={t.journey.generating.ariaLabel(heroName)}
    >
      <div className="generation-portal">
        <div className="generation-atelier">
          <div className="generation-atelier-topline">
            <span>
              <Sparkles aria-hidden="true" /> BEKI BOOK ATELIER
            </span>
            <strong>
              {t.journey.generating.stageLabel}
              {Math.min(step + 1, 4)} / 4
            </strong>
          </div>
          <div className="generation-paper-trail" aria-hidden="true">
            <i />
            <i />
            <i />
          </div>
          <article className="ux-book-cover generation-book">
            <div
              className="ux-cover-art"
              style={{ backgroundImage: `url("${artSrc}")` }}
              aria-hidden="true"
            />
            <div className="ux-cover-shade" aria-hidden="true" />
            <span className="ux-cover-brand">BEKI</span>
            <small>{world.theme}</small>
            <h2>{bookTitle}</h2>
          </article>
          <div className="generation-ring ring-one" aria-hidden="true" />
          <div className="generation-ring ring-two" aria-hidden="true" />
        </div>

        {pages.length > 0 ? (
          <div className="generation-making-of" aria-label={t.journey.generating.pagesDrawn}>
            {pages.map((page) => (
              <img
                key={page.spread}
                src={page.url}
                alt={t.journey.generating.pageAlt(page.spread)}
                className={page.spread === newest?.spread ? "is-newest" : ""}
              />
            ))}
          </div>
        ) : null}
      </div>

      <div className="generation-copy">
        <p className="eyebrow">
          <Sparkles aria-hidden="true" /> {t.journey.generating.heading}
        </p>
        <h1>
          {heroName}
          {t.journey.generating.titleSuffix}
        </h1>
        <p>
          {t.journey.generating.companionPrefix}
          {heroName}
          {t.journey.generating.companionSuffix}
        </p>
        <p>{t.journey.generating.leaveNote}</p>
        <div className="soft-time">
          <span>
            <i />
          </span>
          {t.journey.generating.softTime}
        </div>

        {/*
          The real number, when the job reports one. A bar that fills on a timer is a promise
          the job did not make; this one moves when the book does and says how many pictures
          exist, which is the fact a waiting parent actually wants.
        */}
        {percent !== null || spreadsDone > 0 ? (
          <div className="generation-progress" role="status" aria-live="polite">
            <div className="preview-loader-progress" aria-hidden="true">
              <i style={{ width: `${Math.max(2, Math.min(100, percent ?? 0))}%` }} />
            </div>
            <p className="generation-progress-line">
              {percent !== null ? <strong>{percent}%</strong> : null}
              {spreadsDone > 0 ? (
                <span>{t.journey.generating.spreadsDrawn(spreadsDone, SPREAD_COUNT)}</span>
              ) : null}
            </p>
          </div>
        ) : null}

        <ul className="preview-loader-stages" style={{ marginTop: 18 }}>
          {t.journey.generating.stages.map((label, index) => (
            <li key={label} className={index <= step ? "active" : ""}>
              {label}
            </li>
          ))}
        </ul>
        {/*
          The real stage, then whatever the job itself is saying. Both, because they answer
          different questions: the first is where the book is, the second is what it is doing.
        */}
        {bookStatus && t.journey.generating.statusLine[bookStatus] ? (
          <p>{t.journey.generating.statusLine[bookStatus]}</p>
        ) : null}
        {progress ? <p>{progress}</p> : null}

        {/*
          Always offered, not only once the poll has run out.

          A paid book takes minutes to draw and this screen had no control on it at all until
          something went slowly or went wrong — so the ordinary case, where everything is fine,
          was the one that trapped the parent. Leaving changes nothing about the book: it is
          being written on the server and lands in the cabinet when it is done.
        */}
        <div className="generation-exit">
          <button
            className="button button-quiet"
            type="button"
            onClick={() => void navigate({ to: "/dashboard" })}
          >
            {t.journey.previewLoader.stopWaiting}
          </button>
        </div>

        {stillWorking ? (
          <div className="generation-still-working">
            <p>{t.journey.generating.stillWorking}</p>
            <button
              className="button journey-primary"
              type="button"
              onClick={() => void navigate({ to: "/dashboard" })}
            >
              {t.journey.generating.toDashboard}
            </button>
          </div>
        ) : null}
      </div>
    </section>
  );
}
