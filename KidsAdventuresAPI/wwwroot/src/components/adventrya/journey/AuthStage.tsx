import { Check, Sparkles } from "lucide-react";
import { useEffect, useState } from "react";

import { PasswordlessAuthPanel } from "@/components/auth/PasswordlessAuthPanel";
import { StorybookVolume } from "@/components/adventrya/storybook/StorybookVolume";
import { useAuth } from "@/lib/auth/AuthContext";
import { useT } from "@/lib/i18n";
import { primaryCharacter, type JourneyDraft } from "@/lib/journey/draft";
import { useWorldById, type WorldId } from "@/lib/worlds";

type Props = {
  draft: JourneyDraft;
  onAuthenticated: () => void;
};

const RETURN_PATH = "/create#checkout";

/*
  The volume's page list, empty and stable between renders, exactly as on the preview step:
  `pages` is required and `buildLeaves` is memoised on it, so a fresh `[]` each render would
  rebuild the leaves and reset the book on every keystroke in the form beside it.
*/
const NO_PAGES: never[] = [];

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
  /*
    The real cover, or nothing.

    Falling back to the world painting here and handing it on as `coverImageUrl` left the book
    unable to tell a cover from a stand-in, so its own fallback never ran and the map artwork was
    presented as this child''s cover. Null lets the one fallback in `StorybookVolume` do the job.
  */
  const coverSrc = draft.preview?.coverImageDataUrl || null;
  const heroName = hero.name || t.common.fallbackHeroName;
  const bookTitle = draft.preview?.title || world.bookTitle(heroName);

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
        {/*
          The cover, at the size of the thing being promised, and nothing to press under it.

          This screen used to hand the volume the story's first page and a prev/next row to reach
          it, with a line under that explaining the swipe. The parent has just read that page on
          the step before; what they are doing here is typing an email address, and the book
          beside the form is there to say what the address is for. Two buttons, a page counter
          and a gesture hint are a second thing to do on a screen that has one - and the reader
          they belong to is the book itself, after the order.

          Same lever as the preview step: no pages and `interactive` off, so `buildLeaves` makes
          the single closed leaf and the controls, the hint, the corner targets, the swipe and
          the keyboard bindings go with them. The room they were using goes to the cover.
        */}
        <StorybookVolume
          variant="display"
          className={`storybook storybook-display theme-${worldId}`}
          heroName={heroName}
          title={bookTitle}
          coverImageUrl={coverSrc}
          worldId={worldId}
          pages={NO_PAGES}
          lockedPageCount={0}
          isUnlocked={false}
          interactive={false}
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
            {devSkipBusy ? "Signing in as demo…" : "DEV: Skip - continue as local demo"}
          </button>
          {devSkipError && <p className="ux-form-error">{devSkipError}</p>}
        </div>
      )}
    </section>
  );
}
