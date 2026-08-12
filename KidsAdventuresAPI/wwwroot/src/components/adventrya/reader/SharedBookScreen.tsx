import { ArrowRight, BookOpen, Sparkles } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { Link, useParams } from "@tanstack/react-router";

import { AppHeader } from "@/components/adventrya/AppHeader";
import { StorybookVolume } from "@/components/adventrya/storybook/StorybookVolume";
import { ApiError } from "@/lib/api/client";
import { getAdventurePack } from "@/lib/api/adventure-packs";
import type { AdventurePackDetailResponse } from "@/lib/api/types";
import { useAuth } from "@/lib/auth/AuthContext";
import { buildContinueHref } from "@/lib/continue";
import { useT } from "@/lib/i18n";
import { useWorldById, WORLD_COVER_ART, isWorldId } from "@/lib/worlds";

/**
 * QR landing page: the printed book's "continue moment".
 * Offers reading the story online or starting the next chapter.
 */
export function SharedBookScreen() {
  const WORLD_BY_ID = useWorldById();
  const t = useT();
  const { bookId } = useParams({ from: "/book/$bookId" });
  const { isAuthenticated, isLoading: authLoading } = useAuth();
  const [pack, setPack] = useState<AdventurePackDetailResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (authLoading || !isAuthenticated) {
      setPack(null);
      return;
    }

    let cancelled = false;
    setLoading(true);
    setError(null);

    void getAdventurePack(bookId)
      .then((detail) => {
        if (!cancelled) setPack(detail);
      })
      .catch((err) => {
        if (cancelled) return;
        setError(err instanceof ApiError ? err.message : null);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [bookId, isAuthenticated, authLoading]);

  const heroName = pack?.childName?.trim() || t.common.fallbackHeroName;
  const worldId = pack?.worldId && isWorldId(pack.worldId) ? pack.worldId : null;
  const world = worldId ? WORLD_BY_ID[worldId] : null;
  const title =
    pack?.title?.trim() ||
    (world ? world.bookTitle(heroName) : t.story.storybook.nextChapter(heroName));
  const pages = pack?.storyPages ?? [];
  const isUnlocked = pack?.isUnlocked === true || pack?.accessLevel === "Full";
  const lockedPageCount = pack?.lockedPageCount ?? 0;
  const coverFallback = worldId ? WORLD_COVER_ART[worldId] : WORLD_COVER_ART.dinosaurs;

  const continueHref = useMemo(
    () =>
      buildContinueHref({
        mode: "continue",
        worldId: pack?.worldId,
        characterId: pack?.primaryCharacterId,
        continuesFromBookId: bookId,
      }),
    [pack?.worldId, pack?.primaryCharacterId, bookId],
  );

  const continueParts = useMemo(() => {
    const [pathAndQuery, hash] = continueHref.split("#");
    const [to, query = ""] = (pathAndQuery || "/create").split("?");
    const search = Object.fromEntries(new URLSearchParams(query).entries());
    return { to: to || "/create", search, hash };
  }, [continueHref]);

  return (
    <div className="screen reader-shell reader-shell-shared-book">
      <div className="reader-glow" aria-hidden="true" />
      <div className="grain" aria-hidden="true" />

      <AppHeader backHref="/" childName={heroName} />

      <main className="reader-shared-stage">
        <div className="reader-shared-heading">
          <p className="eyebrow">
            <Sparkles aria-hidden="true" /> {t.story.storybook.qrTitle}
          </p>
          <h1>
            {t.story.reader.flipPrefix}
            {heroName}
            {t.story.reader.flipSuffix}
          </h1>
          <p>{t.story.storybook.backTap(heroName)}</p>

          <div className="ux-generated-actions" style={{ marginTop: 20 }}>
            {isAuthenticated ? (
              <Link className="button button-quiet" to="/reader/$bookId" params={{ bookId }}>
                <BookOpen aria-hidden="true" />
                Online Reader
              </Link>
            ) : (
              <Link className="button button-quiet" to="/create" hash="auth">
                <BookOpen aria-hidden="true" />
                შესვლა წასაკითხად
              </Link>
            )}
            <Link
              className="button button-primary"
              to={continueParts.to}
              search={continueParts.search}
              hash={continueParts.hash}
            >
              {t.story.world.unlockNext}
              <ArrowRight aria-hidden="true" />
            </Link>
          </div>
          {!authLoading && !isAuthenticated ? (
            <p className="eyebrow" style={{ marginTop: 14, color: "#f8f2e5a8" }}>
              სრული წიგნის გასახსნელად შედი იმ ანგარიშით, რომლითაც წიგნი შეიქმნა.
            </p>
          ) : null}
          {error ? (
            <p className="eyebrow" style={{ marginTop: 14, color: "#f1c970" }}>
              {error}
            </p>
          ) : null}
        </div>

        {loading || authLoading ? (
          <p className="eyebrow" style={{ color: "#f8f2e5a8" }}>
            იტვირთება…
          </p>
        ) : pack ? (
          <StorybookVolume
            className="storybook storybook-full"
            heroName={heroName}
            title={title}
            coverImageUrl={pack.coverImageUrl}
            worldId={pack.worldId}
            pages={pages}
            lockedPageCount={lockedPageCount}
            isUnlocked={isUnlocked}
            interactive
            initialIndex={isUnlocked && pages.length > 0 ? Math.max(2, pages.length) : 0}
          />
        ) : (
          <div className="storybook storybook-full is-closed">
            <div className="storybook-volume">
              <div className="storybook-surface">
                <article className="storybook-cover">
                  <div
                    className="storybook-cover-art"
                    style={{ backgroundImage: `url("${coverFallback}")` }}
                  />
                  <div className="storybook-cover-wash" aria-hidden="true" />
                  <span className="storybook-brand">{t.story.storybook.brand}</span>
                  <div className="storybook-cover-copy">
                    <small>{t.story.storybook.belongsTo(heroName)}</small>
                    <h2>{title}</h2>
                  </div>
                </article>
              </div>
            </div>
          </div>
        )}
      </main>

      <div className="reader-memory">
        <Sparkles aria-hidden="true" />
        {t.story.reader.memoryPrefix}
        {heroName}
        {t.story.reader.memorySuffix}
      </div>
    </div>
  );
}
