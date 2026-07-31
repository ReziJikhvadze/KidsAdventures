import { Check, Sparkles } from "lucide-react";
import { useEffect, useMemo } from "react";

import { PasswordlessAuthPanel } from "@/components/auth/PasswordlessAuthPanel";
import { StorybookVolume } from "@/components/adventrya/storybook/StorybookVolume";
import type { StoryPageContent } from "@/lib/api/types";
import { useAuth } from "@/lib/auth/AuthContext";
import { t } from "@/lib/i18n";
import { primaryCharacter, type JourneyDraft } from "@/lib/journey/draft";
import { heroDemoPages } from "@/lib/story/heroDemoPages";
import { WORLD_BY_ID, WORLD_COVER_ART, type WorldId } from "@/lib/worlds";

type Props = {
  draft: JourneyDraft;
  onAuthenticated: () => void;
};

const RETURN_PATH = "/create#checkout";

export function AuthStage({ draft, onAuthenticated }: Props) {
  const { isAuthenticated, isLoading } = useAuth();
  const hero = primaryCharacter(draft);
  const worldId = (draft.worldId ?? "dinosaurs") as WorldId;
  const world = WORLD_BY_ID[worldId];
  const coverSrc = draft.preview?.coverImageDataUrl || WORLD_COVER_ART[worldId];
  const heroName = hero.name || t.common.fallbackHeroName;
  const bookTitle = draft.preview?.title || world.bookTitle(heroName);

  const displayPages = useMemo((): StoryPageContent[] => {
    if (draft.preview?.storyJson) {
      try {
        const parsed = JSON.parse(draft.preview.storyJson) as {
          pages?: Array<{
            title?: string;
            text?: string;
            content?: string;
            illustrationUrl?: string | null;
          }>;
        };
        if (parsed.pages?.length) {
          return parsed.pages.slice(0, 1).map((page) => ({
            title: page.title || "",
            content: page.content || page.text || "",
            illustrationUrl: page.illustrationUrl ?? null,
          }));
        }
      } catch {
        /* fall through */
      }
    }
    return heroDemoPages(heroName, worldId).slice(0, 1);
  }, [draft.preview?.storyJson, heroName, worldId]);

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
    </section>
  );
}
