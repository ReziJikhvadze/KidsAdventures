import { Check, Sparkles } from "lucide-react";
import { useEffect, useMemo, useState } from "react";

import { PasswordlessAuthPanel } from "@/components/auth/PasswordlessAuthPanel";
import { StorybookVolume } from "@/components/adventrya/storybook/StorybookVolume";
import type { StoryPageContent } from "@/lib/api/types";
import { useAuth } from "@/lib/auth/AuthContext";
import { useT } from "@/lib/i18n";
import { primaryCharacter, type JourneyDraft } from "@/lib/journey/draft";
import { heroDemoPages } from "@/lib/story/heroDemoPages";
import { useWorldById, WORLD_COVER_ART, type WorldId } from "@/lib/worlds";

type Props = {
  draft: JourneyDraft;
  onAuthenticated: () => void;
};

const RETURN_PATH = "/create#checkout";

export function AuthStage({ draft, onAuthenticated }: Props) {
  const WORLD_BY_ID = useWorldById();
  const t = useT();
  const { isAuthenticated, isLoading, login } = useAuth();
  // Dev skip state. Only ever read inside the import.meta.env.DEV block below.
  const [devSkipBusy, setDevSkipBusy] = useState(false);
  const [devSkipError, setDevSkipError] = useState<string | null>(null);
  const hero = primaryCharacter(draft);
  const worldId = (draft.worldId ?? "dinosaurs") as WorldId;
  const world = WORLD_BY_ID[worldId];
  const coverSrc = draft.preview?.coverImageDataUrl || WORLD_COVER_ART[worldId];
  const heroName = hero.name || t.common.fallbackHeroName;
  const bookTitle = draft.preview?.title || world.bookTitle(heroName);

  const displayPages = useMemo((): StoryPageContent[] => {
    // The teaser's own first page, which the preview already holds. This used to dig it out of
    // the serialised book by looking for a "pages" key — the book serialises "storyPages", so the
    // lookup always missed and every parent signing up read the same demo paragraph instead of
    // the story written for their child.
    if (draft.preview?.firstPageText) {
      return [
        {
          title: draft.preview.firstPageTitle || "",
          content: draft.preview.firstPageText,
          isTextOnlyPage: true,
        },
      ];
    }
    return heroDemoPages(heroName, worldId).slice(0, 1);
  }, [draft.preview?.firstPageText, draft.preview?.firstPageTitle, heroName, worldId]);

  useEffect(() => {
    if (!isLoading && isAuthenticated) onAuthenticated();
  }, [isAuthenticated, isLoading, onAuthenticated]);

  if (isLoading || isAuthenticated) {
    return (
      <section className="journey-stage auth-stage ux-auth-stage">
        <p>{t.common.actions.checking}</p>
      </section>
    );
  }

  return (
    <section className="journey-stage auth-stage ux-auth-stage">
      <div className="ux-auth-book">
        <StorybookVolume
          variant="display"
          className={`storybook storybook-display theme-${worldId}`}
          heroName={heroName}
          title={bookTitle}
          coverImageUrl={coverSrc}
          worldId={worldId}
          pages={displayPages}
          lockedPageCount={0}
          isUnlocked={false}
          // The parent is looking at their own book here; freezing it made these two
          // screens the only places it could not be turned.
          interactive
          initialIndex={0}
        />
        <p>
          <Check aria-hidden="true" /> {t.journey.auth.previewSaved}
        </p>
      </div>

      <PasswordlessAuthPanel
        returnPath={RETURN_PATH}
        onAuthenticated={onAuthenticated}
        header={
          <>
            <p className="eyebrow">
              <Sparkles aria-hidden="true" /> {t.journey.auth.eyebrow}
            </p>
            <h1>
              {t.journey.auth.titlePrefix}
              {heroName}
            </h1>
            <p>{t.journey.auth.lead}</p>
          </>
        }
      />

      {import.meta.env.DEV && (
        // Local development only — vite dev sets DEV; production builds eliminate this
        // block entirely. Signs in as the seeded demo account (SeedOptions defaults, created
        // by DatabaseSeeder when Seed:Enabled is on) so the journey can be exercised without
        // real credentials. The isAuthenticated effect above then advances to checkout,
        // where Stripe:BypassPayment — a user-secrets switch, also local-only — completes
        // the order without payment.
        <div className="ux-auth-dev-skip" style={{ marginTop: "1rem", textAlign: "center" }}>
          <button
            type="button"
            className="button"
            disabled={devSkipBusy}
            onClick={async () => {
              setDevSkipBusy(true);
              setDevSkipError(null);
              try {
                await login("demo@adventurepacks.com", "Adventure123!");
                // No navigation here: the effect watching isAuthenticated calls
                // onAuthenticated, the same path a real sign-in takes.
              } catch (err) {
                setDevSkipError(err instanceof Error ? err.message : "Demo login failed.");
                setDevSkipBusy(false);
              }
            }}
          >
            {devSkipBusy ? "Signing in as demo…" : "DEV: Skip — continue as local demo"}
          </button>
          {devSkipError && <p className="ux-form-error">{devSkipError}</p>}
        </div>
      )}
    </section>
  );
}
