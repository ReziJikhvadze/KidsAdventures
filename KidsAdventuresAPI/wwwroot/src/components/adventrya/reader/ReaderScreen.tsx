import { BookOpen, Download, Sparkles } from "lucide-react";
import { useEffect, useState } from "react";
import { Link, useParams } from "@tanstack/react-router";

import { AppHeader } from "@/components/adventrya/AppHeader";
import { StorybookVolume } from "@/components/adventrya/storybook/StorybookVolume";
import { ApiError } from "@/lib/api/client";
import { downloadAdventurePack, getAdventurePack } from "@/lib/api/adventure-packs";
import type { AdventurePackDetailResponse } from "@/lib/api/types";
import { useAuth } from "@/lib/auth/AuthContext";
import { useT } from "@/lib/i18n";
import { useWorldById, isWorldId } from "@/lib/worlds";

export function ReaderScreen() {
  const WORLD_BY_ID = useWorldById();
  const t = useT();
  const { bookId } = useParams({ from: "/reader/$bookId" });
  const { isAuthenticated, isLoading: authLoading } = useAuth();
  const [pack, setPack] = useState<AdventurePackDetailResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [downloading, setDownloading] = useState(false);

  useEffect(() => {
    if (authLoading) return;
    if (!isAuthenticated) {
      setLoading(false);
      setError("წიგნის წასაკითხად გაიარე ავტორიზაცია.");
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
        setError(err instanceof ApiError ? err.message : "წიგნი ვერ ჩაიტვირთა.");
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
    (world ? world.bookTitle(heroName) : t.story.storybook.adventureOf(heroName));
  const pages = pack?.storyPages ?? [];
  const isUnlocked = pack?.isUnlocked === true || pack?.accessLevel === "Full";
  const lockedPageCount = pack?.lockedPageCount ?? (isUnlocked ? 0 : Math.max(0, 7 - pages.length));

  const onDownload = async () => {
    if (!pack || downloading) return;
    setDownloading(true);
    try {
      await downloadAdventurePack(pack.id, `${title}.pdf`);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "PDF ვერ ჩამოიტვირთა.");
    } finally {
      setDownloading(false);
    }
  };

  return (
    <div className="screen reader-shell reader-shell-shared-book">
      <div className="reader-glow" aria-hidden="true" />
      <div className="grain" aria-hidden="true" />

      <AppHeader backHref="/dashboard" childName={heroName} />

      <main className="reader-shared-stage">
        <div className="reader-shared-heading">
          <p className="eyebrow">
            <Sparkles aria-hidden="true" /> ONLINE READER
          </p>
          <h1>
            {t.story.reader.flipPrefix}
            {heroName}
            {t.story.reader.flipSuffix}
          </h1>
          <p>{t.story.reader.lead}</p>
          <div className="ux-generated-actions" style={{ marginTop: 18 }}>
            <button
              className="button button-quiet"
              type="button"
              disabled={!pack?.pdfUrl || downloading}
              onClick={() => void onDownload()}
            >
              <Download aria-hidden="true" />
              {downloading ? "…" : t.journey.generated.downloadPdf}
            </button>
            <Link className="button button-quiet" to="/dashboard">
              <BookOpen aria-hidden="true" />
              {t.story.reader.library.trim()}
            </Link>
          </div>
        </div>

        {loading || authLoading ? (
          <p className="eyebrow" style={{ color: "#f8f2e5a8" }}>
            იტვირთება…
          </p>
        ) : error ? (
          <p className="eyebrow" style={{ color: "#f1c970" }}>
            {error}
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
            isSpreadBook={pack.isSpreadBook}
            interactive
          />
        ) : null}
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
