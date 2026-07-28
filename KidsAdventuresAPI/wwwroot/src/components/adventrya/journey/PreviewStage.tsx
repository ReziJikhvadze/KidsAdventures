import { Check, Lock, Sparkles } from "lucide-react";
import { useEffect, useMemo, useState } from "react";

import { StorybookVolume } from "@/components/adventrya/storybook/StorybookVolume";
import * as adventurePacksApi from "@/lib/api/adventure-packs";
import { storeGuestPreviewIds } from "@/lib/api/auth";
import { ApiError } from "@/lib/api/client";
import { THEME_ID_TO_API, type StoryPageContent } from "@/lib/api/types";
import { dataUrlToFile } from "@/lib/api/utils";
import { formatGel, t } from "@/lib/i18n";
import {
  ageFromBirthDate,
  primaryCharacter,
  type JourneyDraft,
  type PreviewTeaser,
} from "@/lib/journey/draft";
import { type BookPackage, PRICES } from "@/lib/pricing";
import { WORLD_BY_ID, WORLD_COVER_ART, type WorldId } from "@/lib/worlds";

type Props = {
  draft: JourneyDraft;
  onChange: (patch: Partial<JourneyDraft> | ((prev: JourneyDraft) => JourneyDraft)) => void;
  onContinue: () => void;
};

export function PreviewStage({ draft, onChange, onContinue }: Props) {
  const hero = primaryCharacter(draft);
  const worldId = (draft.worldId ?? "dinosaurs") as WorldId;
  const world = WORLD_BY_ID[worldId];
  const [loading, setLoading] = useState(!draft.preview);
  const [loaderStep, setLoaderStep] = useState(0);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (draft.preview) {
      setLoading(false);
      return;
    }

    let cancelled = false;
    const stageTimer = window.setInterval(() => {
      setLoaderStep((s) => Math.min(s + 1, t.journey.previewLoader.stages.length - 1));
    }, 5500);

    void (async () => {
      try {
        const photo = hero.photoDataUrl
          ? dataUrlToFile(hero.photoDataUrl, `${hero.name || "hero"}.jpg`)
          : null;

        const result = await adventurePacksApi.generateGuestPreview({
          name: hero.name.trim() || t.common.fallbackHeroName,
          age: ageFromBirthDate(hero.birthDate),
          theme: THEME_ID_TO_API[worldId] ?? "Dinosaurs",
          storyLanguage: draft.bookLanguage,
          optionalStoryNotes: draft.storyNotes || undefined,
          photo,
        });

        if (cancelled) return;

        const teaser: PreviewTeaser = {
          guestPreviewId: result.guestPreviewId,
          storyId: result.storyId,
          title: result.title,
          firstPageTitle: result.firstPageTitle,
          firstPageText: result.firstPageText,
          coverImageDataUrl: result.coverImageDataUrl,
          storyJson: result.storyJson,
        };

        storeGuestPreviewIds(result.guestPreviewId, result.storyId);
        try {
          localStorage.setItem("ka_guest_preview_used", "1");
        } catch {
          /* ignore */
        }

        onChange({ preview: teaser });
        setLoading(false);
      } catch (err) {
        if (cancelled) return;
        setError(
          err instanceof ApiError
            ? err.message
            : "Preview ვერ შეიქმნა. სცადე თავიდან.",
        );
        setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
      window.clearInterval(stageTimer);
    };
    // Intentionally once per mount when preview is missing.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const coverSrc = draft.preview?.coverImageDataUrl || WORLD_COVER_ART[worldId];
  const bookTitle =
    draft.preview?.title || world.bookTitle(hero.name || t.common.fallbackHeroName);

  const previewPages: StoryPageContent[] = useMemo(() => {
    return [
      {
        title: draft.preview?.firstPageTitle || world.teaserTitle,
        caption: draft.preview?.firstPageTitle || t.journey.preview.freeFirstPage,
        content: draft.preview?.firstPageText || world.teaserBody,
        illustrationUrl: coverSrc,
        isIllustrated: true,
      },
    ];
  }, [
    coverSrc,
    draft.preview?.firstPageText,
    draft.preview?.firstPageTitle,
    world.teaserBody,
    world.teaserTitle,
  ]);

  if (loading) {
    return (
      <section className="ux-preview-loading-stage">
        <div
          className="ux-preview-loader"
          aria-live="polite"
          aria-busy="true"
          aria-label={t.journey.previewLoader.ariaLabel(hero.name || t.common.fallbackHeroName)}
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
            <strong>{t.journey.previewLoader.heading}</strong>
            <p>
              {hero.name || t.common.fallbackHeroName}
              {t.journey.previewLoader.subheading}
            </p>
            <p>{t.journey.previewLoader.reassurance}</p>
            <div className="preview-loader-progress" aria-hidden="true">
              <i style={{ width: `${Math.min(95, (loaderStep + 1) * 18)}%` }} />
            </div>
            <div className="preview-loader-stages">
              {t.journey.previewLoader.stages.map((label, index) => (
                <span
                  key={label}
                  className={
                    index < loaderStep ? "done" : index === loaderStep ? "active" : ""
                  }
                >
                  {index <= loaderStep ? <Check aria-hidden="true" /> : <Sparkles aria-hidden="true" />}
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
  const total = useMemo(
    () => (selected === "print" ? PRICES.print : PRICES.digital),
    [selected],
  );

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
          {selected === "print"
            ? t.journey.packages.print.title
            : t.journey.packages.digital.title}
        </strong>
        <b>{formatGel(total)}</b>
      </div>

      <button className="button journey-primary ux-preview-continue" type="button" onClick={onContinue}>
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
