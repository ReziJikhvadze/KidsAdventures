import { Check, Sparkles } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";

import { StorybookVolume } from "@/components/adventrya/storybook/StorybookVolume";
import * as adventurePacksApi from "@/lib/api/adventure-packs";
import { storeGuestPreviewIds } from "@/lib/api/auth";
import { ApiError, resolveApiUrl } from "@/lib/api/client";
import { THEME_ID_TO_API, type MasterStoryRunStatus } from "@/lib/api/types";
import { dataUrlToFile } from "@/lib/api/utils";
import { formatGel, useLocale, useT } from "@/lib/i18n";
import {
  ageFromBirthDate,
  primaryCharacter,
  useJourneyDraft,
  type JourneyDraft,
} from "@/lib/journey/draft";
import { type BookPackage, PRICES } from "@/lib/pricing";
import {
  clearPendingRun,
  heroKeyOf,
  readPendingRun,
  resumablePendingRun,
  savedCharacterIdOf,
  writePendingRun,
} from "@/lib/journey/pendingRun";
import { patchJourneyResume, writeJourneyResume } from "@/lib/journey/resume";
import { hasRenderedPreview, readyPreviewPatch } from "@/lib/journey/previewRecovery";
import { useWorldById, WORLD_SCENE_ART, type WorldId } from "@/lib/worlds";

/*
  The preview volume's page list, which is empty and must keep the same identity between renders.

  `pages` is required, and `buildLeaves` is memoised on it — a fresh `[]` on every render would
  rebuild the leaves and reset the volume each time this stage re-rendered, which it does on
  every package click in the panel beside it.
*/
const NO_PAGES: never[] = [];

type Props = {
  draft: JourneyDraft;
  onChange: (patch: Partial<JourneyDraft> | ((prev: JourneyDraft) => JourneyDraft)) => void;
  onContinue: () => void;
};

// One book is one vision call + one whole-book call + a cover image, so the paid start must
// happen once — but "once" guarded by a flag wedged the page: when the effect that set the
// flag was torn down before its polling began (dev double-mounting, a dependency changing
// mid-flight), the next run bailed on the flag and nobody was left polling. The server had
// still been asked — the request is not cancellable and is already billed — so a whole book
// was generated with nothing on screen waiting for it. Sharing the in-flight promise keeps
// the guarantee while letting every later effect run await the same result and carry on.
// Module scope on purpose: it must survive a remount.
let inflightStart: Promise<string> | null = null;

/**
 * Discards the stored run, and the shared start that belongs to it.
 *
 * The module-level promise above is the once-guarantee for one particular run; keeping it past
 * the id would hand the next book the previous book's runId.
 */
function clearPendingRunId(): void {
  inflightStart = null;
  clearPendingRun();
}

