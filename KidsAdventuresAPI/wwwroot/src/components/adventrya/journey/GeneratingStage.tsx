import { Sparkles } from "lucide-react";
import { useEffect, useState } from "react";
import { useNavigate } from "@tanstack/react-router";

import { ApiError } from "@/lib/api/client";
import * as ordersApi from "@/lib/api/orders";
import { t } from "@/lib/i18n";
import { primaryCharacter, type JourneyDraft } from "@/lib/journey/draft";
import { WORLD_BY_ID, WORLD_COVER_ART, type WorldId } from "@/lib/worlds";

type Props = {
  draft: JourneyDraft;
  onChange: (patch: Partial<JourneyDraft> | ((prev: JourneyDraft) => JourneyDraft)) => void;
};

export function GeneratingStage({ draft, onChange }: Props) {
  const navigate = useNavigate();
  const hero = primaryCharacter(draft);
  const worldId = (draft.worldId ?? "dinosaurs") as WorldId;
  const world = WORLD_BY_ID[worldId];
  const coverSrc = draft.preview?.coverImageDataUrl || WORLD_COVER_ART[worldId];
  const bookTitle =
    draft.preview?.title || world.bookTitle(hero.name || t.common.fallbackHeroName);

  const [step, setStep] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [progress, setProgress] = useState<string | null>(null);

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
        setError(err instanceof ApiError ? err.message : err instanceof Error ? err.message : "შეცდომა");
      }
    })();

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [draft.orderId]);

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
              style={{ backgroundImage: `url("${coverSrc}")` }}
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
