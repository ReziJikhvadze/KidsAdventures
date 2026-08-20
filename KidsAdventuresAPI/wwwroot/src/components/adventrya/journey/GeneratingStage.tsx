import { Sparkles } from "lucide-react";
import { useEffect, useState } from "react";
import { useNavigate } from "@tanstack/react-router";

import * as adventurePacksApi from "@/lib/api/adventure-packs";
import { ApiError } from "@/lib/api/client";
import * as ordersApi from "@/lib/api/orders";
import { useT } from "@/lib/i18n";
import { primaryCharacter, type JourneyDraft } from "@/lib/journey/draft";
import { useWorldById, WORLD_COVER_ART, type WorldId } from "@/lib/worlds";

type Props = {
  draft: JourneyDraft;
  onChange: (patch: Partial<JourneyDraft> | ((prev: JourneyDraft) => JourneyDraft)) => void;
};

export function GeneratingStage({ draft, onChange }: Props) {
  const WORLD_BY_ID = useWorldById();
  const t = useT();
  const navigate = useNavigate();
  const hero = primaryCharacter(draft);
  const worldId = (draft.worldId ?? "dinosaurs") as WorldId;
  const world = WORLD_BY_ID[worldId];
  const coverSrc = draft.preview?.coverImageDataUrl || WORLD_COVER_ART[worldId];
  const bookTitle = draft.preview?.title || world.bookTitle(hero.name || t.common.fallbackHeroName);

  const [step, setStep] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [progress, setProgress] = useState<string | null>(null);
  // The spreads drawn so far, in page order. Real pictures beat a spinner: the book takes
  // minutes, and each finished illustration appearing here is the proof something is happening.
  const [pages, setPages] = useState<{ spread: number; url: string }[]>([]);

  useEffect(() => {
    const timer = window.setInterval(() => {
      setStep((s) => Math.min(s + 1, t.journey.generating.stages.length - 1));
    }, 8000);
    return () => window.clearInterval(timer);
  }, []);

  useEffect(() => {
    const orderId = draft.orderId;
    if (!orderId) {
      setError("შეკვეთა ვერ მოიძებნა.");
      return;
    }

    let cancelled = false;

    void (async () => {
      try {
        // Returning from Stripe: reconcile payment before polling readiness.
        try {
          await ordersApi.confirmOrder(orderId);
        } catch {
          /* confirm is best-effort when webhook already ran */
        }

        const status = await ordersApi.pollOrderUntilReady(orderId, (current) => {
          if (cancelled) return;
          setProgress(current.progressMessage ?? null);
          if (current.bookId) onChange({ bookId: current.bookId });
        });

        if (cancelled) return;
        onChange({ bookId: status.bookId ?? draft.bookId });
        if (status.bookId) {
          void navigate({ to: "/reader/$bookId", params: { bookId: status.bookId } });
        } else {
          void navigate({ to: "/dashboard" });
        }
      } catch (err) {
        if (cancelled) return;
        setError(
          err instanceof ApiError ? err.message : err instanceof Error ? err.message : "შეცდომა",
        );
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

  return (
    <section
      className="journey-stage generating-stage ux-generating"
      aria-label={t.journey.generating.ariaLabel(hero.name || t.common.fallbackHeroName)}
    >
      <div className="generation-portal">
        <div className="generation-atelier">
          <div className="generation-atelier-topline">
            <span>
              <Sparkles aria-hidden="true" /> ADVENTRYA BOOK ATELIER
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
            <span className="ux-cover-brand">ADVENTRYA</span>
            <small>{world.theme}</small>
            <h2>{bookTitle}</h2>
          </article>
          <div className="generation-ring ring-one" aria-hidden="true" />
          <div className="generation-ring ring-two" aria-hidden="true" />
        </div>

        {pages.length > 0 ? (
          <div className="generation-making-of" aria-label="დახატული გვერდები">
            {pages.map((page) => (
              <img
                key={page.spread}
                src={page.url}
                alt={`გვერდი ${page.spread}`}
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
          {hero.name || t.common.fallbackHeroName}
          {t.journey.generating.titleSuffix}
        </h1>
        <p>
          {t.journey.generating.companionPrefix}
          {hero.name || t.common.fallbackHeroName}
          {t.journey.generating.companionSuffix}
        </p>
        <p>{t.journey.generating.leaveNote}</p>
        <div className="soft-time">
          <span>
            <i />
          </span>
          {t.journey.generating.softTime}
        </div>
        <ul className="preview-loader-stages" style={{ marginTop: 18 }}>
          {t.journey.generating.stages.map((label, index) => (
            <li key={label} className={index <= step ? "active" : ""}>
              {label}
            </li>
          ))}
        </ul>
        {progress ? <p>{progress}</p> : null}
        {error ? (
          <div>
            <p className="ux-form-error">{error}</p>
            <button
              className="button journey-primary"
              type="button"
              onClick={() => void navigate({ to: "/dashboard" })}
            >
              Dashboard
            </button>
          </div>
        ) : null}
      </div>
    </section>
  );
}
