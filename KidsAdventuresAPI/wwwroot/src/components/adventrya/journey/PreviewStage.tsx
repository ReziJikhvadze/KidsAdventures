import { Check, Lock, Sparkles } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";

import { StorybookVolume } from "@/components/adventrya/storybook/StorybookVolume";
import * as adventurePacksApi from "@/lib/api/adventure-packs";
import { storeGuestPreviewIds } from "@/lib/api/auth";
import { ApiError } from "@/lib/api/client";
import { THEME_ID_TO_API, type MasterStoryRunStatus, type StoryPageContent } from "@/lib/api/types";
import { dataUrlToFile } from "@/lib/api/utils";
import { formatGel, useT } from "@/lib/i18n";
import {
  ageFromBirthDate,
  primaryCharacter,
  useJourneyDraft,
  type JourneyDraft,
  type PreviewTeaser,
} from "@/lib/journey/draft";
import { type BookPackage, PRICES } from "@/lib/pricing";
import { SESSION_KEYS } from "@/lib/storage/session";
import { useWorldById, WORLD_COVER_ART, type WorldId } from "@/lib/worlds";

type Props = {
  draft: JourneyDraft;
  onChange: (patch: Partial<JourneyDraft> | ((prev: JourneyDraft) => JourneyDraft)) => void;
  onContinue: () => void;
};

// Storage can throw — Safari in private mode is the usual culprit — and a book that is already
// being written should not be lost to a failed write of its own id.
function readPendingRunId(): string | null {
  try {
    return localStorage.getItem(SESSION_KEYS.pendingBookRunId);
  } catch {
    return null;
  }
}

function writePendingRunId(runId: string): void {
  try {
    localStorage.setItem(SESSION_KEYS.pendingBookRunId, runId);
  } catch {
    /* ignore */
  }
}

function clearPendingRunId(): void {
  try {
    localStorage.removeItem(SESSION_KEYS.pendingBookRunId);
  } catch {
    /* ignore */
  }
}

