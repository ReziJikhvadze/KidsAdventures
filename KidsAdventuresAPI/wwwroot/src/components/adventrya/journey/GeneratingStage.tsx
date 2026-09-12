import { Loader2, Sparkles } from "lucide-react";
import { useEffect, useState } from "react";
import { useNavigate } from "@tanstack/react-router";

import * as adventurePacksApi from "@/lib/api/adventure-packs";
import * as ordersApi from "@/lib/api/orders";
import { BookFailedError, OrderStillWorkingError } from "@/lib/api/orders";
import type { OrderStatusResponse } from "@/lib/api/types";
import { useIllustrationUrl } from "@/lib/hooks/useIllustrationUrl";
import { useT } from "@/lib/i18n";
import { primaryCharacter, type JourneyDraft } from "@/lib/journey/draft";
import { BEKI_MARK_WHITE_URL, BRAND_NAME } from "@/lib/brand";
import { useWorldById, isWorldId, type WorldId } from "@/lib/worlds";

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
  /*
    The cover this book actually has, or an empty frame.

    The world's painting stood here as the third fallback, so a parent who had just been
    shown their own child's cover and paid for it watched the map's stock island being
    presented as their book. There is no third thing to show: either the cover the preview
    made is known - from the draft in this tab, or from the order once it answers - or
    nothing is, and an empty board under a spinner says that without lying about it.
  */
  const coverSrc = draft.preview?.coverImageDataUrl || storedCover || null;
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
    // Draft hydration can supply the order after the initial render. Do not retain the
    // earlier missing-order error while polling a now-valid order.
    setError(null);
    setStillWorking(false);

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

  /*
    Whether anything on this screen is yet the parent's own book.

    A parent arriving back from the bank has an order id and nothing else: no draft, no
    title, no cover, until the first poll answers - and that first answer took long enough
    to read. What stood there meanwhile was the world's stock painting under a title made
    from a formula, presented as the book they had just paid for. A spinner says the true
    thing instead, and it is gone the moment either half of the real book arrives.
  */
  const bookKnown = Boolean(artSrc);

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
          <div className="generation-paper-trail" aria-hidden="true">
            <i />
            <i />
            <i />
          </div>
          <article className={`ux-book-cover generation-book${bookKnown ? "" : " is-loading"}`}>
            {artSrc ? (
              <div
                className="ux-cover-art"
                style={{ backgroundImage: `url("${artSrc}")` }}
                aria-hidden="true"
              />
            ) : null}
            <div className="ux-cover-shade" aria-hidden="true" />
            <img className="ux-cover-brand" src={BEKI_MARK_WHITE_URL} alt={BRAND_NAME} />
            {bookKnown ? (
              <h2>{bookTitle}</h2>
            ) : (
              <span className="generation-book-loading" role="status">
                <Loader2 aria-hidden="true" />
                {t.common.states.loading}
              </span>
            )}
          </article>
          <div className="generation-ring ring-one" aria-hidden="true" />
          <div className="generation-ring ring-two" aria-hidden="true" />
        </div>
      </div>

      <div className="generation-copy">
        {/*
          Beki, one last time: the line the checkout was leading to. He said what was missing,
          then that it was good, then that the order could go; this is the fourth and final
          state, on the screen the payment lands on. The same idiom as the checkout - the small
          figure and the paper bubble - so it reads as the same voice arriving, not a new one.
        */}
        <div className="ux-beki-guide">
          <img src="/adventrya/beki-canonical.webp" alt="" width={480} height={685} />
          <p className="ux-beki-bubble">{t.journey.generating.bekiLine}</p>
        </div>
        <p className="eyebrow">
          <Sparkles aria-hidden="true" /> {t.journey.generating.heading}
        </p>
        <h1>
          {heroName}
          {t.journey.generating.titleSuffix}
        </h1>

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

        {/*
          The pictures themselves, under the line that counts them.

          They used to sit in the portal, after the atelier — which is absolutely positioned
          across the whole of it and painted above them, so the strip was drawn behind the cover
          and nobody ever saw one. Measured at 375 and at 1440 alike: the element was there, with
          the right number of images in it, and a hit test at its centre returned the cover.

          Here they are in the text column instead, directly under "დაიხატა 5 / 8 ილუსტრაცია",
          so the number and the thing it counts are read together. There is no room left for them
          in the portal on a phone now that the cover fills its frame, and this is the one column
          that has room at every width.
        */}
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

        {/*
          Three states, because there are three things to say.

          The row carried one class, `active`, on every step up to and including the current one,
          and the stylesheet it borrowed dresses `.preview-loader-stages span` — this list is
          made of `li`. So the colour rule reached nothing, the four lines were drawn identically,
          and a list whose whole job is to show where the book has got to showed nothing at all.

          Done, current and still to come are now separate classes and are drawn as separate
          things. `step` is unchanged and is still the real one: STATUS_STEP maps the book's own
          status onto these four, and the eight-second timer only runs while no status has
          arrived yet. Nothing here invents progress.
        */}
        <ul className="preview-loader-stages generation-stages" style={{ marginTop: 18 }}>
          {t.journey.generating.stages.map((label, index) => (
            <li
              key={label}
              className={index < step ? "is-done" : index === step ? "is-current" : "is-todo"}
              aria-current={index === step ? "step" : undefined}
            >
              {label}
            </li>
          ))}
        </ul>

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
            {t.journey.generating.stopWaiting}
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