export function PreviewStage({ draft, onChange, onContinue }: Props) {
  const WORLD_BY_ID = useWorldById();
  const t = useT();
  // The story is written in the language the site is being read in; there is no separate
  // book-language choice to carry through the journey.
  const { locale } = useLocale();
  const hero = primaryCharacter(draft);
  // Fourth slot: false until the URL has been read, see JourneyDraftProvider.
  const hydrated = useJourneyDraft()[3];
  const worldId = (draft.worldId ?? "dinosaurs") as WorldId;
  const world = WORLD_BY_ID[worldId];
  const [loading, setLoading] = useState(!draft.preview);
  const [loaderStep, setLoaderStep] = useState(0);
  const [progressMessage, setProgressMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  // Bumped to ask for another attempt. The draft lives in memory above the routes and is
  // deliberately never restored from storage, so reloading the page to retry threw away the
  // child's name, birth date, photo and everything else the parent had just typed.
  const [retryToken, setRetryToken] = useState(0);
  // When the story finished, so the wait for its cover can be bounded from that moment rather
  // than from the start of a generation that takes minutes on its own.
  const readyAt = useRef(Infinity);

  useEffect(() => {
    if (draft.preview) {
      setLoading(false);
      return;
    }

    // The draft is filled from the URL in a parent effect, and React runs child effects
    // first — so on the very first pass the world chosen on /themes has not arrived yet.
    // Judging it here produced "choose a world first" for a world that had been chosen,
    // which cleared only by leaving and coming back.
    if (!hydrated) return;

    /*
      A book already being written outranks every question about which book it would be.

      The world gate below is right for a book that has not started: writing one without a theme
      produced a book about the wrong subject. It was wrong for a book already in progress. The
      draft is deliberately never restored from storage, so a parent who left this screen and came
      back after a real page load arrived with an empty draft — and was told to choose a world for
      a story the server had already finished writing, with the run id sitting in localStorage the
      whole time. The theme was settled when the run started; polling needs nothing but its id.

      "A book" and not "any book", though. The stored run carries the world and the child it was
      started for, so a run left behind by another book is dropped rather than shown as this one.
      A blank world or a hero the draft does not know yet is not a contradiction — that is exactly
      the empty draft of a fresh page load — so only a world or a hero that disagrees rejects it.
    */
    const pending = readPendingRun();
    /* The rule lives in `pendingRun.ts` now: the questions ask the same thing, to know whether a
       book is already being written before offering to start another. */
    const resumable = resumablePendingRun(hero, draft.worldId);
    if (pending && !resumable) clearPendingRunId();

    // A saved hero named in the URL is still on its way from the account. Starting now would
    // write — and bill — a book for "შენი გმირი", aged five, with no photograph, and then adopt
    // that story into the paid book. Wait; the hero's arrival re-runs this effect. A run already
    // in progress is exempt: there is nothing left to start, only a story to collect.
    if (!resumable && draft.characters.some((c) => c.serverId && !c.name.trim())) return;

    // Once the draft is known, a missing world is real: there is no story to write.
    // Generating anyway produced a book about the wrong subject, which costs a real
    // generation and reads as a bug — so stop and send them back to choose.
    const apiTheme = draft.worldId ? THEME_ID_TO_API[draft.worldId] : undefined;
    if (!apiTheme && !resumable) {
      setLoading(false);
      setError(t.journey.preview.chooseWorldFirst);
      return;
    }

    setError(null);
    // Re-armed on every start. An earlier pass that found no world had switched the loader off,
    // and when the world arrived a moment later the stage showed the finished layout — stock art
    // and a working "continue" button — over a book that was only now being written.
    setLoading(true);

    let cancelled = false;
    let pollTimer = 0;
    // Slowly. A book takes minutes, and a bar that reached its last step in under two used to
    // sit full for the rest of the wait, which reads as "stuck" rather than "working".
    const stageTimer = window.setInterval(() => {
      setLoaderStep((s) => Math.min(s + 1, t.journey.previewLoader.stages.length - 1));
    }, 45000);

    // The cover is painted after the story is finished, so a book can be ready to read while
    // its picture is still a minute away. Rather than hold the whole preview back for it, the
    // story is shown against the world's own artwork and the cover is collected afterwards.
    const waitForCover = (runId: string, attemptsLeft: number) => {
      if (attemptsLeft <= 0) return;
      pollTimer = window.setTimeout(async () => {
        if (cancelled) return;
        try {
          const status = await adventurePacksApi.getGuestPreviewStatus(runId);
          if (cancelled) return;
          if (status.coverImageUrl) {
            onChange((prev) =>
              prev.preview
                ? {
                    ...prev,
                    preview: {
                      ...prev.preview,
                      // Resolved against the API origin: the path renders against the page's
                      // origin otherwise, which is the wrong host whenever the SPA and the
                      // API are served separately.
                      coverImageDataUrl: resolveApiUrl(status.coverImageUrl!),
                    },
                  }
                : prev,
            );
            return;
          }
          waitForCover(runId, attemptsLeft - 1);
        } catch {
          waitForCover(runId, attemptsLeft - 1);
        }
      }, 5000);
    };

    const finish = (status: MasterStoryRunStatus) => {
      const restored = readyPreviewPatch(
        draft,
        {
          ...status,
          coverImageUrl: status.coverImageUrl ? resolveApiUrl(status.coverImageUrl) : "",
          introImageUrl: status.introImageUrl ? resolveApiUrl(status.introImageUrl) : undefined,
        },
        {
          worldId: resumable?.worldId,
          characterId: resumable ? savedCharacterIdOf(resumable) : undefined,
        },
      );

      storeGuestPreviewIds(status.runId, status.runId);
      clearPendingRunId();
      onChange(restored);
      setLoading(false);

      // The pointer an emailed sign-in link needs to find this book again in a new tab. No
      // child data: the run row holds it, and the signed-in parent reads it back.
      writeJourneyResume({
        runId: status.runId,
        worldId: restored.worldId,
        bookPackage: draft.bookPackage,
        characterId: restored.characters.find((child) => child.isPrimary)?.serverId,
        storyNotes: draft.storyNotes || undefined,
      });

      // Only reached when the grace period ran out, so the cover is late rather than coming.
      // Keep looking a little longer: it can still arrive while they read page one.
      if (!status.coverImageUrl) waitForCover(status.runId, 12);
    };

    // Books take minutes, so a poll every four seconds costs almost nothing and still feels
    // immediate at the moment one finishes.
    //
    // Bounded, because a job whose process died between being queued and being run leaves a row
    // that says "Writing" and never changes. Polling that forever is a spinner with nothing
    // behind it; better to say so and offer another attempt, which no longer costs the parent
    // their answers.
    const startedAt = Date.now();
    const giveUpAfterMs = 15 * 60 * 1000;
    // How long the finished story waits for its cover before being shown without one.
    const coverGraceMs = 90 * 1000;

    const poll = (runId: string) => {
      pollTimer = window.setTimeout(async () => {
        if (cancelled) return;

        if (Date.now() - startedAt > giveUpAfterMs) {
          clearPendingRunId();
          setError(t.journey.preview.tookTooLong);
          setLoading(false);
          return;
        }

        try {
          const status = await adventurePacksApi.getGuestPreviewStatus(runId);
          if (cancelled) return;

          if (status.status === "Failed") {
            clearPendingRunId();
            setError(t.journey.preview.failed);
            setLoading(false);
            return;
          }

          if (status.status === "Ready") {
            // Wait for the cover before showing anything.
            //
            // Marking a book ready as soon as its text exists saves a minute in the reader, where
            // the story is the thing. On the preview it is the wrong trade: the cover is what a
            // parent is deciding about, and revealing the book against stock world art means the
            // first thing they see of their child's book is not their child. They noticed
            // immediately — the real cover turned up one screen later.
            //
            // Bounded, so a cover that never arrives costs a minute and a half rather than the
            // whole preview.
            if (status.coverImageUrl || Date.now() - readyAt.current > coverGraceMs) {
              finish(status);
              return;
            }

            readyAt.current = readyAt.current === Infinity ? Date.now() : readyAt.current;
            setProgressMessage(t.journey.previewLoader.paintingCover);
            poll(runId);
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
            setError(t.journey.preview.expired);
            setLoading(false);
            return;
          }
          poll(runId);
        }
      }, 1500);
    };

    void (async () => {
      try {
        // A reload used to throw away a book that was already being written and start
        // another. The id is kept so the page comes back to the one in progress — decided
        // above, where the world and the hero it belongs to were checked against this draft.
        if (resumable) {
          poll(resumable.runId);
          return;
        }

        // Only reachable with a theme: the branch above returns for every resumable run, and the
        // gate above that refuses a start without one.
        if (!apiTheme) return;

        // A saved hero's portrait is an object URL for the screen, not bytes to send: the
        // server is told which character and reads its own copy. Only a photo chosen in this
        // session travels as a file.
        const usesStoredPortrait = !!hero.serverId && hero.photoStored;
        const photo =
          !usesStoredPortrait && hero.photoDataUrl?.startsWith("data:")
            ? dataUrlToFile(hero.photoDataUrl, `${hero.name || "hero"}.jpg`)
            : null;

        // ??= is the once-guarantee: whichever effect run gets here first pays; every
        // other run — including one that outlives it — awaits the same promise.
        inflightStart ??= adventurePacksApi
          .startGuestPreview({
            name: hero.name.trim() || t.common.fallbackHeroName,
            reusePreviewId: draft.reusePreviewId,
            age: ageFromBirthDate(hero.birthDate),
            gender: hero.gender ?? undefined,
            eyeColor: hero.eyeColor ?? undefined,
            // Sent alongside the age we worked out, so the server can disagree with us and win.
            birthDate: hero.birthDate || undefined,
            // No silent default. A missing mapping used to fall back to "Dinosaurs", which
            // is how choosing the star path produced a dinosaur book: the theme was wrong
            // before the model ever saw it.
            theme: apiTheme,
            storyLanguage: locale,
            optionalStoryNotes: draft.storyNotes || undefined,
            photo,
            characterId: usesStoredPortrait ? hero.serverId : undefined,
          })
          .then((r) => r.runId);

        const runId = await inflightStart;

        // Written before the cancelled check: the run exists and is billed whether or not
        // this effect survived to see the response, and the id in storage is what lets the
        // next run — or the next page load — pick the book up instead of buying another.
        writePendingRun({ runId, worldId: draft.worldId, heroKey: heroKeyOf(hero) });
        if (cancelled) return;
        poll(runId);
      } catch (err) {
        // A failed start is not "started": drop the shared promise so retry can try again.
        inflightStart = null;
        if (cancelled) return;
        // In the parent's language, whatever the server said. Its refusals on this route are
        // English sentences written for a log, and one of them tells a signed-in parent behind a
        // shared address to "sign in".
        setError(
          err instanceof ApiError && err.status === 429
            ? t.journey.preview.tooBusy
            : t.journey.preview.failed,
        );
        setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
      window.clearInterval(stageTimer);
      window.clearTimeout(pollTimer);
    };
    // Re-evaluated when the draft hydrates, the world changes, or the hero named in the URL
    // finishes loading; the shared `inflightStart` promise is what guarantees the paid call
    // itself still happens only once.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [hydrated, draft.worldId, retryToken, hero.serverId, hero.name, hero.photoStored]);

  /*
    The real cover, or nothing.

    This used to fall back to the world painting here, and then hand that painting to
    `StorybookVolume` as `coverImageUrl` — so the book had no way to tell a cover from a
    stand-in, and its own fallback never ran. What a parent saw was the map's artwork presented
    as their child's cover. Passing null lets the one fallback in the component do the job, and
    `waitForCover` above swaps the real picture in the moment it is painted.
  */
  const coverSrc = draft.preview?.coverImageDataUrl || null;
  const bookTitle = draft.preview?.title || world.bookTitle(hero.name || t.common.fallbackHeroName);

  /*
    The endpaper, the dedication plate and the written first page that used to be assembled here
    are gone with the paging that reached them — see the note on the volume below. The strings
    they were built from (`preview.dedicationOwner` and the rest) are still in the catalogue, and
    `heroDemoPages` still builds the same front matter for the sample on the home page, so
    putting a readable page back on this screen is a matter of handing the component pages again
    rather than of writing any of it a second time.
  */

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
        </header>

        <div
          className="ux-preview-loader"
          aria-live="polite"
          aria-busy="true"
          aria-label={t.journey.previewLoader.ariaLabel(heroName)}
        >
          <div className="preview-atelier-book">
            {/*
              The child's own cover, as soon as there is one.

              The server saves the cover while the parent is still on this screen — the story is
              marked ready before the picture is painted, and the poll above collects it — so for
              the last stretch of the wait the real thing exists and was not being shown. The
              world's painting stands in until then, which is the honest answer to "we have not
              drawn it yet", and the swap is the moment the wait starts being worth it.
            */}
            <div
              className="preview-atelier-art"
              style={{
                backgroundImage: `url("${draft.preview?.coverImageDataUrl || WORLD_SCENE_ART[worldId]}")`,
              }}
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
            {/*
              The server says what it is actually doing, so prefer that over the timed
              guess. The stage list stays as the fallback for the seconds before the first
              poll comes back.
            */}
            <strong>{progressMessage || t.journey.previewLoader.stages[loaderStep]}</strong>
            {/* Out of however many stages there are, rather than out of the five there used to
                be — a hard 20% a step filled the bar to 60% and stopped. */}
            <div className="preview-loader-progress" aria-hidden="true">
              <i
                style={{
                  width: `${((loaderStep + 1) * 100) / t.journey.previewLoader.stages.length}%`,
                }}
              />
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

            {/*
              The way off this screen is the arrow in the header, and only that.

              A panel here said the same thing in a paragraph and a second button: the story is
              being written, you may wait or go back, nothing is lost. It was two more things to
              read on a screen whose whole job is to be waited on, and the button did exactly
              what the arrow above it already does — leave for the questions, keeping the run id
              so coming back rejoins this book rather than buying another.
            */}
          </div>
        </div>
      </section>
    );
  }

  if (error && !draft.preview) {
    return (
      <section className="ux-preview-stage">
        <header className="ux-stage-heading ux-preview-heading">
          <p className="eyebrow">{t.journey.preview.eyebrow}</p>
          <h1>{t.journey.preview.failedTitle}</h1>
          <p className="ux-form-error">{error}</p>
          <button
            className="button journey-primary"
            type="button"
            onClick={() => {
              // clearPendingRunId also drops the shared in-flight start, so this really
              // does buy a fresh book rather than resuming the failed one.
              clearPendingRunId();
              setError(null);
              setProgressMessage(null);
              setLoaderStep(0);
              setLoading(true);
              setRetryToken((token) => token + 1);
            }}
          >
            {t.journey.preview.tryAgain}
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
      </header>

      <div className="ux-preview-layout">
        <div className="ux-preview-product">
          {/* The preview shows only the cover. Its saved typography must not be overlaid again. */}
          {hasRenderedPreview(draft.preview) ? (
            <img
              src={coverSrc || undefined}
              alt={bookTitle}
              style={{
                width: "min(100%, 380px)",
                height: "auto",
                aspectRatio: "1.1",
                objectFit: "contain",
                display: "block",
                margin: "0 auto",
              }}
            />
          ) : (
            <StorybookVolume
              variant="preview"
              className={`storybook storybook-preview theme-${worldId}`}
              heroName={hero.name.trim() || t.common.fallbackHeroName}
              title={bookTitle}
              coverImageUrl={coverSrc}
              worldId={worldId}
              pages={NO_PAGES}
              isUnlocked={false}
              interactive={false}
            />
          )}

          {/*
            The world painting used to sit here, on the reasoning that a cover alone is not a
            book. It is gone: the parent chose that painting one screen earlier and has just
            been shown the cover their own child is on, so a stock illustration between the two
            adds length rather than anything to decide on.
          */}

          {/* The line that said the cover and first page are free went with the one in the
              heading above it: the same promise, twice on one screen, over a sample that is
              plainly a sample. */}
          {draft.storyNotes.trim() ? (
            <p className="ux-preview-book-note">{t.journey.preview.wishAcknowledged}</p>
          ) : null}
        </div>

        <PackagePanel
          selected={draft.bookPackage}
          onSelect={(bookPackage) => {
            onChange({ bookPackage });
            patchJourneyResume({ bookPackage });
          }}
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

      {/*
        The printed book first, because it is the product: a parent who came here to have a book
        made should meet the book, and the digital copy is the cheaper alternative to it rather
        than the default it was standing in as.
      */}
      <PackageOption
        id="print"
        title={t.journey.packages.print.title}
        price={formatGel(PRICES.print)}
        features={t.journey.packages.print.features}
        badge={t.journey.packages.print.badge}
        selected={selected === "print"}
        onSelect={() => onSelect("print")}
      />
      <PackageOption
        id="digital"
        title={t.journey.packages.digital.title}
        price={formatGel(PRICES.digital)}
        features={t.journey.packages.digital.features}
        selected={selected === "digital"}
        onSelect={() => onSelect("digital")}
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