export function PreviewStage({ draft, onChange, onContinue }: Props) {
  const WORLD_BY_ID = useWorldById();
  const t = useT();
  const hero = primaryCharacter(draft);
  // Fourth slot: false until the URL has been read, see JourneyDraftProvider.
  const hydrated = useJourneyDraft()[3];
  const worldId = (draft.worldId ?? "dinosaurs") as WorldId;
  const world = WORLD_BY_ID[worldId];
  const [loading, setLoading] = useState(!draft.preview);
  const [loaderStep, setLoaderStep] = useState(0);
  const [progressMessage, setProgressMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  // One book is one vision call + one whole-book call + a cover image. A remounted
  // or re-run effect must not buy a second one, and `cancelled` alone only drops
  // the result — the request is already in flight and already billed.
  const requestedRef = useRef(false);

  useEffect(() => {
    if (draft.preview) {
      setLoading(false);
      return;
    }
    if (requestedRef.current) return;

    // The draft is filled from the URL in a parent effect, and React runs child effects
    // first — so on the very first pass the world chosen on /themes has not arrived yet.
    // Judging it here produced "choose a world first" for a world that had been chosen,
    // which cleared only by leaving and coming back.
    if (!hydrated) return;

    // Once the draft is known, a missing world is real: there is no story to write.
    // Generating anyway produced a book about the wrong subject, which costs a real
    // generation and reads as a bug — so stop and send them back to choose.
    const apiTheme = draft.worldId ? THEME_ID_TO_API[draft.worldId] : undefined;
    if (!apiTheme) {
      setLoading(false);
      setError(t.journey.preview.chooseWorldFirst);
      return;
    }

    setError(null);
    requestedRef.current = true;

    let cancelled = false;
    let pollTimer = 0;
    const stageTimer = window.setInterval(() => {
      setLoaderStep((s) => Math.min(s + 1, t.journey.previewLoader.stages.length - 1));
    }, 20000);

    const finish = (status: MasterStoryRunStatus) => {
      const teaser: PreviewTeaser = {
        guestPreviewId: status.runId,
        storyId: status.runId,
        title: status.title || "",
        firstPageTitle: status.firstPageTitle || "",
        firstPageText: status.firstPageText || "",
        coverImageDataUrl: status.coverImageUrl || "",
        storyJson: status.storyJson || undefined,
        pageCount: status.pageCount,
      };

      storeGuestPreviewIds(status.runId, status.runId);
      clearPendingRunId();
      onChange({ preview: teaser });
      setLoading(false);
    };

    // Books take minutes, so a poll every four seconds costs almost nothing and still feels
    // immediate at the moment one finishes.
    const poll = (runId: string) => {
      pollTimer = window.setTimeout(async () => {
        if (cancelled) return;
        try {
          const status = await adventurePacksApi.getGuestPreviewStatus(runId);
          if (cancelled) return;

          if (status.status === "Failed") {
            clearPendingRunId();
            setError(status.errorMessage || "წიგნი ვერ დაიწერა. სცადე თავიდან.");
            setLoading(false);
            return;
          }

          if (status.status === "Ready") {
            finish(status);
            return;
          }

          if (status.progressMessage) setProgressMessage(status.progressMessage);
          poll(runId);
        } catch (err) {
          if (cancelled) return;
          // A run that no longer exists is gone for good — its row has expired. Anything
          // else is worth another poll rather than throwing away a book that is still
          // being written.
          if (err instanceof ApiError && err.status === 404) {
            clearPendingRunId();
            setError("შენი ზღაპარი ვადაგასულია. შექმენი ახალი.");
            setLoading(false);
            return;
          }
          poll(runId);
        }
      }, 4000);
    };

    void (async () => {
      try {
        // A reload used to throw away a book that was already being written and start
        // another. The id is kept so the page comes back to the one in progress.
        const pending = readPendingRunId();
        if (pending) {
          poll(pending);
          return;
        }

        const photo = hero.photoDataUrl
          ? dataUrlToFile(hero.photoDataUrl, `${hero.name || "hero"}.jpg`)
          : null;

        const { runId } = await adventurePacksApi.startGuestPreview({
          name: hero.name.trim() || t.common.fallbackHeroName,
          age: ageFromBirthDate(hero.birthDate),
          gender: hero.gender ?? undefined,
          eyeColor: hero.eyeColor ?? undefined,
          // No silent default. A missing mapping used to fall back to "Dinosaurs", which
          // is how choosing the star path produced a dinosaur book: the theme was wrong
          // before the model ever saw it.
          theme: apiTheme,
          storyLanguage: draft.bookLanguage,
          optionalStoryNotes: draft.storyNotes || undefined,
          photo,
        });

        if (cancelled) return;
        writePendingRunId(runId);
        poll(runId);
      } catch (err) {
        if (cancelled) return;
        setError(err instanceof ApiError ? err.message : "Preview ვერ შეიქმნა. სცადე თავიდან.");
        setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
      window.clearInterval(stageTimer);
      window.clearTimeout(pollTimer);
    };
    // Re-evaluated when the draft hydrates or the world changes; `requestedRef` is what
    // guarantees the paid call itself still happens only once.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [hydrated, draft.worldId]);

  const coverSrc = draft.preview?.coverImageDataUrl || WORLD_COVER_ART[worldId];
  const bookTitle = draft.preview?.title || world.bookTitle(hero.name || t.common.fallbackHeroName);

  const previewPages: StoryPageContent[] = useMemo(() => {
    return [
      {
        title: draft.preview?.firstPageTitle || world.teaserTitle,
        caption: draft.preview?.firstPageTitle || t.journey.preview.freeFirstPage,
        content: draft.preview?.firstPageText || world.teaserBody,
        // The cover is already shown as the cover. Repeating it here printed the first page's
        // words across the picture, which is the one thing giving the text its own page was
        // supposed to prevent.
        isTextOnlyPage: true,
      },
    ];
  }, [
    draft.preview?.firstPageText,
    draft.preview?.firstPageTitle,
    world.teaserBody,
    world.teaserTitle,
  ]);

  if (loading) {
    const heroName = hero.name || t.common.fallbackHeroName;
    return (
      <section className="ux-preview-stage ux-preview-loading-stage">
        <header className="ux-stage-heading ux-preview-heading">
          <p className="eyebrow">
            <Sparkles aria-hidden="true" />
            {t.journey.previewLoader.heading}
          </p>
          <h1>
            {heroName}
            {t.journey.previewLoader.subheading}
          </h1>
          <p>{t.journey.previewLoader.reassurance}</p>
        </header>

        <div
          className="ux-preview-loader"
          aria-live="polite"
          aria-busy="true"
          aria-label={t.journey.previewLoader.ariaLabel(heroName)}
        >
          <div className="preview-atelier-book">
            <div
              className="preview-atelier-art"
              style={{ backgroundImage: `url("${WORLD_COVER_ART[worldId]}")` }}
            />
            <div className="preview-atelier-cover-lines" aria-hidden="true" />
            <span className="preview-atelier-spine" aria-hidden="true" />
            <div className="preview-atelier-page" aria-hidden="true">
              <i />
              <i />
              <i />
            </div>
            <div className="preview-atelier-sparkles" aria-hidden="true">
              {Array.from({ length: 6 }, (_, i) => (
                <i key={i} />
              ))}
            </div>
          </div>

          <div className="preview-loader-copy">
            <small>{t.journey.previewLoader.atelier}</small>
            {/*
              The server says what it is actually doing, so prefer that over the timed
              guess. The stage list stays as the fallback for the seconds before the first
              poll comes back.
            */}
            <strong>{progressMessage || t.journey.previewLoader.stages[loaderStep]}</strong>
            <div className="preview-loader-progress" aria-hidden="true">
              <i style={{ width: `${(loaderStep + 1) * 20}%` }} />
            </div>
            <div className="preview-loader-stages">
              {t.journey.previewLoader.stages.map((label, index) => (
                <span
                  key={label}
                  className={index < loaderStep ? "done" : index === loaderStep ? "active" : ""}
                >
                  {index < loaderStep ? (
                    <Check aria-hidden="true" />
                  ) : (
                    <Sparkles aria-hidden="true" />
                  )}
                  {label}
                </span>
              ))}
            </div>
          </div>
        </div>
      </section>
    );
  }

  if (error && !draft.preview) {
    return (
      <section className="ux-preview-stage">
        <header className="ux-stage-heading ux-preview-heading">
          <p className="eyebrow">Preview</p>
          <h1>Preview ვერ მზადაა</h1>
          <p className="ux-form-error">{error}</p>
          <button
            className="button journey-primary"
            type="button"
            onClick={() => {
              onChange({ preview: null });
              window.location.hash = "preview";
              window.location.reload();
            }}
          >
            თავიდან ცდა
          </button>
        </header>
      </section>
    );
  }

  return (
    <section className="ux-preview-stage">
      <header className="ux-stage-heading ux-preview-heading">
        <p className="eyebrow">
          <Sparkles aria-hidden="true" /> {t.journey.preview.eyebrow}
        </p>
        <h1>
          {t.journey.preview.titlePrefix}
          {hero.name || t.common.fallbackHeroName}
          {t.journey.preview.titleSuffix}
        </h1>
        <p>{t.journey.preview.lead}</p>
      </header>

      <div className="ux-preview-layout">
        <div className="ux-preview-product">
          <StorybookVolume
            variant="preview"
            className={`storybook storybook-preview theme-${worldId}`}
            heroName={hero.name.trim() || t.common.fallbackHeroName}
            title={bookTitle}
            coverImageUrl={coverSrc}
            worldId={worldId}
            pages={previewPages}
            isSpreadBook
            lockedPageCount={6}
            isUnlocked={false}
            interactive
            initialIndex={0}
          />

          <p className="ux-preview-book-note">
            <Lock aria-hidden="true" /> {t.journey.preview.bookNote}
          </p>
          {draft.storyNotes.trim() ? (
            <p className="ux-preview-book-note">{t.journey.preview.wishAcknowledged}</p>
          ) : null}
        </div>

        <PackagePanel
          selected={draft.bookPackage}
          onSelect={(bookPackage) => onChange({ bookPackage })}
          onContinue={onContinue}
        />
      </div>
    </section>
  );
}

function PackagePanel({
  selected,
  onSelect,
  onContinue,
}: {
  selected: BookPackage;
  onSelect: (pkg: BookPackage) => void;
  onContinue: () => void;
}) {
  const t = useT();
  const total = useMemo(() => (selected === "print" ? PRICES.print : PRICES.digital), [selected]);

  return (
    <aside className="ux-package-panel">
      <div className="ux-package-heading">
        <small>{t.journey.preview.packageHeading}</small>
        <h2>{t.journey.preview.packageQuestion}</h2>
      </div>

      <PackageOption
        id="digital"
        title={t.journey.packages.digital.title}
        price={formatGel(PRICES.digital)}
        features={t.journey.packages.digital.features}
        selected={selected === "digital"}
        onSelect={() => onSelect("digital")}
      />
      <PackageOption
        id="print"
        title={t.journey.packages.print.title}
        price={formatGel(PRICES.print)}
        features={t.journey.packages.print.features}
        badge={t.journey.packages.print.badge}
        selected={selected === "print"}
        onSelect={() => onSelect("print")}
      />

      <div className="ux-preview-total">
        <span>{t.journey.preview.selectedPackage}</span>
        <strong>
          {selected === "print" ? t.journey.packages.print.title : t.journey.packages.digital.title}
        </strong>
        <b>{formatGel(total)}</b>
      </div>

      <button
        className="button journey-primary ux-preview-continue"
        type="button"
        onClick={onContinue}
      >
        {t.journey.preview.continue}
        {formatGel(total)}
      </button>
    </aside>
  );
}

function PackageOption({
  title,
  price,
  features,
  badge,
  selected,
  onSelect,
}: {
  id: string;
  title: string;
  price: string;
  features: readonly string[];
  badge?: string;
  selected: boolean;
  onSelect: () => void;
}) {
  return (
    <button
      className={`ux-package-option ${selected ? "selected" : ""}`}
      type="button"
      aria-pressed={selected}
      onClick={onSelect}
    >
      {badge ? <span className="ux-package-badge">{badge}</span> : null}
      <span className="ux-package-radio">{selected ? <Check aria-hidden="true" /> : null}</span>
      <span className="ux-package-title">
        <strong>{title}</strong>
        <b>{price}</b>
      </span>
      <span className="ux-package-list">
        {features.map((feature) => (
          <span key={feature}>
            <Check aria-hidden="true" />
            {feature}
          </span>
        ))}
      </span>
    </button>
  );
}
